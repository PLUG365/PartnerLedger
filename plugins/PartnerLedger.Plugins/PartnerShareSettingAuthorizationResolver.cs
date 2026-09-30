using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準共有設定CRUDの呼出者を、Dataverseの実際の主体・行権限から解決する。
    /// 承認者判定は、登録ステップの設定で指定された専用Teamのmembershipだけを使い、
    /// クライアント入力や表示上のRole名は認可根拠にしない。
    /// </summary>
    public static class PartnerShareSettingAuthorizationResolver
    {
        public static PartnerShareCaller Resolve(
            IOrganizationService initiatingUserService,
            IOrganizationService executionService,
            Guid initiatingUserId,
            Guid partnerId,
            Guid approverTeamId)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));
            if (executionService == null) throw new ArgumentNullException(nameof(executionService));
            if (initiatingUserId == Guid.Empty) throw new InvalidPluginExecutionException("共有設定のInitiatingUserがありません。");
            if (partnerId == Guid.Empty) throw new InvalidPluginExecutionException("共有設定の取引先がありません。");

            var isApprover = approverTeamId != Guid.Empty
                && IsTeamMember(executionService, approverTeamId, initiatingUserId);
            var rights = RetrievePrincipalAccess(initiatingUserService, partnerId, initiatingUserId);

            return new PartnerShareCaller
            {
                UserId = initiatingUserId,
                IsApprover = isApprover,
                EffectiveAccess = ToEffectiveAccess(rights),
            };
        }

        public static bool IsTeamMember(
            IOrganizationService service,
            Guid teamId,
            Guid userId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (teamId == Guid.Empty || userId == Guid.Empty) return false;

            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
            };
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        public static AccessRights RetrievePrincipalAccess(
            IOrganizationService service,
            Guid partnerId,
            Guid userId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (partnerId == Guid.Empty) throw new ArgumentException("取引先IDが必要です。", nameof(partnerId));
            if (userId == Guid.Empty) throw new ArgumentException("ユーザーIDが必要です。", nameof(userId));

            var response = (RetrievePrincipalAccessResponse)service.Execute(
                new RetrievePrincipalAccessRequest
                {
                    Target = new EntityReference(PartnerStandardCreateContract.EntityName, partnerId),
                    Principal = new EntityReference("systemuser", userId),
                });
            return response.AccessRights;
        }

        public static PartnerShareAccessLevel? ToEffectiveAccess(AccessRights rights)
        {
            if ((rights & AccessRights.WriteAccess) != AccessRights.None)
            {
                return PartnerShareAccessLevel.Write;
            }

            if ((rights & AccessRights.ReadAccess) != AccessRights.None)
            {
                return PartnerShareAccessLevel.Read;
            }

            return null;
        }
    }
}
