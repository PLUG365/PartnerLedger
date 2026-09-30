using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class StandardApprovalSubmitServiceTests
    {
        private static readonly Guid RequesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid SubmissionVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ReturnedSubmissionVersionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid ApproverTeamId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly Guid PartnerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        private static readonly DateTime SubmittedAtUtc = new(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void 下書きを提出し権威情報と承認リンクをサーバー確定する()
        {
            var service = SeedValid();
            var target = SubmitTarget();

            var result = SubmitAndPersist(service, target);

            Assert.True(result.Success);
            Assert.False(result.IsReplay);
            Assert.Equal(RequestId, result.RequestId);
            Assert.Equal(SubmissionVersionId, result.SubmissionVersionId);
            Assert.NotEqual(Guid.Empty, result.ApprovalLinkId);
            Assert.Equal(StandardApprovalSubmitError.None, result.ErrorCode);

            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApprovalRequestStatus.提出中.ToString(), request.GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(ApproverTeamId, request.GetAttributeValue<EntityReference>("pl_approverteamlookup")!.Id);
            Assert.Equal("1", request.GetAttributeValue<string>("pl_approvalpolicyversion"));
            Assert.Equal(SubmittedAtUtc, request.GetAttributeValue<DateTime>("pl_requestedat"));

            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.提出済み.ToString(), version.GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Equal("1", version.GetAttributeValue<string>("pl_policyversion"));
            Assert.StartsWith(ApprovalTargetBaseline.Prefix, version.GetAttributeValue<string>("pl_rowversiontoken"));
            // 承認依頼に載せる概要は、サーバーが検証済みの変更と対象行から作って提出版に残す。
            Assert.Equal(
                "対象：取引先「[TEST] Standard approval partner」\n変更内容：\n・会社名：[TEST] Standard approval partner → [TEST] changed partner",
                version.GetAttributeValue<string>(ApprovalChangeSummary.AttributeName));
            Assert.Equal("会社名の変更：[TEST] Standard approval partner", version.GetAttributeValue<string>(ApprovalChangeSummary.TitleAttributeName));
            Assert.Equal(1, version.GetAttributeValue<int>("pl_versionnumber"));
            Assert.Equal(RequesterId, version.GetAttributeValue<EntityReference>("pl_submittedbylookup")!.Id);
            Assert.Equal(SubmittedAtUtc, version.GetAttributeValue<DateTime>("pl_submittedat"));

            var link = service.Retrieve("pl_approvallink", result.ApprovalLinkId, new ColumnSet(true));
            Assert.Equal(RequestId, link.GetAttributeValue<EntityReference>("pl_requestlookup")!.Id);
            Assert.Equal(SubmissionVersionId, link.GetAttributeValue<EntityReference>("pl_submissionversionlookup")!.Id);
            Assert.Equal(ApprovalResultSource.PowerAutomateGroup.ToString(), link.GetAttributeValue<string>("pl_approvalsourcecode"));
            Assert.Equal(ApprovalLinkStatus.送信待ち.ToString(), link.GetAttributeValue<string>("pl_linkstatuscode"));
            Assert.False(link.GetAttributeValue<bool>("pl_resultknown"));
            Assert.StartsWith("standard-submit-", link.GetAttributeValue<string>("pl_externalrequestkey"));

            var logs = FindSubmitLogs(service);
            Assert.Single(logs.Entities);
            Assert.Equal(RequestId, logs.Entities[0].GetAttributeValue<EntityReference>("pl_targetrequestlookup")!.Id);
            Assert.Equal(SubmissionVersionId, logs.Entities[0].GetAttributeValue<EntityReference>("pl_targetsubmissionversionlookup")!.Id);
            Assert.Equal(result.IdempotencyKey, logs.Entities[0].GetAttributeValue<string>("pl_idempotencykey"));
            var versionUpdate = Assert.Single(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Equal("pl_submissionversion", versionUpdate.Target.LogicalName);
            Assert.Equal(
                ApprovalServerWriteBypass.SubmissionVersionCrudGuardUpdateStepId,
                versionUpdate.Parameters[ApprovalServerWriteBypass.ParameterName]);
        }

        [Fact]
        public void 同じ提出の再送は既存の確定結果を返し内部書込みを増やさない()
        {
            var service = SeedValid();
            var first = SubmitAndPersist(service, SubmitTarget());
            var updatesAfterFirst = service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count();
            var createsAfterFirst = FindSubmitLogs(service).Entities.Count
                + service.RetrieveMultiple(new QueryExpression("pl_approvallink")).Entities.Count;

            var replay = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc.AddMinutes(1));

            Assert.True(first.Success);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.SubmissionVersionId, replay.SubmissionVersionId);
            Assert.Equal(first.ApprovalLinkId, replay.ApprovalLinkId);
            Assert.Equal(updatesAfterFirst, service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Count());
            Assert.Equal(createsAfterFirst, FindSubmitLogs(service).Entities.Count
                + service.RetrieveMultiple(new QueryExpression("pl_approvallink")).Entities.Count);
        }

        [Fact]
        public void 子の確定記録が揃った旧デモの親権威列だけを再送で復旧する()
        {
            var service = SeedValid();
            var first = SubmitAndPersist(service, SubmitTarget());
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            request.Attributes.Remove("pl_approverteamlookup");
            request.Attributes.Remove("pl_approvalpolicyversion");
            request.Attributes.Remove("pl_requestedat");
            var repairTarget = SubmitTarget();

            var repaired = service.RunInTransaction(() =>
            {
                var result = StandardApprovalSubmitService.Submit(
                    service,
                    SubmitTarget(),
                    repairTarget,
                    RequesterId,
                    ApproverTeamId,
                    SubmittedAtUtc.AddMinutes(1));
                if (result.Success)
                {
                    service.Update(repairTarget);
                }
                return result;
            });

            Assert.True(first.Success);
            Assert.True(repaired.Success);
            Assert.True(repaired.IsReplay);
            Assert.Equal(ApproverTeamId, request.GetAttributeValue<EntityReference>("pl_approverteamlookup")!.Id);
            Assert.Equal("1", request.GetAttributeValue<string>("pl_approvalpolicyversion"));
            Assert.Equal(SubmittedAtUtc, request.GetAttributeValue<DateTime>("pl_requestedat"));
            Assert.Single(FindSubmitLogs(service).Entities);
            Assert.Single(service.RetrieveMultiple(new QueryExpression("pl_approvallink")).Entities);
        }

        [Fact]
        public void 入力検証Targetを複製しても実保存Targetへ親の権威情報をスタンプする()
        {
            var service = SeedValid();
            var validationTarget = SubmitTarget();
            var writeTarget = SubmitTarget();

            var result = service.RunInTransaction(() =>
            {
                var submitted = StandardApprovalSubmitService.Submit(
                    service,
                    validationTarget,
                    writeTarget,
                    RequesterId,
                    ApproverTeamId,
                    SubmittedAtUtc);
                if (submitted.Success && !submitted.IsReplay)
                {
                    service.Update(writeTarget);
                }
                return submitted;
            });

            Assert.True(result.Success);
            var request = service.Retrieve("pl_request", RequestId, new ColumnSet(true));
            Assert.Equal(ApproverTeamId, request.GetAttributeValue<EntityReference>("pl_approverteamlookup")!.Id);
            Assert.Equal("1", request.GetAttributeValue<string>("pl_approvalpolicyversion"));
            Assert.Equal(SubmittedAtUtc, request.GetAttributeValue<DateTime>("pl_requestedat"));
        }

        [Fact]
        public void 再送時に対象rowversionが欠損していれば確定結果を返さない()
        {
            var service = SeedValid();
            var first = SubmitAndPersist(service, SubmitTarget());
            var version = service.Retrieve("pl_submissionversion", first.SubmissionVersionId, new ColumnSet(true));
            version["pl_rowversiontoken"] = string.Empty;

            var replay = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc.AddMinutes(1));

            Assert.True(first.Success);
            Assert.False(replay.Success);
            Assert.Equal(StandardApprovalSubmitError.ReplayDataInvalid, replay.ErrorCode);
        }

        [Fact]
        public void 差戻し後は旧版を再利用せず新しい下書き版を固定する()
        {
            var service = SeedValid(
                requestStatus: ApprovalRequestStatus.差戻し,
                submissionVersionStatus: ApprovalSubmissionVersionStatus.差戻し);
            service.Seed(new Entity("pl_submissionversion", ReturnedSubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString(),
                ["pl_changesetjson"] = ChangeSetJson(),
                ["statecode"] = new OptionSetValue(0),
            });

            var result = SubmitAndPersist(service, SubmitTarget());

            Assert.True(result.Success);
            Assert.Equal(ReturnedSubmissionVersionId, result.SubmissionVersionId);
            var oldVersion = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Equal(ApprovalSubmissionVersionStatus.差戻し.ToString(), oldVersion.GetAttributeValue<string>("pl_submissionstatuscode"));
            var newVersion = service.Retrieve("pl_submissionversion", ReturnedSubmissionVersionId, new ColumnSet(true));
            Assert.Equal(2, newVersion.GetAttributeValue<int>("pl_versionnumber"));
        }

        [Fact]
        public void 差戻し済み旧Linkが残っていても現在提出版を再送解決する()
        {
            var service = SeedValid(
                requestStatus: ApprovalRequestStatus.差戻し,
                submissionVersionStatus: ApprovalSubmissionVersionStatus.差戻し);
            service.Retrieve("pl_request", RequestId, new ColumnSet(true))["pl_approverteamlookup"]
                = new EntityReference("team", ApproverTeamId);
            service.Retrieve("pl_request", RequestId, new ColumnSet(true))["pl_approvalpolicyversion"] = "1";
            service.Seed(new Entity("pl_submissionversion", ReturnedSubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString(),
                ["pl_changesetjson"] = ChangeSetJson(),
                ["statecode"] = new OptionSetValue(0),
            });
            service.Seed(new Entity("pl_approvallink", Guid.Parse("99999999-9999-9999-9999-999999999999"))
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionversionlookup"] = new EntityReference("pl_submissionversion", SubmissionVersionId),
                ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                ["pl_externalrequestkey"] = "old-submission-link",
                ["pl_resultknown"] = true,
            });

            var first = SubmitAndPersist(service, SubmitTarget());
            var replay = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc.AddMinutes(1));

            Assert.True(first.Success);
            Assert.Equal(ReturnedSubmissionVersionId, first.SubmissionVersionId);
            Assert.True(replay.Success);
            Assert.True(replay.IsReplay);
            Assert.Equal(first.SubmissionVersionId, replay.SubmissionVersionId);
            Assert.Equal(first.ApprovalLinkId, replay.ApprovalLinkId);
        }

        [Fact]
        public void 申請者以外は提出できず書込みを行わない()
        {
            var service = SeedValid();
            var result = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                OtherUserId,
                ApproverTeamId,
                SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.NotRequester, result.ErrorCode);
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_approvallink")).Entities);
        }

        [Fact]
        public void 主担当の変更を提出すると概要に前後の名前が載る()
        {
            var service = SeedValid(mainOwnerChangeTo: NewMainOwnerId);

            var result = StandardApprovalSubmitService.Submit(service, SubmitTarget(), RequesterId, ApproverTeamId, SubmittedAtUtc);

            Assert.True(result.Success);
            var version = service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true));
            Assert.Contains("・主担当：田中 健一 → 佐伯 菜月", version.GetAttributeValue<string>(ApprovalChangeSummary.AttributeName));
        }

        [Fact]
        public void 通常の利用者でない人を主担当にする申請は提出できない()
        {
            var service = SeedValid(mainOwnerChangeTo: NewMainOwnerId);
            service.Retrieve("systemuser", NewMainOwnerId, new ColumnSet(true))["accessmode"] = new OptionSetValue(3);

            var result = StandardApprovalSubmitService.Submit(service, SubmitTarget(), RequesterId, ApproverTeamId, SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.MainOwnerUnavailable, result.ErrorCode);
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 今と同じ人を主担当にする申請は提出できない()
        {
            var service = SeedValid(mainOwnerChangeTo: CurrentMainOwnerId);

            var result = StandardApprovalSubmitService.Submit(service, SubmitTarget(), RequesterId, ApproverTeamId, SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.MainOwnerUnchanged, result.ErrorCode);
        }

        private static readonly Guid CurrentMainOwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid NewMainOwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [Fact]
        public void 設定の行が無ければ既定値の版0で提出する()
        {
            // 2026-09-27：インポート直後に承認設定の行を作る手順を無くした。行が無いときは既定値（版0）。
            var service = SeedValid();
            var settings = service.RetrieveMultiple(new QueryExpression("pl_settings")).Entities.Single();
            settings["statecode"] = new OptionSetValue(1);

            var result = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc);

            Assert.True(result.Success);
            Assert.Equal("0", service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true)).GetAttributeValue<string>("pl_policyversion"));
        }

        [Fact]
        public void 不正な設定の行があれば既定値へ逃がさず提出を拒否し状態を変えない()
        {
            var service = SeedValid();
            var settings = service.RetrieveMultiple(new QueryExpression("pl_settings")).Entities.Single();
            settings["pl_policyjson"] = "{}";

            var result = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.SettingsInvalid, result.ErrorCode);
            Assert.Equal(ApprovalRequestStatus.下書き.ToString(), service.Retrieve("pl_request", RequestId, new ColumnSet(true)).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(ApprovalSubmissionVersionStatus.下書き.ToString(), service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true)).GetAttributeValue<string>("pl_submissionstatuscode"));
        }

        [Fact]
        public void 下書き版が複数なら推測せず拒否する()
        {
            var service = SeedValid();
            service.Seed(new Entity("pl_submissionversion", ReturnedSubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString(),
                ["pl_changesetjson"] = ChangeSetJson(),
                ["statecode"] = new OptionSetValue(0),
            });

            var result = StandardApprovalSubmitService.Submit(
                service,
                SubmitTarget(),
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.SubmissionVersionAmbiguous, result.ErrorCode);
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void クライアントが権威列を混入した標準Updateは拒否する()
        {
            var service = SeedValid();
            var target = SubmitTarget();
            target["pl_approverteamlookup"] = new EntityReference("team", ApproverTeamId);

            var result = StandardApprovalSubmitService.Submit(
                service,
                target,
                RequesterId,
                ApproverTeamId,
                SubmittedAtUtc);

            Assert.False(result.Success);
            Assert.Equal(StandardApprovalSubmitError.AuthorityAttributeForbidden, result.ErrorCode);
            Assert.Empty(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>());
        }

        [Fact]
        public void 承認リンク作成失敗は提出版と監査をロールバックする()
        {
            var service = SeedValid();
            service.BeforeCreate = entity => entity.LogicalName == "pl_approvallink"
                ? new InvalidPluginExecutionException("test link create failure")
                : null;

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
            {
                var target = SubmitTarget();
                var result = StandardApprovalSubmitService.Submit(
                    service,
                    target,
                    RequesterId,
                    ApproverTeamId,
                    SubmittedAtUtc);
                Assert.True(result.Success);
                service.Update(target);
                return result;
            }));

            Assert.Equal(ApprovalRequestStatus.下書き.ToString(), service.Retrieve("pl_request", RequestId, new ColumnSet(true)).GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.Equal(ApprovalSubmissionVersionStatus.下書き.ToString(), service.Retrieve("pl_submissionversion", SubmissionVersionId, new ColumnSet(true)).GetAttributeValue<string>("pl_submissionstatuscode"));
            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_approvallink")).Entities);
            Assert.Empty(FindSubmitLogs(service).Entities);
        }

        private static StandardApprovalSubmitResult SubmitAndPersist(
            FakeOrganizationService service,
            Entity target)
        {
            return service.RunInTransaction(() =>
            {
                var result = StandardApprovalSubmitService.Submit(
                    service,
                    target,
                    RequesterId,
                    ApproverTeamId,
                    SubmittedAtUtc);
                if (result.Success && !result.IsReplay)
                {
                    service.Update(target);
                }
                return result;
            });
        }

        private static FakeOrganizationService SeedValid(
            ApprovalRequestStatus requestStatus = ApprovalRequestStatus.下書き,
            ApprovalSubmissionVersionStatus submissionVersionStatus = ApprovalSubmissionVersionStatus.下書き,
            Guid? mainOwnerChangeTo = null)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["pl_name"] = "[TEST] Standard approval partner",
                ["statecode"] = new OptionSetValue(0),
                ["pl_tradingstatuscode"] = new OptionSetValue(100000000),
                ["pl_mainownerlookup"] = new EntityReference("systemuser", CurrentMainOwnerId),
            });
            service.Seed(new Entity("systemuser", CurrentMainOwnerId) { ["fullname"] = "田中 健一", ["accessmode"] = new OptionSetValue(0), ["isdisabled"] = false });
            service.Seed(new Entity("systemuser", NewMainOwnerId) { ["fullname"] = "佐伯 菜月", ["accessmode"] = new OptionSetValue(0), ["isdisabled"] = false });
            service.Seed(new Entity("team", ApproverTeamId)
            {
            });
            service.Seed(new Entity("pl_settings", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}",
            });
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = requestStatus.ToString(),
                ["pl_requesttypecode"] = mainOwnerChangeTo.HasValue ? ApprovalPolicyContract.MainOwnerRequestType : ApprovalPolicyContract.CompanyNameRequestType,
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", RequesterId),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_requestkey"] = "standard-submit-request",
                ["statecode"] = new OptionSetValue(0),
            });
            service.Seed(new Entity("pl_submissionversion", SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                ["pl_submissionstatuscode"] = submissionVersionStatus.ToString(),
                ["pl_changesetjson"] = mainOwnerChangeTo.HasValue
                    ? "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"" + PartnerId + "\"},\"changes\":[{\"attribute\":\"pl_mainownerlookup\",\"value\":\"" + mainOwnerChangeTo.Value + "\"}]}"
                    : ChangeSetJson(),
                ["statecode"] = new OptionSetValue(0),
                ["pl_versionnumber"] = submissionVersionStatus == ApprovalSubmissionVersionStatus.差戻し ? 1 : (int?)null,
                ["pl_policyversion"] = submissionVersionStatus == ApprovalSubmissionVersionStatus.差戻し ? "1" : null,
                ["pl_rowversiontoken"] = submissionVersionStatus == ApprovalSubmissionVersionStatus.差戻し ? "1" : null,
            });
            return service;
        }

        private static Entity SubmitTarget()
            => new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
            };

        private static string ChangeSetJson()
            => "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\""
                + PartnerId.ToString()
                + "\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"[TEST] changed partner\"}]}";

        private static EntityCollection FindSubmitLogs(FakeOrganizationService service)
        {
            var query = new QueryExpression("pl_operationlog")
            {
                ColumnSet = new ColumnSet(true),
            };
            query.Criteria.AddCondition("pl_operationcode", ConditionOperator.Equal, "SubmitApproval");
            return service.RetrieveMultiple(query);
        }
    }
}
