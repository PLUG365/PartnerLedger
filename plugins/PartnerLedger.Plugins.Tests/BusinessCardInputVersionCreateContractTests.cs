using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BusinessCardInputVersionCreateContractTests
    {
        private static readonly Guid CaptureId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void 画像到着済みの現行Captureから状態をサーバー導出する()
        {
            var target = ValidTarget();
            var service = CaptureService(fileToken: Guid.Parse("33333333-3333-4333-8333-333333333333"));

            BusinessCardInputVersionCreateContract.ValidateAndApply(
                target,
                service,
                new DateTime(2026, 9, 18, 1, 2, 3, DateTimeKind.Utc));

            Assert.Equal(1, target.GetAttributeValue<int>(BusinessCardInputVersionCreateContract.VersionNumberAttribute));
            Assert.Equal(BusinessCardInputVersionCreateContract.FixedInputState, target.GetAttributeValue<string>(BusinessCardInputVersionCreateContract.InputStateAttribute));
            Assert.Equal(BusinessCardInputVersionCreateContract.ImageArrivedState, target.GetAttributeValue<string>(BusinessCardInputVersionCreateContract.ImageStateAttribute));
            Assert.Equal("名刺入力版 R11-C-01 v1", target.GetAttributeValue<string>(BusinessCardInputVersionCreateContract.NameAttribute));
            Assert.Equal(new DateTime(2026, 9, 18, 1, 2, 3, DateTimeKind.Utc), target.GetAttributeValue<DateTime>(BusinessCardInputVersionCreateContract.RegisteredAtAttribute));
        }

        [Theory]
        [InlineData("pl_versionnumber")]
        [InlineData("pl_inputstatecode")]
        [InlineData("pl_imagestatecode")]
        [InlineData("pl_registeredat")]
        [InlineData("ownerid")]
        [InlineData("statecode")]
        public void クライアント権威列は拒否する(string attributeName)
        {
            var target = ValidTarget();
            target[attributeName] = "client-value";

            var result = BusinessCardInputVersionCreateContract.Validate(target);

            Assert.False(result.IsValid);
            Assert.Equal(BusinessCardInputVersionCreateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void ファイル未到着のCaptureは拒否する()
        {
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardInputVersionCreateContract.ValidateAndApply(
                    ValidTarget(),
                    CaptureService(fileToken: null),
                    DateTime.UtcNow));

            Assert.Contains("画像が到着していない", exception.Message);
        }

        [Fact]
        public void 未登録でないCaptureは拒否する()
        {
            var service = CaptureService(fileToken: Guid.NewGuid());
            service.Seed(new Entity("pl_cardcapture", CaptureId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_capturekey"] = "R11-C-01",
                ["pl_capturestatuscode"] = "確認待ち",
                ["pl_currentversionnumber"] = 1,
                ["pl_imagefile"] = Guid.NewGuid(),
            });

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardInputVersionCreateContract.ValidateAndApply(ValidTarget(), service, DateTime.UtcNow));

            Assert.Contains("未登録ではない", exception.Message);
        }

        [Fact]
        public void 同じCaptureと版の既存行は再送を拒否する()
        {
            var service = CaptureService(fileToken: Guid.NewGuid());
            service.Seed(new Entity(BusinessCardInputVersionCreateContract.EntityName, Guid.NewGuid())
            {
                [BusinessCardInputVersionCreateContract.CaptureLookupAttribute] = new EntityReference("pl_cardcapture", CaptureId),
                [BusinessCardInputVersionCreateContract.VersionNumberAttribute] = 1,
                ["statecode"] = new OptionSetValue(0),
            });

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardInputVersionCreateContract.ValidateAndApply(ValidTarget(), service, DateTime.UtcNow));

            Assert.Contains("既に存在", exception.Message);
        }

        [Fact]
        public void CaptureLookupが必須で別テーブル参照は拒否する()
        {
            var missing = new Entity(BusinessCardInputVersionCreateContract.EntityName);
            var wrong = new Entity(BusinessCardInputVersionCreateContract.EntityName)
            {
                [BusinessCardInputVersionCreateContract.CaptureLookupAttribute] = new EntityReference("pl_partner", CaptureId),
            };

            Assert.Equal(BusinessCardInputVersionCreateValidationError.CaptureLookupRequired, BusinessCardInputVersionCreateContract.Validate(missing).Error);
            Assert.Equal(BusinessCardInputVersionCreateValidationError.CaptureLookupTypeInvalid, BusinessCardInputVersionCreateContract.Validate(wrong).Error);
        }

        private static Entity ValidTarget()
            => new Entity(BusinessCardInputVersionCreateContract.EntityName)
            {
                [BusinessCardInputVersionCreateContract.CaptureLookupAttribute] = new EntityReference("pl_cardcapture", CaptureId),
            };

        private static FakeOrganizationService CaptureService(Guid? fileToken)
        {
            var service = new FakeOrganizationService();
            var capture = new Entity("pl_cardcapture", CaptureId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_capturekey"] = "R11-C-01",
                ["pl_capturestatuscode"] = "未登録",
                ["pl_currentversionnumber"] = 1,
            };
            if (fileToken.HasValue) capture["pl_imagefile"] = fileToken.Value;
            service.Seed(capture);
            return service;
        }
    }
}
