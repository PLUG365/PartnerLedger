using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingProjectionExecutorTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void Grant計画は標準GrantAccessへ変換する()
        {
            var service = new FakeOrganizationService();
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(PartnerShareAccessLevel.Read, "executor-grant"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.None));

            PartnerShareSettingProjectionExecutor.Execute(service, plan);

            var request = Assert.IsType<GrantAccessRequest>(service.ExecutedRequests.Single());
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(UserId, request.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess, request.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void Modify計画は標準ModifyAccessへ変換する()
        {
            var service = new FakeOrganizationService();
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(PartnerShareAccessLevel.Write, "executor-modify"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.ReadAccess));

            PartnerShareSettingProjectionExecutor.Execute(service, plan);

            var request = Assert.IsType<ModifyAccessRequest>(service.ExecutedRequests.Single());
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(UserId, request.PrincipalAccess.Principal.Id);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, request.PrincipalAccess.AccessMask);
        }

        [Fact]
        public void Revoke計画は標準RevokeAccessへ変換する()
        {
            var service = new FakeOrganizationService();
            var target = Record(PartnerShareAccessLevel.Read, "executor-revoke");
            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] {target},
                Direct(AccessRights.ReadAccess));

            PartnerShareSettingProjectionExecutor.Execute(service, plan);

            var request = Assert.IsType<RevokeAccessRequest>(service.ExecutedRequests.Single());
            Assert.Equal(PartnerId, request.Target.Id);
            Assert.Equal(UserId, request.Revokee.Id);
        }

        [Fact]
        public void NoOp計画は共有メッセージを呼ばない()
        {
            var service = new FakeOrganizationService();
            var target = Record(PartnerShareAccessLevel.Read, "executor-noop");
            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] {target},
                Direct(AccessRights.None));
            PartnerShareSettingProjectionExecutor.Execute(service, plan);

            Assert.Empty(service.ExecutedRequests);
        }

        [Fact]
        public void 拒否計画は共有メッセージを呼ばず例外にする()
        {
            var service = new FakeOrganizationService();
            var target = Record(PartnerShareAccessLevel.Read, "executor-deny");
            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] {target},
                Direct(AccessRights.ReadAccess, PartnerShareDirectShareProvenance.UntrackedOrExternal));

            Assert.Throws<Microsoft.Xrm.Sdk.InvalidPluginExecutionException>(
                () => PartnerShareSettingProjectionExecutor.Execute(service, plan));
            Assert.Empty(service.ExecutedRequests);
        }

        private static PartnerShareCaller Approver()
            => new PartnerShareCaller {UserId = UserId, IsApprover = true};

        private static PartnerShareSettingInput Input(
            PartnerShareAccessLevel access,
            string requestKey)
            => new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = UserId,
                AccessLevel = access,
                RequestKey = requestKey,
            };

        private static PartnerShareSettingRecord Record(
            PartnerShareAccessLevel access,
            string requestKey)
            => new PartnerShareSettingRecord
            {
                Id = Guid.NewGuid(),
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                PrincipalId = UserId,
                AccessLevel = access,
                State = PartnerShareSettingState.Active,
                CreatedByUserId = UserId,
                RequestKey = requestKey,
                ContentHash = PartnerShareSettingFingerprint.Compute(Input(access, requestKey)),
            };

        private static PartnerShareDirectShareState Direct(
            AccessRights rights,
            PartnerShareDirectShareProvenance provenance = PartnerShareDirectShareProvenance.ManagedByPartnerLedger)
            => new PartnerShareDirectShareState
            {
                IsResolved = true,
                CurrentRights = rights,
                Provenance = provenance,
            };
    }
}
