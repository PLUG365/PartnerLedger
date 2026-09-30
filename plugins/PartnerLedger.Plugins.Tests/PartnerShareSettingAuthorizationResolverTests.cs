using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingAuthorizationResolverTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [Fact]
        public void Write権限はWriteとして解決しTeammembershipだけを承認者とする()
        {
            var initiating = new FakeOrganizationService
            {
                RetrievedPrincipalAccessRights = AccessRights.ReadAccess | AccessRights.WriteAccess,
            };
            var execution = new FakeOrganizationService();
            execution.Seed(new Entity("teammembership")
            {
                ["teamid"] = TeamId,
                ["systemuserid"] = UserId,
            });

            var caller = PartnerShareSettingAuthorizationResolver.Resolve(
                initiating,
                execution,
                UserId,
                PartnerId,
                TeamId);

            Assert.True(caller.IsApprover);
            Assert.Equal(PartnerShareAccessLevel.Write, caller.EffectiveAccess);
            var request = Assert.IsType<RetrievePrincipalAccessRequest>(initiating.ExecutedRequests[0]);
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(UserId, request.Principal.Id);
        }

        [Fact]
        public void Team未所属は承認者ではなくReadだけを返す()
        {
            var initiating = new FakeOrganizationService
            {
                RetrievedPrincipalAccessRights = AccessRights.ReadAccess,
            };

            var caller = PartnerShareSettingAuthorizationResolver.Resolve(
                initiating,
                new FakeOrganizationService(),
                UserId,
                PartnerId,
                TeamId);

            Assert.False(caller.IsApprover);
            Assert.Equal(PartnerShareAccessLevel.Read, caller.EffectiveAccess);
        }

        [Fact]
        public void 読取り権限も無い場合はEffectiveAccessを返さない()
        {
            Assert.Null(PartnerShareSettingAuthorizationResolver.ToEffectiveAccess(AccessRights.None));
            Assert.Null(PartnerShareSettingAuthorizationResolver.ToEffectiveAccess(AccessRights.AppendAccess));
        }
    }
}
