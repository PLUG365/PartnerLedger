using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class StandardApprovalDecisionServiceTests
    {
        private static readonly Guid InitiatingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid LegacyApplicationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid SubmissionVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ApprovalLinkId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        private static readonly Guid PartnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid SettingsId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        private static readonly DateTime ResponseAtUtc = new(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);

        private const string DefaultPolicyJson
            = "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}";

        [Theory]
        [InlineData(ApprovalDecision.承認, ApprovalRequestStatus.反映済み, ApprovalSubmissionVersionStatus.反映済み)]
        [InlineData(ApprovalDecision.差戻し, ApprovalRequestStatus.差戻し, ApprovalSubmissionVersionStatus.差戻し)]
        [InlineData(ApprovalDecision.却下, ApprovalRequestStatus.却下, ApprovalSubmissionVersionStatus.却下)]
        public void 確知済みLinkの判定を申請と提出版へ同じ遷移として適用する(
            ApprovalDecision decision,
            ApprovalRequestStatus expectedRequestStatus,
            ApprovalSubmissionVersionStatus expectedVersionStatus)
        {
            var service = SeedLinkedResult(decision.ToString());
            var responseAtBefore = RetrieveLink(service).GetAttributeValue<DateTime>("pl_responseat");

            var result = Apply(service);

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal(expectedRequestStatus, result.RequestStatus);
            Assert.Equal(expectedVersionStatus, result.SubmissionVersionStatus);

            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            var link = RetrieveLink(service);
            Assert.Equal(expectedRequestStatus.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(expectedVersionStatus.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Equal(ApprovalLinkStatus.結果確認済み.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Equal(ResponseAtUtc, link.GetAttributeValue<DateTime>("pl_decidedat"));
            Assert.Equal(responseAtBefore, link.GetAttributeValue<DateTime>("pl_responseat"));

            foreach (var update in service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>())
            {
                var expected = ApprovalServerWriteBypass.StepIdsFor(update.Target.LogicalName);
                if (expected.Length == 0)
                    Assert.False(update.Parameters.Contains(ApprovalServerWriteBypass.ParameterName));
                else
                    Assert.Equal(expected, update.Parameters[ApprovalServerWriteBypass.ParameterName]);
            }

            var logs = FindLogs(service).Entities;
            Assert.Single(logs);
            Assert.Equal("Success", logs.Single().GetAttributeValue<string>("pl_resultcode"));
            Assert.Equal(
                $"decision:{decision}:link:{ApprovalLinkId}",
                logs.Single().GetAttributeValue<string>("pl_name"));
            Assert.Equal(
                $"standard-decision-{ApprovalLinkId:N}-{decision}",
                logs.Single().GetAttributeValue<string>("pl_idempotencykey"));
        }

        [Fact]
        public void 結果不明は申請とLinkを承認状態へ進めない()
        {
            var service = SeedLinkedResult(null, linkStatus: ApprovalLinkStatus.連携不明, resultKnown: false);

            var result = Apply(service);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalDecisionError.ResultUnknown, result.ErrorCode);
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), RetrieveRequest(service).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), RetrieveVersion(service).GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Equal(ApprovalLinkStatus.連携不明.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Single(FindLogs(service).Entities);
            Assert.Equal("Rejected", FindLogs(service).Entities.Single().GetAttributeValue<string>("pl_resultcode"));
        }

        [Fact]
        public void 申請提出中以外または提出版対応不一致は拒否する()
        {
            var wrongRequestState = SeedLinkedResult("承認", requestStatus: ApprovalRequestStatus.承認済み);
            var wrongStateResult = Apply(wrongRequestState);
            Assert.False(wrongStateResult.Success);
            Assert.Equal(StandardApprovalDecisionError.StateCannotRecord, wrongStateResult.ErrorCode);
            Assert.Empty(wrongRequestState.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());

            var pairMismatch = SeedLinkedResult("承認", versionRequestId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
            var mismatchResult = Apply(pairMismatch);
            Assert.False(mismatchResult.Success);
            Assert.Equal(StandardApprovalDecisionError.PairMismatch, mismatchResult.ErrorCode);
            Assert.Empty(pairMismatch.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 不正な判定と連携前Linkは拒否する()
        {
            var unsupported = SeedLinkedResult("保留");
            var unsupportedResult = Apply(unsupported);
            Assert.False(unsupportedResult.Success);
            Assert.Equal(StandardApprovalDecisionError.DecisionUnsupported, unsupportedResult.ErrorCode);
            Assert.Empty(unsupported.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());

            var notReady = SeedLinkedResult("承認", linkStatus: ApprovalLinkStatus.送信待ち, resultKnown: false, decidedAt: null);
            var notReadyResult = Apply(notReady);
            Assert.False(notReadyResult.Success);
            Assert.Equal(StandardApprovalDecisionError.LinkNotReady, notReadyResult.ErrorCode);
            Assert.Empty(notReady.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 同じ確定結果の再送は更新も監査も増やさない()
        {
            var service = SeedLinkedResult("承認");
            var first = Apply(service);
            var updateCount = service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count();

            var replay = Apply(service);

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(updateCount, service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count());
            Assert.Single(FindLogs(service).Entities);
        }

        [Fact]
        public void 判定確定の副作用で共有を取り消さない()
        {
            var service = SeedLinkedResult("承認");

            var result = Apply(service);

            Assert.True(result.Success);
            Assert.DoesNotContain(service.ExecutedRequests, request => request is Microsoft.Crm.Sdk.Messages.RevokeAccessRequest);
        }

        [Fact]
        public void 外部要求キー欠損は拒否する()
        {
            var missingExternalKey = SeedLinkedResult("承認", omitExternalKey: true);
            var externalKeyResult = Apply(missingExternalKey);
            Assert.False(externalKeyResult.Success);
            Assert.Equal(StandardApprovalDecisionError.DataIntegrityError, externalKeyResult.ErrorCode);
        }

        [Fact]
        public void グループ承認LinkはApplicationなしで申請と提出版を進める()
        {
            var service = SeedLinkedResult("承認");

            var result = Apply(service);

            Assert.True(result.Success);
            Assert.Equal(ApprovalRequestStatus.反映済み, result.RequestStatus);
            Assert.Equal(ApprovalSubmissionVersionStatus.反映済み, result.SubmissionVersionStatus);
            Assert.Equal(ApprovalLinkStatus.結果確認済み.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Equal("更新会社", service.Retrieve("pl_partner", PartnerId, new ColumnSet(true)).GetAttributeValue<string>("pl_name"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DecisionFlow源のLinkは判定を適用しない(bool withLegacyApplication)
        {
            var service = SeedLinkedResult("承認", sourceCode: "DecisionFlow", withLegacyApplication: withLegacyApplication);

            var result = Apply(service);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalDecisionError.DataIntegrityError, result.ErrorCode);
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), RetrieveRequest(service).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal("現行会社", service.Retrieve("pl_partner", PartnerId, new ColumnSet(true)).GetAttributeValue<string>("pl_name"));
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 撤去済みApplication参照が残るグループ承認Linkも判定を適用する()
        {
            var service = SeedLinkedResult("承認", withLegacyApplication: true);

            var result = Apply(service);

            Assert.True(result.Success);
            Assert.Equal(ApprovalRequestStatus.反映済み, result.RequestStatus);
        }

        [Fact]
        public void 結果提供元未指定は推測せず拒否する()
        {
            var service = SeedLinkedResult("承認", sourceCode: null);

            var result = Apply(service);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalDecisionError.DataIntegrityError, result.ErrorCode);
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), RetrieveRequest(service).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 判定監査に失敗した場合は全更新をロールバックする()
        {
            var service = SeedLinkedResult("承認");
            service.BeforeCreate = entity => entity.LogicalName == "pl_operationlog"
                ? new InvalidPluginExecutionException("test log create failure")
                : null;

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() => Apply(service)));

            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), RetrieveRequest(service).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), RetrieveVersion(service).GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Equal(ApprovalLinkStatus.連携済み.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Empty(FindLogs(service).Entities);
        }

        private static StandardApprovalDecisionResult Apply(FakeOrganizationService service)
            => service.RunInTransaction(() => StandardApprovalDecisionService.Apply(
                service,
                ApprovalLinkId,
                InitiatingUserId));

        [Fact]
        public void 承認が反映できないときは反映失敗で判定を確定し再送では何も増やさない()
        {
            var service = SeedLinkedResult("承認");
            // 旧形式の版（行全体）が一致しない＝承認待ちの間に対象が更新された。
            service.Update(new Entity("pl_partner", PartnerId) { ["pl_phone"] = "03-9999-9999" });

            var first = Apply(service);

            Assert.True(first.Success);
            Assert.False(first.IsReplay);
            Assert.Equal(ApprovalRequestStatus.反映失敗, first.RequestStatus);
            Assert.Equal(ApprovalSubmissionVersionStatus.反映失敗, first.SubmissionVersionStatus);
            var partner = service.Retrieve("pl_partner", PartnerId, new ColumnSet(true));
            Assert.Equal("現行会社", partner.GetAttributeValue<string>("pl_name"));
            var updateCount = service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count();
            var logCount = FindLogs(service).Entities.Count;

            var replay = Apply(service);

            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(ApprovalRequestStatus.反映失敗, replay.RequestStatus);
            Assert.Equal(updateCount, service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count());
            Assert.Equal(logCount, FindLogs(service).Entities.Count);
        }

        private static FakeOrganizationService SeedLinkedResult(
            string? decisionCode,
            ApprovalRequestStatus requestStatus = ApprovalRequestStatus.提出中,
            ApprovalLinkStatus linkStatus = ApprovalLinkStatus.連携済み,
            bool resultKnown = true,
            DateTime? decidedAt = null,
            Guid? versionRequestId = null,
            string? sourceCode = ApprovalResultSourceCodes.PowerAutomateGroup,
            bool withLegacyApplication = false,
            bool omitExternalKey = false)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_settings", SettingsId)
            {
                ["statecode"] = 0,
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = DefaultPolicyJson,
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_normalizedname"] = "現行会社",
                ["pl_tradingstatuscode"] = new OptionSetValue(100000000),
            });
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = requestStatus.ToString(),
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", InitiatingUserId),
                ["pl_requesttypecode"] = ApprovalPolicyContract.CompanyNameRequestType,
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_approvalpolicyversion"] = "1",
            });
            service.Seed(new Entity("pl_submissionversion", SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", versionRequestId ?? RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["pl_policyversion"] = "1",
                ["pl_rowversiontoken"] = "1",
                ["pl_changesetjson"] = "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"更新会社\"}]}",
            });
            var link = new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, ApprovalLinkId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", SubmissionVersionId),
                ["pl_approvalsourcecode"] = sourceCode,
                ["pl_linkstatuscode"] = linkStatus.ToString(),
                ["pl_resultknown"] = resultKnown,
                ["pl_decisioncode"] = decisionCode ?? string.Empty,
            };
            // DecisionFlow連携時代のLinkを再現する。列は撤去対象で、サーバーは読まない。
            if (withLegacyApplication)
                link["pl_applicationlookup"] = new EntityReference("ds_application", LegacyApplicationId);
            if (!omitExternalKey)
                link["pl_externalrequestkey"] = "df-request-1";
            var actualDecidedAt = decidedAt ?? (resultKnown ? (DateTime?)ResponseAtUtc : null);
            if (actualDecidedAt.HasValue)
                link["pl_decidedat"] = actualDecidedAt.Value;
            link["pl_responseat"] = ResponseAtUtc;
            service.Seed(link);
            return service;
        }

        private static Entity RetrieveRequest(FakeOrganizationService service)
            => service.Retrieve("pl_request", RequestId, new ColumnSet(true));

        private static Entity RetrieveVersion(FakeOrganizationService service)
            => service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));

        private static Entity RetrieveLink(FakeOrganizationService service)
            => service.Retrieve(StandardApprovalCrudContract.ApprovalLinkEntityName, ApprovalLinkId, new ColumnSet(true));

        private static EntityCollection FindLogs(FakeOrganizationService service)
        {
            var query = new QueryExpression(OperationLogRepository.EntityName)
            {
                ColumnSet = new ColumnSet(true),
            };
            query.Criteria.AddCondition(
                "pl_operationcode",
                ConditionOperator.Equal,
                StandardApprovalDecisionService.OperationCode);
            return service.RetrieveMultiple(query);
        }
    }
}
