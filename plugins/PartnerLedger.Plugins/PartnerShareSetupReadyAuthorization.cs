using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// InitialSetupからReadyへの更新を、保存済みの保護共有行と直接ACLで検証する。
    /// 直接要求と同期投影を同じ状態条件で判定し、Depth/ParentContext/SharedVariablesは認可根拠にしない。
    /// </summary>
    public static class PartnerShareSetupReadyAuthorization
    {
        public static void Validate(
            IPluginExecutionContext context,
            IOrganizationService service,
            Entity? target,
            Guid approverTeamId)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (service == null) throw new ArgumentNullException(nameof(service));
            PartnerShareSetupStatusGuardPlugin.ValidateReadyTransition(target, context.PrimaryEntityName);
            if (approverTeamId == Guid.Empty || !context.IsInTransaction)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定状態の更新に必要なTeam設定または同期トランザクションを確認できません。");
            }

            var partner = service.Retrieve(
                PartnerStandardCreateContract.EntityName,
                target!.Id,
                new ColumnSet(PartnerStandardCreateContract.ShareSetupStatusAttribute));
            var status = partner.GetAttributeValue<OptionSetValue>(
                PartnerStandardCreateContract.ShareSetupStatusAttribute);
            if (status == null
                || status.Value != PartnerStandardCreateContract.InitialShareSetupStatusCode)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定状態は初期設定中から設定完了へ一度だけ遷移できます。");
            }

            var protectedRows = PartnerShareSettingRepository.RetrieveByPartner(service, target.Id)
                .Where(row => row.State == PartnerShareSettingState.Active
                    && row.IsProtectedManagementPath)
                .ToList();
            if (protectedRows.Count != 1
                || protectedRows[0].PrincipalKind != PartnerSharePrincipalKind.Team
                || protectedRows[0].PrincipalId != approverTeamId
                || protectedRows[0].AccessLevel != PartnerShareAccessLevel.Read
                || !protectedRows[0].IsManagedProjection)
            {
                throw new InvalidPluginExecutionException(
                    "承認者Teamの有効な保護共有設定を確認できません。");
            }

            var directShare = PartnerShareSettingRepository.RetrieveDirectShareState(
                service,
                target.Id,
                PartnerSharePrincipalKind.Team,
                approverTeamId,
                PartnerShareDirectShareProvenance.ManagedByPartnerLedger);
            if (!directShare.IsResolved || directShare.CurrentRights != AccessRights.ReadAccess)
            {
                throw new InvalidPluginExecutionException(
                    "承認者Teamの直接Read共有を確認できません。");
            }
        }
    }
}
