using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalChangeSummaryTests
    {
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid PartnerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid ContractId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        [Fact]
        public void 取引先の変更は対象と項目ごとの変更前後を業務の表記で書く()
        {
            var target = PartnerTarget(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_tradingstatuscode"] = new OptionSetValue(100000001),
            });
            var changeSet = Validate("pl_partner", PartnerId,
                "[{\"attribute\":\"pl_name\",\"value\":\"新会社\"},{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000002}]",
                "pl_name", "pl_tradingstatuscode");

            var summary = ApprovalChangeSummary.Build(target, changeSet);

            Assert.Equal(
                "対象：取引先「現行会社」\n変更内容：\n・会社名：現行会社 → 新会社\n・取引状態：取引中 → 休止中",
                summary);
        }

        [Fact]
        public void 主担当の変更は前後の人を名前で書く()
        {
            var current = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var next = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var target = PartnerTarget(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
                ["pl_mainownerlookup"] = new EntityReference("systemuser", current),
            });
            var changeSet = Validate("pl_partner", PartnerId,
                "[{\"attribute\":\"pl_mainownerlookup\",\"value\":\"" + next + "\"}]",
                "pl_mainownerlookup");
            var names = new Dictionary<Guid, string> { [current] = "田中 健一", [next] = "佐伯 菜月" };

            var summary = ApprovalChangeSummary.Build(target, changeSet, id => names.TryGetValue(id, out var name) ? name : null);

            Assert.Equal("対象：取引先「現行会社」\n変更内容：\n・主担当：田中 健一 → 佐伯 菜月", summary);
            Assert.Equal("主担当の変更：現行会社", ApprovalChangeSummary.BuildTitle(target, ApprovalPolicyContract.MainOwnerRequestType));
        }

        [Fact]
        public void 契約の変更は取引先名と選択肢と真偽と日付と空欄を表記する()
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
                ["pl_name"] = "基本契約",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId) { Name = "青空商事" },
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
                ["pl_autorenew"] = true,
                ["pl_enddate"] = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                ["pl_link"] = "https://example.com/contract",
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
            var changeSet = Validate("pl_contract", ContractId,
                "[{\"attribute\":\"pl_contractstatuscode\",\"value\":100000001},{\"attribute\":\"pl_autorenew\",\"value\":false},"
                + "{\"attribute\":\"pl_enddate\",\"value\":\"2028-03-31T00:00:00+09:00\"},{\"attribute\":\"pl_noticedate\",\"value\":null},"
                + "{\"attribute\":\"pl_link\",\"value\":null}]",
                "pl_contractstatuscode", "pl_autorenew", "pl_enddate", "pl_noticedate", "pl_link");

            var summary = ApprovalChangeSummary.Build(target, changeSet);

            // 日付は日本時間の年月日。アプリが送るUTCの0時も、日本時間の0時も同じ日になる。
            Assert.Equal(
                "対象：契約「基本契約」（取引先「青空商事」）\n変更内容：\n"
                + "・判断：更新（締結済み） → 終了\n"
                + "・契約終了日：2027-03-31 → 2028-03-31\n"
                + "・解約通知期限：（空欄） → （空欄）\n"
                + "・自動更新：あり → なし\n"
                + "・契約書リンク：https://example.com/contract → （空欄）",
                summary);
        }

        [Fact]
        public void 値の改行とリンクの書式を無害化し全体を上限に収める()
        {
            var target = PartnerTarget(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "旧会社\r\n# 見出し",
                ["pl_address"] = new string('あ', 200),
            });
            var changeSet = Validate("pl_partner", PartnerId,
                "[{\"attribute\":\"pl_name\",\"value\":\"[こちら](https://example.com/phish)\"},{\"attribute\":\"pl_address\",\"value\":\"" + new string('い', 200) + "\"}]",
                "pl_name", "pl_address");

            var summary = ApprovalChangeSummary.Build(target, changeSet);

            Assert.Contains("・会社名：旧会社 # 見出し → [こちら] (https://example.com/phish)", summary);
            Assert.DoesNotContain("](", summary);
            Assert.DoesNotContain("\r", summary);
            Assert.True(summary.Length <= ApprovalChangeSummary.MaxLength);
        }

        [Theory]
        [InlineData(ApprovalPolicyContract.CompanyNameRequestType, "会社名の変更：現行会社")]
        [InlineData(ApprovalPolicyContract.TradingStatusRequestType, "取引状態の変更：現行会社")]
        [InlineData(ApprovalPolicyContract.AddressRequestType, "住所の変更：現行会社")]
        [InlineData(ApprovalPolicyContract.PhoneRequestType, "代表電話の変更：現行会社")]
        [InlineData("unknown.type", "承認申請：現行会社")]
        public void 件名は申請の種類と対象の名前から作る(string requestType, string expected)
        {
            var target = PartnerTarget(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "現行会社",
            });

            Assert.Equal(expected, ApprovalChangeSummary.BuildTitle(target, requestType));
        }

        [Fact]
        public void 契約の件名は取引先名を添え改行を除き上限に収める()
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
                ["pl_name"] = "基本契約\n2026",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId) { Name = "青空商事" },
            });
            var target = ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;

            Assert.Equal("契約の更新・終了判断：基本契約 2026（青空商事）", ApprovalChangeSummary.BuildTitle(target, ApprovalPolicyContract.ContractUpdateRequestType));

            var longTarget = PartnerTarget(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = new string('あ', 500),
            });
            Assert.True(ApprovalChangeSummary.BuildTitle(longTarget, ApprovalPolicyContract.CompanyNameRequestType).Length <= ApprovalChangeSummary.TitleMaxLength);
        }

        [Fact]
        public void 対象または変更セットがなければ使えない()
        {
            var target = PartnerTarget(new Entity("pl_partner", PartnerId) { ["statecode"] = new OptionSetValue(0) });
            Assert.Throws<ArgumentNullException>(() => ApprovalChangeSummary.Build(null!, Validate("pl_partner", PartnerId, "[{\"attribute\":\"pl_name\",\"value\":\"A\"}]", "pl_name")));
            Assert.Throws<ArgumentNullException>(() => ApprovalChangeSummary.Build(target, null!));
        }

        private static ApprovalTargetRecord PartnerTarget(Entity partner)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(partner);
            return ApprovalTargetRepository.RetrieveForRequest(service, RequestId).Target!;
        }

        private static ApprovalChangeSet Validate(string entity, Guid id, string changes, params string[] allowed)
        {
            var result = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = "{\"schemaVersion\":1,\"target\":{\"entity\":\"" + entity + "\",\"id\":\"" + id + "\"},\"changes\":" + changes + "}",
                ExpectedEntityName = entity,
                ExpectedTargetId = id,
                AllowedAttributes = new HashSet<string>(allowed, StringComparer.Ordinal),
            });
            Assert.True(result.IsValid, result.Error.ToString());
            return result.ChangeSet!;
        }
    }
}
