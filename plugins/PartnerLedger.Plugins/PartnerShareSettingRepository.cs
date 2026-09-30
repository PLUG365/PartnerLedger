using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準CRUD同期処理が必要とする共有設定行・会社状態・現在の直接共有権の読取り境界。
    /// 認可主体の解決や、旧経路由来の判定を推測して補完しない。由来が未確定な場合は、
    /// 呼出し側からUnknown／UntrackedOrExternalを明示して投影計画へ渡す。
    /// </summary>
    public static class PartnerShareSettingRepository
    {
        private const int MaxSettingsPerPartner = 5000;

        private static readonly ColumnSet SettingColumns = new ColumnSet(
            "pl_partnersharesettingid",
            PartnerShareSettingRecordFactory.PartnerLookupAttribute,
            PartnerShareSettingRecordFactory.PrincipalKindAttribute,
            PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute,
            PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute,
            PartnerShareSettingRecordFactory.AccessLevelAttribute,
            PartnerShareSettingRecordFactory.SettingStateAttribute,
            PartnerShareSettingRecordFactory.ProtectedPathAttribute,
            PartnerShareSettingRecordFactory.ManagedProjectionAttribute,
            PartnerShareSettingRecordFactory.RequestKeyAttribute,
            PartnerShareSettingRecordFactory.ContentHashAttribute,
            "createdby");

        public static List<PartnerShareSettingRecord> RetrieveByPartner(
            IOrganizationService service,
            Guid partnerId)
        {
            RequireService(service);
            RequireId(partnerId, nameof(partnerId));

            var query = new QueryExpression(PartnerShareSettingRecordFactory.EntityName)
            {
                ColumnSet = SettingColumns,
                PageInfo = new PagingInfo { Count = MaxSettingsPerPartner, PageNumber = 1 },
            };
            query.Criteria.AddCondition(
                PartnerShareSettingRecordFactory.PartnerLookupAttribute,
                ConditionOperator.Equal,
                partnerId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Ascending);

            var result = service.RetrieveMultiple(query);
            if (result.MoreRecords)
            {
                throw new InvalidPluginExecutionException(
                    "対象取引先の共有設定件数が上限を超えています。");
            }

            return result.Entities.Select(PartnerShareSettingRecordFactory.Parse).ToList();
        }

        public static PartnerShareSettingRecord Retrieve(
            IOrganizationService service,
            Guid settingId)
        {
            RequireService(service);
            RequireId(settingId, nameof(settingId));
            return PartnerShareSettingRecordFactory.Parse(
                service.Retrieve(
                    PartnerShareSettingRecordFactory.EntityName,
                    settingId,
                    SettingColumns));
        }

        /// <summary>
        /// pl_partnerの共有設定状態はクライアントやTargetから採用せず、保存済み行から解決する。
        /// 未設定・未知のChoiceは初期設定／設定完了のどちらにも倒さず拒否する。
        /// </summary>
        public static PartnerSharePhase ResolvePhase(
            IOrganizationService service,
            Guid partnerId)
        {
            RequireService(service);
            RequireId(partnerId, nameof(partnerId));

            var partner = service.Retrieve(
                PartnerStandardCreateContract.EntityName,
                partnerId,
                new ColumnSet(PartnerStandardCreateContract.ShareSetupStatusAttribute));
            var status = partner.GetAttributeValue<OptionSetValue>(
                PartnerStandardCreateContract.ShareSetupStatusAttribute);
            if (status == null)
            {
                throw new InvalidPluginExecutionException(
                    "取引先の共有設定状態が未設定です。");
            }

            return status.Value switch
            {
                PartnerStandardCreateContract.InitialShareSetupStatusCode => PartnerSharePhase.InitialSetup,
                PartnerStandardCreateContract.ReadyShareSetupStatusCode => PartnerSharePhase.Ready,
                _ => throw new InvalidPluginExecutionException(
                    "取引先の共有設定状態が不正です。"),
            };
        }

        /// <summary>
        /// Dataverseが返すprincipalの直接共有権を標準メッセージで取得する。
        /// RetrievePrincipalAccessはロールや親からの継承権も合算するため、共有設定行の
        /// 投影対象を判定するこの境界では使わない。RetrieveSharedPrincipalsAndAccessの
        /// PrincipalAccessesから対象principalの直接共有だけを取り出す。共有権の由来は
        /// 標準APIだけでは分からないため、呼出し側が解決済みの値を明示する。
        /// </summary>
        public static PartnerShareDirectShareState RetrieveDirectShareState(
            IOrganizationService service,
            Guid partnerId,
            PartnerSharePrincipalKind principalKind,
            Guid principalId,
            PartnerShareDirectShareProvenance provenance)
        {
            RequireService(service);
            RequireId(partnerId, nameof(partnerId));
            RequireId(principalId, nameof(principalId));

            var principalLogicalName = principalKind == PartnerSharePrincipalKind.Team
                ? "team"
                : "systemuser";
            var response = service.Execute(new RetrieveSharedPrincipalsAndAccessRequest
            {
                Target = new EntityReference(PartnerStandardCreateContract.EntityName, partnerId),
            }) as RetrieveSharedPrincipalsAndAccessResponse;
            if (response == null)
            {
                throw new InvalidPluginExecutionException(
                    "principalの直接共有権を解決できません。");
            }

            var currentRights = response.PrincipalAccesses
                .Where(access => access.Principal != null
                    && access.Principal.LogicalName == principalLogicalName
                    && access.Principal.Id == principalId)
                .Select(access => access.AccessMask)
                .Aggregate(AccessRights.None, (rights, accessMask) => rights | accessMask);

            return new PartnerShareDirectShareState
            {
                IsResolved = true,
                CurrentRights = currentRights,
                Provenance = provenance,
            };
        }

        private static void RequireService(IOrganizationService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
        }

        private static void RequireId(Guid id, string parameterName)
        {
            if (id == Guid.Empty) throw new ArgumentException("GUIDが必要です。", parameterName);
        }
    }
}
