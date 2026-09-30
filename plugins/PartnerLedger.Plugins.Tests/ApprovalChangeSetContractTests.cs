using System;
using System.Collections.Generic;
using System.Linq;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalChangeSetContractTests
    {
        private static readonly Guid PartnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid ContractId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        [Fact]
        public void 取引先の許可項目を型付き変更として正規化する()
        {
            var result = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001},{\"attribute\":\"pl_name\",\"value\":\"株式会社サンプル\"}]}",
                "pl_name",
                "pl_tradingstatuscode");

            Assert.True(result.IsValid);
            Assert.NotNull(result.ChangeSet);
            Assert.Equal(new[] { "pl_name", "pl_tradingstatuscode" }, result.ChangeSet!.Operations.Select(operation => operation.AttributeName));
            Assert.Equal(ApprovalChangeValueKind.Text, result.ChangeSet.Operations[0].Value.Kind);
            Assert.Equal(ApprovalChangeValueKind.Choice, result.ChangeSet.Operations[1].Value.Kind);
            Assert.Equal(100000001, result.ChangeSet.Operations[1].Value.ChoiceValue);
            Assert.Equal(64, result.ChangeSet.Fingerprint.Length);
        }

        [Fact]
        public void 主担当はユーザーのIDとして受け付け_IDでない値と空を拒否する()
        {
            // 2026-09-29：主担当の変更。値は新しい主担当のsystemuserid（GUID文字列）。
            const string prefix = "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_mainownerlookup\",\"value\":";
            var valid = Validate("pl_partner", PartnerId, prefix + "\"CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC\"}]}", "pl_mainownerlookup");
            var notGuid = Validate("pl_partner", PartnerId, prefix + "\"someone\"}]}", "pl_mainownerlookup");
            var empty = Validate("pl_partner", PartnerId, prefix + "\"00000000-0000-0000-0000-000000000000\"}]}", "pl_mainownerlookup");
            var nullValue = Validate("pl_partner", PartnerId, prefix + "null}]}", "pl_mainownerlookup");
            var number = Validate("pl_partner", PartnerId, prefix + "1}]}", "pl_mainownerlookup");
            var notAllowed = Validate("pl_partner", PartnerId, prefix + "\"cccccccc-cccc-cccc-cccc-cccccccccccc\"}]}", "pl_name");

            Assert.True(valid.IsValid);
            var operation = Assert.Single(valid.ChangeSet!.Operations);
            Assert.Equal(ApprovalChangeValueKind.SystemUser, operation.Value.Kind);
            Assert.Equal(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), operation.Value.UserIdValue);
            Assert.Equal(ApprovalChangeSetValidationError.ValueTypeMismatch, notGuid.Error);
            Assert.Equal(ApprovalChangeSetValidationError.ValueRequired, empty.Error);
            Assert.Equal(ApprovalChangeSetValidationError.ValueRequired, nullValue.Error);
            Assert.Equal(ApprovalChangeSetValidationError.ValueTypeMismatch, number.Error);
            Assert.False(notAllowed.IsValid);
        }

        [Fact]
        public void 取引先の住所電話は任意文字列としてnullも受理する()
        {
            var result = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_address\",\"value\":null},{\"attribute\":\"pl_phone\",\"value\":\"03-0000-0000\"}]}",
                "pl_address",
                "pl_phone");

            Assert.True(result.IsValid);
            Assert.True(result.ChangeSet!.Operations.Single(operation => operation.AttributeName == "pl_address").Value.IsNull);
            Assert.Equal("03-0000-0000", result.ChangeSet.Operations.Single(operation => operation.AttributeName == "pl_phone").Value.TextValue);
        }

        [Fact]
        public void 契約の日付はオフセットを必須にしてUTCへ正規化する()
        {
            var result = Validate(
                "pl_contract",
                ContractId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_contract\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_enddate\",\"value\":\"2026-09-13T12:00:00+09:00\"},{\"attribute\":\"pl_noticedate\",\"value\":null},{\"attribute\":\"pl_autorenew\",\"value\":true}]}",
                "pl_enddate",
                "pl_noticedate",
                "pl_autorenew");

            Assert.True(result.IsValid);
            Assert.Equal(
                new DateTime(2026, 9, 13, 3, 0, 0, DateTimeKind.Utc),
                result.ChangeSet!.Operations.Single(operation => operation.AttributeName == "pl_enddate").Value.DateTimeValue);
            Assert.True(result.ChangeSet.Operations.Single(operation => operation.AttributeName == "pl_noticedate").Value.IsNull);
            Assert.True(result.ChangeSet.Operations.Single(operation => operation.AttributeName == "pl_autorenew").Value.BooleanValue);

            var noOffset = Validate(
                "pl_contract",
                ContractId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_contract\",\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"},\"changes\":[{\"attribute\":\"pl_enddate\",\"value\":\"2026-09-13T12:00:00\"}]}",
                "pl_enddate");
            Assert.Equal(ApprovalChangeSetValidationError.DateTimeOffsetRequired, noOffset.Error);
        }

        [Fact]
        public void 対象とサーバー許可項目が一致しない変更を拒否する()
        {
            var targetMismatch = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"別対象\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.TargetIdMismatch, targetMismatch.Error);

            var notPermitted = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.AttributeNotPermitted, notPermitted.Error);

            var unsupported = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_normalizedname\",\"value\":\"任意の正規化名\"}]}",
                "pl_normalizedname");
            Assert.Equal(ApprovalChangeSetValidationError.AttributeUnsupported, unsupported.Error);
        }

        [Fact]
        public void 不変項目や余分なJSON要素と重複項目を拒否する()
        {
            var extraProperty = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\",\"before\":\"B\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.UnknownProperty, extraProperty.Error);

            var duplicateProperty = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"attribute\":\"pl_name\",\"value\":\"A\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.DuplicateProperty, duplicateProperty.Error);

            var duplicateAttribute = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\"},{\"attribute\":\"pl_name\",\"value\":\"B\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.DuplicateAttribute, duplicateAttribute.Error);

            var systemField = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"statecode\",\"value\":0}]}",
                "statecode");
            Assert.Equal(ApprovalChangeSetValidationError.AttributeUnsupported, systemField.Error);
        }

        [Fact]
        public void スキーマ不正と値の範囲外を拒否する()
        {
            var malformed = Validate("pl_partner", PartnerId, "{", "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.JsonMalformed, malformed.Error);

            var badChoice = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":999}]}",
                "pl_tradingstatuscode");
            Assert.Equal(ApprovalChangeSetValidationError.ValueOutOfRange, badChoice.Error);

            var longName = new string('x', 201);
            var tooLong = Validate(
                "pl_partner",
                PartnerId,
                $"{{\"schemaVersion\":1,\"target\":{{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}},\"changes\":[{{\"attribute\":\"pl_name\",\"value\":\"{longName}\"}}]}}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.ValueTooLong, tooLong.Error);
        }

        [Fact]
        public void 変更順序は指紋に影響せず値違いは別指紋になる()
        {
            var first = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\"},{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001}]}",
                "pl_name",
                "pl_tradingstatuscode");
            var reordered = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001},{\"attribute\":\"pl_name\",\"value\":\"A\"}]}",
                "pl_name",
                "pl_tradingstatuscode");
            var changed = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"B\"},{\"attribute\":\"pl_tradingstatuscode\",\"value\":100000001}]}",
                "pl_name",
                "pl_tradingstatuscode");

            Assert.True(first.IsValid);
            Assert.Equal(first.ChangeSet!.Fingerprint, reordered.ChangeSet!.Fingerprint);
            Assert.NotEqual(first.ChangeSet.Fingerprint, changed.ChangeSet!.Fingerprint);
        }

        [Fact]
        public void JSON文法の抜け道を拒否しエスケープ文字は正規化する()
        {
            var trailingComma = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\"},]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.JsonMalformed, trailingComma.Error);

            var leadingZero = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":01,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.JsonMalformed, leadingZero.Error);

            var escaped = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"\\u682a\\u5f0f\\u4f1a\\u793e\\ud83d\\ude80\"}]}",
                "pl_name");
            Assert.True(escaped.IsValid);
            Assert.Equal("株式会社🚀", escaped.ChangeSet!.Operations.Single().Value.TextValue);

            var invalidEscape = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\\x41\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.JsonMalformed, invalidEscape.Error);

            var unescapedControl = Validate(
                "pl_partner",
                PartnerId,
                "{\"schemaVersion\":1,\"target\":{\"entity\":\"pl_partner\",\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"},\"changes\":[{\"attribute\":\"pl_name\",\"value\":\"A\nB\"}]}",
                "pl_name");
            Assert.Equal(ApprovalChangeSetValidationError.JsonMalformed, unescapedControl.Error);
        }

        private static ApprovalChangeSetResult Validate(
            string entityName,
            Guid targetId,
            string json,
            params string[] allowedAttributes)
            => ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = json,
                ExpectedEntityName = entityName,
                ExpectedTargetId = targetId,
                AllowedAttributes = new HashSet<string>(allowedAttributes, StringComparer.Ordinal),
            });
    }
}
