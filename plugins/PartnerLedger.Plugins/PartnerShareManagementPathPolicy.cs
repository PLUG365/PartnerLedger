using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 初期共有の保護経路を、サーバーで解決した専用承認者Teamだけへ限定する。
    /// 保護フラグはクライアント入力ではなく、このポリシーから導出する。
    /// </summary>
    public static class PartnerShareManagementPathPolicy
    {
        public static bool IsProtected(
            PartnerSharePhase phase,
            PartnerShareSettingInput input,
            Guid resolvedApproverTeamId)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (phase != PartnerSharePhase.InitialSetup)
            {
                return false;
            }

            if (resolvedApproverTeamId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "初期共有の承認者Teamをサーバーで解決できません。");
            }

            return input.PrincipalKind == PartnerSharePrincipalKind.Team
                && input.TeamId == resolvedApproverTeamId;
        }
    }
}
