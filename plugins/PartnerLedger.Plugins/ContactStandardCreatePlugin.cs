using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_contact Create のPreValidation境界。
    /// 画面のRead権限だけを信用せず、親pl_partnerを呼出者本人のサービスで取得して
    /// 可視性とActive状態を再確認する。既存Contactへの直接Createを増幅する別経路は
    /// 作らず、標準CreateのTargetへサーバー導出値だけを適用する。
    ///
    /// 担当者のowneridは親取引先ではなく登録実行者（initiatingUserId）を設定する
    /// （理由はPartnerStandardCreateContract.ApplyServerDerivedAttributesの注記を参照）。
    /// </summary>
    public sealed class ContactStandardCreatePlugin : PluginBase
    {
        public ContactStandardCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ContactStandardCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, ContactStandardCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準担当者Createの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            ValidateAndApply(
                target,
                context.InitiatingUserId,
                DateTime.UtcNow,
                localPluginContext.InitiatingUserService);
        }

        public static void ValidateAndApply(
            Entity? target,
            Guid initiatingUserId,
            DateTime registeredAtUtc,
            IOrganizationService initiatingUserService)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));

            var result = ContactStandardCreateContract.Validate(target, initiatingUserId, registeredAtUtc);
            if (!result.IsValid || result.Value == null)
            {
                throw new InvalidPluginExecutionException(
                    "標準担当者Createの入力が不正です: " + result.Error);
            }

            EnsureParentReadableAndActive(initiatingUserService, result.Value.PartnerId);
            EnsureRegistrationKeyAvailable(initiatingUserService, result.Value);
            ContactStandardCreateContract.ApplyServerDerivedAttributes(target!, result.Value, initiatingUserId);
        }

        private static void EnsureParentReadableAndActive(
            IOrganizationService service,
            Guid partnerId)
        {
            Entity parent;
            try
            {
                parent = service.Retrieve(
                    "pl_partner",
                    partnerId,
                    new ColumnSet("statecode"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "親取引先を読み取れないため、担当者を登録できません。", exception);
            }

            var state = parent.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null || state.Value != 0)
            {
                throw new InvalidPluginExecutionException(
                    "非アクティブな取引先へ担当者を登録できません。");
            }
        }

        private static void EnsureRegistrationKeyAvailable(
            IOrganizationService service,
            ValidatedContactStandardCreateInput input)
        {
            var query = new QueryExpression(ContactStandardCreateContract.EntityName)
            {
                ColumnSet = new ColumnSet(
                    ContactStandardCreateContract.NameAttribute,
                    ContactStandardCreateContract.PartnerLookupAttribute,
                    ContactStandardCreateContract.StatusAttribute,
                    ContactStandardCreateContract.DepartmentRoleAttribute,
                    ContactStandardCreateContract.EmailAttribute,
                    ContactStandardCreateContract.PhoneAttribute,
                    ContactStandardCreateContract.RegistrationKeyAttribute),
                TopCount = 2,
            };
            query.Criteria.AddCondition(
                ContactStandardCreateContract.RegistrationKeyAttribute,
                ConditionOperator.Equal,
                input.RegistrationKey);

            EntityCollection existing;
            try
            {
                existing = service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "担当者の再送状態を確認できないため、登録を中止しました。", exception);
            }

            if (existing.Entities.Count == 0) return;
            if (existing.Entities.Count > 1)
            {
                throw new InvalidPluginExecutionException(
                    "同じ担当者登録キーが複数存在するため、登録を中止しました。");
            }

            if (ContactStandardCreateContract.MatchesExisting(existing.Entities[0], input))
            {
                throw new InvalidPluginExecutionException(
                    "同じ担当者登録要求は既に処理されています。画面側で既存結果を照合してください。");
            }

            throw new InvalidPluginExecutionException(
                "同じ担当者登録キーへ異なる内容を指定することはできません。");
        }
    }
}
