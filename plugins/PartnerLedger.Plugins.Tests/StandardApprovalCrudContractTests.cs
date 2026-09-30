using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class StandardApprovalCrudContractTests
    {
        private static readonly Guid PartnerId = Guid.NewGuid();
        private static readonly Guid ContractId = Guid.NewGuid();
        private static readonly Guid RequestId = Guid.NewGuid();

        [Fact]
        public void 申請Createは許可された業務入力を受け入れる()
        {
            var target = new Entity("pl_request")
            {
                ["pl_name"] = "会社名変更申請",
                ["pl_requesttypecode"] = "partner.company-name",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_contract", ContractId),
                ["pl_requestkey"] = "request-1",
            };

            Assert.True(StandardApprovalCrudContract.ValidateRequestCreate(target).IsValid);
        }

        [Theory]
        [InlineData("pl_requestinguserlookup")]
        [InlineData("pl_approverteamlookup")]
        [InlineData("pl_approvalpolicyversion")]
        [InlineData("pl_requestedat")]
        [InlineData("statecode")]
        [InlineData("statuscode")]
        public void 申請Createは権威列を拒否する(string attributeName)
        {
            var target = new Entity("pl_request")
            {
                ["pl_name"] = "申請",
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_requestkey"] = "request-2",
                [attributeName] = attributeName.Contains("lookup")
                    ? (object)new EntityReference("systemuser", Guid.NewGuid())
                    : "forbidden",
            };

            var result = StandardApprovalCrudContract.ValidateRequestCreate(target);

            Assert.False(result.IsValid);
            Assert.Equal(StandardApprovalCrudError.AuthorityAttributeForbidden, result.Error);
        }

        [Fact]
        public void 申請Createは申請キーとPartnerを必須にする()
        {
            var missingKey = new Entity("pl_request")
            {
                ["pl_name"] = "申請",
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            };
            var wrongPartner = new Entity("pl_request")
            {
                ["pl_name"] = "申請",
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference("account", PartnerId),
                ["pl_requestkey"] = "request-3",
            };

            Assert.Equal(StandardApprovalCrudError.RequiredAttributeMissing, StandardApprovalCrudContract.ValidateRequestCreate(missingKey).Error);
            Assert.Equal(StandardApprovalCrudError.LookupMismatch, StandardApprovalCrudContract.ValidateRequestCreate(wrongPartner).Error);
        }

        [Fact]
        public void 提出版Createは変更内容と申請Lookupだけを受け入れる()
        {
            var target = new Entity("pl_submissionversion")
            {
                ["pl_name"] = "提出版 v1",
                ["pl_changesetjson"] = "{\"schemaVersion\":1}",
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
            };

            Assert.True(StandardApprovalCrudContract.ValidateSubmissionVersionCreate(target).IsValid);
        }

        [Theory]
        [InlineData("pl_policyversion")]
        [InlineData("pl_rowversiontoken")]
        [InlineData("pl_changesummary")]
        [InlineData("pl_approvaltitle")]
        [InlineData("pl_versionnumber")]
        [InlineData("pl_submittedbylookup")]
        [InlineData("pl_submittedat")]
        [InlineData("pl_fixedat")]
        [InlineData("pl_submissionstatuscode")]
        [InlineData("statecode")]
        public void 提出版Createは版と提出の権威列を拒否する(string attributeName)
        {
            var target = new Entity("pl_submissionversion")
            {
                ["pl_name"] = "提出版 v1",
                ["pl_changesetjson"] = "{}",
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
                [attributeName] = attributeName.EndsWith("lookup", StringComparison.Ordinal)
                    ? (object)new EntityReference("systemuser", Guid.NewGuid())
                    : "forbidden",
            };

            var result = StandardApprovalCrudContract.ValidateSubmissionVersionCreate(target);

            Assert.False(result.IsValid);
            Assert.Equal(StandardApprovalCrudError.AuthorityAttributeForbidden, result.Error);
        }

        [Fact]
        public void 提出版Createは申請Lookupの型を検査する()
        {
            var target = new Entity("pl_submissionversion")
            {
                ["pl_name"] = "提出版 v1",
                ["pl_changesetjson"] = "{}",
                ["pl_requestlookup"] = new EntityReference("account", RequestId),
            };

            Assert.Equal(StandardApprovalCrudError.LookupMismatch, StandardApprovalCrudContract.ValidateSubmissionVersionCreate(target).Error);
        }

        [Fact]
        public void 申請Updateは提出または取消だけを公開状態として受け入れる()
        {
            var submit = new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = "提出中",
            };
            var cancel = new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = "取消",
                ["pl_cancellationreason"] = "承認者不在のため管理者取消",
            };
            var approved = new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = "承認済み",
            };

            Assert.True(StandardApprovalCrudContract.ValidateRequestUpdate(submit).IsValid);
            Assert.True(StandardApprovalCrudContract.ValidateRequestUpdate(cancel).IsValid);
            // 理由の要否は実行者で変わる（申請者の取下げは任意、管理者取消は必須）ため、
            // 入力ガードでは形だけを見て、要否はサーバーの取消サービスが判定する。
            Assert.True(StandardApprovalCrudContract.ValidateRequestUpdate(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = "取消",
            }).IsValid);
            Assert.Equal(StandardApprovalCrudError.ValueTooLong, StandardApprovalCrudContract.ValidateRequestUpdate(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = "取消",
                ["pl_cancellationreason"] = new string('あ', ApprovalCancellationContract.ReasonMaxLength + 1),
            }).Error);
            Assert.Equal(StandardApprovalCrudError.InvalidStatus, StandardApprovalCrudContract.ValidateRequestUpdate(approved).Error);
        }

        [Fact]
        public void 申請Updateは状態変更以外の申請専用列を拒否する()
        {
            var target = new Entity("pl_request", RequestId)
            {
                ["pl_name"] = "更新後",
                ["pl_requestkey"] = "changed",
            };

            Assert.Equal(StandardApprovalCrudError.UnexpectedAttribute, StandardApprovalCrudContract.ValidateRequestUpdate(target).Error);
        }

        [Fact]
        public void 提出版Updateは下書き内容だけを受け入れ提出済み状態を拒否する()
        {
            var draft = new Entity("pl_submissionversion", Guid.NewGuid())
            {
                ["pl_name"] = "修正版",
                ["pl_changesetjson"] = "{}",
            };
            var submitted = new Entity("pl_submissionversion", Guid.NewGuid())
            {
                ["pl_submissionstatuscode"] = "提出済み",
            };

            Assert.True(StandardApprovalCrudContract.ValidateSubmissionVersionUpdate(draft).IsValid);
            Assert.Equal(StandardApprovalCrudError.AuthorityAttributeForbidden, StandardApprovalCrudContract.ValidateSubmissionVersionUpdate(submitted).Error);
            // 承認依頼に載る概要はサーバーだけが書く。下書きの編集でも書けない。
            var forgedSummary = new Entity("pl_submissionversion", Guid.NewGuid())
            {
                ["pl_changesetjson"] = "{}",
                ["pl_changesummary"] = "対象：取引先「偽」",
            };
            Assert.Equal(StandardApprovalCrudError.AuthorityAttributeForbidden, StandardApprovalCrudContract.ValidateSubmissionVersionUpdate(forgedSummary).Error);
        }

        [Fact]
        public void Createの変更内容と名前の上限を検査する()
        {
            var request = new Entity("pl_request")
            {
                ["pl_name"] = new string('x', 851),
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_requestkey"] = "request-4",
            };
            var version = new Entity("pl_submissionversion")
            {
                ["pl_name"] = "提出版",
                ["pl_changesetjson"] = new string('x', 1048577),
                ["pl_requestlookup"] = new EntityReference("pl_request", RequestId),
            };

            Assert.Equal(StandardApprovalCrudError.ValueTooLong, StandardApprovalCrudContract.ValidateRequestCreate(request).Error);
            Assert.Equal(StandardApprovalCrudError.ValueTooLong, StandardApprovalCrudContract.ValidateSubmissionVersionCreate(version).Error);
        }

        [Fact]
        public void Updateは対象IDを必須にする()
        {
            var request = new Entity("pl_request") { ["pl_name"] = "更新" };
            var version = new Entity("pl_submissionversion") { ["pl_name"] = "更新" };

            Assert.Equal(StandardApprovalCrudError.TargetIdRequired, StandardApprovalCrudContract.ValidateRequestUpdate(request).Error);
            Assert.Equal(StandardApprovalCrudError.TargetIdRequired, StandardApprovalCrudContract.ValidateSubmissionVersionUpdate(version).Error);
        }

        [Fact]
        public void 不明列と不正エンティティを受け入れない()
        {
            var unknown = new Entity("pl_request")
            {
                ["pl_name"] = "申請",
                ["pl_requesttypecode"] = "partner.phone",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_requestkey"] = "request-5",
                ["pl_unknown"] = "x",
            };

            Assert.Equal(StandardApprovalCrudError.UnexpectedAttribute, StandardApprovalCrudContract.ValidateRequestCreate(unknown).Error);
            Assert.Equal(StandardApprovalCrudError.EntityMismatch, StandardApprovalCrudContract.ValidateRequestCreate(new Entity("account")).Error);
        }

        [Fact]
        public void 権威列一覧はPC入力境界と同じ列を示す()
        {
            Assert.True(StandardApprovalCrudContract.IsAuthorityAttribute("pl_approverteamlookup"));
            Assert.True(StandardApprovalCrudContract.IsAuthorityAttribute("pl_submissionstatuscode"));
            Assert.False(StandardApprovalCrudContract.IsAuthorityAttribute("pl_name"));
        }
    }
}
