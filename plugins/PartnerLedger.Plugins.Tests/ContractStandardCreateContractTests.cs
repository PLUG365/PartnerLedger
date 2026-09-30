using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ContractStandardCreateContractTests
    {
        private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid PartnerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid ContractTypeId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        [Fact]
        public void 標準Createは許可列だけを受理し契約状態をサーバー導出する()
        {
            var target = ValidTarget();
            var service = ParentAndTypeService();
            target[ContractStandardCreateContract.ContractStatusAttribute] = new OptionSetValue(100000001);
            target.Attributes.Remove(ContractStandardCreateContract.ContractStatusAttribute);

            ContractStandardCreatePlugin.ValidateAndApply(target, CallerId, service);

            Assert.Equal("基本取引契約（2026年度）", target.GetAttributeValue<string>(ContractStandardCreateContract.NameAttribute));
            Assert.Equal(PartnerId, target.GetAttributeValue<EntityReference>(ContractStandardCreateContract.PartnerLookupAttribute)!.Id);
            Assert.Equal(ContractTypeId, target.GetAttributeValue<EntityReference>(ContractStandardCreateContract.ContractTypeLookupAttribute)!.Id);
            Assert.Equal(ContractStandardCreateContract.DefaultContractStatusCode, target.GetAttributeValue<OptionSetValue>(ContractStandardCreateContract.ContractStatusAttribute)!.Value);
            Assert.False(target.Attributes.Contains("signedDate"));
        }

        [Fact]
        public void 読み取れない親取引先は拒否する()
        {
            var service = ParentAndTypeService();
            service.CanRetrieve = (entityName, _) => entityName != "pl_partner";

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, service));

            Assert.Contains("親取引先を読み取れない", exception.Message);
        }

        [Fact]
        public void 非Activeまたは選択不可の契約種類は拒否する()
        {
            var inactive = ParentAndTypeService();
            inactive.Seed(new Entity("pl_contracttype", ContractTypeId)
            {
                ["statecode"] = new OptionSetValue(1),
                ["pl_selectable"] = true,
            });
            var inactiveException = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, inactive));
            Assert.Contains("選択できない", inactiveException.Message);

            var notSelectable = ParentAndTypeService();
            notSelectable.Seed(new Entity("pl_contracttype", ContractTypeId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_selectable"] = false,
            });
            var notSelectableException = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, notSelectable));
            Assert.Contains("選択できない", notSelectableException.Message);
        }

        [Theory]
        [InlineData("ownerid")]
        [InlineData("statecode")]
        [InlineData("statuscode")]
        [InlineData("pl_contractstatuscode")]
        [InlineData("createdby")]
        public void クライアント権威列は拒否する(string attributeName)
        {
            var target = ValidTarget();
            target[attributeName] = attributeName == "ownerid"
                ? (object)new EntityReference("systemuser", Guid.NewGuid())
                : "client-value";

            var result = ContractStandardCreateContract.Validate(target, CallerId);

            Assert.False(result.IsValid);
            Assert.Equal(ContractStandardCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void 必須Lookupと要求キーを検査する()
        {
            var missingPartner = ValidTarget();
            missingPartner.Attributes.Remove(ContractStandardCreateContract.PartnerLookupAttribute);
            Assert.Equal(ContractStandardCreateValidationError.PartnerRequired, ContractStandardCreateContract.Validate(missingPartner, CallerId).Error);

            var missingType = ValidTarget();
            missingType.Attributes.Remove(ContractStandardCreateContract.ContractTypeLookupAttribute);
            Assert.Equal(ContractStandardCreateValidationError.ContractTypeRequired, ContractStandardCreateContract.Validate(missingType, CallerId).Error);

            var missingKey = ValidTarget();
            missingKey.Attributes.Remove(ContractStandardCreateContract.RegistrationKeyAttribute);
            Assert.Equal(ContractStandardCreateValidationError.RegistrationKeyRequired, ContractStandardCreateContract.Validate(missingKey, CallerId).Error);
        }

        [Fact]
        public void 同じ要求キーの内容違いは一致扱いにしない()
        {
            var input = ContractStandardCreateContract.Validate(ValidTarget(), CallerId).Value!;
            var existing = ValidTarget();
            ContractStandardCreateContract.ApplyServerDerivedAttributes(existing, input);
            existing[ContractStandardCreateContract.NameAttribute] = "別契約";

            Assert.False(ContractStandardCreateContract.MatchesExisting(existing, input));
        }

        [Fact]
        public void 同じ要求キーと同じ内容はプラグインで再処理せず拒否する()
        {
            var service = ParentAndTypeService();
            var existing = ValidTarget();
            existing.Id = Guid.NewGuid();
            var input = ContractStandardCreateContract.Validate(existing, CallerId).Value!;
            ContractStandardCreateContract.ApplyServerDerivedAttributes(existing, input);
            service.Seed(existing);

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, service));

            Assert.Contains("既に処理", exception.Message);
        }

        private static Entity ValidTarget()
            => new Entity(ContractStandardCreateContract.EntityName)
            {
                [ContractStandardCreateContract.NameAttribute] = "基本取引契約（2026年度）",
                [ContractStandardCreateContract.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [ContractStandardCreateContract.ContractTypeLookupAttribute] = new EntityReference("pl_contracttype", ContractTypeId),
                [ContractStandardCreateContract.EndDateAttribute] = new DateTime(2027, 3, 31),
                [ContractStandardCreateContract.NoticeDateAttribute] = new DateTime(2027, 1, 31),
                [ContractStandardCreateContract.DecisionDateAttribute] = new DateTime(2027, 1, 15),
                [ContractStandardCreateContract.AutoRenewAttribute] = true,
                [ContractStandardCreateContract.LinkAttribute] = "https://contoso.example/contracts/2026-basic",
                [ContractStandardCreateContract.RegistrationKeyAttribute] = "contract-registration-1",
            };

        private static FakeOrganizationService ParentAndTypeService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
            });
            service.Seed(new Entity("pl_contracttype", ContractTypeId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_selectable"] = true,
            });
            return service;
        }
    }
}
