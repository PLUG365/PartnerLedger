using System;
using System.Collections.Generic;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalPolicyContractTests
    {
        private const string ValidPolicyJson
            = "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}";

        [Fact]
        public void 固定スキーマのポリシーを読み取る()
        {
            var result = ApprovalPolicyContract.Validate(ValidPolicyJson);

            Assert.True(result.IsValid);
            Assert.Equal(ApprovalPolicyValidationError.None, result.Error);
            Assert.Equal(1, result.Policy!.SchemaVersion);
            Assert.True(result.Policy.CompanyName);
            Assert.True(result.Policy.TradingStatus);
            Assert.False(result.Policy.Address);
            Assert.False(result.Policy.Phone);
            Assert.True(result.Policy.ContractUpdate);
        }

        [Fact]
        public void 未知重複欠落型違いと末尾カンマを拒否する()
        {
            var unknown = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"extra\":false}");
            var duplicate = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"companyName\":false}");
            var missing = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false}");
            var wrongType = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":\"true\",\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}");
            var trailingComma = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,}");
            var leadingZero = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":01,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}");

            Assert.Equal(ApprovalPolicyValidationError.UnknownProperty, unknown.Error);
            Assert.Equal(ApprovalPolicyValidationError.DuplicateProperty, duplicate.Error);
            Assert.Equal(ApprovalPolicyValidationError.RequiredPropertyMissing, missing.Error);
            Assert.Equal(ApprovalPolicyValidationError.ValueTypeMismatch, wrongType.Error);
            Assert.Equal(ApprovalPolicyValidationError.JsonMalformed, trailingComma.Error);
            Assert.Equal(ApprovalPolicyValidationError.JsonMalformed, leadingZero.Error);
        }

        [Fact]
        public void 空または未対応バージョンを拒否する()
        {
            var empty = ApprovalPolicyContract.Validate("  ");
            var unsupported = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":3,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":true}");
            var decimalVersion = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1.0,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}");

            Assert.Equal(ApprovalPolicyValidationError.PayloadRequired, empty.Error);
            Assert.Equal(ApprovalPolicyValidationError.SchemaVersionUnsupported, unsupported.Error);
            Assert.Equal(ApprovalPolicyValidationError.SchemaVersionUnsupported, decimalVersion.Error);
        }

        [Fact]
        public void 版2は主担当の変更の承認要否を読む()
        {
            // 2026-09-29ユーザー決定：主担当の変更も承認の要否を切り替える。
            var off = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":false}");
            var on = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":true}");

            Assert.True(off.IsValid);
            Assert.Equal(2, off.Policy!.SchemaVersion);
            Assert.False(off.Policy.MainOwner);
            Assert.True(on.IsValid);
            Assert.True(on.Policy!.MainOwner);
        }

        [Fact]
        public void 版1の保存済み設定は主担当の変更を承認必要として読む()
        {
            // 開発環境には版1の設定（版1〜5）が保存されている。新しいDLLでも読めること。
            var result = ApprovalPolicyContract.Validate(ValidPolicyJson);

            Assert.True(result.IsValid);
            Assert.Equal(1, result.Policy!.SchemaVersion);
            Assert.True(result.Policy.MainOwner);
        }

        [Fact]
        public void 版と項目の組合せが合わなければ拒否する()
        {
            var v2Missing = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true}");
            var v1WithMainOwner = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":false}");
            var v2WrongType = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":\"no\"}");

            Assert.Equal(ApprovalPolicyValidationError.RequiredPropertyMissing, v2Missing.Error);
            Assert.Equal(ApprovalPolicyValidationError.UnknownProperty, v1WithMainOwner.Error);
            Assert.Equal(ApprovalPolicyValidationError.ValueTypeMismatch, v2WrongType.Error);
        }

        [Fact]
        public void 既定値は版2で主担当の変更は承認が必要()
        {
            var result = ApprovalPolicyContract.Validate(ApprovalPolicyContract.DefaultPolicyJson);

            Assert.True(result.IsValid);
            Assert.Equal(2, result.Policy!.SchemaVersion);
            Assert.True(result.Policy.MainOwner);
            Assert.True(result.Policy.CompanyName);
            Assert.False(result.Policy.Address);
        }

        [Fact]
        public void 主担当の変更の申請は設定に従って許可項目を解決する()
        {
            var on = ApprovalPolicyContract.Validate(ValidPolicyJson).Policy!;
            var off = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":false}").Policy!;

            var enabled = ApprovalPolicyContract.Resolve(on, ApprovalPolicyContract.MainOwnerRequestType, "pl_partner");
            var disabled = ApprovalPolicyContract.Resolve(off, ApprovalPolicyContract.MainOwnerRequestType, "pl_partner");
            var mismatch = ApprovalPolicyContract.Resolve(on, ApprovalPolicyContract.MainOwnerRequestType, "pl_contract");

            Assert.Equal("partner.main-owner", ApprovalPolicyContract.MainOwnerRequestType);
            Assert.True(enabled.IsValid);
            Assert.Equal(new HashSet<string>(new[] { "pl_mainownerlookup" }, StringComparer.Ordinal), enabled.Resolution!.AllowedAttributes);
            Assert.Equal(ApprovalPolicyResolutionError.TargetDisabled, disabled.Error);
            Assert.Equal(ApprovalPolicyResolutionError.TargetEntityMismatch, mismatch.Error);
        }

        [Fact]
        public void 種別と対象から許可項目を解決する()
        {
            var policy = ApprovalPolicyContract.Validate(ValidPolicyJson).Policy!;

            var company = ApprovalPolicyContract.Resolve(
                policy,
                ApprovalPolicyContract.CompanyNameRequestType,
                "pl_partner");
            var contract = ApprovalPolicyContract.Resolve(
                policy,
                ApprovalPolicyContract.ContractUpdateRequestType,
                "pl_contract");

            Assert.True(company.IsValid);
            Assert.Equal("pl_partner", company.Resolution!.EntityName);
            Assert.Equal(new HashSet<string>(new[] { "pl_name" }, StringComparer.Ordinal), company.Resolution.AllowedAttributes);
            Assert.True(contract.IsValid);
            Assert.Equal("pl_contract", contract.Resolution!.EntityName);
            Assert.Contains("pl_name", contract.Resolution.AllowedAttributes);
            Assert.Contains("pl_contractstatuscode", contract.Resolution.AllowedAttributes);
            Assert.Contains("pl_link", contract.Resolution.AllowedAttributes);
        }

        [Fact]
        public void 無効化対象不一致と未知種別をfail_closedする()
        {
            var policy = ApprovalPolicyContract.Validate(ValidPolicyJson).Policy!;
            var disabled = ApprovalPolicyContract.Resolve(
                policy,
                ApprovalPolicyContract.AddressRequestType,
                "pl_partner");
            var addressEnabledPolicy = ApprovalPolicyContract.Validate(
                "{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":true,\"phone\":true,\"contractUpdate\":true}").Policy!;
            var unavailable = ApprovalPolicyContract.Resolve(
                addressEnabledPolicy,
                ApprovalPolicyContract.AddressRequestType,
                "pl_partner");
            var mismatch = ApprovalPolicyContract.Resolve(
                policy,
                ApprovalPolicyContract.CompanyNameRequestType,
                "pl_contract");
            var legacyType = ApprovalPolicyContract.Resolve(policy, "company-name", "pl_partner");

            Assert.Equal(ApprovalPolicyResolutionError.TargetDisabled, disabled.Error);
            Assert.True(unavailable.IsValid);
            Assert.Equal("pl_partner", unavailable.Resolution!.EntityName);
            Assert.Equal(new HashSet<string>(new[] { "pl_address" }, StringComparer.Ordinal), unavailable.Resolution.AllowedAttributes);
            Assert.Equal(ApprovalPolicyResolutionError.TargetEntityMismatch, mismatch.Error);
            Assert.Equal(ApprovalPolicyResolutionError.RequestTypeUnsupported, legacyType.Error);
        }

        [Fact]
        public void 必須コンテキストが無ければ解決しない()
        {
            var policy = ApprovalPolicyContract.Validate(ValidPolicyJson).Policy!;

            Assert.Equal(
                ApprovalPolicyResolutionError.PolicyRequired,
                ApprovalPolicyContract.Resolve(null, ApprovalPolicyContract.CompanyNameRequestType, "pl_partner").Error);
            Assert.Equal(
                ApprovalPolicyResolutionError.RequestTypeRequired,
                ApprovalPolicyContract.Resolve(policy, "", "pl_partner").Error);
            Assert.Equal(
                ApprovalPolicyResolutionError.TargetEntityRequired,
                ApprovalPolicyContract.Resolve(policy, ApprovalPolicyContract.CompanyNameRequestType, "").Error);
            Assert.Equal(
                ApprovalPolicyResolutionError.TargetEntityUnsupported,
                ApprovalPolicyContract.Resolve(policy, ApprovalPolicyContract.CompanyNameRequestType, "account").Error);
        }
    }
}
