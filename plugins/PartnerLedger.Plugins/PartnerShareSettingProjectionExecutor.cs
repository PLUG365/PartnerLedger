using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// PartnerShareSettingProjectionの計画を標準Dataverse共有メッセージへ変換する薄い境界。
    /// 計画の認可・由来判定をここで再計算せず、IsAllowed=falseの計画は必ず拒否する。
    /// 呼出し側は設定行の保存とこの実行を同一Dataverseトランザクションへ置く。
    /// </summary>
    public static class PartnerShareSettingProjectionExecutor
    {
        public static void Execute(
            IOrganizationService service,
            PartnerShareSettingProjectionPlan plan)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsAllowed)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定の投影計画が許可されていません: " + plan.Error);
            }

            switch (plan.Operation)
            {
                case PartnerShareProjectionOperation.None:
                    return;
                case PartnerShareProjectionOperation.Grant:
                    service.Execute(new GrantAccessRequest
                    {
                        Target = plan.TargetReference,
                        PrincipalAccess = new PrincipalAccess
                        {
                            Principal = plan.PrincipalReference,
                            AccessMask = plan.AccessMask,
                        },
                    });
                    return;
                case PartnerShareProjectionOperation.Modify:
                    service.Execute(new ModifyAccessRequest
                    {
                        Target = plan.TargetReference,
                        PrincipalAccess = new PrincipalAccess
                        {
                            Principal = plan.PrincipalReference,
                            AccessMask = plan.AccessMask,
                        },
                    });
                    return;
                case PartnerShareProjectionOperation.Revoke:
                    service.Execute(new RevokeAccessRequest
                    {
                        Target = plan.TargetReference,
                        Revokee = plan.PrincipalReference,
                    });
                    return;
                case PartnerShareProjectionOperation.ReconciliationRequired:
                    throw new InvalidPluginExecutionException(
                        "共有権の由来を確認できないため、投影を保留します。");
                default:
                    throw new InvalidPluginExecutionException(
                        "共有設定の投影操作が不正です: " + plan.Operation);
            }
        }
    }
}
