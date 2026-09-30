using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ContactStandardCreateContractTests
    {
        private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid PartnerId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void 標準Createの許可列は受理し状態と登録日時をサーバー導出する()
        {
            var target = ValidTarget();
            target.Attributes.Remove(ContactStandardCreateContract.StatusAttribute);
            var registeredAt = new DateTime(2026, 9, 15, 2, 3, 4, DateTimeKind.Utc);
            var service = ParentService();

            ContactStandardCreatePlugin.ValidateAndApply(target, CallerId, registeredAt, service);

            Assert.Equal("井上 真帆", target.GetAttributeValue<string>(ContactStandardCreateContract.NameAttribute));
            Assert.Equal(PartnerId, target.GetAttributeValue<EntityReference>(ContactStandardCreateContract.PartnerLookupAttribute)!.Id);
            Assert.Equal(ContactStandardCreateContract.DefaultStatusCode, target.GetAttributeValue<OptionSetValue>(ContactStandardCreateContract.StatusAttribute)!.Value);
            Assert.Equal(registeredAt, target.GetAttributeValue<DateTime>(ContactStandardCreateContract.RegisteredAtAttribute));
            Assert.Equal("contact-registration-1", target.GetAttributeValue<string>(ContactStandardCreateContract.RegistrationKeyAttribute));
            Assert.Equal("営業部", target.GetAttributeValue<string>(ContactStandardCreateContract.DepartmentRoleAttribute));
            Assert.Equal(CallerId, target.GetAttributeValue<EntityReference>(ContactStandardCreateContract.OwnerAttribute)!.Id);
            Assert.Equal("systemuser", target.GetAttributeValue<EntityReference>(ContactStandardCreateContract.OwnerAttribute)!.LogicalName);
        }

        [Fact]
        public void 読み取れない親取引先は標準Createでも拒否する()
        {
            var service = ParentService();
            service.CanRetrieve = (_, _) => false;

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContactStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, DateTime.UtcNow, service));

            Assert.Contains("親取引先を読み取れない", exception.Message);
        }

        [Fact]
        public void 非アクティブな親取引先は拒否する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(1),
            });

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContactStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, DateTime.UtcNow, service));

            Assert.Contains("非アクティブ", exception.Message);
        }

        [Fact]
        public void 担当者の所有者は登録実行者であり親取引先の所有者とは独立()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["ownerid"] = new EntityReference("systemuser", Guid.NewGuid()),
            });
            var target = ValidTarget();

            ContactStandardCreatePlugin.ValidateAndApply(target, CallerId, DateTime.UtcNow, service);

            Assert.Equal(CallerId, target.GetAttributeValue<EntityReference>(ContactStandardCreateContract.OwnerAttribute)!.Id);
        }

        [Theory]
        [InlineData("ownerid")]
        [InlineData("createdby")]
        [InlineData("pl_registeredat")]
        [InlineData("statecode")]
        [InlineData("pl_contactid")]
        public void クライアント権威列は拒否する(string attributeName)
        {
            var target = ValidTarget();
            target[attributeName] = attributeName == "ownerid"
                ? (object)new EntityReference("systemuser", Guid.NewGuid())
                : "client-value";

            var result = ContactStandardCreateContract.Validate(target, CallerId, DateTime.UtcNow);

            Assert.False(result.IsValid);
            Assert.Equal(ContactStandardCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void 所属Lookupと未知列はfail_closedで拒否する()
        {
            var missingParent = ValidTarget();
            missingParent.Attributes.Remove(ContactStandardCreateContract.PartnerLookupAttribute);
            Assert.Equal(
                ContactStandardCreateValidationError.PartnerRequired,
                ContactStandardCreateContract.Validate(missingParent, CallerId, DateTime.UtcNow).Error);

            var wrongParent = ValidTarget();
            wrongParent[ContactStandardCreateContract.PartnerLookupAttribute] = new EntityReference("account", PartnerId);
            Assert.Equal(
                ContactStandardCreateValidationError.PartnerTypeInvalid,
                ContactStandardCreateContract.Validate(wrongParent, CallerId, DateTime.UtcNow).Error);

            var unknown = ValidTarget();
            unknown["pl_unknownclientfield"] = "value";
            Assert.Equal(
                ContactStandardCreateValidationError.UnsupportedAttribute,
                ContactStandardCreateContract.Validate(unknown, CallerId, DateTime.UtcNow).Error);
        }

        [Fact]
        public void 同じ要求キーの内容違いは一致扱いにしない()
        {
            var service = ParentService();
            var existing = ValidTarget();
            existing[ContactStandardCreateContract.StatusAttribute] = new OptionSetValue(ContactStandardCreateContract.DefaultStatusCode);
            ContactStandardCreateContract.ApplyServerDerivedAttributes(
                existing,
                ContactStandardCreateContract.Validate(existing, CallerId, DateTime.UtcNow).Value!,
                CallerId);
            service.Seed(existing);

            var changed = ValidTarget();
            changed[ContactStandardCreateContract.NameAttribute] = "別の氏名";
            var changedValue = ContactStandardCreateContract.Validate(changed, CallerId, DateTime.UtcNow).Value!;
            var stored = service.Retrieve("pl_contact", existing.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));

            Assert.False(ContactStandardCreateContract.MatchesExisting(stored, changedValue));
        }

        [Fact]
        public void 同じ要求キーと同じ内容は一致扱いにできる()
        {
            var target = ValidTarget();
            var input = ContactStandardCreateContract.Validate(target, CallerId, DateTime.UtcNow).Value!;
            var stored = new Entity("pl_contact")
            {
                [ContactStandardCreateContract.NameAttribute] = input.Name,
                [ContactStandardCreateContract.PartnerLookupAttribute] = new EntityReference("pl_partner", input.PartnerId),
                [ContactStandardCreateContract.StatusAttribute] = new OptionSetValue(input.StatusCode),
                [ContactStandardCreateContract.DepartmentRoleAttribute] = input.DepartmentRole,
                [ContactStandardCreateContract.EmailAttribute] = input.Email,
                [ContactStandardCreateContract.PhoneAttribute] = input.Phone,
                [ContactStandardCreateContract.RegistrationKeyAttribute] = input.RegistrationKey,
            };

            Assert.True(ContactStandardCreateContract.MatchesExisting(stored, input));
        }

        [Fact]
        public void 同じ要求キーの再送はプラグインで再処理せず拒否する()
        {
            var service = ParentService();
            var existing = ValidTarget();
            existing.Id = Guid.NewGuid();
            var input = ContactStandardCreateContract.Validate(existing, CallerId, DateTime.UtcNow).Value!;
            ContactStandardCreateContract.ApplyServerDerivedAttributes(existing, input, CallerId);
            service.Seed(existing);

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContactStandardCreatePlugin.ValidateAndApply(ValidTarget(), CallerId, DateTime.UtcNow, service));

            Assert.Contains("既に処理", exception.Message);
        }

        [Fact]
        public void 同じ要求キーの内容違いはプラグインで拒否する()
        {
            var service = ParentService();
            var existing = ValidTarget();
            existing.Id = Guid.NewGuid();
            var input = ContactStandardCreateContract.Validate(existing, CallerId, DateTime.UtcNow).Value!;
            ContactStandardCreateContract.ApplyServerDerivedAttributes(existing, input, CallerId);
            service.Seed(existing);

            var changed = ValidTarget();
            changed[ContactStandardCreateContract.NameAttribute] = "別の氏名";
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContactStandardCreatePlugin.ValidateAndApply(changed, CallerId, DateTime.UtcNow, service));

            Assert.Contains("異なる内容", exception.Message);
        }

        private static Entity ValidTarget()
            => new Entity(ContactStandardCreateContract.EntityName)
            {
                [ContactStandardCreateContract.NameAttribute] = "井上 真帆",
                [ContactStandardCreateContract.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [ContactStandardCreateContract.StatusAttribute] = new OptionSetValue(100000001),
                [ContactStandardCreateContract.DepartmentRoleAttribute] = "営業部",
                [ContactStandardCreateContract.EmailAttribute] = "m.inoue@example.com",
                [ContactStandardCreateContract.PhoneAttribute] = "03-1234-5678",
                [ContactStandardCreateContract.RegistrationKeyAttribute] = "contact-registration-1",
            };

        private static FakeOrganizationService ParentService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
            });
            return service;
        }
    }
}
