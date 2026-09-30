using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public enum PartnerMainOwnerChangeError
    {
        None,
        /// <summary>新しい主担当が通常の有効な利用者でない（Microsoftのサポート用アカウント・無効・アプリ用など）。</summary>
        UserNotShareable,
        /// <summary>今の主担当と同じ人。</summary>
        SameAsCurrent,
        /// <summary>既存の共有を縮めずに編集の共有を整えられない（投影の計画が保留・拒否）。</summary>
        ShareCannotBeArranged,
        /// <summary>新しく作る行の所有者にできる人がいない（登録者も新しい主担当も共有設定を読めない）。</summary>
        RowOwnerUnavailable,
    }

    /// <summary>主担当の変更で行う書き込みの計画。書き込みの前に作り、反映できない理由をここで決める。</summary>
    public sealed class PartnerMainOwnerChangePlan
    {
        public PartnerMainOwnerChangeError Error { get; internal set; }
        public bool IsValid => Error == PartnerMainOwnerChangeError.None;
        public Guid PartnerId { get; internal set; }
        public Guid NewMainOwnerId { get; internal set; }
        public Entity? RowToCreate { get; internal set; }
        public Entity? RowToUpdate { get; internal set; }
        public PartnerShareSettingProjectionPlan? ProjectionPlan { get; internal set; }
    }

    /// <summary>
    /// 主担当の変更（2026-09-29ユーザー決定：申請→承認、または承認が不要な設定なら直接）で、新しい主担当に編集の共有を整える。
    /// 共有の判断は、利用者の共有設定と同じ投影の計画（PlanCreate・PlanUpdate）に任せる。呼び出し主体はサーバー内部の
    /// 承認者と同等（クライアントの入力ではない）。前の主担当の共有・行は変えない（ユーザー決定a）。
    /// 反映・直接の変更の両方から使う。Planで反映できない理由をすべて決め、最初の書き込みはExecuteで行う。
    /// </summary>
    public static class PartnerMainOwnerChange
    {
        public const string RequestKeyPrefix = "PartnerLedger-MainOwner-";
        public const string ShareSettingReadPrivilege = "prvReadpl_PartnerShareSetting";

        /// <summary>
        /// 主担当にできる通常の有効な利用者か（アクセスモード0・無効でない・アプリケーションユーザーでない）。
        /// 存在しないIDでも例外にしない（監査L-3）。Retrieveは見つからないとFaultExceptionになり、同期プラグインの中では
        /// 捕まえても取引全体が失敗扱いになるため、検索（0件）で読む。
        /// </summary>
        public static bool IsShareableUser(IOrganizationService service, Guid userId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (userId == Guid.Empty) return false;
            var user = RetrieveUser(service, userId, "accessmode", "isdisabled", "applicationid");
            if (user == null) return false;
            var accessMode = user.GetAttributeValue<OptionSetValue>("accessmode");
            var isDisabled = user.GetAttributeValue<bool?>("isdisabled") ?? false;
            var applicationId = user.GetAttributeValue<Guid?>("applicationid");
            return accessMode != null
                && accessMode.Value == 0
                && !isDisabled
                && !(applicationId.HasValue && applicationId.Value != Guid.Empty);
        }

        /// <summary>ユーザーを検索で読む。無ければnull。</summary>
        public static Entity? RetrieveUser(IOrganizationService service, Guid userId, params string[] columns)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (userId == Guid.Empty) return null;
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet(columns),
                TopCount = 1,
            };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        /// <summary>共有設定の行の所有者にできるか。行を読めない人を所有者にすると、Dataverseは作成を拒否する（2026-09-29 開発環境で確認：Read Privilege Check For Owner failed）。</summary>
        public static bool CanOwnShareSettingRow(IOrganizationService service, Guid userId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            // チーム経由のロールも含めて調べる（RetrieveUserPrivilegesはチーム経由の深さを正しく返さないため使わない）。
            var response = (RetrieveUserPrivilegeByPrivilegeNameResponse)service.Execute(new RetrieveUserPrivilegeByPrivilegeNameRequest
            {
                UserId = userId,
                PrivilegeName = ShareSettingReadPrivilege,
            });
            return response.RolePrivileges != null && response.RolePrivileges.Length > 0;
        }

        public static PartnerMainOwnerChangePlan Plan(
            IOrganizationService service,
            Guid partnerId,
            Guid newMainOwnerId,
            Guid serverCallerId,
            Guid approverTeamId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (partnerId == Guid.Empty) throw new InvalidPluginExecutionException("主担当を変える取引先がありません。");
            if (serverCallerId == Guid.Empty) throw new InvalidPluginExecutionException("主担当の変更の実行者がありません。");
            if (approverTeamId == Guid.Empty) throw new InvalidPluginExecutionException("承認者Teamが設定されていません。");

            var plan = new PartnerMainOwnerChangePlan { PartnerId = partnerId, NewMainOwnerId = newMainOwnerId };
            var partner = service.Retrieve(
                PartnerStandardCreateContract.EntityName,
                partnerId,
                new ColumnSet(PartnerStandardCreateContract.MainOwnerAttribute, "ownerid"));
            var current = partner.GetAttributeValue<EntityReference>(PartnerStandardCreateContract.MainOwnerAttribute);
            if (current != null && current.Id == newMainOwnerId) return Fail(plan, PartnerMainOwnerChangeError.SameAsCurrent);
            if (!IsShareableUser(service, newMainOwnerId)) return Fail(plan, PartnerMainOwnerChangeError.UserNotShareable);

            // 取引先の所有者なら共有は要らない。ロール・チーム経由で編集できる人には行を作る（監査M-1・2026-09-29ユーザー決定）。
            var partnerOwner = partner.GetAttributeValue<EntityReference>("ownerid");
            var partnerOwnerUserId = partnerOwner != null && string.Equals(partnerOwner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
                ? partnerOwner.Id
                : Guid.Empty;
            if (partnerOwnerUserId == newMainOwnerId) return plan;

            var phase = PartnerShareSettingRepository.ResolvePhase(service, partnerId);
            var existing = PartnerShareSettingRepository.RetrieveByPartner(service, partnerId);
            var caller = new PartnerShareCaller
            {
                UserId = serverCallerId,
                IsApprover = true,
                EffectiveAccess = PartnerShareAccessLevel.Write,
            };
            var desired = new PartnerShareSettingInput
            {
                PartnerId = partnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = newMainOwnerId,
                AccessLevel = PartnerShareAccessLevel.Write,
                RequestKey = RequestKeyPrefix + partnerId.ToString("D") + "-" + Guid.NewGuid().ToString("N"),
            };

            var row = existing.FirstOrDefault(record =>
                record.State == PartnerShareSettingState.Active
                && record.PrincipalKind == PartnerSharePrincipalKind.User
                && record.PrincipalId == newMainOwnerId);
            if (row != null && row.AccessLevel == PartnerShareAccessLevel.Write) return plan;
            if (row != null)
            {
                var direct = PartnerShareSettingRepository.RetrieveDirectShareState(
                    service,
                    partnerId,
                    PartnerSharePrincipalKind.User,
                    newMainOwnerId,
                    row.IsManagedProjection
                        ? PartnerShareDirectShareProvenance.ManagedByPartnerLedger
                        : PartnerShareDirectShareProvenance.UntrackedOrExternal);
                var projection = PartnerShareSettingProjection.PlanUpdate(phase, caller, row, desired, existing, direct);
                if (!projection.IsAllowed) return Fail(plan, PartnerMainOwnerChangeError.ShareCannotBeArranged);
                plan.RowToUpdate = PartnerShareSettingRecordFactory.BuildUpdate(row.Id, desired);
                plan.ProjectionPlan = projection;
                return plan;
            }

            var directShare = PartnerShareSettingRepository.RetrieveDirectShareState(
                service,
                partnerId,
                PartnerSharePrincipalKind.User,
                newMainOwnerId,
                PartnerShareDirectShareProvenance.UntrackedOrExternal);
            var createPlan = PartnerShareSettingProjection.PlanCreate(phase, caller, desired, existing, directShare, approverTeamId);
            if (!createPlan.IsAllowed || createPlan.IsReplay) return Fail(plan, PartnerMainOwnerChangeError.ShareCannotBeArranged);
            // 行の所有者は取引先の所有者（登録者）。登録者は自分が所有する行しか読めないため、共有設定タブで見えるようにする。
            // 登録者が無効・共有設定を読めない（異動でロールを外された等）なら、新しい主担当を所有者にする（監査H-1・2026-09-29ユーザー決定）。
            Guid rowOwnerId;
            if (partnerOwnerUserId != Guid.Empty
                && IsShareableUser(service, partnerOwnerUserId)
                && CanOwnShareSettingRow(service, partnerOwnerUserId))
            {
                rowOwnerId = partnerOwnerUserId;
            }
            else if (CanOwnShareSettingRow(service, newMainOwnerId))
            {
                rowOwnerId = newMainOwnerId;
            }
            else
            {
                return Fail(plan, PartnerMainOwnerChangeError.RowOwnerUnavailable);
            }

            var newRow = PartnerShareSettingRecordFactory.BuildCreate(phase, desired, approverTeamId, createPlan.IsManagedProjection);
            newRow["ownerid"] = new EntityReference("systemuser", rowOwnerId);
            plan.RowToCreate = newRow;
            plan.ProjectionPlan = createPlan;
            return plan;
        }

        /// <summary>計画どおりに書く。行を先に書き、そのあと共有する（親の共有が子の行へ連鎖するように）。</summary>
        public static void Execute(IOrganizationService service, PartnerMainOwnerChangePlan plan)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsValid) throw new InvalidPluginExecutionException("主担当の変更の計画が許可されていません: " + plan.Error);

            if (plan.RowToCreate != null)
            {
                var create = new CreateRequest { Target = plan.RowToCreate };
                create.Parameters[ApprovalServerWriteBypass.ParameterName] = ApprovalServerWriteBypass.ProjectionCreateStepIds;
                service.Execute(create);
            }
            if (plan.RowToUpdate != null)
            {
                var update = new UpdateRequest { Target = plan.RowToUpdate };
                update.Parameters[ApprovalServerWriteBypass.ParameterName] = ApprovalServerWriteBypass.ProjectionUpdateStepIds;
                service.Execute(update);
            }
            if (plan.ProjectionPlan != null)
            {
                PartnerShareSettingProjectionExecutor.Execute(service, plan.ProjectionPlan);
            }
        }

        private static PartnerMainOwnerChangePlan Fail(PartnerMainOwnerChangePlan plan, PartnerMainOwnerChangeError error)
        {
            plan.Error = error;
            plan.RowToCreate = null;
            plan.RowToUpdate = null;
            plan.ProjectionPlan = null;
            return plan;
        }
    }
}
