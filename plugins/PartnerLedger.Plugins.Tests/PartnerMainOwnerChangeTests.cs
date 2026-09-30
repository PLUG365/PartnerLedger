using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    /// <summary>
    /// 主担当の変更（2026-09-29ユーザー決定）：新しい主担当に編集の共有を整える。
    /// 共有の判断は既存の投影の計画（PlanCreate・PlanUpdate）に任せる。前の主担当の共有は変えない。
    /// </summary>
    public class PartnerMainOwnerChangeTests
    {
        private static readonly Guid PartnerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid PartnerOwnerId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        private static readonly Guid CurrentMainOwnerId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        private static readonly Guid NewMainOwnerId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        private static readonly Guid ApproverTeamId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        private static readonly Guid ServerCallerId = Guid.Parse("60000000-0000-0000-0000-000000000006");

        [Fact]
        public void 共有も行も無ければ_編集の行を作ってから共有する()
        {
            var service = SeededService();

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var before = service.ExecutedRequests.Count;
            PartnerMainOwnerChange.Execute(service, plan);
            var writes = service.ExecutedRequests.Skip(before).ToList();

            var create = Assert.IsType<CreateRequest>(writes[0]);
            Assert.Equal(ApprovalServerWriteBypass.ProjectionCreateStepIds, create.Parameters[ApprovalServerWriteBypass.ParameterName]);
            var row = create.Target;
            Assert.Equal(NewMainOwnerId, row.GetAttributeValue<EntityReference>("pl_userprincipallookup")!.Id);
            Assert.Equal(PartnerShareSettingRecordFactory.AccessLevelWriteOption, row.GetAttributeValue<OptionSetValue>("pl_accesslevelcode")!.Value);
            Assert.False(row.GetAttributeValue<bool>("pl_isprotectedmanagementpath"));
            Assert.True(row.GetAttributeValue<bool>("pl_ismanagedprojection"));
            Assert.Equal(PartnerOwnerId, row.GetAttributeValue<EntityReference>("ownerid")!.Id);
            Assert.StartsWith("PartnerLedger-MainOwner-" + PartnerId.ToString("D") + "-", row.GetAttributeValue<string>("pl_requestkey"));
            var grant = Assert.IsType<GrantAccessRequest>(writes[1]);
            Assert.Equal(NewMainOwnerId, grant.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, grant.PrincipalAccess.AccessMask);
            Assert.Equal(2, writes.Count);
        }

        [Fact]
        public void 閲覧の行があれば_行と共有を編集へ上げる()
        {
            var service = SeededService();
            var rowId = SeedUserRow(service, PartnerShareAccessLevel.Read, managed: true);
            service.RetrievedSharedPrincipalAccesses = new[] { UserShare(AccessRights.ReadAccess) };

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var before = service.ExecutedRequests.Count;
            PartnerMainOwnerChange.Execute(service, plan);
            var writes = service.ExecutedRequests.Skip(before).ToList();

            var update = Assert.IsType<UpdateRequest>(writes[0]);
            Assert.Equal(ApprovalServerWriteBypass.ProjectionUpdateStepIds, update.Parameters[ApprovalServerWriteBypass.ParameterName]);
            Assert.Equal(rowId, update.Target.Id);
            Assert.Equal(PartnerShareSettingRecordFactory.AccessLevelWriteOption, update.Target.GetAttributeValue<OptionSetValue>("pl_accesslevelcode")!.Value);
            Assert.Equal(64, update.Target.GetAttributeValue<string>("pl_contenthash")!.Length);
            var modify = Assert.IsType<ModifyAccessRequest>(writes[1]);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, modify.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void ロールやチーム経由で編集できても_行を作って共有する()
        {
            // 監査M-1・2026-09-29ユーザー決定「取引先の所有者以外には作る」。ロールが外れても主担当が編集でき、共有設定タブにも出る。
            var service = SeededService();
            service.RetrievedPrincipalAccessRights = AccessRights.ReadAccess | AccessRights.WriteAccess;

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var writes = ExecuteAndCollect(service, plan);

            Assert.IsType<CreateRequest>(writes[0]);
            Assert.IsType<GrantAccessRequest>(writes[1]);
        }

        [Fact]
        public void 取引先の所有者を主担当にするなら何もしない()
        {
            var service = SeededService();

            var plan = PartnerMainOwnerChange.Plan(service, PartnerId, PartnerOwnerId, ServerCallerId, ApproverTeamId);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);

            Assert.Empty(ExecuteAndCollect(service, plan));
        }

        [Fact]
        public void 有効な編集の行がある人なら何もしない()
        {
            var service = SeededService();
            SeedUserRow(service, PartnerShareAccessLevel.Write, managed: true);
            service.RetrievedSharedPrincipalAccesses = new[] { UserShare(AccessRights.ReadAccess | AccessRights.WriteAccess) };

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);

            Assert.Empty(ExecuteAndCollect(service, plan));
        }

        [Fact]
        public void 行の所有者にできるかは_共有設定を読む権限で調べる()
        {
            var service = SeededService();

            Plan(service);

            var checks = service.ExecutedRequests.OfType<RetrieveUserPrivilegeByPrivilegeNameRequest>().ToList();
            Assert.Contains(checks, check => check.UserId == PartnerOwnerId && check.PrivilegeName == "prvReadpl_PartnerShareSetting");
        }

        [Fact]
        public void 登録者が無効なら_行の所有者は新しい主担当にする()
        {
            // 監査H-1・2026-09-29ユーザー決定「登録者が使えなければ新しい主担当」。
            var service = SeededService();
            service.Retrieve("systemuser", PartnerOwnerId, new ColumnSet(true))["isdisabled"] = true;

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var writes = ExecuteAndCollect(service, plan);

            var create = Assert.IsType<CreateRequest>(writes[0]);
            Assert.Equal(NewMainOwnerId, create.Target.GetAttributeValue<EntityReference>("ownerid")!.Id);
            Assert.IsType<GrantAccessRequest>(writes[1]);
        }

        [Fact]
        public void 登録者が共有設定を読めなければ_行の所有者は新しい主担当にする()
        {
            // 異動でロールを外された登録者。行の所有者にするとDataverseが作成を拒否する（2026-09-29 開発環境で確認）。
            var service = SeededService();
            service.UserHasPrivilege = (userId, _) => userId != PartnerOwnerId;

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var writes = ExecuteAndCollect(service, plan);

            var create = Assert.IsType<CreateRequest>(writes[0]);
            Assert.Equal(NewMainOwnerId, create.Target.GetAttributeValue<EntityReference>("ownerid")!.Id);
        }

        [Fact]
        public void 登録者も新しい主担当も共有設定を読めなければ_何も書かずに拒否する()
        {
            var service = SeededService();
            service.UserHasPrivilege = (_, _) => false;

            var plan = Plan(service);

            Assert.Equal(PartnerMainOwnerChangeError.RowOwnerUnavailable, plan.Error);
            Assert.Null(plan.RowToCreate);
            Assert.DoesNotContain(service.ExecutedRequests, request => request is CreateRequest || request is UpdateRequest || request is GrantAccessRequest || request is ModifyAccessRequest);
        }

        [Fact]
        public void 閲覧の行を上げるだけなら_登録者が使えなくても行の所有者は変えない()
        {
            var service = SeededService();
            service.UserHasPrivilege = (_, _) => false;
            SeedUserRow(service, PartnerShareAccessLevel.Read, managed: true);
            service.RetrievedSharedPrincipalAccesses = new[] { UserShare(AccessRights.ReadAccess) };

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var writes = ExecuteAndCollect(service, plan);

            var update = Assert.IsType<UpdateRequest>(writes[0]);
            Assert.False(update.Target.Attributes.Contains("ownerid"));
        }

        [Fact]
        public void 存在しないユーザーのIDは_本体の例外にせず通常の利用者でないとする()
        {
            // 監査L-3。本番のRetrieveは見つからないとFaultExceptionを投げ、同期プラグインの中では取引全体が失敗扱いになる。
            // 読み取りは検索（0件）で行い、Retrieveは使わない。
            var service = SeededService();
            service.BeforeRetrieve = (entityName, _) => entityName == "systemuser"
                ? new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault(), new System.ServiceModel.FaultReason("systemuser does not exist"))
                : null;
            var unknown = Guid.NewGuid();

            Assert.False(PartnerMainOwnerChange.IsShareableUser(service, unknown));
            var plan = PartnerMainOwnerChange.Plan(service, PartnerId, unknown, ServerCallerId, ApproverTeamId);
            Assert.Equal(PartnerMainOwnerChangeError.UserNotShareable, plan.Error);
            Assert.Null(ApprovalChangeSummary.RetrieveUserName(service, unknown));
        }

        [Fact]
        public void 行の無い外部の共有だけがあれば_権利を足し_PL管理外の行にする()
        {
            // 既存の共有を縮めない（あとでこの行を外しても元の共有は残る）。
            var service = SeededService();
            service.RetrievedSharedPrincipalAccesses = new[] { UserShare(AccessRights.ReadAccess) };

            var plan = Plan(service);
            Assert.Equal(PartnerMainOwnerChangeError.None, plan.Error);
            var before = service.ExecutedRequests.Count;
            PartnerMainOwnerChange.Execute(service, plan);
            var writes = service.ExecutedRequests.Skip(before).ToList();

            var create = Assert.IsType<CreateRequest>(writes[0]);
            Assert.False(create.Target.GetAttributeValue<bool>("pl_ismanagedprojection"));
            var modify = Assert.IsType<ModifyAccessRequest>(writes[1]);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, modify.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void 投影の計画が保留を返したら_何も書かずに拒否する()
        {
            // PL管理の閲覧の行に、管理外の権利（削除）まで付いた直接共有がある。縮めずに上げられないので保留。
            var service = SeededService();
            SeedUserRow(service, PartnerShareAccessLevel.Read, managed: true);
            service.RetrievedSharedPrincipalAccesses = new[] { UserShare(AccessRights.ReadAccess | AccessRights.DeleteAccess) };

            var plan = Plan(service);

            Assert.Equal(PartnerMainOwnerChangeError.ShareCannotBeArranged, plan.Error);
            Assert.DoesNotContain(service.ExecutedRequests, request => request is CreateRequest || request is UpdateRequest || request is GrantAccessRequest || request is ModifyAccessRequest);
        }

        [Theory]
        [InlineData(3, false, false)]
        [InlineData(4, false, false)]
        [InlineData(0, true, false)]
        [InlineData(0, false, true)]
        public void 新しい主担当が通常の有効な利用者でなければ拒否する(int accessMode, bool disabled, bool application)
        {
            var service = SeededService();
            var user = service.Retrieve("systemuser", NewMainOwnerId, new ColumnSet(true));
            user["accessmode"] = new OptionSetValue(accessMode);
            user["isdisabled"] = disabled;
            if (application) user["applicationid"] = Guid.NewGuid();

            Assert.Equal(PartnerMainOwnerChangeError.UserNotShareable, Plan(service).Error);
        }

        [Fact]
        public void 今と同じ人への変更は拒否する()
        {
            var service = SeededService();

            var plan = PartnerMainOwnerChange.Plan(service, PartnerId, CurrentMainOwnerId, ServerCallerId, ApproverTeamId);

            Assert.Equal(PartnerMainOwnerChangeError.SameAsCurrent, plan.Error);
        }

        private static PartnerMainOwnerChangePlan Plan(FakeOrganizationService service)
            => PartnerMainOwnerChange.Plan(service, PartnerId, NewMainOwnerId, ServerCallerId, ApproverTeamId);

        private static FakeOrganizationService SeededService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
                ["ownerid"] = new EntityReference("systemuser", PartnerOwnerId),
                [PartnerStandardCreateContract.MainOwnerAttribute] = new EntityReference("systemuser", CurrentMainOwnerId),
            });
            service.Seed(new Entity("systemuser", NewMainOwnerId)
            {
                ["accessmode"] = new OptionSetValue(0),
                ["isdisabled"] = false,
            });
            service.Seed(new Entity("systemuser", PartnerOwnerId)
            {
                ["accessmode"] = new OptionSetValue(0),
                ["isdisabled"] = false,
            });
            return service;
        }

        private static System.Collections.Generic.List<OrganizationRequest> ExecuteAndCollect(FakeOrganizationService service, PartnerMainOwnerChangePlan plan)
        {
            var before = service.ExecutedRequests.Count;
            PartnerMainOwnerChange.Execute(service, plan);
            return service.ExecutedRequests.Skip(before).ToList();
        }

        private static Guid SeedUserRow(FakeOrganizationService service, PartnerShareAccessLevel level, bool managed)
        {
            var row = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.Ready,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = NewMainOwnerId,
                    AccessLevel = level,
                    RequestKey = "existing-" + Guid.NewGuid().ToString("N"),
                },
                ApproverTeamId,
                isManagedProjection: managed);
            row.Id = Guid.NewGuid();
            row["createdby"] = new EntityReference("systemuser", PartnerOwnerId);
            row["statecode"] = new OptionSetValue(0);
            service.Seed(row);
            return row.Id;
        }

        private static PrincipalAccess UserShare(AccessRights rights)
            => new PrincipalAccess { Principal = new EntityReference("systemuser", NewMainOwnerId), AccessMask = rights };
    }
}
