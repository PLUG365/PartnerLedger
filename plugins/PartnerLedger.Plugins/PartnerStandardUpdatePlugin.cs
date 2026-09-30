using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準pl_partner UpdateのPreOperation境界。直接更新はポリシーOFFのみ、
    /// 承認反映のネストUpdateはサーバー内部SharedVariablesマーカーでallow-listだけを通す。
    /// </summary>
    public sealed class PartnerStandardUpdatePlugin : PluginBase
    {
        public const string PreImageAlias = "PartnerStandardUpdatePreImage";
        public const string TrustedInternalWriteSharedVariable = "PartnerLedger.PartnerStandardUpdatePlugin.TrustedInternalWrite";
        public const string OperationCode = "DirectPartnerUpdate";

        public PartnerStandardUpdatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(PartnerStandardUpdatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, PartnerStandardUpdateContract.EntityName, StringComparison.Ordinal)
                || context.Stage != 20)
            {
                throw new InvalidPluginExecutionException("標準取引先Update Pluginはpl_partner UpdateのPreOperationだけで実行できます。");
            }
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            // Ready is a distinct, durable-state-validated transition. An approval
            // marker must never permit changing this authority column.
            if (PartnerShareSetupStatusGuardPlugin.HasShareSetupStatus(target))
            {
                var teamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                    localPluginContext.SystemService,
                    PartnerLedgerEnvironmentVariableNames.ApproverTeamId);
                PartnerShareSetupReadyAuthorization.Validate(
                    context,
                    localPluginContext.SystemService,
                    target,
                    teamId);
                return;
            }
            if (HasTrustedMarker(context, TrustedInternalWriteSharedVariable))
            {
                PartnerStandardUpdateContract.ValidateTrustedInternalTarget(target, context.PrimaryEntityName);
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

        public static PartnerStandardUpdateValidationResult ValidateAndAudit(
            Entity? target,
            Entity? preImage,
            Guid initiatingUserId,
            IOrganizationService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var settings = ApprovalSettingsRepository.RetrieveActive(service);
            var validation = PartnerStandardUpdateContract.Validate(target, preImage, initiatingUserId, settings);
            if (!validation.IsValid)
            {
                throw new InvalidPluginExecutionException("標準取引先Updateの入力契約違反です: " + validation.Error);
            }

            if (validation.ChangedAttributes.Contains(PartnerStandardUpdateContract.MainOwnerAttribute))
            {
                // 主担当の直接の変更（承認が不要な設定のとき、2026-09-29）。新しい主担当の共有を同じトランザクションで整える。
                // 反映できない理由は書く前に決め、分かる文言で拒否する。
                var newMainOwner = target!.GetAttributeValue<EntityReference>(PartnerStandardUpdateContract.MainOwnerAttribute);
                var approverTeamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                    service,
                    PartnerLedgerEnvironmentVariableNames.ApproverTeamId);
                var plan = PartnerMainOwnerChange.Plan(service, target.Id, newMainOwner!.Id, initiatingUserId, approverTeamId);
                if (!plan.IsValid) throw new InvalidPluginExecutionException(MainOwnerRejectionMessage(plan.Error));
                PartnerMainOwnerChange.Execute(service, plan);
            }

            OperationLogRepository.RecordPartnerUpdate(
                service,
                OperationCode,
                "Success",
                null,
                initiatingUserId,
                target!.Id,
                validation.ChangedAttributes);
            if (target.Attributes.Contains(PartnerStandardUpdateContract.NameAttribute))
            {
                if (!PartnerNameNormalizer.TryNormalize(target.GetAttributeValue<string>(PartnerStandardUpdateContract.NameAttribute), out var normalizedName, out _))
                    throw new InvalidPluginExecutionException("取引先名の正規化に失敗しました。");
                target[PartnerStandardUpdateContract.NormalizedNameAttribute] = normalizedName;
            }
            return validation;
        }

        private static string MainOwnerRejectionMessage(PartnerMainOwnerChangeError error)
            => error switch
            {
                PartnerMainOwnerChangeError.SameAsCurrent => "主担当はすでにその人です。",
                PartnerMainOwnerChangeError.ShareCannotBeArranged => "新しい主担当の共有を整えられないため、主担当を変えられません。共有設定を確認してください。",
                PartnerMainOwnerChangeError.RowOwnerUnavailable => "取引先の登録者も新しい主担当も共有設定を使える状態でない（PartnerLedgerのロールが無い、など）ため、主担当を変えられません。新しい主担当にロールを割り当ててください。",
                _ => "主担当に選んだユーザーには共有できません（無効なユーザーや、Microsoftのサポート用アカウントなど）。主担当には通常の利用者を選んでください。",
            };

        private static bool HasTrustedMarker(IPluginExecutionContext context, string markerName)
        {
            for (var current = context; current != null; current = current.ParentContext)
            {
                if (current.SharedVariables != null
                    && current.SharedVariables.TryGetValue(markerName, out var value)
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
