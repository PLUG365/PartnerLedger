using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_bulletinpost Create のPreValidation境界。
    /// 唯一の親である取引先を共有の正本とし、担当者向け投稿はContactKeyと
    /// 担当者のRead／所属を呼出者本人のサービスで再検証する。
    /// </summary>
    public sealed class BulletinPostStandardCreatePlugin : PluginBase
    {
        public BulletinPostStandardCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BulletinPostStandardCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, BulletinPostStandardCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準掲示板投稿Createの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            ValidateAndApply(target, context.InitiatingUserId, DateTime.UtcNow, localPluginContext.InitiatingUserService);
        }

        public static void ValidateAndApply(
            Entity? target,
            Guid initiatingUserId,
            DateTime registeredAtUtc,
            IOrganizationService initiatingUserService)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));

            var result = BulletinPostStandardCreateContract.Validate(target, initiatingUserId);
            if (!result.IsValid || result.Value == null)
            {
                throw new InvalidPluginExecutionException("標準掲示板投稿Createの入力が不正です: " + result.Error);
            }

            EnsurePartnerReadableAndActive(initiatingUserService, result.Value.PartnerId);
            EnsureContactReadableAndBelongsToPartner(initiatingUserService, result.Value.ContactId, result.Value.PartnerId);
            EnsureRequestKeyAvailable(initiatingUserService, result.Value);
            BulletinPostStandardCreateContract.ApplyServerDerivedAttributes(target!, result.Value, registeredAtUtc);
        }

        private static void EnsurePartnerReadableAndActive(IOrganizationService service, Guid partnerId)
        {
            Entity partner;
            try
            {
                partner = service.Retrieve("pl_partner", partnerId, new ColumnSet("statecode"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "親取引先を読み取れないため、掲示板へ投稿できません。", exception);
            }

            var state = partner.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null || state.Value != 0)
            {
                throw new InvalidPluginExecutionException(
                    "非アクティブな取引先へ掲示板投稿できません。");
            }
        }

        private static void EnsureContactReadableAndBelongsToPartner(
            IOrganizationService service,
            Guid? contactId,
            Guid partnerId)
        {
            if (!contactId.HasValue) return;

            Entity contact;
            try
            {
                contact = service.Retrieve(
                    "pl_contact",
                    contactId.Value,
                    new ColumnSet("statecode", ContactStandardCreateContract.PartnerLookupAttribute));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "対象担当者を読み取れないため、掲示板へ投稿できません。", exception);
            }

            var state = contact.GetAttributeValue<OptionSetValue>("statecode");
            var parent = contact.GetAttributeValue<EntityReference>(ContactStandardCreateContract.PartnerLookupAttribute);
            if (state == null || state.Value != 0 || parent == null || parent.Id != partnerId)
            {
                throw new InvalidPluginExecutionException(
                    "担当者が指定した取引先のActive担当者ではありません。");
            }
        }

        private static void EnsureRequestKeyAvailable(
            IOrganizationService service,
            ValidatedBulletinPostStandardCreateInput input)
        {
            var query = new QueryExpression(BulletinPostStandardCreateContract.EntityName)
            {
                ColumnSet = new ColumnSet(
                    BulletinPostStandardCreateContract.BodyAttribute,
                    BulletinPostStandardCreateContract.RequestKeyAttribute,
                    BulletinPostStandardCreateContract.PartnerLookupAttribute,
                    BulletinPostStandardCreateContract.ContactKeyAttribute),
                TopCount = 2,
            };
            query.Criteria.AddCondition(
                BulletinPostStandardCreateContract.RequestKeyAttribute,
                ConditionOperator.Equal,
                input.RequestKey);

            EntityCollection existing;
            try
            {
                existing = service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "掲示板投稿の再送状態を確認できないため、投稿を中止しました。", exception);
            }

            if (existing.Entities.Count == 0) return;
            if (existing.Entities.Count > 1)
            {
                throw new InvalidPluginExecutionException(
                    "同じ掲示板要求キーが複数存在するため、投稿を中止しました。");
            }

            if (BulletinPostStandardCreateContract.MatchesExisting(existing.Entities[0], input))
            {
                throw new InvalidPluginExecutionException(
                    "同じ掲示板投稿要求は既に処理されています。画面側で既存結果を照合してください。");
            }

            throw new InvalidPluginExecutionException(
                "同じ掲示板要求キーへ異なる内容を指定することはできません。");
        }
    }
}
