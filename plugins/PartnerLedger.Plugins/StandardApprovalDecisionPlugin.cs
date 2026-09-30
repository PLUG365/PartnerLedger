using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_ApprovalLink UpdateのPostOperationで、確知済みのグループ承認結果を
    /// 申請／提出版へ反映する。内部Updateはサーバー専用SharedVariablesマーカーで
    /// 再処理を防ぎ、特定のApplication UserやDepthを認可根拠にしない。
    /// </summary>
    public sealed class StandardApprovalDecisionPlugin : PluginBase
    {
        public const string TrustedDecisionInternalWriteSharedVariable =
            "PartnerLedger.StandardApprovalDecisionPlugin.TrustedDecisionInternalWrite";

        public StandardApprovalDecisionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(StandardApprovalDecisionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, StandardApprovalCrudContract.ApprovalLinkEntityName, StringComparison.Ordinal)
                || context.Stage != 40)
            {
                throw new InvalidPluginExecutionException(
                    "標準判定Pluginはpl_ApprovalLink UpdateのPostOperationだけで実行できます。");
            }
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;

            // 結果PreOperationを通った標準Updateだけが、SharedVariablesに印を持つ。
            // ParentContextの形は実行環境差があるため、このサーバー専用の印で判定する。
            if (!StandardApprovalResultPlugin.HasStandardResultInput(context))
                return;
            if (target == null)
                throw new InvalidPluginExecutionException("標準判定PluginのTargetがありません。");

            if (!target.Attributes.Contains("pl_resultknown"))
                throw new InvalidPluginExecutionException("標準判定Pluginへ結果確知性が渡されていません。");
            if (!(target["pl_resultknown"] is bool resultKnown))
                throw new InvalidPluginExecutionException("標準判定Pluginの結果確知性が不正です。");
            if (!resultKnown)
                return;

            // SharedVariables cannot be supplied by a Web API caller. Nested
            // Request/SubmissionVersion updates use this marker to pass the
            // narrow internal-write boundary through the pipeline.
            context.SharedVariables[TrustedDecisionInternalWriteSharedVariable] = true;
            // Approved changes may also update Partner or Contract in nested
            // pipelines. Both guards require an explicit server-side marker;
            // the SYSTEM service identity and Depth alone are not authorization.
            context.SharedVariables[PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable] = true;
            context.SharedVariables[ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable] = true;

            var result = StandardApprovalDecisionService.Apply(
                localPluginContext.SystemService,
                target.Id,
                context.InitiatingUserId);
            if (!result.Success)
            {
                throw new InvalidPluginExecutionException(
                    "承認結果の標準判定反映を拒否しました: " + result.ErrorCode);
            }
        }

    }
}
