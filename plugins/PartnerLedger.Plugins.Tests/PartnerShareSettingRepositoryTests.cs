using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingRepositoryTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OtherPartnerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [Fact]
        public void RetrieveByPartnerは会社とアクティブ行に限定し共有設定Recordへ変換する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, PartnerStandardCreateContract.InitialShareSetupStatusCode));
            service.Seed(ValidSetting(Guid.Parse("44444444-4444-4444-8444-444444444444"), PartnerId, stateCode: 0));
            service.Seed(ValidSetting(Guid.Parse("66666666-6666-4666-8666-666666666666"), PartnerId, stateCode: 1));
            service.Seed(ValidSetting(Guid.Parse("77777777-7777-4777-8777-777777777777"), OtherPartnerId, stateCode: 0));

            var records = PartnerShareSettingRepository.RetrieveByPartner(service, PartnerId);

            var record = Assert.Single(records);
            Assert.Equal(PartnerId, record.PartnerId);
            Assert.Equal(PartnerSharePrincipalKind.User, record.PrincipalKind);
        }

        [Fact]
        public void ResolvePhaseは保存済みChoiceだけで初期設定と設定完了を判定する()
        {
            var service = new FakeOrganizationService();
            service.Seed(Partner(PartnerId, PartnerStandardCreateContract.InitialShareSetupStatusCode));
            Assert.Equal(PartnerSharePhase.InitialSetup, PartnerShareSettingRepository.ResolvePhase(service, PartnerId));

            var readyId = Guid.Parse("88888888-8888-4888-8888-888888888888");
            service.Seed(Partner(readyId, PartnerStandardCreateContract.ReadyShareSetupStatusCode));
            Assert.Equal(PartnerSharePhase.Ready, PartnerShareSettingRepository.ResolvePhase(service, readyId));

            var invalidId = Guid.Parse("99999999-9999-4999-8999-999999999999");
            service.Seed(Partner(invalidId, 999));
            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingRepository.ResolvePhase(service, invalidId));
        }

        [Fact]
        public void RetrieveDirectShareStateは直接共有一覧から対象principalだけを取り由来を勝手に推測しない()
        {
            var service = new FakeOrganizationService
            {
                RetrievedPrincipalAccessRights = AccessRights.ReadAccess | AccessRights.AppendToAccess,
                RetrievedSharedPrincipalAccesses = new[]
                {
                    new PrincipalAccess
                    {
                        Principal = new EntityReference("team", TeamId),
                        AccessMask = AccessRights.ReadAccess,
                    },
                },
            };

            var state = PartnerShareSettingRepository.RetrieveDirectShareState(
                service,
                PartnerId,
                PartnerSharePrincipalKind.Team,
                TeamId,
                PartnerShareDirectShareProvenance.UntrackedOrExternal);

            var request = Assert.IsType<RetrieveSharedPrincipalsAndAccessRequest>(service.ExecutedRequests.Single());
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(AccessRights.ReadAccess, state.CurrentRights);
            Assert.Equal(PartnerShareDirectShareProvenance.UntrackedOrExternal, state.Provenance);
        }

        private static Entity Partner(Guid id, int phase)
            => new Entity(PartnerStandardCreateContract.EntityName, id)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] = new OptionSetValue(phase),
            };

        private static Entity ValidSetting(Guid id, Guid partnerId, int stateCode)
        {
            var input = new PartnerShareSettingInput
            {
                PartnerId = partnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = UserId,
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = id.ToString(),
            };
            return new Entity(PartnerShareSettingRecordFactory.EntityName, id)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", partnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateActiveOption),
                [PartnerShareSettingRecordFactory.ProtectedPathAttribute] = false,
                [PartnerShareSettingRecordFactory.ManagedProjectionAttribute] = false,
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = input.RequestKey,
                [PartnerShareSettingRecordFactory.ContentHashAttribute] = PartnerShareSettingFingerprint.Compute(input),
                ["createdby"] = new EntityReference("systemuser", UserId),
                ["statecode"] = new OptionSetValue(stateCode),
            };
        }
    }
}
