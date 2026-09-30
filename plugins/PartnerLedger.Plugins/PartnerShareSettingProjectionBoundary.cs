using System;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準共有設定行のCRUDと、共有投影計画の接続点。
    /// 呼出者は必ずサーバー側で解決済みの値を受け取り、Targetの権威値やクライアントの
    /// ロール指定を認可情報として使わない。ここでは永続化・認可主体の解決を行わず、
    /// Repositoryの再取得結果を純粋なProjectionへ渡してから、標準SDKメッセージを実行する。
    ///
    /// 実際のPlugin登録では、標準CreateのPreOperationで認可・入力を検証し、PostOperationで
    /// 保存済み行へ共有を投影する。UpdateはPreOperationで現行行を参照して投影する。
    /// いずれも同一トランザクション内で実行し、承認者Team未設定時のInitialSetupは拒否する。
    /// </summary>
    public static class PartnerShareSettingProjectionBoundary
    {
        public static PartnerShareSettingProjectionPlan Create(
            IOrganizationService service,
            Guid initiatingUserId,
            PartnerShareCaller resolvedCaller,
            Entity target,
            PartnerShareDirectShareProvenance directShareProvenance,
            Guid excludedSettingId = default,
            Guid resolvedApproverTeamId = default)
        {
            RequireResolvedCaller(initiatingUserId, resolvedCaller);
            var input = PartnerShareSettingStandardInputParser.ParseCreate(target);
            var phase = PartnerShareSettingRepository.ResolvePhase(service, input.PartnerId);
            var existing = PartnerShareSettingRepository.RetrieveByPartner(service, input.PartnerId);
            if (excludedSettingId != Guid.Empty)
            {
                existing = existing.Where(record => record.Id != excludedSettingId).ToList();
            }
            var directShare = PartnerShareSettingRepository.RetrieveDirectShareState(
                service,
                input.PartnerId,
                input.PrincipalKind!.Value,
                PartnerShareSettingContract.PrincipalId(input),
                directShareProvenance);
            return PartnerShareSettingProjection.PlanCreate(
                phase,
                resolvedCaller,
                input,
                existing,
                directShare,
                resolvedApproverTeamId);
        }

        public static PartnerShareSettingProjectionPlan Update(
            IOrganizationService service,
            Guid initiatingUserId,
            PartnerShareCaller resolvedCaller,
            Entity target,
            PartnerShareDirectShareProvenance directShareProvenance)
        {
            RequireResolvedCaller(initiatingUserId, resolvedCaller);
            if (target == null)
            {
                throw new InvalidPluginExecutionException(
                    "標準共有設定のUpdate対象がありません。");
            }
            var current = PartnerShareSettingRepository.Retrieve(service, target.Id);
            var command = PartnerShareSettingStandardInputParser.ParseUpdate(target, current);
            var phase = PartnerShareSettingRepository.ResolvePhase(service, current.PartnerId);
            var existing = PartnerShareSettingRepository.RetrieveByPartner(service, current.PartnerId);
            var directShare = PartnerShareSettingRepository.RetrieveDirectShareState(
                service,
                current.PartnerId,
                current.PrincipalKind,
                current.PrincipalId,
                directShareProvenance);

            return command.IsRevoke
                ? PartnerShareSettingProjection.PlanRevoke(
                    phase,
                    resolvedCaller,
                    current,
                    existing,
                    directShare)
                : PartnerShareSettingProjection.PlanUpdate(
                    phase,
                    resolvedCaller,
                    current,
                    command.Desired!,
                    existing,
                    directShare);
        }

        public static void Execute(
            IOrganizationService service,
            PartnerShareSettingProjectionPlan plan)
            => PartnerShareSettingProjectionExecutor.Execute(service, plan);

        private static void RequireResolvedCaller(
            Guid initiatingUserId,
            PartnerShareCaller resolvedCaller)
        {
            if (initiatingUserId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "標準共有設定のInitiatingUserがありません。");
            }
            if (resolvedCaller == null || resolvedCaller.UserId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "標準共有設定の認可主体をサーバーで解決できません。");
            }
            if (resolvedCaller.UserId != initiatingUserId)
            {
                throw new InvalidPluginExecutionException(
                    "標準共有設定の認可主体とInitiatingUserが一致しません。");
            }
        }
    }
}
