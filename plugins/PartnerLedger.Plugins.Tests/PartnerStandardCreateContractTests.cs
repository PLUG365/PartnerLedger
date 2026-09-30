using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerStandardCreateContractTests
    {
        private static readonly Guid RegistrantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MainOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public void 標準Createの許可列は受理し権威値をサーバー導出する()
        {
            var target = ValidTarget();
            var registeredAt = new DateTime(2026, 9, 14, 1, 2, 3, DateTimeKind.Utc);

            var result = PartnerStandardCreateContract.Validate(target, RegistrantId, registeredAt);
            Assert.True(result.IsValid);
            Assert.Equal(PartnerStandardCreateValidationError.None, result.Error);
            Assert.NotNull(result.Value);
            Assert.Equal("registration-1", result.Value!.Registration.IdempotencyKey);
            Assert.Equal(64, result.Value.ContentHash.Length);

            PartnerStandardCreateContract.ValidateAndApply(target, RegistrantId, registeredAt);

            Assert.Equal("株式会社　ＡＢＣ", target.GetAttributeValue<string>("pl_name"));
            Assert.Equal("ABC", target.GetAttributeValue<string>("pl_normalizedname"));
            Assert.Equal(100000000, target.GetAttributeValue<OptionSetValue>("pl_tradingstatuscode")!.Value);
            // 会社確認（pl_identitystatuscode）は2026-09-26に列ごと廃止した。登録で書かない。
            Assert.False(target.Attributes.Contains("pl_identitystatuscode"));
            Assert.Equal(registeredAt, target.GetAttributeValue<DateTime>("pl_registeredat"));
            Assert.Equal(1, target.GetAttributeValue<int>("pl_aclversion"));
            Assert.Equal(PartnerStandardCreateContract.InitialShareSetupStatusCode,
                target.GetAttributeValue<OptionSetValue>(PartnerStandardCreateContract.ShareSetupStatusAttribute)!.Value);
            Assert.Equal(RegistrantId, target.GetAttributeValue<EntityReference>("pl_registeredbylookup")!.Id);
            Assert.Equal(MainOwnerId, target.GetAttributeValue<EntityReference>("pl_mainownerlookup")!.Id);
            Assert.Equal(RegistrantId, target.GetAttributeValue<EntityReference>("ownerid")!.Id);
            Assert.Equal("systemuser", target.GetAttributeValue<EntityReference>("ownerid")!.LogicalName);
            Assert.Equal("registration-1", target.GetAttributeValue<string>("pl_registrationkey"));
            Assert.Equal(result.Value.ContentHash, target.GetAttributeValue<string>("pl_registrationcontenthash"));
        }

        [Fact]
        public void 標準Createの検査は不正Targetを例外として拒否する()
        {
            var target = ValidTarget();
            target["pl_tradingstatuscode"] = new OptionSetValue(100000001);

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerStandardCreateContract.ValidateAndApply(target, RegistrantId, DateTime.UtcNow));

            Assert.Contains(nameof(PartnerStandardCreateValidationError.ForbiddenAuthorityInput), exception.Message);
        }

        [Fact]
        public void 任意項目の空白は標準CreateのTargetから除去する()
        {
            var target = ValidTarget();
            target["pl_industry"] = " ";
            target["pl_address"] = null;
            target["pl_phone"] = "";

            var result = PartnerStandardCreateContract.Validate(
                target,
                RegistrantId,
                new DateTime(2026, 9, 14, 1, 2, 3, DateTimeKind.Utc));

            Assert.True(result.IsValid);
            PartnerStandardCreateContract.ApplyServerDerivedAttributes(target, result.Value!);

            Assert.DoesNotContain("pl_industry", target.Attributes.Keys);
            Assert.DoesNotContain("pl_address", target.Attributes.Keys);
            Assert.DoesNotContain("pl_phone", target.Attributes.Keys);
        }

        [Fact]
        public void Clientが内容ハッシュを指定することは拒否する()
        {
            var target = ValidTarget();
            target[PartnerStandardCreateContract.RegistrationContentHashAttribute] = "client-hash";

            var result = PartnerStandardCreateContract.Validate(target, RegistrantId, DateTime.UtcNow);

            Assert.False(result.IsValid);
            Assert.Equal(PartnerStandardCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Theory]
        [InlineData("pl_normalizedname")]
        [InlineData("pl_tradingstatuscode")]
        [InlineData("pl_registeredat")]
        [InlineData("pl_registeredbylookup")]
        [InlineData("pl_aclversion")]
        [InlineData("ownerid")]
        [InlineData("statecode")]
        [InlineData("statuscode")]
        [InlineData("createdby")]
        public void Clientが権威列を直接指定することは拒否する(string attributeName)
        {
            var target = ValidTarget();
            target[attributeName] = attributeName.EndsWith("lookup", StringComparison.Ordinal)
                ? (object)new EntityReference("systemuser", Guid.NewGuid())
                : "client-value";

            var result = PartnerStandardCreateContract.Validate(target, RegistrantId, DateTime.UtcNow);

            Assert.False(result.IsValid);
            Assert.Equal(PartnerStandardCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void 未知の列は画面で隠すだけでなく標準Createでも拒否する()
        {
            var target = ValidTarget();
            target["pl_unknownclientfield"] = "value";

            var result = PartnerStandardCreateContract.Validate(target, RegistrantId, DateTime.UtcNow);

            Assert.False(result.IsValid);
            Assert.Equal(PartnerStandardCreateValidationError.UnsupportedAttribute, result.Error);
        }

        [Theory]
        [InlineData(PartnerStandardCreateValidationError.TargetRequired)]
        [InlineData(PartnerStandardCreateValidationError.TargetEntityInvalid)]
        [InlineData(PartnerStandardCreateValidationError.InitiatingUserRequired)]
        [InlineData(PartnerStandardCreateValidationError.NameRequired)]
        [InlineData(PartnerStandardCreateValidationError.RegistrationKeyRequired)]
        [InlineData(PartnerStandardCreateValidationError.MainOwnerRequired)]
        [InlineData(PartnerStandardCreateValidationError.MainOwnerTypeInvalid)]
        public void 標準Createの必須境界を満たさない入力は拒否する(
            PartnerStandardCreateValidationError expected)
        {
            Entity? target = expected == PartnerStandardCreateValidationError.TargetRequired
                ? null
                : ValidTarget();
            var caller = expected == PartnerStandardCreateValidationError.InitiatingUserRequired
                ? Guid.Empty
                : RegistrantId;

            if (target != null)
            {
                if (expected == PartnerStandardCreateValidationError.TargetEntityInvalid)
                {
                    target = new Entity("account");
                }
                else if (expected == PartnerStandardCreateValidationError.NameRequired)
                {
                    target.Attributes.Remove("pl_name");
                }
                else if (expected == PartnerStandardCreateValidationError.RegistrationKeyRequired)
                {
                    target.Attributes.Remove("pl_registrationkey");
                }
                else if (expected == PartnerStandardCreateValidationError.MainOwnerTypeInvalid)
                {
                    target["pl_mainownerlookup"] = new EntityReference("team", Guid.NewGuid());
                }
                else if (expected == PartnerStandardCreateValidationError.MainOwnerRequired)
                {
                    target.Attributes.Remove("pl_mainownerlookup");
                }
            }

            var result = PartnerStandardCreateContract.Validate(
                target,
                caller,
                new DateTime(2026, 9, 14, 1, 2, 3, DateTimeKind.Utc));

            Assert.False(result.IsValid);
            Assert.Equal(expected, result.Error);
            Assert.Null(result.Value);
        }

        [Theory]
        [InlineData("pl_name")]
        [InlineData("pl_industry")]
        [InlineData("pl_address")]
        [InlineData("pl_phone")]
        [InlineData("pl_mainownerlookup")]
        [InlineData("pl_registrationkey")]
        public void 業務入力列だけが許可された入力である(string attributeName)
        {
            Assert.True(PartnerStandardCreateContract.IsAllowedInputAttribute(attributeName));
            Assert.False(PartnerStandardCreateContract.IsForbiddenAuthorityAttribute(attributeName));
        }

        [Fact]
        public void 同じ入力は同じ内容ハッシュになり入力変更は異なるハッシュになる()
        {
            var first = ValidTarget();
            var changed = ValidTarget();
            changed["pl_phone"] = "03-9999-9999";

            var firstResult = PartnerStandardCreateContract.Validate(first, RegistrantId, DateTime.UtcNow);
            var sameResult = PartnerStandardCreateContract.Validate(ValidTarget(), RegistrantId, DateTime.UtcNow);
            var changedResult = PartnerStandardCreateContract.Validate(changed, RegistrantId, DateTime.UtcNow);

            Assert.Equal(firstResult.Value!.ContentHash, sameResult.Value!.ContentHash);
            Assert.NotEqual(firstResult.Value.ContentHash, changedResult.Value!.ContentHash);
        }

        private static Entity ValidTarget()
            => new Entity(PartnerStandardCreateContract.EntityName)
            {
                ["pl_name"] = " 株式会社　ＡＢＣ ",
                ["pl_industry"] = " ITサービス ",
                ["pl_address"] = " 東京都 ",
                ["pl_phone"] = " 03-1234-5678 ",
                ["pl_mainownerlookup"] = new EntityReference("systemuser", MainOwnerId),
                ["pl_registrationkey"] = " registration-1 ",
            };
    }
}
