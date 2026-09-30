using System;
using Microsoft.Crm.Sdk.Messages;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalReflectionApplyServiceTests
    {
        private static readonly Guid RequesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid SubmissionVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ApprovalLinkId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        private static readonly Guid PartnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid ContractId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        private static readonly Guid SettingsId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        private const string DefaultPolicyJson
            = "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}";

        [Fact]
        public void 会社名変更を正規化値とともに反映し申請と提出版を完了する()
        {
            var service = SeedPartnerApply();
            var reflectedAt = new DateTime(2026, 9, 13, 6, 0, 0, DateTimeKind.Utc);

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                reflectedAt);

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal(PartnerId, result.TargetId);
            Assert.Equal("pl_partner", result.TargetEntityName);
            Assert.Equal(ApprovalRequestStatus.反映済み, result.RequestStatus);
            Assert.Equal(ApprovalSubmissionVersionStatus.反映済み, result.SubmissionVersionStatus);

            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("株式会社　ＡＢＣ㈱", partner.GetAttributeValue<string>("pl_name"));
            Assert.Equal("ABC", partner.GetAttributeValue<string>("pl_normalizedname"));
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApprovalRequestStatus.反映済み.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.反映済み.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Equal(reflectedAt, version.GetAttributeValue<DateTime>("pl_fixedat"));
            // 反映待ちへの遷移2件、対象行、提出版、申請、Linkの6件。
            Assert.Equal(6, service.ExecutedRequests.OfType<UpdateRequest>().Count());
            Assert.All(
                service.ExecutedRequests.OfType<UpdateRequest>(),
                update => Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior));
        }

        [Fact]
        public void 反映の内部更新は対象ごとのガードStepだけを飛ばしLinkには付けない()
        {
            var service = SeedPartnerApply();

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            var updates = service.ExecutedRequests.OfType<UpdateRequest>().ToList();
            Assert.Equal(6, updates.Count);
            foreach (var update in updates)
            {
                var hasBypass = update.Parameters.Contains("BypassBusinessLogicExecutionStepIds");
                switch (update.Target.LogicalName)
                {
                    case "pl_partner":
                        Assert.Equal("bed8d8a4-1db5-f111-aaad-e4fb1eff79c7", update.Parameters["BypassBusinessLogicExecutionStepIds"]);
                        break;
                    case "pl_request":
                        Assert.Equal("7379e607-1ab1-f111-aaac-e4fb1eff79c7,21c4bac4-26b1-f111-aaac-e4fb1eff79c7", update.Parameters["BypassBusinessLogicExecutionStepIds"]);
                        break;
                    case "pl_submissionversion":
                        Assert.Equal("7a79e607-1ab1-f111-aaac-e4fb1eff79c7", update.Parameters["BypassBusinessLogicExecutionStepIds"]);
                        break;
                    case "pl_approvallink":
                        Assert.False(hasBypass);
                        break;
                    default:
                        Assert.Fail("想定外の更新先: " + update.Target.LogicalName);
                        break;
                }
            }
        }

        [Fact]
        public void 同じLinkの完了済み反映は更新なしで再送できる()
        {
            var service = SeedPartnerApply();
            var first = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);
            var updateCount = service.ExecutedRequests.OfType<UpdateRequest>().Count();

            var replay = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow.AddMinutes(1));

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(updateCount, service.ExecutedRequests.OfType<UpdateRequest>().Count());
        }

        [Fact]
        public void 設定版が変わっていたら反映失敗として記録し無効な種別は従来どおり拒否する()
        {
            var policyMismatch = SeedPartnerApply(requestPolicyVersion: "2");
            var mismatch = ApprovalReflectionApplyService.ApplyStandardApproved(
                policyMismatch,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);
            AssertReflectionFailureRecorded(policyMismatch, mismatch, ApprovalReflectionApplyError.PolicyVersionMismatch, "現行会社");

            var unsupported = SeedPartnerApply(requestTypeCode: "company-name");
            var unsupportedResult = ApprovalReflectionApplyService.ApplyStandardApproved(
                unsupported,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);
            Assert.False(unsupportedResult.Success);
            Assert.Equal(ApprovalReflectionApplyError.PolicyResolutionFailed, unsupportedResult.ErrorCode);
            Assert.Empty(unsupported.ExecutedRequests.OfType<UpdateRequest>());
        }

        [Fact]
        public void 旧形式の行全体の版が一致しなければ反映失敗として記録する()
        {
            var service = SeedPartnerApply(targetRowVersion: "old-row-version");

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.TargetChanged, "現行会社");
        }

        [Fact]
        public void 変更する項目が提出時のままなら無関係な項目が更新されていても反映する()
        {
            var service = SeedPartnerApply(useFieldToken: true);
            // 承認待ちの間に、無関係な代表電話が直接編集されて行の版も進んだ。
            service.Update(new Entity("pl_partner", PartnerId) { ["pl_phone"] = "03-9999-9999" });

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Equal(ApprovalRequestStatus.反映済み, result.RequestStatus);
            Assert.Equal(string.Empty, result.FailureCode);
            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("株式会社　ＡＢＣ㈱", partner.GetAttributeValue<string>("pl_name"));
            Assert.Equal("03-9999-9999", partner.GetAttributeValue<string>("pl_phone"));
        }

        [Fact]
        public void 変更する項目が提出後に変わっていたら反映せず反映失敗として記録する()
        {
            var service = SeedPartnerApply(useFieldToken: true);
            service.Update(new Entity("pl_partner", PartnerId) { ["pl_name"] = "別の操作で変わった会社" });

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.TargetChanged, "別の操作で変わった会社");
        }

        [Fact]
        public void 対象が無効になっていたら反映失敗として記録する()
        {
            var service = SeedPartnerApply(useFieldToken: true);
            service.Update(new Entity("pl_partner", PartnerId) { ["statecode"] = new OptionSetValue(1) });

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.TargetInactive, "現行会社");
        }

        private static void AssertReflectionFailureRecorded(
            FakeOrganizationService service,
            ApprovalReflectionApplyResult result,
            string expectedCode,
            string expectedPartnerName)
        {
            // 例外は投げず、判定のトランザクションを確定できる結果を返す。
            Assert.True(result.Success);
            Assert.Equal(expectedCode, result.FailureCode);
            Assert.Equal(ApprovalRequestStatus.反映失敗, result.RequestStatus);
            Assert.Equal(ApprovalSubmissionVersionStatus.反映失敗, result.SubmissionVersionStatus);

            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal(expectedPartnerName, partner.GetAttributeValue<string>("pl_name"));
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApprovalRequestStatus.反映失敗.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Null(request.GetAttributeValue<string>(ApprovalActiveRequestKey.AttributeName));
            Assert.False(string.IsNullOrWhiteSpace(request.GetAttributeValue<string>("pl_cancellationreason")));
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.反映失敗.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            var link = service.Retrieve("pl_approvallink", ApprovalLinkId, new ColumnSet(true));
            Assert.Equal(ApprovalLinkStatus.結果確認済み.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));

            // 対象行へは書かない。申請・提出版・Linkの3件だけ。
            var updates = service.ExecutedRequests.OfType<UpdateRequest>().ToList();
            Assert.DoesNotContain(updates, update => update.Target.LogicalName == "pl_partner" || update.Target.LogicalName == "pl_contract");
            Assert.Equal(3, updates.Count);
            var logs = service.RetrieveMultiple(new QueryExpression("pl_operationlog") { ColumnSet = new ColumnSet(true) }).Entities;
            var failureLog = Assert.Single(logs);
            Assert.Equal(ApprovalReflectionApplyService.OperationCode, failureLog.GetAttributeValue<string>("pl_operationcode"));
            Assert.Equal(ApprovalReflectionApplyService.FailureResultCode, failureLog.GetAttributeValue<string>("pl_resultcode"));
            Assert.Equal(expectedCode, failureLog.GetAttributeValue<string>("pl_errorcode"));
        }

        [Fact]
        public void 契約変更は契約Lookupの所属を再確認して反映する()
        {
            var service = SeedContractApply();

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Equal(ContractId, result.TargetId);
            Assert.Equal("pl_contract", result.TargetEntityName);
            var contract = service.Retrieve("pl_contract", ContractId, new ColumnSet(true));
            Assert.Equal(new OptionSetValue(100000001), contract.GetAttributeValue<OptionSetValue>("pl_contractstatuscode"));
            var contractUpdate = Assert.Single(service.ExecutedRequests.OfType<UpdateRequest>(), update => update.Target.LogicalName == "pl_contract");
            Assert.Equal(
                ApprovalServerWriteBypass.ContractStandardUpdateStepId,
                contractUpdate.Parameters[ApprovalServerWriteBypass.ParameterName]);
        }

        [Fact]
        public void 無効化ポリシーと_提出後に設定の行が無くなった場合は安全側に倒す()
        {
            var disabled = SeedPartnerApply(
                policyJson: "{\"schemaVersion\":1,\"companyName\":false,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}");
            var disabledResult = ApprovalReflectionApplyService.ApplyStandardApproved(
                disabled,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);
            Assert.False(disabledResult.Success);
            Assert.Equal(ApprovalReflectionApplyError.PolicyResolutionFailed, disabledResult.ErrorCode);
            Assert.Empty(disabled.ExecutedRequests.OfType<UpdateRequest>());

            // 版1で提出した後に設定の行が無くなると、現在は既定値（版0）なので版が合わず反映失敗になる。
            var noSettings = SeedPartnerApply(includeSettings: false);
            var noSettingsResult = ApprovalReflectionApplyService.ApplyStandardApproved(
                noSettings,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);
            AssertReflectionFailureRecorded(noSettings, noSettingsResult, ApprovalReflectionApplyError.PolicyVersionMismatch, "現行会社");
        }

        [Fact]
        public void 設定の行が無い環境では既定値の版0で提出した申請を反映する()
        {
            // 2026-09-27：インポート直後は行が無く、既定値（会社名＝承認あり）の版0で提出・反映する。
            var service = SeedPartnerApply(requestPolicyVersion: "0", includeSettings: false);

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Equal(string.Empty, result.FailureCode);
            Assert.NotEqual("現行会社", service.Retrieve("pl_partner", PartnerId, new ColumnSet(true)).GetAttributeValue<string>("pl_name"));
        }

        [Fact]
        public void 対象更新後の状態更新失敗はトランザクション境界で全体を巻き戻す()
        {
            var service = SeedPartnerApply();
            service.BeforeExecute = request =>
            {
                // 対象行の更新後に来る「反映済み」への提出版更新だけを失敗させる。
                if (request is UpdateRequest update
                    && update.Target.LogicalName == "pl_submissionversion"
                    && (update.Target.GetAttributeValue<string>("pl_submissionstatuscode")
                        == ApprovalSubmissionVersionStatus.反映済み.ToString()))
                {
                    throw new InvalidPluginExecutionException("提出版更新を模擬失敗");
                }
            };

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
                ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow)));

            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("現行会社", partner.GetAttributeValue<string>("pl_name"));
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Empty(service.ExecutedRequests.OfType<UpdateRequest>());
        }

        [Fact]
        public void 対象行を読み取った後のrow_version競合はトランザクション全体を失敗させる()
        {
            var service = SeedPartnerApply();
            var raced = false;
            service.BeforeExecute = organizationRequest =>
            {
                if (!raced
                    && organizationRequest is UpdateRequest update
                    && update.Target.LogicalName == "pl_partner")
                {
                    raced = true;
                    service.ForceRowVersion("pl_partner", PartnerId, 2);
                }
            };

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
                ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow)));

            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("現行会社", partner.GetAttributeValue<string>("pl_name"));
            Assert.Empty(service.ExecutedRequests.OfType<UpdateRequest>());
        }

        [Theory]
        [InlineData("DecisionFlow")]
        [InlineData(null)]
        public void DecisionFlow源と源未指定のLinkは反映しない(string? sourceCode)
        {
            var service = SeedPartnerApply(sourceCode: sourceCode);

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(
                service,
                ApprovalLinkId,
                RequesterId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalReflectionApplyError.DataIntegrityError, result.ErrorCode);
            Assert.Empty(service.ExecutedRequests.OfType<UpdateRequest>());
            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("現行会社", partner.GetAttributeValue<string>("pl_name"));
        }

        private static readonly Guid CurrentMainOwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid NewMainOwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid PartnerOwnerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid ApproverTeamId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        [Fact]
        public void 主担当の変更を反映し_そのあと新しい主担当へ編集の共有を付ける()
        {
            // 2026-09-29ユーザー決定：主担当の変更（申請→承認）。前の主担当の共有は変えない。
            var service = SeedMainOwnerApply();

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Equal(ApprovalRequestStatus.反映済み, result.RequestStatus);
            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal(NewMainOwnerId, partner.GetAttributeValue<EntityReference>("pl_mainownerlookup")!.Id);
            var requests = service.ExecutedRequests;
            var partnerUpdate = requests.FindIndex(r => r is UpdateRequest update && update.Target.LogicalName == "pl_partner");
            var rowCreate = requests.FindIndex(r => r is CreateRequest create && create.Target.LogicalName == "pl_partnersharesetting");
            var grant = requests.FindIndex(r => r is GrantAccessRequest);
            Assert.True(partnerUpdate >= 0 && rowCreate > partnerUpdate && grant > rowCreate, "主担当→行→共有の順");
            var granted = (GrantAccessRequest)requests[grant];
            Assert.Equal(NewMainOwnerId, granted.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, granted.PrincipalAccess.AccessMask);
            Assert.DoesNotContain(requests, r => r is RevokeAccessRequest);
        }

        [Fact]
        public void 新しい主担当が通常の利用者でなくなっていたら_何も書かずに反映失敗にする()
        {
            var service = SeedMainOwnerApply();
            service.Retrieve("systemuser", NewMainOwnerId, new ColumnSet(true))["isdisabled"] = true;

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.MainOwnerUnavailable, "現行会社");
            Assert.DoesNotContain(service.ExecutedRequests, r => r is CreateRequest || r is GrantAccessRequest || r is ModifyAccessRequest);
        }

        [Fact]
        public void 今と同じ主担当への変更は_何も書かずに反映失敗にする()
        {
            var service = SeedMainOwnerApply(newOwner: CurrentMainOwnerId);

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.MainOwnerUnchanged, "現行会社");
        }

        [Fact]
        public void 新しい主担当の共有を整えられなければ_何も書かずに反映失敗にする()
        {
            var service = SeedMainOwnerApply();
            var row = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.Ready,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = NewMainOwnerId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "existing-read",
                },
                ApproverTeamId,
                isManagedProjection: true);
            row.Id = Guid.NewGuid();
            row["createdby"] = new EntityReference("systemuser", PartnerOwnerId);
            row["statecode"] = new OptionSetValue(0);
            service.Seed(row);
            service.RetrievedSharedPrincipalAccesses = new[]
            {
                new PrincipalAccess { Principal = new EntityReference("systemuser", NewMainOwnerId), AccessMask = AccessRights.ReadAccess | AccessRights.DeleteAccess },
            };

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.MainOwnerShareUnavailable, "現行会社");
        }

        [Fact]
        public void 行の所有者にできる人がいなければ_何も書かずに反映失敗にする()
        {
            // 監査H-1。登録者も新しい主担当も共有設定を読めない。最初の書き込みの前に決める。
            var service = SeedMainOwnerApply();
            service.UserHasPrivilege = (_, _) => false;

            var result = ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow);

            AssertReflectionFailureRecorded(service, result, ApprovalReflectionApplyError.MainOwnerRowOwnerUnavailable, "現行会社");
            Assert.DoesNotContain(service.ExecutedRequests, r => r is CreateRequest || r is GrantAccessRequest || r is ModifyAccessRequest);
        }

        [Fact]
        public void 共有の途中で失敗したら_主担当も申請も行も元に戻る()
        {
            // 監査M-2。主担当→行→共有の順に書き、共有（Grant）が失敗した場合。
            var service = SeedMainOwnerApply();
            service.BeforeExecute = request =>
            {
                if (request is GrantAccessRequest)
                {
                    throw new InvalidPluginExecutionException("共有を模擬失敗");
                }
            };

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
                ApprovalReflectionApplyService.ApplyStandardApproved(service, ApprovalLinkId, RequesterId, DateTime.UtcNow)));

            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal(CurrentMainOwnerId, partner.GetAttributeValue<EntityReference>("pl_mainownerlookup")!.Id);
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            var rows = service.RetrieveMultiple(new QueryExpression("pl_partnersharesetting") { ColumnSet = new ColumnSet(true) });
            Assert.Empty(rows.Entities);
            Assert.Empty(service.ExecutedRequests.OfType<CreateRequest>());
        }

        private static FakeOrganizationService SeedMainOwnerApply(Guid? newOwner = null)
        {
            var service = SeedPartnerApply(
                requestTypeCode: ApprovalPolicyContract.MainOwnerRequestType,
                changeSetJson: "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"},\"changes\":[{\"attribute\":\"pl_mainownerlookup\",\"value\":\"" + (newOwner ?? NewMainOwnerId).ToString("D") + "\"}]}",
                fieldTokenAttributes: new[] { "pl_mainownerlookup" });
            service.Retrieve("pl_partner", PartnerId, new ColumnSet(true))["pl_sharesetupstatuscode"] =
                new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode);
            service.Seed(new Entity("systemuser", NewMainOwnerId) { ["accessmode"] = new OptionSetValue(0), ["isdisabled"] = false });
            service.Seed(new Entity("systemuser", PartnerOwnerId) { ["accessmode"] = new OptionSetValue(0), ["isdisabled"] = false });
            var definitionId = Guid.NewGuid();
            service.Seed(new Entity("environmentvariabledefinition", definitionId) { ["schemaname"] = PartnerLedgerEnvironmentVariableNames.ApproverTeamId });
            service.Seed(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", definitionId),
                ["value"] = ApproverTeamId.ToString("D"),
            });
            service.Seed(new Entity("team", ApproverTeamId) { ["teamtype"] = new OptionSetValue(3) });
            return service;
        }

        private static FakeOrganizationService SeedPartnerApply(
            string requestPolicyVersion = "1",
            string requestTypeCode = ApprovalPolicyContract.CompanyNameRequestType,
            string targetRowVersion = "1",
            string policyJson = DefaultPolicyJson,
            bool includeSettings = true,
            string? sourceCode = ApprovalResultSourceCodes.PowerAutomateGroup,
            bool useFieldToken = false,
            string? changeSetJson = null,
            string[]? fieldTokenAttributes = null)
        {
            var service = new FakeOrganizationService();
            if (includeSettings)
            {
                service.Seed(new Entity("pl_settings", SettingsId)
                {
                    ["statecode"] = 0,
                    ["pl_settingsversion"] = 1,
                    ["pl_policyjson"] = policyJson,
                });
            }
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
                ["pl_requesttypecode"] = requestTypeCode,
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", RequesterId),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_approvalpolicyversion"] = requestPolicyVersion,
                [ApprovalActiveRequestKey.AttributeName] = "ApprovalActiveV1-TEST",
            });
            var partnerRow = new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_normalizedname"] = "現行会社",
                ["pl_phone"] = "03-0000-0000",
                ["pl_tradingstatuscode"] = new OptionSetValue(100000000),
                ["pl_mainownerlookup"] = new EntityReference("systemuser", CurrentMainOwnerId),
                ["ownerid"] = new EntityReference("systemuser", PartnerOwnerId),
            };
            service.Seed(partnerRow);
            if (fieldTokenAttributes != null)
            {
                targetRowVersion = ApprovalTargetBaseline.Compute(partnerRow, fieldTokenAttributes);
            }
            else if (useFieldToken)
            {
                // 提出時にサーバーが記録する形（変更する項目だけの現在値の指紋）。
                targetRowVersion = ApprovalTargetBaseline.Compute(partnerRow, new[] { "pl_name" });
            }
            service.Seed(new Entity("pl_submissionversion", SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["pl_policyversion"] = requestPolicyVersion,
                ["pl_rowversiontoken"] = targetRowVersion,
                ["pl_changesetjson"] = changeSetJson ?? "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"株式会社　ＡＢＣ㈱\"}]}",
            });
            service.Seed(new Entity("pl_approvallink", ApprovalLinkId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", SubmissionVersionId),
                ["pl_approvalsourcecode"] = sourceCode,
                ["pl_linkstatuscode"] = ApprovalLinkStatus.連携済み.ToString(),
                ["pl_externalrequestkey"] = "df-request-apply",
                ["pl_decisioncode"] = ApprovalDecision.承認.ToString(),
                ["pl_resultknown"] = true,
                ["pl_decidedat"] = DateTime.UtcNow,
                ["pl_responseat"] = DateTime.UtcNow,
            });
            return service;
        }

        private static FakeOrganizationService SeedContractApply()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_settings", SettingsId)
            {
                ["statecode"] = 0,
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = DefaultPolicyJson,
            });
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
                ["pl_requesttypecode"] = ApprovalPolicyContract.ContractUpdateRequestType,
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", RequesterId),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_contract", ContractId),
                ["pl_approvalpolicyversion"] = "1",
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
            });
            service.Seed(new Entity("pl_contract", ContractId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
            });
            service.Seed(new Entity("pl_submissionversion", SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["pl_policyversion"] = "1",
                ["pl_rowversiontoken"] = "1",
                ["pl_changesetjson"] = "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_contract\",\"id\":\"ffffffff-ffff-ffff-ffff-ffffffffffff\"},\"changes\":[{\"attribute\":\"pl_contractstatuscode\",\"value\":100000001}]}" ,
            });
            service.Seed(new Entity("pl_approvallink", ApprovalLinkId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", SubmissionVersionId),
                ["pl_approvalsourcecode"] = ApprovalResultSourceCodes.PowerAutomateGroup,
                ["pl_linkstatuscode"] = ApprovalLinkStatus.連携済み.ToString(),
                ["pl_externalrequestkey"] = "df-request-contract",
                ["pl_decisioncode"] = ApprovalDecision.承認.ToString(),
                ["pl_resultknown"] = true,
                ["pl_decidedat"] = DateTime.UtcNow,
                ["pl_responseat"] = DateTime.UtcNow,
            });
            return service;
        }
    }
}
