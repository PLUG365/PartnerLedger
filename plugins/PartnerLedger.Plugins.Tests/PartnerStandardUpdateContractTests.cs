using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerStandardUpdateContractTests
    {
        private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid PartnerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid SettingsId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        [Fact]
        public void ポリシーOFFの住所電話は標準Updateで正規化し監査する()
        {
            var service = ServiceWithPolicy(address: false, phone: false);
            var target = new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardUpdateContract.AddressAttribute] = "  東京都千代田区  ",
                [PartnerStandardUpdateContract.PhoneAttribute] = " 03-1234-5678 ",
            };

            var result = PartnerStandardUpdatePlugin.ValidateAndAudit(target, ActivePartner(), CallerId, service);

            Assert.True(result.IsValid);
            Assert.Equal("東京都千代田区", target.GetAttributeValue<string>(PartnerStandardUpdateContract.AddressAttribute));
            Assert.Equal("03-1234-5678", target.GetAttributeValue<string>(PartnerStandardUpdateContract.PhoneAttribute));
            Assert.Equal(new[] { "pl_address", "pl_phone" }, result.ChangedAttributes);
            var log = service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities.Single();
            Assert.Equal("DirectPartnerUpdate", log.GetAttributeValue<string>("pl_operationcode"));
            Assert.Contains("pl_address,pl_phone", log.GetAttributeValue<string>("pl_name"));
        }

        [Fact]
        public void ポリシーOFFの空文字はDataverseのnullへ正規化する()
        {
            var target = new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardUpdateContract.AddressAttribute] = " ",
                [PartnerStandardUpdateContract.PhoneAttribute] = null,
            };

            var result = PartnerStandardUpdateContract.Validate(
                target,
                ActivePartner(),
                CallerId,
                SettingsWithPolicy(address: false, phone: false));

            Assert.True(result.IsValid);
            Assert.Null(target[PartnerStandardUpdateContract.AddressAttribute]);
            Assert.Null(target[PartnerStandardUpdateContract.PhoneAttribute]);
        }

        [Theory]
        [InlineData(PartnerStandardUpdateContract.NameAttribute, "companyName")]
        [InlineData(PartnerStandardUpdateContract.TradingStatusAttribute, "tradingStatus")]
        [InlineData(PartnerStandardUpdateContract.AddressAttribute, "address")]
        [InlineData(PartnerStandardUpdateContract.PhoneAttribute, "phone")]
        public void ポリシーONの各項目は標準Updateを拒否する(string attributeName, string policyName)
        {
            var service = ServiceWithPolicy(
                companyName: policyName == "companyName",
                tradingStatus: policyName == "tradingStatus",
                address: policyName == "address",
                phone: policyName == "phone");
            var target = new Entity("pl_partner", PartnerId)
            {
                [attributeName] = attributeName == PartnerStandardUpdateContract.TradingStatusAttribute
                    ? (object)new OptionSetValue(100000001)
                    : "変更後",
            };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerStandardUpdatePlugin.ValidateAndAudit(target, ActivePartner(), CallerId, service));

            Assert.Contains(nameof(PartnerStandardUpdateValidationError.ApprovalRequired), exception.Message);
            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
        }

        [Theory]
        [InlineData("pl_normalizedname")]
        [InlineData("pl_industry")]
        [InlineData("ownerid")]
        [InlineData("statecode")]
        [InlineData("statuscode")]
        public void 権威列はポリシーOFFでも拒否する(string attributeName)
        {
            var result = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { [attributeName] = "client-value" },
                ActivePartner(),
                CallerId,
                SettingsWithPolicy(address: false, phone: false));

            Assert.False(result.IsValid);
            Assert.Equal(PartnerStandardUpdateValidationError.ForbiddenAuthorityInput, result.Error);
        }

        [Fact]
        public void 不正な型値状態PreImage設定はfail_closedする()
        {
            var settings = SettingsWithPolicy(address: false, phone: false);
            var wrongName = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { [PartnerStandardUpdateContract.NameAttribute] = 42 },
                ActivePartner(), CallerId, settings);
            Assert.Equal(PartnerStandardUpdateValidationError.NameTypeInvalid, wrongName.Error);

            var wrongState = ActivePartner();
            wrongState["statecode"] = new OptionSetValue(1);
            var inactive = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { [PartnerStandardUpdateContract.AddressAttribute] = "変更" },
                wrongState, CallerId, settings);
            Assert.Equal(PartnerStandardUpdateValidationError.CurrentStateInvalid, inactive.Error);

            var missingPreImage = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { [PartnerStandardUpdateContract.AddressAttribute] = "変更" },
                null, CallerId, settings);
            Assert.Equal(PartnerStandardUpdateValidationError.PreImageRequired, missingPreImage.Error);
        }

        [Fact]
        public void 内部承認反映は派生名を許可し権威列を拒否する()
        {
            PartnerStandardUpdateContract.ValidateTrustedInternalTarget(
                new Entity("pl_partner", PartnerId)
                {
                    [PartnerStandardUpdateContract.AddressAttribute] = "承認済み住所",
                    [PartnerStandardUpdateContract.NormalizedNameAttribute] = "承認済み"
                },
                "pl_partner");

            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerStandardUpdateContract.ValidateTrustedInternalTarget(
                    new Entity("pl_partner", PartnerId) { ["ownerid"] = "forbidden" },
                    "pl_partner"));
        }

        private static readonly Guid CurrentMainOwnerId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly Guid NewMainOwnerId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        private static readonly Guid ApproverTeamId = Guid.Parse("66666666-6666-4666-8666-666666666666");

        [Fact]
        public void 主担当は承認設定で承認が必要なら標準Updateを拒否する()
        {
            // 版1の設定は主担当の変更を「承認が必要」として読む（2026-09-29）。
            var service = ServiceWithPolicy();
            var target = new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = new EntityReference("systemuser", NewMainOwnerId) };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerStandardUpdatePlugin.ValidateAndAudit(target, ActivePartner(), CallerId, service));

            Assert.Contains(nameof(PartnerStandardUpdateValidationError.ApprovalRequired), exception.Message);
            Assert.DoesNotContain(service.ExecutedRequests, r => r is Microsoft.Xrm.Sdk.Messages.CreateRequest || r is Microsoft.Crm.Sdk.Messages.GrantAccessRequest);
        }

        [Fact]
        public void 主担当は承認が不要なら受け付け_新しい主担当の共有を整える()
        {
            var service = ServiceForMainOwnerChange();
            var target = new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = new EntityReference("systemuser", NewMainOwnerId) };

            var result = PartnerStandardUpdatePlugin.ValidateAndAudit(target, ActivePartner(), CallerId, service);

            Assert.True(result.IsValid);
            Assert.Equal(new[] { "pl_mainownerlookup" }, result.ChangedAttributes);
            var create = Assert.Single(service.ExecutedRequests.OfType<Microsoft.Xrm.Sdk.Messages.CreateRequest>());
            Assert.Equal(NewMainOwnerId, create.Target.GetAttributeValue<EntityReference>("pl_userprincipallookup")!.Id);
            var grant = Assert.Single(service.ExecutedRequests.OfType<Microsoft.Crm.Sdk.Messages.GrantAccessRequest>());
            Assert.Equal(NewMainOwnerId, grant.PrincipalAccess.Principal.Id);
        }

        [Fact]
        public void 主担当の直接の変更でも_通常の利用者でない人と今と同じ人は拒否する()
        {
            var disabled = ServiceForMainOwnerChange();
            disabled.Retrieve("systemuser", NewMainOwnerId, new ColumnSet(true))["isdisabled"] = true;
            var same = ServiceForMainOwnerChange();

            var notShareable = Assert.Throws<InvalidPluginExecutionException>(() => PartnerStandardUpdatePlugin.ValidateAndAudit(
                new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = new EntityReference("systemuser", NewMainOwnerId) },
                ActivePartner(), CallerId, disabled));
            var unchanged = Assert.Throws<InvalidPluginExecutionException>(() => PartnerStandardUpdatePlugin.ValidateAndAudit(
                new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = new EntityReference("systemuser", CurrentMainOwnerId) },
                ActivePartner(), CallerId, same));

            Assert.Contains("主担当", notShareable.Message);
            Assert.Contains("主担当", unchanged.Message);
            Assert.DoesNotContain(disabled.ExecutedRequests, r => r is Microsoft.Xrm.Sdk.Messages.CreateRequest || r is Microsoft.Crm.Sdk.Messages.GrantAccessRequest);
        }

        [Fact]
        public void 主担当の値がユーザーへの参照でなければ拒否する()
        {
            var result = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = "client-value" },
                ActivePartner(),
                CallerId,
                SettingsWithPolicy(mainOwner: false));
            var team = PartnerStandardUpdateContract.Validate(
                new Entity("pl_partner", PartnerId) { ["pl_mainownerlookup"] = new EntityReference("team", NewMainOwnerId) },
                ActivePartner(),
                CallerId,
                SettingsWithPolicy(mainOwner: false));

            Assert.Equal(PartnerStandardUpdateValidationError.MainOwnerTypeInvalid, result.Error);
            Assert.Equal(PartnerStandardUpdateValidationError.MainOwnerTypeInvalid, team.Error);
        }

        private static FakeOrganizationService ServiceForMainOwnerChange()
        {
            var service = ServiceWithPolicy(mainOwner: false);
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_mainownerlookup"] = new EntityReference("systemuser", CurrentMainOwnerId),
                ["ownerid"] = new EntityReference("systemuser", CallerId),
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] = new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
            });
            service.Seed(new Entity("systemuser", NewMainOwnerId) { ["accessmode"] = new OptionSetValue(0), ["isdisabled"] = false });
            var definitionId = Guid.NewGuid();
            service.Seed(new Entity("environmentvariabledefinition", definitionId) { ["schemaname"] = PartnerLedgerEnvironmentVariableNames.ApproverTeamId });
            service.Seed(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", definitionId),
                ["value"] = ApproverTeamId.ToString("D"),
            });
            service.Seed(new Entity("team", ApproverTeamId) { ["teamtype"] = new OptionSetValue(3) });
            return service;
        }

        private static Entity ActivePartner()
            => new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(0),
            };

        private static FakeOrganizationService ServiceWithPolicy(
            bool companyName = true,
            bool tradingStatus = true,
            bool address = false,
            bool phone = false,
            bool? mainOwner = null)
        {
            var service = new FakeOrganizationService();
            service.Seed(SettingsEntity(companyName, tradingStatus, address, phone, mainOwner));
            return service;
        }

        private static ApprovalSettingsReadResult SettingsWithPolicy(
            bool companyName = true,
            bool tradingStatus = true,
            bool address = false,
            bool phone = false,
            bool? mainOwner = null)
            => ApprovalSettingsRepository.RetrieveActive(
                ServiceWithPolicy(companyName, tradingStatus, address, phone, mainOwner));

        // mainOwnerを指定したときは版2、指定しないときは版1（主担当＝承認が必要）の設定にする。
        private static Entity SettingsEntity(bool companyName, bool tradingStatus, bool address, bool phone, bool? mainOwner)
            => new Entity("pl_settings", SettingsId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = $"{{\"schemaVersion\":{(mainOwner.HasValue ? 2 : 1)},\"companyName\":{companyName.ToString().ToLowerInvariant()},\"tradingStatus\":{tradingStatus.ToString().ToLowerInvariant()},\"address\":{address.ToString().ToLowerInvariant()},\"phone\":{phone.ToString().ToLowerInvariant()},\"contractUpdate\":false"
                    + (mainOwner.HasValue ? $",\"mainOwner\":{mainOwner.Value.ToString().ToLowerInvariant()}" : "") + "}",
            };
    }
}
