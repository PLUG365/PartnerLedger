using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerRegistrationRecordFactoryTests
    {
        private static readonly Guid RegistrantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MainOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public void 作成属性はサーバー固定状態と入力列だけを含む()
        {
            var validation = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput
                {
                    Name = "株式会社　ＡＢＣ",
                    Industry = "ITサービス",
                    Address = "東京都",
                    Phone = "03-1234-5678",
                    MainOwnerId = MainOwnerId,
                    IdempotencyKey = "register-1",
                });

            var entity = PartnerRegistrationRecordFactory.Build(
                validation.Value!,
                new DateTime(2026, 9, 13, 1, 2, 3, DateTimeKind.Utc));

            Assert.Equal("pl_partner", entity.LogicalName);
            Assert.Equal("株式会社　ＡＢＣ", entity.GetAttributeValue<string>("pl_name"));
            Assert.Equal("ABC", entity.GetAttributeValue<string>("pl_normalizedname"));
            Assert.Equal("ITサービス", entity.GetAttributeValue<string>("pl_industry"));
            Assert.Equal("東京都", entity.GetAttributeValue<string>("pl_address"));
            Assert.Equal("03-1234-5678", entity.GetAttributeValue<string>("pl_phone"));
            Assert.Equal(100000000, entity.GetAttributeValue<OptionSetValue>("pl_tradingstatuscode")!.Value);
            // 会社確認（pl_identitystatuscode）は2026-09-26に列ごと廃止した。登録で書かない。
            Assert.False(entity.Attributes.Contains("pl_identitystatuscode"));
            Assert.Equal(PartnerRegistrationRecordFactory.InitialAclVersion, entity.GetAttributeValue<int>("pl_aclversion"));
            Assert.Equal(new DateTime(2026, 9, 13, 1, 2, 3, DateTimeKind.Utc), entity.GetAttributeValue<DateTime>("pl_registeredat"));
            Assert.Equal(MainOwnerId, entity.GetAttributeValue<EntityReference>("pl_mainownerlookup")!.Id);
            Assert.Equal(RegistrantId, entity.GetAttributeValue<EntityReference>("pl_registeredbylookup")!.Id);
        }

        [Fact]
        public void 作成属性へDataverse所有者を混ぜない()
        {
            var validation = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput
                {
                    Name = "青空ソリューションズ株式会社",
                    MainOwnerId = MainOwnerId,
                    IdempotencyKey = "register-2",
                });

            var entity = PartnerRegistrationRecordFactory.Build(
                validation.Value!,
                new DateTime(2026, 9, 13, 1, 2, 3, DateTimeKind.Utc));

            Assert.DoesNotContain("ownerid", entity.Attributes.Keys);
            Assert.Contains("pl_mainownerlookup", entity.Attributes.Keys);
            Assert.Contains("pl_registeredbylookup", entity.Attributes.Keys);
            Assert.DoesNotContain("createdby", entity.Attributes.Keys);
        }

        [Fact]
        public void 任意列が空なら未設定のままにする()
        {
            var validation = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput
                {
                    Name = "青空ソリューションズ株式会社",
                    MainOwnerId = MainOwnerId,
                    Industry = " ",
                    Address = null,
                    Phone = "",
                    IdempotencyKey = "register-3",
                });

            var entity = PartnerRegistrationRecordFactory.Build(
                validation.Value!,
                new DateTime(2026, 9, 13, 1, 2, 3, DateTimeKind.Utc));

            Assert.DoesNotContain("pl_industry", entity.Attributes.Keys);
            Assert.DoesNotContain("pl_address", entity.Attributes.Keys);
            Assert.DoesNotContain("pl_phone", entity.Attributes.Keys);
        }
    }
}
