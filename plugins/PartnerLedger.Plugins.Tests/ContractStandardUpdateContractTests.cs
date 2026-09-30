using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ContractStandardUpdateContractTests
    {
        private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid ContractId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid SettingsId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        [Fact]
        public void ポリシーOFFでは許可列だけを検証して監査する()
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var target = new Entity("pl_contract", ContractId)
            {
                ["pl_name"] = "  変更後契約  ",
                ["pl_contractstatuscode"] = new OptionSetValue(100000001),
                ["pl_link"] = "  https://contoso.example/new  ",
            };

            var result = ContractStandardUpdatePlugin.ValidateAndAudit(
                target,
                ActiveContract(),
                CallerId,
                service);

            Assert.True(result.IsValid);
            Assert.Equal("変更後契約", target.GetAttributeValue<string>("pl_name"));
            Assert.Equal("https://contoso.example/new", target.GetAttributeValue<string>("pl_link"));
            var logs = service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities;
            Assert.Single(logs);
            Assert.Equal("DirectContractUpdate", logs.Single().GetAttributeValue<string>("pl_operationcode"));
            Assert.Equal(CallerId, logs.Single().GetAttributeValue<EntityReference>("pl_initiatinguserlookup")!.Id);
            Assert.Contains(ContractId.ToString("D"), logs.Single().GetAttributeValue<string>("pl_name"));
        }

        [Fact]
        public void Dataverseのプラットフォーム管理列は変更セットと監査から除外する()
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var target = new Entity("pl_contract", ContractId)
            {
                ["pl_contractid"] = ContractId,
                ["modifiedby"] = new EntityReference("systemuser", CallerId),
                ["modifiedon"] = DateTime.UtcNow,
                ["modifiedonbehalfby"] = new EntityReference("systemuser", CallerId),
                ["pl_name"] = "変更後契約",
            };

            var result = ContractStandardUpdatePlugin.ValidateAndAudit(
                target,
                ActiveContract(),
                CallerId,
                service);

            Assert.True(result.IsValid);
            Assert.Equal(new[] { "pl_name" }, result.ChangedAttributes);
            var log = service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities.Single();
            Assert.Contains(":fields:pl_name", log.GetAttributeValue<string>("pl_name"));
            Assert.DoesNotContain("modified", log.GetAttributeValue<string>("pl_name"));
        }

        [Fact]
        public void プラットフォーム管理列だけのUpdateは空の業務変更として拒否する()
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var target = new Entity("pl_contract", ContractId)
            {
                ["pl_contractid"] = ContractId,
                ["modifiedon"] = DateTime.UtcNow,
            };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    target,
                    ActiveContract(),
                    CallerId,
                    service));

            Assert.Contains(nameof(ContractStandardUpdateValidationError.UpdateEmpty), exception.Message);
            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
        }

        [Fact]
        public void ポリシーONでは標準Updateを拒否し監査を書かない()
        {
            var service = ServiceWithSettings(contractUpdate: true);

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "変更" },
                    ActiveContract(),
                    CallerId,
                    service));

            Assert.Contains(nameof(ContractStandardUpdateValidationError.ApprovalRequired), exception.Message);
            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
        }

        [Fact]
        public void 設定の行が無ければ既定値で承認対象にし_不正な設定なら直接Updateを拒否する()
        {
            // 2026-09-27：行が無いときは既定値（契約の更新・終了判断＝承認あり）で動く。
            var service = new FakeOrganizationService();
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "変更" },
                    ActiveContract(),
                    CallerId,
                    service));
            Assert.Contains(nameof(ContractStandardUpdateValidationError.ApprovalRequired), exception.Message);

            service.Seed(new Entity("pl_settings", SettingsId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = "{}",
            });
            var invalid = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "変更" },
                    ActiveContract(),
                    CallerId,
                    service));
            Assert.Contains(nameof(ContractStandardUpdateValidationError.SettingsUnavailable), invalid.Message);
        }

        [Theory]
        [InlineData("ownerid")]
        [InlineData("pl_partnerlookup")]
        [InlineData("pl_contracttypelookup")]
        [InlineData("pl_registrationkey")]
        [InlineData("statecode")]
        [InlineData("statuscode")]
        public void 権威列はポリシーOFFでも拒否する(string attributeName)
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var target = new Entity("pl_contract", ContractId) { [attributeName] = "client-value" };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(target, ActiveContract(), CallerId, service));

            Assert.Contains(nameof(ContractStandardUpdateValidationError.ForbiddenAuthorityInput), exception.Message);
        }

        [Theory]
        [InlineData("pl_name")]
        [InlineData("pl_contractstatuscode")]
        [InlineData("pl_enddate")]
        [InlineData("pl_autorenew")]
        [InlineData("pl_link")]
        public void 許可列の型違いは拒否する(string attributeName)
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var target = new Entity("pl_contract", ContractId) { [attributeName] = 42 };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(target, ActiveContract(), CallerId, service));

            Assert.Contains("TypeInvalid", exception.Message);
        }

        [Fact]
        public void 終了済み契約と非Active契約は変更できない()
        {
            var service = ServiceWithSettings(contractUpdate: false);
            var ended = ActiveContract();
            ended["pl_contractstatuscode"] = new OptionSetValue(100000001);
            var endedException = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "再開" }, ended, CallerId, service));
            Assert.Contains(nameof(ContractStandardUpdateValidationError.ContractAlreadyEnded), endedException.Message);

            var inactive = ActiveContract();
            inactive["statecode"] = new OptionSetValue(1);
            var inactiveException = Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "変更" }, inactive, CallerId, service));
            Assert.Contains(nameof(ContractStandardUpdateValidationError.CurrentStateInvalid), inactiveException.Message);
        }

        [Fact]
        public void 監査作成失敗はトランザクション境界で成功扱いにならない()
        {
            var service = ServiceWithSettings(contractUpdate: false);
            service.BeforeCreate = entity => entity.LogicalName == "pl_operationlog"
                ? new InvalidPluginExecutionException("audit failed")
                : null;

            Assert.Throws<InvalidPluginExecutionException>(() =>
                service.RunInTransaction(() => ContractStandardUpdatePlugin.ValidateAndAudit(
                    new Entity("pl_contract", ContractId) { ["pl_name"] = "変更" },
                    ActiveContract(),
                    CallerId,
                    service)));

            Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
        }

        [Fact]
        public void 内部承認反映も同じallow_listと型検査を通す()
        {
            var target = new Entity("pl_contract", ContractId)
            {
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
                ["pl_autorenew"] = false,
            };

            ContractStandardUpdateContract.ValidateTrustedInternalTarget(target, "pl_contract");

            Assert.Throws<InvalidPluginExecutionException>(() =>
                ContractStandardUpdateContract.ValidateTrustedInternalTarget(
                    new Entity("pl_contract", ContractId) { ["ownerid"] = "forbidden" },
                    "pl_contract"));
        }

        private static Entity ActiveContract()
            => new Entity("pl_contract", ContractId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
            };

        private static FakeOrganizationService ServiceWithSettings(bool contractUpdate)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_settings", SettingsId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_settingsversion"] = 1,
                ["pl_policyjson"] = $"{{\"schemaVersion\":1,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":{contractUpdate.ToString().ToLowerInvariant()}}}",
            });
            return service;
        }
    }
}
