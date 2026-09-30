using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    /// <summary>
    /// 新規取引先の初期共有を、登録と同時に完了する（2026-09-28ユーザー決定）。
    /// 承認者Teamの保護行と主担当の行を作り、共有し、共有設定状態を「設定完了」にする。
    /// 旧pl_AccessGrantの互換Bootstrapは2026-09-27に削除した。
    /// </summary>
    public class PartnerAccessBootstrapPluginTests
    {
        private const string BypassParameterName = "BypassBusinessLogicExecutionStepIds";
        private const string ProjectionCreatePreStepId = "5df0f894-15b0-f111-aaac-e4fb1eff79c7";
        private const string ProjectionCreatePostStepId = "a3fd50d3-20b0-f111-aaac-e4fb1eff79c7";

        private static readonly Guid PartnerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid RegistrantId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        private static readonly Guid MainOwnerId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        private static readonly Guid ApproverTeamId = Guid.Parse("40000000-0000-0000-0000-000000000004");

        [Fact]
        public void 保護行と主担当の行を作ってから共有し_最後に設定完了にする()
        {
            var service = SeededService();
            var executedBeforeStatusUpdate = -1;
            service.BeforeUpdate = entity =>
            {
                if (entity.LogicalName == "pl_partner") executedBeforeStatusUpdate = service.ExecutedRequests.Count;
            };

            Complete(service, MainOwnerId);

            var kinds = service.ExecutedRequests.Select(Describe).ToList();
            Assert.Equal(new[] { "Create:Team", "Create:User", "Grant:systemuser", "Grant:team" }, kinds);
            // 状態の更新は、行と共有がそろった後に一度だけ行う。
            Assert.Equal(4, executedBeforeStatusUpdate);
            Assert.Equal(
                PartnerStandardCreateContract.ReadyShareSetupStatusCode,
                service.Retrieve("pl_partner", PartnerId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true))
                    .GetAttributeValue<OptionSetValue>(PartnerStandardCreateContract.ShareSetupStatusAttribute)!.Value);
        }

        [Fact]
        public void 承認者Teamの保護行は手動の経路と同じ中身で_投影の2つのStepだけを飛ばして作る()
        {
            var service = SeededService();

            Complete(service, MainOwnerId);

            var request = CreateRequests(service).Single(r => r.Target.Contains("pl_teamprincipallookup"));
            Assert.Equal(ProjectionCreatePreStepId + "," + ProjectionCreatePostStepId, request.Parameters[BypassParameterName]);
            var row = request.Target;
            Assert.Equal(PartnerId, row.GetAttributeValue<EntityReference>("pl_partnerlookup")!.Id);
            Assert.Equal(ApproverTeamId, row.GetAttributeValue<EntityReference>("pl_teamprincipallookup")!.Id);
            Assert.Equal(PartnerShareSettingRecordFactory.PrincipalKindTeamOption, row.GetAttributeValue<OptionSetValue>("pl_principalkindcode")!.Value);
            Assert.Equal(PartnerShareSettingRecordFactory.AccessLevelReadOption, row.GetAttributeValue<OptionSetValue>("pl_accesslevelcode")!.Value);
            Assert.Equal(PartnerShareSettingRecordFactory.SettingStateActiveOption, row.GetAttributeValue<OptionSetValue>("pl_settingstatuscode")!.Value);
            Assert.True(row.GetAttributeValue<bool>("pl_isprotectedmanagementpath"));
            Assert.True(row.GetAttributeValue<bool>("pl_ismanagedprojection"));
            AssertBootstrapKey("PartnerLedger-Bootstrap-Approver-", row.GetAttributeValue<string>("pl_requestkey"));
            Assert.Equal(
                PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = ApproverTeamId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "any",
                }),
                row.GetAttributeValue<string>("pl_contenthash"));
            // 所有者は登録者（共有設定タブで登録者が読めるようにする）。
            Assert.Equal(RegistrantId, row.GetAttributeValue<EntityReference>("ownerid")!.Id);
            Assert.Equal("systemuser", row.GetAttributeValue<EntityReference>("ownerid")!.LogicalName);
        }

        [Fact]
        public void 主担当の行は保護しない編集の共有で_同じく投影の2つのStepだけを飛ばして作る()
        {
            var service = SeededService();

            Complete(service, MainOwnerId);

            var request = CreateRequests(service).Single(r => r.Target.Contains("pl_userprincipallookup"));
            Assert.Equal(ProjectionCreatePreStepId + "," + ProjectionCreatePostStepId, request.Parameters[BypassParameterName]);
            var row = request.Target;
            Assert.Equal(MainOwnerId, row.GetAttributeValue<EntityReference>("pl_userprincipallookup")!.Id);
            Assert.Equal(PartnerShareSettingRecordFactory.PrincipalKindUserOption, row.GetAttributeValue<OptionSetValue>("pl_principalkindcode")!.Value);
            Assert.Equal(PartnerShareSettingRecordFactory.AccessLevelWriteOption, row.GetAttributeValue<OptionSetValue>("pl_accesslevelcode")!.Value);
            Assert.False(row.GetAttributeValue<bool>("pl_isprotectedmanagementpath"));
            Assert.True(row.GetAttributeValue<bool>("pl_ismanagedprojection"));
            AssertBootstrapKey("PartnerLedger-Bootstrap-MainOwner-", row.GetAttributeValue<string>("pl_requestkey"));
            Assert.Equal(RegistrantId, row.GetAttributeValue<EntityReference>("ownerid")!.Id);

            var grant = service.ExecutedRequests.OfType<GrantAccessRequest>().Single(g => g.PrincipalAccess.Principal.LogicalName == "systemuser");
            Assert.Equal(MainOwnerId, grant.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, grant.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void 承認者TeamへはReadだけを共有する()
        {
            var service = SeededService();

            Complete(service, MainOwnerId);

            var grant = service.ExecutedRequests.OfType<GrantAccessRequest>().Single(g => g.PrincipalAccess.Principal.LogicalName == "team");
            Assert.Equal(PartnerId, grant.Target.Id);
            Assert.Equal(ApproverTeamId, grant.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess, grant.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void 主担当が登録者本人なら主担当の行と共有は作らない()
        {
            var service = SeededService();

            Complete(service, RegistrantId);

            Assert.Equal(new[] { "Create:Team", "Grant:team" }, service.ExecutedRequests.Select(Describe).ToArray());
        }

        [Fact]
        public void 設定完了への更新にはバイパスを付けず_状態の列だけを送る()
        {
            var service = SeededService();
            Entity? statusUpdate = null;
            service.BeforeUpdate = entity =>
            {
                if (entity.LogicalName == "pl_partner") statusUpdate = entity;
            };

            Complete(service, MainOwnerId);

            // UpdateRequest（任意パラメータを付けられる形）を使わず、共有状態ガードに検査させる。
            Assert.DoesNotContain(service.ExecutedRequests, r => r is UpdateRequest);
            Assert.NotNull(statusUpdate);
            Assert.Equal(new[] { PartnerStandardCreateContract.ShareSetupStatusAttribute }, statusUpdate!.Attributes.Keys.ToArray());
            Assert.Equal(PartnerId, statusUpdate.Id);
        }

        [Fact]
        public void 要求キーは毎回違い_他の人が先に取って登録を妨害できない()
        {
            // 監査#1：要求キーの代替キーは環境全体で一意。固定値だと、他の人が自分の行に同じキーを
            // 先に付けて、この取引先の登録を失敗させられた。
            var first = SeededService();
            var second = SeededService();

            Complete(first, MainOwnerId);
            Complete(second, MainOwnerId);

            var firstKeys = CreateRequests(first).Select(r => r.Target.GetAttributeValue<string>("pl_requestkey")).ToArray();
            var secondKeys = CreateRequests(second).Select(r => r.Target.GetAttributeValue<string>("pl_requestkey")).ToArray();
            Assert.Empty(firstKeys.Intersect(secondKeys));
            Assert.All(firstKeys.Concat(secondKeys), key => Assert.True(key.Length <= PartnerShareSettingContract.RequestKeyMaxLength));
        }

        [Theory]
        [InlineData(3, false, false)]  // Microsoftのサポート用アカウント
        [InlineData(4, false, false)]  // 非対話型
        [InlineData(0, true, false)]   // 無効
        [InlineData(0, false, true)]   // アプリケーションユーザー
        public void 主担当が通常の有効な利用者でなければ_何も書かずに分かる文言で拒否する(int accessMode, bool disabled, bool application)
        {
            var service = SeededService();
            var user = service.Retrieve("systemuser", MainOwnerId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            user["accessmode"] = new OptionSetValue(accessMode);
            user["isdisabled"] = disabled;
            if (application) user["applicationid"] = Guid.NewGuid();

            var error = Assert.Throws<InvalidPluginExecutionException>(() => Complete(service, MainOwnerId));

            Assert.Contains("主担当", error.Message);
            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 入口では環境変数を先に読み_登録者は呼び出した人_主担当は保存済みの行から取る()
        {
            var service = SeededService();
            SeedApproverTeamSetting(service);
            service.Retrieve("pl_partner", PartnerId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true))
                [PartnerStandardCreateContract.MainOwnerAttribute] = new EntityReference("systemuser", MainOwnerId);

            Execute(service, RegistrantId, PartnerId);

            var rows = CreateRequests(service).Select(r => r.Target).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => Assert.Equal(RegistrantId, row.GetAttributeValue<EntityReference>("ownerid")!.Id));
            Assert.Equal(MainOwnerId, rows.Single(r => r.Contains("pl_userprincipallookup")).GetAttributeValue<EntityReference>("pl_userprincipallookup")!.Id);
        }

        [Fact]
        public void 入口で環境変数が不正なら何も書かない()
        {
            var service = SeededService();

            Assert.Throws<InvalidPluginExecutionException>(() => Execute(service, RegistrantId, PartnerId));

            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 入口で作成したIDが無ければ何も書かない()
        {
            var service = SeededService();
            SeedApproverTeamSetting(service);

            Assert.Throws<InvalidPluginExecutionException>(() => Execute(service, RegistrantId, partnerId: null));

            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 承認者Teamが存在しない場合は何も書かない()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId));

            Assert.Throws<InvalidPluginExecutionException>(() => Complete(service, MainOwnerId));

            Assert.Empty(service.ExecutedRequests);
        }

        [Theory]
        [InlineData("partner")]
        [InlineData("registrant")]
        [InlineData("mainOwner")]
        [InlineData("team")]
        public void IDが欠けていれば何も書かない(string missing)
        {
            var service = SeededService();

            Assert.Throws<InvalidPluginExecutionException>(() => PartnerAccessBootstrapPlugin.CompleteInitialShareSetup(
                service,
                missing == "partner" ? Guid.Empty : PartnerId,
                missing == "registrant" ? Guid.Empty : RegistrantId,
                missing == "mainOwner" ? Guid.Empty : MainOwnerId,
                missing == "team" ? Guid.Empty : ApproverTeamId));

            Assert.Empty(service.ExecutedRequests);
        }

        [Theory]
        [InlineData(ProjectionCreatePreStepId)]
        [InlineData(ProjectionCreatePostStepId)]
        public void 飛ばすStep_IDはSolution内の共有設定投影のCreate_Stepと一致する(string stepId)
        {
            const string createMessageId = "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8";
            var path = Path.Combine(FindRepositoryRoot(), "solutions", "PartnerLedger", "SdkMessageProcessingSteps", "{" + stepId + "}.xml");
            Assert.True(File.Exists(path), "Solution内にStepが無い: " + stepId);

            var step = XDocument.Load(path).Root!;
            Assert.Equal(createMessageId, step.Element("SdkMessageId")!.Value);
            Assert.StartsWith("PartnerLedger.Plugins.PartnerShareSettingProjectionPlugin,", step.Element("PluginTypeName")!.Value);
            Assert.Equal("pl_partnersharesetting", step.Element("PrimaryEntity")!.Value);
            Assert.Contains(stepId, PartnerAccessBootstrapPlugin.ProjectionCreateStepIds);
        }

        private static FakeOrganizationService SeededService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.InitialShareSetupStatusCode),
            });
            service.Seed(new Entity("team", ApproverTeamId));
            service.Seed(new Entity("systemuser", MainOwnerId)
            {
                ["accessmode"] = new OptionSetValue(0),
                ["isdisabled"] = false,
            });
            return service;
        }

        private static void AssertBootstrapKey(string prefix, string? key)
        {
            Assert.NotNull(key);
            var fixedPart = prefix + PartnerId.ToString("D") + "-";
            Assert.StartsWith(fixedPart, key);
            Assert.True(key!.Length > fixedPart.Length);
            Assert.True(key.Length <= PartnerShareSettingContract.RequestKeyMaxLength);
        }

        private static void SeedApproverTeamSetting(FakeOrganizationService service)
        {
            var definitionId = Guid.NewGuid();
            service.Seed(new Entity("environmentvariabledefinition", definitionId)
            {
                ["schemaname"] = PartnerLedgerEnvironmentVariableNames.ApproverTeamId,
            });
            service.Seed(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", definitionId),
                ["value"] = ApproverTeamId.ToString("D"),
            });
            service.Retrieve("team", ApproverTeamId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true))["teamtype"] = new OptionSetValue(3);
        }

        private static void Execute(FakeOrganizationService service, Guid initiatingUserId, Guid? partnerId)
        {
            var context = System.Reflection.DispatchProxy.Create<IPluginExecutionContext, PartnerShareSetupStatusGuardTests.PropertyProxy>();
            var contextValues = ((PartnerShareSetupStatusGuardTests.PropertyProxy)context).Values;
            contextValues[nameof(IPluginExecutionContext.MessageName)] = "Create";
            contextValues[nameof(IPluginExecutionContext.PrimaryEntityName)] = PartnerStandardCreateContract.EntityName;
            contextValues[nameof(IPluginExecutionContext.Stage)] = 40;
            contextValues[nameof(IPluginExecutionContext.InitiatingUserId)] = initiatingUserId;
            var outputs = new ParameterCollection();
            if (partnerId.HasValue) outputs["id"] = partnerId.Value;
            contextValues[nameof(IPluginExecutionContext.OutputParameters)] = outputs;

            var local = System.Reflection.DispatchProxy.Create<ILocalPluginContext, PartnerShareSetupStatusGuardTests.PropertyProxy>();
            var localValues = ((PartnerShareSetupStatusGuardTests.PropertyProxy)local).Values;
            localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = context;
            localValues[nameof(ILocalPluginContext.SystemService)] = service;

            var method = typeof(PartnerAccessBootstrapPlugin).GetMethod(
                "ExecuteDataversePlugin",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            try
            {
                method!.Invoke(new PartnerAccessBootstrapPlugin("", ""), new object[] { local });
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            }
        }

        private static void Complete(FakeOrganizationService service, Guid mainOwnerId)
            => PartnerAccessBootstrapPlugin.CompleteInitialShareSetup(service, PartnerId, RegistrantId, mainOwnerId, ApproverTeamId);

        private static CreateRequest[] CreateRequests(FakeOrganizationService service)
            => service.ExecutedRequests.OfType<CreateRequest>().ToArray();

        private static string Describe(OrganizationRequest request) => request switch
        {
            CreateRequest create => "Create:" + (create.Target.Contains("pl_teamprincipallookup") ? "Team" : "User"),
            GrantAccessRequest grant => "Grant:" + grant.PrincipalAccess.Principal.LogicalName,
            _ => request.GetType().Name,
        };

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "solutions", "PartnerLedger")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つからない。");
        }
    }
}
