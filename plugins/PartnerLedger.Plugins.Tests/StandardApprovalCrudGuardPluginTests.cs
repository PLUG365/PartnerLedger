using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class StandardApprovalCrudGuardPluginTests
    {
        private static readonly Guid UserId = Guid.NewGuid();
        private static readonly Guid OtherUserId = Guid.NewGuid();
        private static readonly Guid PartnerId = Guid.NewGuid();
        private static readonly Guid InactivePartnerId = Guid.NewGuid();
        private static readonly Guid RequestId = Guid.NewGuid();
        private static readonly Guid SubmissionVersionId = Guid.NewGuid();

        [Fact]
        public void 申請Createは本人と下書き状態をサーバーで補正する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            var target = RequestCreateTarget();

            StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                target,
                StandardApprovalCrudContract.RequestEntityName,
                UserId,
                service);

            Assert.Equal(
                new EntityReference("systemuser", UserId),
                target.GetAttributeValue<EntityReference>("pl_requestinguserlookup"));
            Assert.Equal(ApprovalRequestStatus.下書き.ToString(), target.GetAttributeValue<string>("pl_requeststatuscode"));
            Assert.StartsWith(
                ApprovalActiveRequestKey.Prefix,
                target.GetAttributeValue<string>(ApprovalActiveRequestKey.AttributeName));
        }

        [Fact]
        public void 申請Createは同一対象同一種別の承認中申請を拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                ApprovalRequestStatus.提出中,
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: PartnerId,
                activeRequestKey: ApprovalActiveRequestKey.Build(
                    StandardApprovalCrudContract.PartnerEntityName,
                    PartnerId,
                    "partner.phone")));

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    RequestCreateTarget(),
                    StandardApprovalCrudContract.RequestEntityName,
                    UserId,
                    service));

            Assert.Contains("承認中申請", exception.Message);
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.却下)]
        [InlineData(ApprovalRequestStatus.取消)]
        [InlineData(ApprovalRequestStatus.反映済み)]
        [InlineData(ApprovalRequestStatus.反映失敗)]
        public void 申請Createは終端状態の同一対象同一種別を再利用できる(ApprovalRequestStatus status)
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                status,
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: PartnerId,
                activeRequestKey: ApprovalActiveRequestKey.Build(
                    StandardApprovalCrudContract.PartnerEntityName,
                    PartnerId,
                    "partner.phone")));

            var target = RequestCreateTarget();
            StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                target,
                StandardApprovalCrudContract.RequestEntityName,
                UserId,
                service);

            Assert.StartsWith(
                ApprovalActiveRequestKey.Prefix,
                target.GetAttributeValue<string>(ApprovalActiveRequestKey.AttributeName));
        }

        [Fact]
        public void 申請Createは別取引先の同一申請種別を許可する()
        {
            var otherPartnerId = Guid.NewGuid();
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Partner(otherPartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                ApprovalRequestStatus.提出中,
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: otherPartnerId,
                activeRequestKey: ApprovalActiveRequestKey.Build(
                    StandardApprovalCrudContract.PartnerEntityName,
                    otherPartnerId,
                    "partner.phone")));

            var target = RequestCreateTarget();
            StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                target,
                StandardApprovalCrudContract.RequestEntityName,
                UserId,
                service);
        }

        [Fact]
        public void 申請Createは旧行のキー欠損でも同一対象同一種別を拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                ApprovalRequestStatus.提出中,
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: PartnerId));

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    RequestCreateTarget(),
                    StandardApprovalCrudContract.RequestEntityName,
                    UserId,
                    service));

            Assert.Contains("承認中申請", exception.Message);
        }

        [Fact]
        public void 申請Createは異なる申請種別なら同一対象でも許可する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                ApprovalRequestStatus.提出中,
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: PartnerId,
                activeRequestKey: ApprovalActiveRequestKey.Build(
                    StandardApprovalCrudContract.PartnerEntityName,
                    PartnerId,
                    "partner.phone")));

            var target = RequestCreateTarget();
            target["pl_requesttypecode"] = "partner.address";
            StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                target,
                StandardApprovalCrudContract.RequestEntityName,
                UserId,
                service);
        }

        [Fact]
        public void 申請Createは既存申請の状態が不明ならfail_closedで拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            service.Seed(Request(
                Guid.NewGuid(),
                OtherUserId,
                ApprovalRequestStatus.下書き,
                statusText: "UnknownStatus",
                active: true,
                requestTypeCode: "partner.phone",
                partnerId: PartnerId,
                activeRequestKey: ApprovalActiveRequestKey.Build(
                    StandardApprovalCrudContract.PartnerEntityName,
                    PartnerId,
                    "partner.phone")));

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    RequestCreateTarget(),
                    StandardApprovalCrudContract.RequestEntityName,
                    UserId,
                    service));
        }

        [Fact]
        public void 申請Createは非アクティブな取引先を拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(InactivePartnerId, active: false));
            var target = RequestCreateTarget(InactivePartnerId);

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    target,
                    StandardApprovalCrudContract.RequestEntityName,
                    UserId,
                    service));
        }

        [Fact]
        public void 申請Createの保護列はサーバー補正前に拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, active: true));
            var target = RequestCreateTarget();
            target["pl_requestinguserlookup"] = new EntityReference("systemuser", OtherUserId);

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    target,
                    StandardApprovalCrudContract.RequestEntityName,
                    UserId,
                    service));

            Assert.Contains("AuthorityAttributeForbidden", exception.Message);
        }

        [Fact]
        public void 提出版Createは本人の下書き申請へだけ紐づけ下書きにする()
        {
            var service = new FakeOrganizationService();
            service.Seed(Request(RequestId, UserId, ApprovalRequestStatus.下書き, active: true));
            var target = SubmissionCreateTarget(RequestId);

            StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                target,
                StandardApprovalCrudContract.SubmissionVersionEntityName,
                UserId,
                service);

            Assert.Equal(
                ApprovalSubmissionVersionStatus.下書き.ToString(),
                target.GetAttributeValue<string>("pl_submissionstatuscode"));
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.提出中)]
        [InlineData(ApprovalRequestStatus.承認済み)]
        [InlineData(ApprovalRequestStatus.却下)]
        public void 提出版Createは下書きまたは差戻し以外の申請を拒否する(ApprovalRequestStatus status)
        {
            var service = new FakeOrganizationService();
            service.Seed(Request(RequestId, UserId, status, active: true));

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    SubmissionCreateTarget(RequestId),
                    StandardApprovalCrudContract.SubmissionVersionEntityName,
                    UserId,
                    service));
        }

        [Fact]
        public void 提出版Createは他人の申請への紐付けを拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Request(RequestId, OtherUserId, ApprovalRequestStatus.下書き, active: true));

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    SubmissionCreateTarget(RequestId),
                    StandardApprovalCrudContract.SubmissionVersionEntityName,
                    UserId,
                    service));
        }

        [Fact]
        public void 申請Updateは下書き内容の編集を許可する()
        {
            var service = new FakeOrganizationService();
            var preImage = Request(RequestId, UserId, ApprovalRequestStatus.下書き, active: true);
            var target = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_name"] = "更新後の申請名",
            };

            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                target,
                StandardApprovalCrudContract.RequestEntityName,
                preImage,
                UserId,
                service);
        }

        [Fact]
        public void 申請Updateは提出を標準提出Pluginへ渡し取消は理由付きで管理者経路へ渡す()
        {
            var service = new FakeOrganizationService();
            var preImage = Request(RequestId, UserId, ApprovalRequestStatus.下書き, active: true);
            var submit = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
            };
            var cancelFromReturned = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
                ["pl_cancellationreason"] = "承認者不在のため管理者取消",
            };
            var returnedPreImage = Request(RequestId, UserId, ApprovalRequestStatus.差戻し, active: true);

            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                submit,
                StandardApprovalCrudContract.RequestEntityName,
                preImage,
                UserId,
                service);
            var replay = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
            };
            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                replay,
                StandardApprovalCrudContract.RequestEntityName,
                Request(RequestId, UserId, ApprovalRequestStatus.提出中, active: true),
                UserId,
                service);
            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                cancelFromReturned,
                StandardApprovalCrudContract.RequestEntityName,
                returnedPreImage,
                UserId,
                service);

            // 申請者の取下げは理由なしでも入力ガードを通し、認可と理由の要否は取消サービスへ渡す。
            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
                {
                    ["pl_requeststatuscode"] = ApprovalRequestStatus.取消.ToString(),
                },
                StandardApprovalCrudContract.RequestEntityName,
                preImage,
                UserId,
                service);
        }

        [Fact]
        public void 申請Updateは状態と内容の同時変更を拒否する()
        {
            var target = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
                ["pl_name"] = "同時変更",
            };

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateUpdate(
                    target,
                    StandardApprovalCrudContract.RequestEntityName,
                    Request(RequestId, UserId, ApprovalRequestStatus.下書き, active: true),
                    UserId,
                    new FakeOrganizationService()));
        }

        [Fact]
        public void 標準提出の内部Updateは提出版のサーバー権威列だけを許可する()
        {
            var target = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["pl_submittedbylookup"] = new EntityReference("systemuser", UserId),
                ["pl_submittedat"] = DateTime.UtcNow,
                ["pl_policyversion"] = "1",
                ["pl_rowversiontoken"] = "row-1",
                ["pl_changesummary"] = "対象：取引先「A」",
                ["pl_approvaltitle"] = "会社名の変更：A",
                ["pl_versionnumber"] = 1,
            };

            StandardApprovalCrudGuardPlugin.ValidateTrustedInternalUpdate(
                target,
                StandardApprovalCrudContract.SubmissionVersionEntityName);
        }

        [Fact]
        public void 標準提出の内部Updateでも申請Lookup変更は許可しない()
        {
            var target = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference(StandardApprovalCrudContract.RequestEntityName, RequestId),
            };

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedInternalUpdate(
                    target,
                    StandardApprovalCrudContract.SubmissionVersionEntityName));
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.承認済み)]
        [InlineData(ApprovalRequestStatus.差戻し)]
        [InlineData(ApprovalRequestStatus.却下)]
        public void 標準判定の内部Updateは申請の判定状態だけを許可する(ApprovalRequestStatus status)
        {
            var target = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = status.ToString(),
            };

            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                target,
                StandardApprovalCrudContract.RequestEntityName);
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.反映待ち)]
        [InlineData(ApprovalRequestStatus.反映済み)]
        public void 標準反映の内部Updateは申請の反映状態だけを許可する(ApprovalRequestStatus status)
        {
            var target = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = status.ToString(),
            };

            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                target,
                StandardApprovalCrudContract.RequestEntityName);
        }

        [Fact]
        public void 標準反映の反映失敗は理由とキー解放だけを同じ内部Updateで許可する()
        {
            var failed = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.反映失敗.ToString(),
                [ApprovalActiveRequestKey.AttributeName] = null,
                ["pl_cancellationreason"] = "承認待ちの間に対象が変わったため反映しませんでした。",
            };
            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                failed,
                StandardApprovalCrudContract.RequestEntityName);

            // 反映済みでは理由を書けない。反映失敗でも空の理由は書けない。
            var reflectedWithReason = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.反映済み.ToString(),
                ["pl_cancellationreason"] = "理由",
            };
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    reflectedWithReason,
                    StandardApprovalCrudContract.RequestEntityName));
            var failedWithBlankReason = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.反映失敗.ToString(),
                ["pl_cancellationreason"] = " ",
            };
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    failedWithBlankReason,
                    StandardApprovalCrudContract.RequestEntityName));

            // 提出版の反映失敗は状態だけ。固定日時は付けない。
            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
                {
                    ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映失敗.ToString(),
                },
                StandardApprovalCrudContract.SubmissionVersionEntityName);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
                    {
                        ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映失敗.ToString(),
                        ["pl_fixedat"] = DateTime.UtcNow,
                    },
                    StandardApprovalCrudContract.SubmissionVersionEntityName));
        }

        [Fact]
        public void 標準判定の内部Updateは申請の状態以外を拒否する()
        {
            var target = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.承認済み.ToString(),
                ["pl_name"] = "不正変更",
            };

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    target,
                    StandardApprovalCrudContract.RequestEntityName));
        }

        [Theory]
        [InlineData(ApprovalSubmissionVersionStatus.承認済み)]
        [InlineData(ApprovalSubmissionVersionStatus.差戻し)]
        [InlineData(ApprovalSubmissionVersionStatus.却下)]
        public void 標準判定の内部Updateは提出版の判定状態だけを許可する(ApprovalSubmissionVersionStatus status)
        {
            var target = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_submissionstatuscode"] = status.ToString(),
            };

            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                target,
                StandardApprovalCrudContract.SubmissionVersionEntityName);
        }

        [Fact]
        public void 標準反映の提出版内部Updateは反映待ちと固定日時付き反映済みだけを許可する()
        {
            var waiting = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映待ち.ToString(),
            };
            var reflected = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映済み.ToString(),
                ["pl_fixedat"] = DateTime.UtcNow,
            };

            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                waiting,
                StandardApprovalCrudContract.SubmissionVersionEntityName);
            StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                reflected,
                StandardApprovalCrudContract.SubmissionVersionEntityName);
        }

        [Fact]
        public void 標準反映の反映済み提出版に固定日時が無ければ拒否する()
        {
            var target = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映済み.ToString(),
            };

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    target,
                    StandardApprovalCrudContract.SubmissionVersionEntityName));
        }

        [Fact]
        public void 標準判定の内部Updateは提出中や別テーブルを許可しない()
        {
            var request = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.提出中.ToString(),
            };
            var wrongEntity = new Entity("pl_approvallink", RequestId)
            {
                ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
            };

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    request,
                    StandardApprovalCrudContract.RequestEntityName));
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateTrustedDecisionInternalUpdate(
                    wrongEntity,
                    StandardApprovalCrudContract.ApprovalLinkEntityName));
        }

        [Fact]
        public void 申請Updateは提出済みまたは他人の申請を拒否する()
        {
            var submitted = new Entity(StandardApprovalCrudContract.RequestEntityName, RequestId)
            {
                ["pl_name"] = "変更",
            };
            var submittedPreImage = Request(RequestId, UserId, ApprovalRequestStatus.提出中, active: true);
            var otherPreImage = Request(RequestId, OtherUserId, ApprovalRequestStatus.下書き, active: true);

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateUpdate(
                    submitted,
                    StandardApprovalCrudContract.RequestEntityName,
                    submittedPreImage,
                    UserId,
                    new FakeOrganizationService()));
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateUpdate(
                    submitted,
                    StandardApprovalCrudContract.RequestEntityName,
                    otherPreImage,
                    UserId,
                    new FakeOrganizationService()));
        }

        [Fact]
        public void 提出版Updateは本人の下書きだけを編集できる()
        {
            var service = new FakeOrganizationService();
            service.Seed(Request(RequestId, UserId, ApprovalRequestStatus.下書き, active: true));
            var preImage = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference(StandardApprovalCrudContract.RequestEntityName, RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString(),
            };
            var target = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_changesetjson"] = "{}",
            };

            StandardApprovalCrudGuardPlugin.ValidateUpdate(
                target,
                StandardApprovalCrudContract.SubmissionVersionEntityName,
                preImage,
                UserId,
                service);
        }

        [Fact]
        public void 提出版Updateは提出済み状態とPreImage欠損を拒否する()
        {
            var submittedTarget = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_changesetjson"] = "{}",
            };
            var submittedPreImage = new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName, SubmissionVersionId)
            {
                ["pl_requestlookup"] = new EntityReference(StandardApprovalCrudContract.RequestEntityName, RequestId),
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
            };
            var service = new FakeOrganizationService();
            service.Seed(Request(RequestId, UserId, ApprovalRequestStatus.提出中, active: true));

            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateUpdate(
                    submittedTarget,
                    StandardApprovalCrudContract.SubmissionVersionEntityName,
                    submittedPreImage,
                    UserId,
                    service));
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateUpdate(
                    submittedTarget,
                    StandardApprovalCrudContract.SubmissionVersionEntityName,
                    null,
                    UserId,
                    service));
        }

        [Fact]
        public void 標準ガードはTargetとPrimaryEntityNameの不一致を拒否する()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                StandardApprovalCrudGuardPlugin.ValidateAndApplyCreate(
                    RequestCreateTarget(),
                    "account",
                    UserId,
                    new FakeOrganizationService()));
        }

        private static Entity RequestCreateTarget(Guid? partnerId = null)
            => new Entity(StandardApprovalCrudContract.RequestEntityName)
            {
                ["pl_name"] = "申請",
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference(
                    StandardApprovalCrudContract.PartnerEntityName,
                    partnerId ?? PartnerId),
                ["pl_requestkey"] = "standard-crud-guard-request",
            };

        private static Entity SubmissionCreateTarget(Guid requestId)
            => new Entity(StandardApprovalCrudContract.SubmissionVersionEntityName)
            {
                ["pl_name"] = "提出版 v1",
                ["pl_changesetjson"] = "{}",
                ["pl_requestlookup"] = new EntityReference(
                    StandardApprovalCrudContract.RequestEntityName,
                    requestId),
            };

        private static Entity Partner(Guid id, bool active)
            => new Entity(StandardApprovalCrudContract.PartnerEntityName, id)
            {
                ["statecode"] = new OptionSetValue(active ? 0 : 1),
            };

        private static Entity Request(
            Guid id,
            Guid requesterId,
            ApprovalRequestStatus status,
            bool active,
            string? requestTypeCode = null,
            Guid? partnerId = null,
            Guid? contractId = null,
            string? activeRequestKey = null,
            string? statusText = null)
        {
            var request = new Entity(StandardApprovalCrudContract.RequestEntityName, id)
            {
                ["pl_requestinguserlookup"] = new EntityReference("systemuser", requesterId),
                ["pl_requeststatuscode"] = statusText ?? status.ToString(),
                ["statecode"] = new OptionSetValue(active ? 0 : 1),
            };
            if (requestTypeCode != null) request["pl_requesttypecode"] = requestTypeCode;
            if (partnerId.HasValue)
            {
                request["pl_partnerlookup"] = new EntityReference(
                    StandardApprovalCrudContract.PartnerEntityName,
                    partnerId.Value);
            }
            if (contractId.HasValue)
            {
                request["pl_contractlookup"] = new EntityReference(
                    StandardApprovalCrudContract.ContractEntityName,
                    contractId.Value);
            }
            if (activeRequestKey != null) request[ApprovalActiveRequestKey.AttributeName] = activeRequestKey;
            return request;
        }
    }
}
