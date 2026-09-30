using System;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 初期共有から通常共有へ進めるサーバー側の遷移条件。
    /// 画面入力や呼出者の自己申告ではなく、保存済みの保護経路からだけ判定する。
    /// </summary>
    public static class PartnerShareSetupCompletion
    {
        public static bool ShouldComplete(
            PartnerSharePhase phase,
            PartnerShareSettingInput input,
            Guid resolvedApproverTeamId)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return phase == PartnerSharePhase.InitialSetup
                && PartnerShareManagementPathPolicy.IsProtected(
                    phase,
                    input,
                    resolvedApproverTeamId);
        }

        public static bool ShouldComplete(
            PartnerSharePhase phase,
            PartnerShareSettingRecord current,
            Guid resolvedApproverTeamId)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            return phase == PartnerSharePhase.InitialSetup
                && current.IsProtectedManagementPath
                && current.PrincipalKind == PartnerSharePrincipalKind.Team
                && current.PrincipalId != Guid.Empty
                && current.PrincipalId == resolvedApproverTeamId;
        }
    }
}
