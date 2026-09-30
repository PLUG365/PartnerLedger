using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BulletinPostStandardCreateContractTests
    {
        private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid PartnerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid ContactId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        [Fact]
        public void Createは投稿本文_要求キー_親だけを入力として受理し派生値を設定する()
        {
            var target = ValidTarget();
            var service = ParentAndContactService();

            BulletinPostStandardCreatePlugin.ValidateAndApply(
                target,
                CallerId,
                new DateTime(2026, 9, 17, 1, 2, 3, DateTimeKind.Utc),
                service);

            Assert.Equal("確認しました。", target.GetAttributeValue<string>(BulletinPostStandardCreateContract.BodyAttribute));
            Assert.Equal("bulletin-1", target.GetAttributeValue<string>(BulletinPostStandardCreateContract.RequestKeyAttribute));
            Assert.Equal(PartnerId, target.GetAttributeValue<EntityReference>(BulletinPostStandardCreateContract.PartnerLookupAttribute)!.Id);
            Assert.Equal(ContactId.ToString("D"), target.GetAttributeValue<string>(BulletinPostStandardCreateContract.ContactKeyAttribute));
            Assert.Equal("掲示板投稿-bulletin-1", target.GetAttributeValue<string>(BulletinPostStandardCreateContract.NameAttribute));
            Assert.Equal(new DateTime(2026, 9, 17, 1, 2, 3, DateTimeKind.Utc), target.GetAttributeValue<DateTime>(BulletinPostStandardCreateContract.RegisteredAtAttribute));
        }

        [Theory]
        [InlineData("pl_name")]
        [InlineData("pl_registeredat")]
        [InlineData("ownerid")]
        [InlineData("statecode")]
        [InlineData("createdby")]
        public void クライアント権威列は拒否する(string attributeName)
        {
            var target = ValidTarget();
            target[attributeName] = "client-value";

            var result = BulletinPostStandardCreateContract.Validate(target, CallerId);

            Assert.False(result.IsValid);
            Assert.Equal(BulletinPostStandardCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void 担当者なしの取引先投稿はContactKeyを持たずに受理する()
        {
            var target = ValidTarget();
            target.Attributes.Remove(BulletinPostStandardCreateContract.ContactKeyAttribute);

            var result = BulletinPostStandardCreateContract.Validate(target, CallerId);

            Assert.True(result.IsValid);
            Assert.Null(result.Value!.ContactId);
        }

        [Fact]
        public void 必須値と担当者キーを検査する()
        {
            var missingBody = ValidTarget();
            missingBody.Attributes.Remove(BulletinPostStandardCreateContract.BodyAttribute);
            Assert.Equal(BulletinPostStandardCreateValidationError.BodyRequired, BulletinPostStandardCreateContract.Validate(missingBody, CallerId).Error);

            var missingKey = ValidTarget();
            missingKey.Attributes.Remove(BulletinPostStandardCreateContract.RequestKeyAttribute);
            Assert.Equal(BulletinPostStandardCreateValidationError.RequestKeyRequired, BulletinPostStandardCreateContract.Validate(missingKey, CallerId).Error);

            var invalidContact = ValidTarget();
            invalidContact[BulletinPostStandardCreateContract.ContactKeyAttribute] = "not-a-guid";
            Assert.Equal(BulletinPostStandardCreateValidationError.ContactKeyInvalid, BulletinPostStandardCreateContract.Validate(invalidContact, CallerId).Error);
        }

        [Fact]
        public void 同じ要求キーの内容違いは一致扱いにしない()
        {
            var input = BulletinPostStandardCreateContract.Validate(ValidTarget(), CallerId).Value!;
            var existing = ValidTarget();
            BulletinPostStandardCreateContract.ApplyServerDerivedAttributes(existing, input, DateTime.UtcNow);
            existing[BulletinPostStandardCreateContract.BodyAttribute] = "別の内容";

            Assert.False(BulletinPostStandardCreateContract.MatchesExisting(existing, input));
        }

        private static Entity ValidTarget()
            => new Entity(BulletinPostStandardCreateContract.EntityName)
            {
                [BulletinPostStandardCreateContract.BodyAttribute] = "確認しました。",
                [BulletinPostStandardCreateContract.RequestKeyAttribute] = "bulletin-1",
                [BulletinPostStandardCreateContract.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [BulletinPostStandardCreateContract.ContactKeyAttribute] = ContactId.ToString("D"),
            };

        private static FakeOrganizationService ParentAndContactService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId) { ["statecode"] = new OptionSetValue(0) });
            service.Seed(new Entity("pl_contact", ContactId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            return service;
        }
    }
}
