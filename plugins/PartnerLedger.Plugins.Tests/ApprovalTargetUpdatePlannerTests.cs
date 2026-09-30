using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalTargetUpdatePlannerTests
    {
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid PartnerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ContractId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        [Fact]
        public void 契約変更を型付き更新要求へ変換しrow_version競合制御を固定する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_contract", ContractId),
            });
            service.Seed(new Entity("pl_contract", ContractId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target;
            var changeSet = ValidateContract(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_contract\",\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"更新契約\"},{\"attribute\":\"pl_contractstatuscode\",\"value\":100000001},{\"attribute\":\"pl_autorenew\",\"value\":false},{\"attribute\":\"pl_enddate\",\"value\":\"2027-03-31T00:00:00+09:00\"},{\"attribute\":\"pl_noticedate\",\"value\":null},{\"attribute\":\"pl_link\",\"value\":null}]}" );

            var result = ApprovalTargetUpdatePlanner.Build(target, changeSet, target!.RowVersion);

            Assert.True(result.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.None, result.Error);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, result.Plan!.UpdateRequest.ConcurrencyBehavior);
            Assert.Equal("1", result.Plan.TargetRowVersion);
            var update = result.Plan.UpdateRequest.Target;
            Assert.Equal("更新契約", update.GetAttributeValue<string>("pl_name"));
            Assert.Equal(new OptionSetValue(100000001), update.GetAttributeValue<OptionSetValue>("pl_contractstatuscode"));
            Assert.False(update.GetAttributeValue<bool>("pl_autorenew"));
            Assert.Equal(new DateTime(2027, 3, 30, 15, 0, 0, DateTimeKind.Utc), update.GetAttributeValue<DateTime>("pl_enddate"));
            Assert.True(update.Attributes.ContainsKey("pl_noticedate"));
            Assert.Null(update["pl_noticedate"]);
            Assert.True(update.Attributes.ContainsKey("pl_link"));
            Assert.Null(update["pl_link"]);
            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 取引先の状態変更と名前変更を一つの更新要求へ変換する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_tradingstatuscode"] = new OptionSetValue(100000000),
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target;
            var status = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001}]}" );
            var statusPlan = ApprovalTargetUpdatePlanner.Build(target, status, target!.RowVersion);
            Assert.True(statusPlan.IsValid);
            Assert.Equal(new OptionSetValue(100000001), statusPlan.Plan!.UpdateRequest.Target.GetAttributeValue<OptionSetValue>("pl_tradingstatuscode"));

            var name = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"株式会社　ＡＢＣ㈱\"}]}" );
            var namePlan = ApprovalTargetUpdatePlanner.Build(target, name, target.RowVersion);
            Assert.True(namePlan.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.None, namePlan.Error);
            Assert.Equal("株式会社　ＡＢＣ㈱", namePlan.Plan!.UpdateRequest.Target.GetAttributeValue<string>("pl_name"));
            Assert.Equal("ABC", namePlan.Plan.UpdateRequest.Target.GetAttributeValue<string>("pl_normalizedname"));
        }

        [Fact]
        public void 取引先の住所電話変更をnullを含む型付き更新要求へ変換する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_address"] = "旧住所",
                ["pl_phone"] = "旧電話",
            });

            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
            var changeSet = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_address\",\"value\":null},{\"attribute\":\"pl_phone\",\"value\":\"03-1111-2222\"}]}",
                "pl_address",
                "pl_phone");

            var result = ApprovalTargetUpdatePlanner.Build(target, changeSet, target.RowVersion);

            Assert.True(result.IsValid);
            Assert.True(result.Plan!.UpdateRequest.Target.Attributes.ContainsKey("pl_address"));
            Assert.Null(result.Plan.UpdateRequest.Target["pl_address"]);
            Assert.Equal("03-1111-2222", result.Plan.UpdateRequest.Target.GetAttributeValue<string>("pl_phone"));
        }

        [Fact]
        public void 主担当の変更をユーザーへの参照として更新要求へ変換する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
            });

            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
            var changeSet = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_mainownerlookup\",\"value\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"}]}",
                "pl_mainownerlookup");

            var result = ApprovalTargetUpdatePlanner.Build(target, changeSet, target.RowVersion);

            Assert.True(result.IsValid);
            var owner = result.Plan!.UpdateRequest.Target.GetAttributeValue<EntityReference>("pl_mainownerlookup");
            Assert.Equal("systemuser", owner!.LogicalName);
            Assert.Equal(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), owner.Id);
        }

        [Fact]
        public void 反映する文字列は標準Updateと同じく前後の空白を除き空白だけの任意項目はnullにする()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_address"] = "旧住所",
                ["pl_phone"] = "旧電話",
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
            var changeSet = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"  株式会社A  \"},{\"attribute\":\"pl_address\",\"value\":\"   \"},{\"attribute\":\"pl_phone\",\"value\":\" 03-1111-2222 \"}]}",
                "pl_name",
                "pl_address",
                "pl_phone");

            var result = ApprovalTargetUpdatePlanner.Build(target, changeSet, target.RowVersion);

            Assert.True(result.IsValid);
            var update = result.Plan!.UpdateRequest.Target;
            Assert.Equal("株式会社A", update.GetAttributeValue<string>("pl_name"));
            Assert.True(update.Attributes.ContainsKey("pl_address"));
            Assert.Null(update["pl_address"]);
            Assert.Equal("03-1111-2222", update.GetAttributeValue<string>("pl_phone"));
            Assert.Equal("A", update.GetAttributeValue<string>("pl_normalizedname"));
        }

        [Fact]
        public void 空白だけの取引先名は変更セットの時点で拒否する()
        {
            var result = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"   \"}]}",
                ExpectedEntityName = "pl_partner",
                ExpectedTargetId = PartnerId,
                AllowedAttributes = new HashSet<string>(new[] { "pl_name" }, StringComparer.Ordinal),
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void 対象とrow_versionが変わった更新計画を拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
            var changeSet = ValidatePartner(
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001}]}" );

            // 提出時に記録した版（旧形式＝行全体）と今の行が違う＝提出後に対象が変わった。
            var stale = ApprovalTargetUpdatePlanner.Build(target, changeSet, "old-row-version");
            Assert.False(stale.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.TargetChangedSinceSubmission, stale.Error);

            // 新形式（変更する項目だけの指紋）は、その項目が同じなら行全体の版が違っても通る。
            var fieldToken = ApprovalTargetBaseline.Compute(target.CurrentRow, new[] { "pl_tradingstatuscode" });
            Assert.True(ApprovalTargetUpdatePlanner.Build(target, changeSet, fieldToken).IsValid);
            var otherFieldToken = ApprovalTargetBaseline.Compute(
                new Entity("pl_partner", PartnerId) { ["pl_tradingstatuscode"] = new OptionSetValue(100000002) },
                new[] { "pl_tradingstatuscode" });
            var changedField = ApprovalTargetUpdatePlanner.Build(target, changeSet, otherFieldToken);
            Assert.False(changedField.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.TargetChangedSinceSubmission, changedField.Error);

            target.CurrentRow.RowVersion = "2";
            var changedAfterRead = ApprovalTargetUpdatePlanner.Build(target, changeSet, "1");
            Assert.False(changedAfterRead.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.TargetRowVersionMismatch, changedAfterRead.Error);

            var otherTarget = new FakeOrganizationService();
            otherTarget.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_contract", ContractId),
            });
            otherTarget.Seed(new Entity("pl_contract", ContractId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            var contractTarget = ApprovalTargetRepository.RetrieveForRequest(otherTarget, RequestId).Target;
            var wrongTarget = ApprovalTargetUpdatePlanner.Build(contractTarget, changeSet, contractTarget!.RowVersion);
            Assert.False(wrongTarget.IsValid);
            Assert.Equal(ApprovalTargetUpdatePlanError.TargetEntityMismatch, wrongTarget.Error);
        }

        private static ApprovalChangeSet ValidatePartner(string json, params string[] allowedAttributes)
        {
            var result = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = json,
                ExpectedEntityName = "pl_partner",
                ExpectedTargetId = PartnerId,
                AllowedAttributes = new HashSet<string>(allowedAttributes.Length == 0
                    ? new[] { "pl_name", "pl_tradingstatuscode" }
                    : allowedAttributes, StringComparer.Ordinal),
            });
            Assert.True(result.IsValid);
            return result.ChangeSet!;
        }

        private static ApprovalChangeSet ValidateContract(string json)
        {
            var result = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = json,
                ExpectedEntityName = "pl_contract",
                ExpectedTargetId = ContractId,
                AllowedAttributes = new HashSet<string>(new[] { "pl_name", "pl_contractstatuscode", "pl_autorenew", "pl_enddate", "pl_noticedate", "pl_link" }, StringComparer.Ordinal),
            });
            Assert.True(result.IsValid);
            return result.ChangeSet!;
        }
    }
}
