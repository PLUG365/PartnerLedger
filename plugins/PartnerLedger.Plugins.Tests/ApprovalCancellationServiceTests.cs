using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalCancellationServiceTests
    {
        private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid VersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid LinkId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid RoleId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly Guid ThirdUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid DraftVersionId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        [Fact]
        public void システム管理者は提出中申請と提出版を取消し監査する()
        {
            var writeService = SeedService();
            var authorizationReadService = SeedAuthorizationReadService(includeAdminRole: true);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                authorizationReadService,
                RequestId,
                " 承認者の休職により管理者取消 ",
                AdminId,
                new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc));

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal("承認者の休職により管理者取消", result.Reason);
            var version = writeService.Retrieve("pl_submissionversion", VersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.取消.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            var logs = writeService.RetrieveMultiple(new QueryExpression("pl_operationlog") {ColumnSet = new ColumnSet(true)});
            Assert.Single(logs.Entities);
            Assert.Equal(ApprovalCancellationService.OperationCode, logs.Entities[0].GetAttributeValue<string>("pl_operationcode"));
            Assert.Equal(AdminId, logs.Entities[0].GetAttributeValue<EntityReference>("pl_initiatinguserlookup")!.Id);
            Assert.Empty(authorizationReadService.RetrieveMultiple(
                new QueryExpression("pl_operationlog") { ColumnSet = new ColumnSet(true) }).Entities);

            writeService.Update(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
            });
            var replay = ApprovalCancellationService.Cancel(
                writeService,
                authorizationReadService,
                RequestId,
                "再送",
                AdminId,
                DateTime.UtcNow);

            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Single(writeService.RetrieveMultiple(new QueryExpression("pl_operationlog") {ColumnSet = new ColumnSet(true)}).Entities);
        }

        [Fact]
        public void PLシステム管理以外は取消できない()
        {
            var service = SeedService();
            var authorizationReadService = SeedAuthorizationReadService(includeAdminRole: false);

            var result = ApprovalCancellationService.Cancel(
                service,
                authorizationReadService,
                RequestId,
                "承認者不在",
                OtherUserId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.Unauthorized, result.ErrorCode);
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), service.Retrieve("pl_submissionversion", VersionId, new ColumnSet(true)).GetAttributeValue<string>("pl_submissionstatuscode"));
        }

        [Fact]
        public void 取消の提出版更新は提出版ガードStepだけを飛ばす()
        {
            var writeService = SeedService();

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "承認者不在",
                AdminId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            var update = Assert.Single(writeService.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Equal("pl_submissionversion", update.Target.LogicalName);
            Assert.Equal(
                ApprovalServerWriteBypass.SubmissionVersionCrudGuardUpdateStepId,
                update.Parameters[ApprovalServerWriteBypass.ParameterName]);
        }

        [Fact]
        public void 実行ユーザーの権限不足は競合ではなく権限エラーとして返す()
        {
            var writeService = SeedService();
            writeService.BeforeExecute = _ => throw new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    ErrorCode = ApprovalCancellationService.PrivilegeDeniedErrorCode,
                    Message = "Principal user is missing prvBypassCustomBusinessLogic privilege.",
                },
                new System.ServiceModel.FaultReason("missing privilege"));

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "承認者不在",
                AdminId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.Unauthorized, result.ErrorCode);
        }

        [Fact]
        public void 行バージョン不一致などその他の更新失敗は競合として返す()
        {
            var writeService = SeedService();
            writeService.BeforeExecute = _ => throw new InvalidPluginExecutionException("行バージョンが一致しません。");

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "承認者不在",
                AdminId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.ConcurrencyConflict, result.ErrorCode);
        }

        [Fact]
        public void 申請者は自分の下書きを理由なしで取り下げ下書き版だけ取消して監査する()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.下書き, includeReturnedVersion: false);
            var authorizationReadService = SeedAuthorizationReadService(includeAdminRole: false);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                authorizationReadService,
                RequestId,
                null,
                OtherUserId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal(string.Empty, result.Reason);
            Assert.Equal(ApprovalSubmissionVersionStatus.取消.ToString(), VersionStatus(writeService, DraftVersionId));
            var update = Assert.Single(writeService.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Equal(
                ApprovalServerWriteBypass.SubmissionVersionCrudGuardUpdateStepId,
                update.Parameters[ApprovalServerWriteBypass.ParameterName]);
            var log = Assert.Single(Logs(writeService));
            Assert.Equal(ApprovalCancellationService.OperationCode, log.GetAttributeValue<string>("pl_operationcode"));
            Assert.Equal(OtherUserId, log.GetAttributeValue<EntityReference>("pl_initiatinguserlookup")!.Id);
            Assert.Contains("取消前状態:下書き", log.GetAttributeValue<string>("pl_name"));

            writeService.Update(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
            });
            var replay = ApprovalCancellationService.Cancel(
                writeService,
                authorizationReadService,
                RequestId,
                null,
                OtherUserId,
                DateTime.UtcNow);

            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Single(Logs(writeService));
        }

        [Fact]
        public void 申請者は差戻し申請を取り下げても差戻し版とLinkを変えない()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.差戻し, includeReturnedVersion: true);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                "",
                OtherUserId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Equal(ApprovalSubmissionVersionStatus.差戻し.ToString(), VersionStatus(writeService, VersionId));
            Assert.Equal(ApprovalSubmissionVersionStatus.取消.ToString(), VersionStatus(writeService, DraftVersionId));
            var link = writeService.Retrieve("pl_approvallink", LinkId, new ColumnSet(true));
            Assert.Equal(ApprovalLinkStatus.結果確認済み.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Single(Logs(writeService));
        }

        [Fact]
        public void 下書き版がない下書きも取り下げられる()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.下書き, includeReturnedVersion: false, includeDraftVersion: false);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                null,
                OtherUserId,
                DateTime.UtcNow);

            Assert.True(result.Success);
            Assert.Empty(writeService.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Single(Logs(writeService));
        }

        [Fact]
        public void 申請者でも管理者でなければ提出中は取り消せない()
        {
            var service = SeedService();

            var result = ApprovalCancellationService.Cancel(
                service,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                "やっぱりやめたい",
                OtherUserId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.Unauthorized, result.ErrorCode);
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), VersionStatus(service, VersionId));
            Assert.Empty(Logs(service));
        }

        [Fact]
        public void 他人の下書きは非管理者は取り消せず管理者は理由付きなら取り消せる()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.下書き, includeReturnedVersion: false);

            var stranger = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                "放置のため",
                ThirdUserId,
                DateTime.UtcNow);
            Assert.False(stranger.Success);
            Assert.Equal(ApprovalCancellationError.Unauthorized, stranger.ErrorCode);

            var adminWithoutReason = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "  ",
                AdminId,
                DateTime.UtcNow);
            Assert.False(adminWithoutReason.Success);
            Assert.Equal(ApprovalCancellationError.InvalidInput, adminWithoutReason.ErrorCode);
            Assert.Equal(ApprovalSubmissionVersionStatus.下書き.ToString(), VersionStatus(writeService, DraftVersionId));
            Assert.Empty(Logs(writeService));

            var admin = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "申請者が退職したため",
                AdminId,
                DateTime.UtcNow);
            Assert.True(admin.Success);
            Assert.Equal("申請者が退職したため", admin.Reason);
            Assert.Equal(ApprovalSubmissionVersionStatus.取消.ToString(), VersionStatus(writeService, DraftVersionId));
            Assert.Equal(AdminId, Assert.Single(Logs(writeService)).GetAttributeValue<EntityReference>("pl_initiatinguserlookup")!.Id);
        }

        [Fact]
        public void 取消済みの再送でも無関係者には成功を返さない()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.取消, includeReturnedVersion: false, includeDraftVersion: false);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                "再送",
                ThirdUserId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.Unauthorized, result.ErrorCode);
        }

        [Fact]
        public void 取下げと提出が同じ下書き版を競合したら取下げは競合として失敗する()
        {
            var writeService = SeedDraftService(ApprovalRequestStatus.下書き, includeReturnedVersion: false);
            writeService.BeforeExecute = _ => throw new InvalidPluginExecutionException("行バージョンが一致しません。");

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: false),
                RequestId,
                null,
                OtherUserId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.ConcurrencyConflict, result.ErrorCode);
            Assert.Empty(Logs(writeService));
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.承認済み)]
        [InlineData(ApprovalRequestStatus.却下)]
        [InlineData(ApprovalRequestStatus.反映待ち)]
        [InlineData(ApprovalRequestStatus.反映失敗)]
        [InlineData(ApprovalRequestStatus.反映済み)]
        public void 判断済みと反映系の申請は申請者も管理者も取り消せない(ApprovalRequestStatus status)
        {
            var writeService = SeedDraftService(status, includeReturnedVersion: false, includeDraftVersion: false);

            var result = ApprovalCancellationService.Cancel(
                writeService,
                SeedAuthorizationReadService(includeAdminRole: true),
                RequestId,
                "取り消したい",
                AdminId,
                DateTime.UtcNow);

            Assert.False(result.Success);
            Assert.Equal(ApprovalCancellationError.RequestStateInvalid, result.ErrorCode);
            Assert.Empty(Logs(writeService));
        }

        [Fact]
        public void 親Updateへの反映はキーを解放し再送では保存済みの理由を上書きしない()
        {
            var first = new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
            };
            StandardApprovalSubmitPlugin.ApplyCancellationToTarget(
                first,
                ApprovalCancellationResult.Succeeded(RequestId, DraftVersionId, "", isReplay: false));
            Assert.Null(first[ApprovalActiveRequestKey.AttributeName]);
            Assert.False(first.Attributes.Contains("pl_cancellationreason"));

            var withReason = new Entity("pl_request", RequestId);
            StandardApprovalSubmitPlugin.ApplyCancellationToTarget(
                withReason,
                ApprovalCancellationResult.Succeeded(RequestId, null, "申請者が退職したため", isReplay: false));
            Assert.Equal("申請者が退職したため", withReason["pl_cancellationreason"]);

            var replay = new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
                ["pl_cancellationreason"] = "再送側の別の理由",
            };
            StandardApprovalSubmitPlugin.ApplyCancellationToTarget(
                replay,
                ApprovalCancellationResult.Succeeded(RequestId, null, "再送側の別の理由", isReplay: true));
            Assert.False(replay.Attributes.Contains("pl_cancellationreason"));
            Assert.Null(replay[ApprovalActiveRequestKey.AttributeName]);

            Assert.Throws<InvalidPluginExecutionException>(() => StandardApprovalSubmitPlugin.ApplyCancellationToTarget(
                new Entity("pl_request", RequestId),
                ApprovalCancellationResult.Failed(RequestId, ApprovalCancellationError.Unauthorized)));
        }

        private static string? VersionStatus(FakeOrganizationService service, Guid versionId)
            => service.Retrieve("pl_submissionversion", versionId, new ColumnSet(true))
                .GetAttributeValue<string>("pl_submissionstatuscode");

        private static System.Collections.Generic.IReadOnlyList<Entity> Logs(FakeOrganizationService service)
            => service.RetrieveMultiple(new QueryExpression("pl_operationlog") { ColumnSet = new ColumnSet(true) }).Entities.ToList();

        /// <summary>申請者はOtherUserId。差戻しの場合は判断済みの提出版とLinkも置く。</summary>
        private static FakeOrganizationService SeedDraftService(
            ApprovalRequestStatus status,
            bool includeReturnedVersion,
            bool includeDraftVersion = true)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = status.ToString(),
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", OtherUserId),
                ["statecode"] = new OptionSetValue(0),
            });
            if (includeReturnedVersion)
            {
                service.Seed(new Entity("pl_submissionversion", VersionId)
                {
                    ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                    ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.差戻し.ToString(),
                    ["statecode"] = new OptionSetValue(0),
                });
                service.Seed(new Entity("pl_approvallink", LinkId)
                {
                    ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                    ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", VersionId),
                    ["pl_approvalsourcecode"] = ApprovalResultSourceCodes.PowerAutomateGroup,
                    ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                    ["pl_externalrequestkey"] = "approval-withdraw-test",
                    ["pl_resultknown"] = true,
                    ["statecode"] = new OptionSetValue(0),
                });
            }
            if (includeDraftVersion)
            {
                service.Seed(new Entity("pl_submissionversion", DraftVersionId)
                {
                    ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                    ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString(),
                    ["statecode"] = new OptionSetValue(0),
                });
            }
            return service;
        }

        private static FakeOrganizationService SeedAuthorizationReadService(bool includeAdminRole)
        {
            var service = new FakeOrganizationService();
            if (includeAdminRole)
            {
                service.Seed(new Entity("role", RoleId)
                {
                    ["roleid"] = RoleId,
                    ["name"] = "PL システム管理",
                });
                service.Seed(new Entity("systemuserroles", Guid.NewGuid())
                {
                    ["roleid"] = RoleId,
                    ["systemuserid"] = AdminId,
                });
            }
            return service;
        }

        private static FakeOrganizationService SeedService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", OtherUserId),
                ["statecode"] = new OptionSetValue(0),
            });
            service.Seed(new Entity("pl_submissionversion", VersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["statecode"] = new OptionSetValue(0),
            });
            service.Seed(new Entity("pl_approvallink", LinkId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", VersionId),
                ["pl_approvalsourcecode"] = ApprovalResultSourceCodes.PowerAutomateGroup,
                ["pl_linkstatuscode"] = ApprovalLinkStatus.送信待ち.ToString(),
                ["pl_externalrequestkey"] = "approval-cancel-test",
                ["pl_resultknown"] = false,
                ["statecode"] = new OptionSetValue(0),
            });
            return service;
        }
    }
}
