using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalTargetRepositoryTests
    {
        private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid PartnerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid OtherPartnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        private static readonly Guid ContractId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        [Fact]
        public void 取引先申請はアクティブな対象行をrow_version付きで解決する()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_requesttypecode"] = "company-name",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            service.Seed(ActivePartner(PartnerId));

            var result = ApprovalTargetRepository.RetrieveForRequest(service, RequestId);

            Assert.True(result.IsValid);
            Assert.Equal(ApprovalTargetResolutionError.None, result.Error);
            Assert.NotNull(result.Target);
            Assert.Equal("pl_partner", result.Target!.EntityName);
            Assert.Equal(PartnerId, result.Target.Id);
            Assert.Equal(PartnerId, result.Target.PartnerId);
            Assert.Equal("1", result.Target.RowVersion);
            Assert.Equal("株式会社サンプル", result.Target.CurrentRow.GetAttributeValue<string>("pl_name"));
        }

        [Fact]
        public void 契約申請は契約Lookupと契約側の所属会社を一致検証する()
        {
            var service = SeedContractRequest(PartnerId, PartnerId);

            var result = ApprovalTargetRepository.RetrieveForRequest(service, RequestId);

            Assert.True(result.IsValid);
            Assert.Equal("pl_contract", result.Target!.EntityName);
            Assert.Equal(ContractId, result.Target.Id);
            Assert.Equal(PartnerId, result.Target.PartnerId);
            Assert.Equal(new OptionSetValue(100000000), result.Target.CurrentRow.GetAttributeValue<OptionSetValue>("pl_contractstatuscode"));
        }

        [Fact]
        public void 契約の所属会社が申請の取引先と違えば拒否する()
        {
            var service = SeedContractRequest(PartnerId, OtherPartnerId);

            var result = ApprovalTargetRepository.RetrieveForRequest(service, RequestId);

            Assert.False(result.IsValid);
            Assert.Equal(ApprovalTargetResolutionError.ContractPartnerMismatch, result.Error);
            Assert.Null(result.Target);
        }

        [Fact]
        public void 契約の所属Lookup欠損と不正論理名を拒否する()
        {
            var missingLookup = SeedContractRequest(PartnerId, null);
            var missingResult = ApprovalTargetRepository.RetrieveForRequest(missingLookup, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.ContractPartnerLookupRequired, missingResult.Error);

            var wrongLogicalName = SeedContractRequest(PartnerId, PartnerId, "account");
            var wrongNameResult = ApprovalTargetRepository.RetrieveForRequest(wrongLogicalName, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.ContractPartnerLookupUnsupported, wrongNameResult.Error);
        }

        [Fact]
        public void 欠損と非アクティブ対象と申請Lookup欠損を拒否する()
        {
            var missing = new FakeOrganizationService();
            missing.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            var missingResult = ApprovalTargetRepository.RetrieveForRequest(missing, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.TargetNotFound, missingResult.Error);

            var inactive = new FakeOrganizationService();
            inactive.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
            });
            inactive.Seed(new Entity("pl_partner", PartnerId)
            {
                ["statecode"] = new OptionSetValue(1),
            });
            var inactiveResult = ApprovalTargetRepository.RetrieveForRequest(inactive, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.TargetInactive, inactiveResult.Error);

            var noPartner = new FakeOrganizationService();
            noPartner.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
            });
            var noPartnerResult = ApprovalTargetRepository.RetrieveForRequest(noPartner, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.PartnerRequired, noPartnerResult.Error);
        }

        [Fact]
        public void 存在しない申請と空の申請IDを拒否する()
        {
            var service = new FakeOrganizationService();

            var emptyId = ApprovalTargetRepository.RetrieveForRequest(service, Guid.Empty);
            Assert.Equal(ApprovalTargetResolutionError.RequestRequired, emptyId.Error);

            var missing = ApprovalTargetRepository.RetrieveForRequest(service, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.RequestNotFound, missing.Error);
        }

        [Fact]
        public void 申請側Lookupの論理名を固定し不正な参照を拒否する()
        {
            var wrongPartnerLookup = new FakeOrganizationService();
            wrongPartnerLookup.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("account", PartnerId),
            });
            var wrongPartnerResult = ApprovalTargetRepository.RetrieveForRequest(wrongPartnerLookup, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.PartnerLookupUnsupported, wrongPartnerResult.Error);

            var wrongContractLookup = new FakeOrganizationService();
            wrongContractLookup.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_partnerlookup"] = new EntityReference("pl_partner", PartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_partner", ContractId),
            });
            var wrongContractResult = ApprovalTargetRepository.RetrieveForRequest(wrongContractLookup, RequestId);
            Assert.Equal(ApprovalTargetResolutionError.ContractLookupUnsupported, wrongContractResult.Error);
        }

        private static FakeOrganizationService SeedContractRequest(
            Guid requestPartnerId,
            Guid? contractPartnerId,
            string contractPartnerLogicalName = "pl_partner")
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_request", RequestId)
            {
                ["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString(),
                ["pl_requesttypecode"] = "contract-update",
                ["pl_partnerlookup"] = new EntityReference("pl_partner", requestPartnerId),
                ["pl_contractlookup"] = new EntityReference("pl_contract", ContractId),
            });
            var contract = new Entity("pl_contract", ContractId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
                ["pl_name"] = "基本取引契約",
            };
            if (contractPartnerId.HasValue)
            {
                contract["pl_partnerlookup"] = new EntityReference(contractPartnerLogicalName, contractPartnerId.Value);
            }
            service.Seed(contract);
            return service;
        }

        private static Entity ActivePartner(Guid id)
            => new Entity("pl_partner", id)
            {
                ["statecode"] = new OptionSetValue(0),
                ["pl_name"] = "株式会社サンプル",
                ["pl_tradingstatuscode"] = new OptionSetValue(100000001),
            };
    }
}
