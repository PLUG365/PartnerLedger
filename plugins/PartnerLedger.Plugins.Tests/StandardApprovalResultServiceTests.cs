using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class StandardApprovalResultServiceTests
    {
        private static readonly Guid InitiatingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid LegacyApplicationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid SubmissionVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ApprovalLinkId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        private static readonly DateTime RespondedAtUtc = new(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void 確知結果は最小入力からサーバー値を補完して保存する()
        {
            var service = SeedLink();
            var target = ResultTarget(resultKnown: true, decisionCode: "承認");

            var result = ApplyAndPersist(service, target, RespondedAtUtc);

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal(ApprovalLinkStatus.連携済み, result.LinkStatus);
            var link = RetrieveLink(service);
            Assert.Equal(ApprovalLinkStatus.連携済み.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Equal("承認", link.GetAttributeValue<string>("pl_decisioncode"));
            Assert.True(link.GetAttributeValue<bool>("pl_resultknown"));
            Assert.Equal(RespondedAtUtc, link.GetAttributeValue<DateTime>("pl_decidedat"));
            Assert.Equal(RespondedAtUtc, link.GetAttributeValue<DateTime>("pl_responseat"));
            var log = FindLogs(service).Entities.Single();
            Assert.Equal("Success", log.GetAttributeValue<string>("pl_resultcode"));
            Assert.Equal("result:承認:link:" + ApprovalLinkId, log.GetAttributeValue<string>("pl_name"));
            Assert.StartsWith("standard-result-", log.GetAttributeValue<string>("pl_idempotencykey"));
        }

        [Fact]
        public void 結果不明は判定値を空にして連携不明へ保存する()
        {
            var service = SeedLink();
            var target = ResultTarget(resultKnown: false);

            var result = ApplyAndPersist(service, target, RespondedAtUtc);

            Assert.True(result.Success);
            Assert.Equal(ApprovalLinkStatus.連携不明, result.LinkStatus);
            var link = RetrieveLink(service);
            Assert.Equal(ApprovalLinkStatus.連携不明.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Equal(string.Empty, link.GetAttributeValue<string>("pl_decisioncode"));
            Assert.False(link.GetAttributeValue<bool>("pl_resultknown"));
            Assert.False(link.Contains("pl_decidedat") && link["pl_decidedat"] != null);
            Assert.Equal(RespondedAtUtc, link.GetAttributeValue<DateTime>("pl_responseat"));
            Assert.Equal("Uncertain", FindLogs(service).Entities.Single().GetAttributeValue<string>("pl_resultcode"));
        }

        [Fact]
        public void 取消済み申請への標準結果取込みはLinkを変更しない()
        {
            var service = SeedLink(sourceCode: null);
            service.Update(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
            });

            var result = StandardApprovalResultService.Apply(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.StateCannotIngest, result.ErrorCode);
            Assert.Equal(ApprovalLinkStatus.送信待ち.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.Empty(FindLogs(service).Entities);
        }

        [Fact]
        public void 結果不明の後に確知結果が届けば同じLinkを確知へ進める()
        {
            var service = SeedLink();
            var unknown = ApplyAndPersist(service, ResultTarget(resultKnown: false), RespondedAtUtc);
            var known = ApplyAndPersist(service, ResultTarget(resultKnown: true, decisionCode: "差戻し"), RespondedAtUtc.AddMinutes(2));

            Assert.True(unknown.Success);
            Assert.True(known.Success);
            Assert.Equal(ApprovalLinkStatus.連携済み, known.LinkStatus);
            Assert.Equal(2, FindLogs(service).Entities.Count);
            Assert.Equal("差戻し", RetrieveLink(service).GetAttributeValue<string>("pl_decisioncode"));
        }

        [Fact]
        public void 同じ標準結果の再送は監査を増やさず再送成功にする()
        {
            var service = SeedLink();
            var first = ApplyAndPersist(service, ResultTarget(resultKnown: true, decisionCode: "承認"), RespondedAtUtc);
            var replayTarget = ResultTarget(resultKnown: true, decisionCode: "承認");

            var replay = StandardApprovalResultService.Apply(
                service,
                replayTarget,
                InitiatingUserId,
                RespondedAtUtc.AddMinutes(10));

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(ApprovalLinkStatus.連携済み, replay.LinkStatus);
            Assert.Single(FindLogs(service).Entities);
        }

        [Fact]
        public void 判定反映後も同じ標準結果の再送は監査を増やさず成功する()
        {
            var service = SeedLink();
            var first = ApplyAndPersist(service, ResultTarget(resultKnown: true, decisionCode: "承認"), RespondedAtUtc);

            service.Update(new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, ApprovalLinkId)
            {
                ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                ["pl_responseat"] = RespondedAtUtc,
            });

            var replay = StandardApprovalResultService.Apply(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc.AddMinutes(10));

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(ApprovalLinkStatus.結果確認済み, replay.LinkStatus);
            Assert.Single(FindLogs(service).Entities);
        }

        [Fact]
        public void 確定済み結果の別判定への上書きは拒否する()
        {
            var service = SeedLink();
            ApplyAndPersist(service, ResultTarget(resultKnown: true, decisionCode: "承認"), RespondedAtUtc);
            var target = ResultTarget(resultKnown: true, decisionCode: "却下");

            var result = StandardApprovalResultService.Apply(
                service,
                target,
                InitiatingUserId,
                RespondedAtUtc.AddMinutes(1));

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.StateCannotIngest, result.ErrorCode);
            Assert.Equal("承認", RetrieveLink(service).GetAttributeValue<string>("pl_decisioncode"));
            Assert.Single(FindLogs(service).Entities);
        }

        [Fact]
        public void 入力Targetに状態や照合列を混入すると保存前に拒否する()
        {
            var service = SeedLink();
            var target = ResultTarget(resultKnown: true, decisionCode: "承認");
            target["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString();
            target["pl_externalrequestkey"] = "df-request-2";

            var result = StandardApprovalResultService.Apply(
                service,
                target,
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.InvalidInput, result.ErrorCode);
            Assert.Equal("承認", target.GetAttributeValue<string>("pl_decisioncode"));
            Assert.Empty(FindLogs(service).Entities);
            Assert.Equal(ApprovalLinkStatus.送信待ち.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
        }

        [Fact]
        public void 撤去予定のApplication参照列を結果入力へ混ぜると拒否する()
        {
            var service = SeedLink();
            var target = ResultTarget(resultKnown: true, decisionCode: "承認");
            target["pl_applicationlookup"] = new EntityReference("ds_application", LegacyApplicationId);

            var result = StandardApprovalResultService.Apply(
                service,
                target,
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.InvalidInput, result.ErrorCode);
            Assert.Empty(FindLogs(service).Entities);
        }

        [Fact]
        public void 不正な判定と結果不明への判定混入は拒否する()
        {
            var unsupported = StandardApprovalResultService.Apply(
                SeedLink(),
                ResultTarget(resultKnown: true, decisionCode: "保留"),
                InitiatingUserId,
                RespondedAtUtc);
            Assert.False(unsupported.Success);
            Assert.Equal(ApprovalResultIngressError.DecisionUnsupported, unsupported.ErrorCode);

            var unknownWithDecisionService = SeedLink();
            var unknownWithDecision = StandardApprovalResultService.Apply(
                unknownWithDecisionService,
                ResultTarget(resultKnown: false, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);
            Assert.False(unknownWithDecision.Success);
            Assert.Equal(ApprovalResultIngressError.InvalidInput, unknownWithDecision.ErrorCode);
            Assert.Empty(FindLogs(unknownWithDecisionService).Entities);
        }

        [Fact]
        public void Linkの外部要求キーが欠損していれば拒否する()
        {
            var missingExternalKey = SeedLink(omitExternalRequestKey: true);
            var externalKeyResult = StandardApprovalResultService.Apply(
                missingExternalKey,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);
            Assert.False(externalKeyResult.Success);
            Assert.Equal(ApprovalResultIngressError.DataIntegrityError, externalKeyResult.ErrorCode);
            Assert.Empty(FindLogs(missingExternalKey).Entities);
        }

        [Fact]
        public void グループ承認結果はApplicationなしで保存できる()
        {
            var service = SeedLink();

            var result = ApplyAndPersist(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                RespondedAtUtc);

            Assert.True(result.Success);
            Assert.Equal(ApprovalResultSourceCodes.PowerAutomateGroup, RetrieveLink(service).GetAttributeValue<string>("pl_approvalsourcecode"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DecisionFlow源のLinkは結果を取り込まない(bool withLegacyApplication)
        {
            var service = SeedLink(sourceCode: "DecisionFlow", withLegacyApplication: withLegacyApplication);

            var result = StandardApprovalResultService.Apply(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.DataIntegrityError, result.ErrorCode);
            Assert.Empty(FindLogs(service).Entities);
            Assert.Equal(ApprovalLinkStatus.送信待ち.ToString(), RetrieveLink(service).GetAttributeValue<string>("pl_linkstatuscode"));
        }

        [Fact]
        public void 撤去済みApplication参照が残るグループ承認Linkも保存できる()
        {
            var service = SeedLink(withLegacyApplication: true);

            var result = ApplyAndPersist(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                RespondedAtUtc);

            Assert.True(result.Success);
        }

        [Fact]
        public void 未指定の結果提供元は推測せず拒否する()
        {
            var service = SeedLink(sourceCode: null);

            var result = StandardApprovalResultService.Apply(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.DataIntegrityError, result.ErrorCode);
            Assert.Empty(FindLogs(service).Entities);
        }

        [Fact]
        public void Linkの保存状態が不整合なら推測して上書きしない()
        {
            var service = SeedLink(
                status: ApprovalLinkStatus.連携済み,
                resultKnown: false);

            var result = StandardApprovalResultService.Apply(
                service,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(ApprovalResultIngressError.DataIntegrityError, result.ErrorCode);
            Assert.Empty(FindLogs(service).Entities);
        }

        [Fact]
        public void 結果確認済みと連携失敗は上書きしない()
        {
            var confirmed = SeedLink(
                status: ApprovalLinkStatus.結果確認済み,
                resultKnown: true,
                decisionCode: "承認",
                decidedAt: RespondedAtUtc);
            var confirmedResult = StandardApprovalResultService.Apply(
                confirmed,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc.AddMinutes(1));
            Assert.False(confirmedResult.Success);
            Assert.Equal(ApprovalResultIngressError.StateCannotIngest, confirmedResult.ErrorCode);

            var failed = SeedLink(status: ApprovalLinkStatus.連携失敗);
            var failedResult = StandardApprovalResultService.Apply(
                failed,
                ResultTarget(resultKnown: true, decisionCode: "承認"),
                InitiatingUserId,
                RespondedAtUtc);
            Assert.False(failedResult.Success);
            Assert.Equal(ApprovalResultIngressError.StateCannotIngest, failedResult.ErrorCode);
        }

        [Fact]
        public void 監査保存に失敗した標準結果更新はトランザクションから漏れない()
        {
            var service = SeedLink();
            service.BeforeCreate = entity => entity.LogicalName == "pl_operationlog"
                ? new InvalidPluginExecutionException("test log create failure")
                : null;

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
            {
                var target = ResultTarget(resultKnown: true, decisionCode: "承認");
                var result = StandardApprovalResultService.Apply(
                    service,
                    target,
                    InitiatingUserId,
                    RespondedAtUtc);
                Assert.True(result.Success);
                service.Update(target);
                return result;
            }));

            var link = RetrieveLink(service);
            Assert.Equal(ApprovalLinkStatus.送信待ち.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.False(link.GetAttributeValue<bool>("pl_resultknown"));
            Assert.Empty(FindLogs(service).Entities);
        }

        private static ApprovalResultIngressResult ApplyAndPersist(
            FakeOrganizationService service,
            Entity target,
            DateTime respondedAtUtc)
        {
            return service.RunInTransaction(() =>
            {
                var result = StandardApprovalResultService.Apply(
                    service,
                    target,
                    InitiatingUserId,
                    respondedAtUtc);
                if (result.Success && !result.IsReplay)
                {
                    service.Update(target);
                }
                return result;
            });
        }

        private static Entity ResultTarget(bool resultKnown, string? decisionCode = null)
        {
            var target = new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, ApprovalLinkId)
            {
                ["pl_resultknown"] = resultKnown,
            };
            if (decisionCode != null)
            {
                target["pl_decisioncode"] = decisionCode;
            }
            return target;
        }

        private static FakeOrganizationService SeedLink(
            ApprovalLinkStatus status = ApprovalLinkStatus.送信待ち,
            bool resultKnown = false,
            string decisionCode = "",
            DateTime? decidedAt = null,
            string? sourceCode = ApprovalResultSourceCodes.PowerAutomateGroup,
            bool withLegacyApplication = false,
            bool omitExternalRequestKey = false)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
            });
            service.Seed(new Entity("pl_submissionversion", SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
            });

            var link = new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, ApprovalLinkId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", SubmissionVersionId),
                ["pl_approvalsourcecode"] = sourceCode,
                ["pl_linkstatuscode"] = status.ToString(),
                ["pl_decisioncode"] = decisionCode,
                ["pl_resultknown"] = resultKnown,
            };
            if (withLegacyApplication)
            {
                // DecisionFlow連携時代のLinkを再現する。列は撤去対象で、サーバーは読まない。
                link["pl_applicationlookup"] = new EntityReference("ds_application", LegacyApplicationId);
            }
            if (!omitExternalRequestKey)
            {
                link["pl_externalrequestkey"] = "df-request-1";
            }
            if (decidedAt.HasValue)
            {
                link["pl_decidedat"] = decidedAt.Value;
            }
            service.Seed(link);
            return service;
        }

        private static Entity RetrieveLink(FakeOrganizationService service)
            => service.Retrieve(
                StandardApprovalCrudContract.ApprovalLinkEntityName,
                ApprovalLinkId,
                new ColumnSet(true));

        private static EntityCollection FindLogs(FakeOrganizationService service)
        {
            var query = new QueryExpression(OperationLogRepository.EntityName)
            {
                ColumnSet = new ColumnSet(true),
            };
            query.Criteria.AddCondition(
                "pl_operationcode",
                ConditionOperator.Equal,
                StandardApprovalResultService.OperationCode);
            return service.RetrieveMultiple(query);
        }
    }
}
