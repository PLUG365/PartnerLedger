using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_partner.pl_sharesetupstatuscode のPreOperationガード。
    /// Ready遷移は経路を問わず保存済み保護共有行・直接ACL・同期トランザクションを再検証する。
    /// ParentContext/SharedVariablesや固定Application Userは認可根拠にしない。
    /// </summary>
    public sealed class PartnerShareSetupStatusGuardPlugin : PluginBase
    {
        // 旧マーカー名は回帰テストで「付いていても権限にならない」ことを検証するために残す。
        public const string TrustedInternalWriteSharedVariable =
            "PartnerLedger.PartnerShareSetupStatusGuard.TrustedInternalWrite";

        public PartnerShareSetupStatusGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(PartnerShareSetupStatusGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, PartnerStandardCreateContract.EntityName, StringComparison.Ordinal)
                || context.Stage != 20)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定状態ガードはpl_partner UpdateのPreOperationだけで実行できます。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            if (!HasShareSetupStatus(target))
            {
                return;
            }

            var teamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                localPluginContext.SystemService,
                PartnerLedgerEnvironmentVariableNames.ApproverTeamId);
            PartnerShareSetupReadyAuthorization.Validate(
                context,
                localPluginContext.SystemService,
                target,
                teamId);
        }

        public static bool HasShareSetupStatus(Entity? target)
            => target != null
               && target.Attributes.Contains(PartnerStandardCreateContract.ShareSetupStatusAttribute);

        public static void ValidateReadyTransition(Entity? target, string primaryEntityName)
        {
            if (target == null
                || !string.Equals(primaryEntityName, PartnerStandardCreateContract.EntityName, StringComparison.Ordinal)
                || target.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("共有設定状態のUpdate対象が不正です。");
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (!string.Equals(attributeName, PartnerStandardCreateContract.ShareSetupStatusAttribute, StringComparison.Ordinal)
                    && !string.Equals(attributeName, "pl_partnerid", StringComparison.Ordinal)
                    && !IsPlatformEnrichment(attributeName))
                {
                    throw new InvalidPluginExecutionException(
                    "共有設定状態のUpdateに許可されていない列があります: " + attributeName);
                }
            }

            var desired = target.GetAttributeValue<OptionSetValue>(
                PartnerStandardCreateContract.ShareSetupStatusAttribute);
            if (desired == null || desired.Value != PartnerStandardCreateContract.ReadyShareSetupStatusCode)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定状態のUpdateは設定完了だけを許可します。");
            }
        }

        private static bool IsPlatformEnrichment(string attributeName)
            => string.Equals(attributeName, "modifiedon", StringComparison.Ordinal)
               || string.Equals(attributeName, "modifiedby", StringComparison.Ordinal)
               || string.Equals(attributeName, "modifiedonbehalfby", StringComparison.Ordinal);

    }
}
