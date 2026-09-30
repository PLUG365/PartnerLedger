using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingProjectionBoundaryTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid CallerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid SettingId = Guid.Parse("44444444-4444-4444-8444-444444444444");

        [Fact]
        public void Createは保存済み状態と現在の直接共有権を使ってGrant計画を作る()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity(PartnerStandardCreateContract.EntityName, PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] = new OptionSetValue(PartnerStandardCreateContract.InitialShareSetupStatusCode),
            });
            service.RetrievedPrincipalAccessRights = AccessRights.None;

            var plan = PartnerShareSettingProjectionBoundary.Create(
                service,
                CallerId,
                new PartnerShareCaller { UserId = CallerId, IsApprover = true },
                new Entity(PartnerShareSettingRecordFactory.EntityName)
                {
                    [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                    [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                    [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                    [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                    [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "boundary-create",
                },
                PartnerShareDirectShareProvenance.Unknown);

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.Grant, plan.Operation);

            PartnerShareSettingProjectionBoundary.Execute(service, plan);
            var request = Assert.IsType<GrantAccessRequest>(service.ExecutedRequests.Last());
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(UserId, request.PrincipalAccess.Principal.Id);
        }

        [Fact]
        public void InitiatingUserと解決済み認可主体が違えば実共有照会前に止める()
        {
            var service = new FakeOrganizationService();
            var target = new Entity(PartnerShareSettingRecordFactory.EntityName)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "boundary-mismatch",
            };

            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingProjectionBoundary.Create(
                service,
                CallerId,
                new PartnerShareCaller { UserId = UserId, IsApprover = true },
                target,
                PartnerShareDirectShareProvenance.Unknown));
            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 共有権の由来を外部として渡した取消はReconciliationで止まる()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity(PartnerStandardCreateContract.EntityName, PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] = new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
            });
            service.Seed(Setting(SettingId));
            service.RetrievedSharedPrincipalAccesses = new[]
            {
                new PrincipalAccess
                {
                    Principal = new EntityReference("systemuser", UserId),
                    AccessMask = AccessRights.ReadAccess,
                },
            };

            var plan = PartnerShareSettingProjectionBoundary.Update(
                service,
                CallerId,
                new PartnerShareCaller { UserId = CallerId, IsApprover = true },
                new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
                {
                    [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateRevokedOption),
                },
                PartnerShareDirectShareProvenance.UntrackedOrExternal);

            Assert.False(plan.IsAllowed);
            Assert.True(plan.RequiresReconciliation);
            Assert.DoesNotContain(service.ExecutedRequests, request => request is RevokeAccessRequest);
        }

        private static Entity Setting(Guid id)
        {
            var input = new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = UserId,
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = "boundary-setting",
            };
            return new Entity(PartnerShareSettingRecordFactory.EntityName, id)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateActiveOption),
                [PartnerShareSettingRecordFactory.ProtectedPathAttribute] = false,
                [PartnerShareSettingRecordFactory.ManagedProjectionAttribute] = false,
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = input.RequestKey,
                [PartnerShareSettingRecordFactory.ContentHashAttribute] = PartnerShareSettingFingerprint.Compute(input),
                ["createdby"] = new EntityReference("systemuser", CallerId),
                ["statecode"] = new OptionSetValue(0),
            };
        }
    }
}
