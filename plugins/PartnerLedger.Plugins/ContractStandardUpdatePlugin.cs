using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_contract Update のPreOperation境界。
    /// 業務認可・監査主体はInitiatingUserIdから解決し、内部書込みはSYSTEMのサービスで行う。
    /// ポリシーONのroot Updateは拒否し、OFFのroot Updateだけをallow-listへ通す。
    /// 承認反映のサーバーマーカーによるネストUpdateは、同じ属性型検査を通したうえで
    /// ポリシー判定を二重適用しない。
    /// </summary>
    public sealed class ContractStandardUpdatePlugin : PluginBase
    {
        public const string PreImageAlias = "ContractStandardUpdatePreImage";
        public const string TrustedInternalWriteSharedVariable = "PartnerLedger.ContractStandardUpdatePlugin.TrustedInternalWrite";
        public const string OperationCode = "DirectContractUpdate";

        public ContractStandardUpdatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ContractStandardUpdatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, ContractStandardUpdateContract.EntityName, StringComparison.Ordinal)
                || context.Stage != 20)
            {
                throw new InvalidPluginExecutionException(
                    "標準契約Update Pluginはpl_contract UpdateのPreOperationだけで実行できます。");
            }
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;

            if (HasTrustedInternalWrite(context))
            {
                ContractStandardUpdateContract.ValidateTrustedInternalTarget(target, context.PrimaryEntityName);
                context.SharedVariables[TrustedInternalWriteSharedVariable] = true;
                return;
            }

            var preImage = context.PreEntityImages != null
                && context.PreEntityImages.Contains(PreImageAlias)
                ? context.PreEntityImages[PreImageAlias]
                : null;
            ValidateAndAudit(
                target,
                preImage,
                context.InitiatingUserId,
                localPluginContext.SystemService);
        }

        public static ContractStandardUpdateValidationResult ValidateAndAudit(
            Entity? target,
            Entity? preImage,
            Guid initiatingUserId,
            IOrganizationService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var settings = ApprovalSettingsRepository.RetrieveActive(service);
            var validation = ContractStandardUpdateContract.Validate(
                target,
                preImage,
                initiatingUserId,
                settings);
            if (!validation.IsValid)
            {
                throw new InvalidPluginExecutionException(
                    "標準契約Updateの入力契約違反です: " + validation.Error);
            }

            // PreOperationで作成した監査は、後続の標準Updateが失敗した場合も同一の
            // Dataverseトランザクションでロールバックされる。操作者はクライアント値
            // ではなくInitiatingUserIdから導出し、targetの列値は権威列へ保存しない。
            OperationLogRepository.RecordContractUpdate(
                service,
                OperationCode,
                "Success",
                null,
                initiatingUserId,
                target!.Id,
                validation.ChangedAttributes);
            return validation;
        }

        private static bool HasTrustedInternalWrite(IPluginExecutionContext context)
        {
            for (var current = context; current != null; current = current.ParentContext)
            {
                if (current.SharedVariables != null
                    && current.SharedVariables.TryGetValue(TrustedInternalWriteSharedVariable, out var value)
                    && value is bool trusted
                    && trusted)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
