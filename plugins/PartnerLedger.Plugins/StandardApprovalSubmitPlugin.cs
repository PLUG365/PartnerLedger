using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_Request UpdateのPreOperation（状態=提出中）から標準提出サービスを呼び出す。
    /// 内部更新はSYSTEMのサービスで実行し、業務操作者の認可はInitiatingUserIdで行う。
    /// 承認者Teamは環境変数Current Valueから解決する。
    /// </summary>
    public sealed class StandardApprovalSubmitPlugin : PluginBase
    {
        public StandardApprovalSubmitPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(StandardApprovalSubmitPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, StandardApprovalCrudContract.RequestEntityName, StringComparison.Ordinal)
                || context.Stage != 20)
            {
                throw new InvalidPluginExecutionException(
                    "標準提出Pluginはpl_Request UpdateのPreOperationだけで実行できます。");
            }

            // 標準判定Pluginが確知済み結果から申請状態を更新するネストした
            // Updateは、提出入口へ戻して再提出処理しない。SharedVariablesの
            // サーバーマーカーがない通常の利用者root Updateはこの経路へ入らない。
            if (StandardApprovalCrudGuardPlugin.HasTrustedDecisionInternalWrite(context)
                || StandardApprovalCrudGuardPlugin.HasTrustedInternalWrite(context))
            {
                return;
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;

            // The PreValidation CRUD guard owns the client-input allow-list. The
            // platform may enrich an Update Target between pipeline stages with
            // authority/system columns, so do not pass that mutable platform
            // object into the strict service contract. Keep only the requested
            // status value; all authority values are resolved and stamped below.
            Entity? submitTarget = null;
            if (target != null)
            {
                submitTarget = new Entity(target.LogicalName, target.Id);
                if (target.Attributes.TryGetValue("pl_requeststatuscode", out var requestedStatus))
                {
                    submitTarget["pl_requeststatuscode"] = requestedStatus;
                }
            }

            if (target != null
                && string.Equals(
                    target.GetAttributeValue<string>("pl_requeststatuscode"),
                    ApprovalRequestStatus.取消.ToString(),
                    StringComparison.Ordinal))
            {
                // 取消の書込みと、管理者ロール所属の照合読取りは、どちらもSYSTEMのサービスで行う。
                // 取消できるかは、操作者（InitiatingUserId）のロールと申請の状態で判定する。
                var cancellation = ApprovalCancellationService.Cancel(
                    localPluginContext.SystemService,
                    localPluginContext.SystemService,
                    target.Id,
                    target.GetAttributeValue<string>("pl_cancellationreason"),
                    context.InitiatingUserId,
                    DateTime.UtcNow);
                if (!cancellation.Success)
                {
                    throw new InvalidPluginExecutionException(
                        "承認取消を拒否しました: " + cancellation.ErrorCode);
                }

                ApplyCancellationToTarget(target, cancellation);
                return;
            }

            var approverTeamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                localPluginContext.SystemService,
                PartnerLedgerEnvironmentVariableNames.ApproverTeamId);
            context.SharedVariables[StandardApprovalCrudGuardPlugin.TrustedInternalWriteSharedVariable] = true;
            var result = StandardApprovalSubmitService.Submit(
                localPluginContext.SystemService,
                submitTarget!,
                target!,
                context.InitiatingUserId,
                approverTeamId,
                DateTime.UtcNow);
            if (!result.Success)
            {
                throw new InvalidPluginExecutionException(
                    "標準提出を拒否しました: " + result.ErrorCode);
            }
        }

        /// <summary>
        /// 取消・取下げの結果を親Updateの対象へ反映する。重複防止キーを解放できるのはこの経路だけ。
        /// 取消済みへの再送では、保存済みの理由を再送側の値（空を含む）で上書きしない。
        /// </summary>
        public static void ApplyCancellationToTarget(Entity target, ApprovalCancellationResult cancellation)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (cancellation == null || !cancellation.Success)
                throw new InvalidPluginExecutionException("承認取消の結果が不正です。");

            target[ApprovalActiveRequestKey.AttributeName] = null;
            // 再送と理由なし（申請者の取下げ）は理由の列に触れない。保存済みの理由を空で上書きしない。
            if (cancellation.IsReplay || cancellation.Reason.Length == 0)
            {
                target.Attributes.Remove("pl_cancellationreason");
                return;
            }
            target["pl_cancellationreason"] = cancellation.Reason;
        }
    }
}
