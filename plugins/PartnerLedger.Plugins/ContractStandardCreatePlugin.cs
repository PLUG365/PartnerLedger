using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_contract Create のPreValidation境界。
    /// 標準Roleの行アクセスを入口とし、初回登録のallow-list・親／種類の有効性・要求キーを
    /// Calling Userで再検証してから、契約状態だけをサーバー導出する。
    /// </summary>
    public sealed class ContractStandardCreatePlugin : PluginBase
    {
        public ContractStandardCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ContractStandardCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, ContractStandardCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準契約Createの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target") ? context.InputParameters["Target"] as Entity : null;
            ValidateAndApply(target, context.InitiatingUserId, localPluginContext.InitiatingUserService);
        }

        public static void ValidateAndApply(Entity? target, Guid initiatingUserId, IOrganizationService initiatingUserService)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));
            var result = ContractStandardCreateContract.Validate(target, initiatingUserId);
            if (!result.IsValid || result.Value == null)
            {
                throw new InvalidPluginExecutionException("標準契約Createの入力が不正です: " + result.Error);
            }

            EnsureParentReadableAndActive(initiatingUserService, result.Value.PartnerId);
            EnsureContractTypeSelectable(initiatingUserService, result.Value.ContractTypeId);
            EnsureRegistrationKeyAvailable(initiatingUserService, result.Value);
            ContractStandardCreateContract.ApplyServerDerivedAttributes(target!, result.Value);
        }

        private static void EnsureParentReadableAndActive(IOrganizationService service, Guid partnerId)
        {
            Entity parent;
            try
            {
                parent = service.Retrieve("pl_partner", partnerId, new ColumnSet("statecode"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("親取引先を読み取れないため、契約を登録できません。", exception);
            }

            var state = parent.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null || state.Value != 0)
            {
                throw new InvalidPluginExecutionException("非アクティブな取引先へ契約を登録できません。");
            }
        }

        private static void EnsureContractTypeSelectable(IOrganizationService service, Guid contractTypeId)
        {
            Entity contractType;
            try
            {
                contractType = service.Retrieve("pl_contracttype", contractTypeId, new ColumnSet("statecode", "pl_selectable"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("契約種類を読み取れないため、契約を登録できません。", exception);
            }

            var state = contractType.GetAttributeValue<OptionSetValue>("statecode");
            var selectable = contractType.Attributes.TryGetValue("pl_selectable", out var selectableValue) && selectableValue is bool boolValue && boolValue;
            if (state == null || state.Value != 0 || !selectable)
            {
                throw new InvalidPluginExecutionException("選択できない契約種類は登録できません。");
            }
        }

        private static void EnsureRegistrationKeyAvailable(IOrganizationService service, ValidatedContractStandardCreateInput input)
        {
            var query = new QueryExpression(ContractStandardCreateContract.EntityName)
            {
                ColumnSet = new ColumnSet(
                    ContractStandardCreateContract.NameAttribute,
                    ContractStandardCreateContract.PartnerLookupAttribute,
                    ContractStandardCreateContract.ContractTypeLookupAttribute,
                    ContractStandardCreateContract.ContractStatusAttribute,
                    ContractStandardCreateContract.EndDateAttribute,
                    ContractStandardCreateContract.NoticeDateAttribute,
                    ContractStandardCreateContract.DecisionDateAttribute,
                    ContractStandardCreateContract.AutoRenewAttribute,
                    ContractStandardCreateContract.LinkAttribute,
                    ContractStandardCreateContract.RegistrationKeyAttribute),
                TopCount = 2,
            };
            query.Criteria.AddCondition(ContractStandardCreateContract.RegistrationKeyAttribute, ConditionOperator.Equal, input.RegistrationKey);

            EntityCollection existing;
            try
            {
                existing = service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("契約の再送状態を確認できないため、登録を中止しました。", exception);
            }

            if (existing.Entities.Count == 0) return;
            if (existing.Entities.Count > 1)
            {
                throw new InvalidPluginExecutionException("同じ契約登録キーが複数存在するため、登録を中止しました。");
            }

            if (ContractStandardCreateContract.MatchesExisting(existing.Entities[0], input))
            {
                throw new InvalidPluginExecutionException("同じ契約登録要求は既に処理されています。画面側で既存結果を照合してください。");
            }

            throw new InvalidPluginExecutionException("同じ契約登録キーへ異なる内容を指定することはできません。");
        }
    }
}
