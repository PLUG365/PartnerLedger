using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BusinessCardCaptureCreateContractTests
    {
        private static readonly Guid BatchId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        [Fact]
        public void Batch_requires_key_and_rejects_authority_fields()
        {
            var missing = BusinessCardCaptureCreateContract.Validate(new Entity(BusinessCardCaptureCreateContract.BatchEntityName));
            var authority = new Entity(BusinessCardCaptureCreateContract.BatchEntityName);
            authority[BusinessCardCaptureCreateContract.BatchKeyAttribute] = "batch-1";
            authority[BusinessCardCaptureCreateContract.BatchStatusAttribute] = "完了";

            Assert.Equal(BusinessCardCaptureCreateValidationError.BatchKeyRequired, missing.Error);
            Assert.Equal(BusinessCardCaptureCreateValidationError.ForbiddenAuthorityInput, BusinessCardCaptureCreateContract.Validate(authority).Error);
        }

        [Fact]
        public void Batch_accepts_key_and_derives_initial_values()
        {
            var target = new Entity(BusinessCardCaptureCreateContract.BatchEntityName);
            target[BusinessCardCaptureCreateContract.BatchKeyAttribute] = " batch-1 ";
            target[BusinessCardCaptureCreateContract.NameAttribute] = " Demo batch ";

            Assert.True(BusinessCardCaptureCreateContract.Validate(target).IsValid);
            BusinessCardCaptureCreateContract.ApplyServerDerivedAttributes(target, new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("batch-1", target.GetAttributeValue<string>(BusinessCardCaptureCreateContract.BatchKeyAttribute));
            Assert.Equal("Demo batch", target.GetAttributeValue<string>(BusinessCardCaptureCreateContract.NameAttribute));
            Assert.Equal("受付中", target.GetAttributeValue<string>(BusinessCardCaptureCreateContract.BatchStatusAttribute));
            Assert.Equal(1, target.GetAttributeValue<int>(BusinessCardCaptureCreateContract.BatchItemCountAttribute));
            Assert.Equal(0, target.GetAttributeValue<int>(BusinessCardCaptureCreateContract.BatchCompletedCountAttribute));
            Assert.Equal(new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), target.GetAttributeValue<DateTime>(BusinessCardCaptureCreateContract.BatchRegisteredAtAttribute));
        }

        [Fact]
        public void Capture_requires_batch_reference_and_key()
        {
            var missing = new Entity(BusinessCardCaptureCreateContract.CaptureEntityName);
            missing[BusinessCardCaptureCreateContract.CaptureKeyAttribute] = "capture-1";
            var wrongLookup = new Entity(BusinessCardCaptureCreateContract.CaptureEntityName);
            wrongLookup[BusinessCardCaptureCreateContract.CaptureKeyAttribute] = "capture-1";
            wrongLookup[BusinessCardCaptureCreateContract.BatchLookupAttribute] = new EntityReference("pl_partner", BatchId);

            Assert.Equal(BusinessCardCaptureCreateValidationError.BatchLookupRequired, BusinessCardCaptureCreateContract.Validate(missing).Error);
            Assert.Equal(BusinessCardCaptureCreateValidationError.BatchLookupTypeInvalid, BusinessCardCaptureCreateContract.Validate(wrongLookup).Error);
        }

        [Fact]
        public void Capture_accepts_batch_reference_and_derives_initial_values()
        {
            var target = new Entity(BusinessCardCaptureCreateContract.CaptureEntityName);
            target[BusinessCardCaptureCreateContract.CaptureKeyAttribute] = " capture-1 ";
            target[BusinessCardCaptureCreateContract.BatchLookupAttribute] = new EntityReference(BusinessCardCaptureCreateContract.BatchEntityName, BatchId);
            target[BusinessCardCaptureCreateContract.NameAttribute] = " card.png ";

            Assert.True(BusinessCardCaptureCreateContract.Validate(target).IsValid);
            BusinessCardCaptureCreateContract.ApplyServerDerivedAttributes(target, new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("capture-1", target.GetAttributeValue<string>(BusinessCardCaptureCreateContract.CaptureKeyAttribute));
            Assert.Equal("未登録", target.GetAttributeValue<string>(BusinessCardCaptureCreateContract.CaptureStatusAttribute));
            Assert.Equal(new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc), target.GetAttributeValue<DateTime>(BusinessCardCaptureCreateContract.CaptureReceivedAtAttribute));
            Assert.Equal(1, target.GetAttributeValue<int>(BusinessCardCaptureCreateContract.CaptureCurrentVersionAttribute));
        }

        [Fact]
        public void Capture_rejects_image_column_and_client_status()
        {
            var target = new Entity(BusinessCardCaptureCreateContract.CaptureEntityName);
            target[BusinessCardCaptureCreateContract.CaptureKeyAttribute] = "capture-1";
            target[BusinessCardCaptureCreateContract.BatchLookupAttribute] = new EntityReference(BusinessCardCaptureCreateContract.BatchEntityName, BatchId);
            target[BusinessCardCaptureCreateContract.CaptureStatusAttribute] = "登録済み";

            Assert.Equal(BusinessCardCaptureCreateValidationError.ForbiddenAuthorityInput, BusinessCardCaptureCreateContract.Validate(target).Error);
        }

        [Fact]
        public void Long_keys_are_rejected()
        {
            var target = new Entity(BusinessCardCaptureCreateContract.BatchEntityName);
            target[BusinessCardCaptureCreateContract.BatchKeyAttribute] = new string('x', 201);

            Assert.Equal(BusinessCardCaptureCreateValidationError.BatchKeyTooLong, BusinessCardCaptureCreateContract.Validate(target).Error);
        }
    }
}
