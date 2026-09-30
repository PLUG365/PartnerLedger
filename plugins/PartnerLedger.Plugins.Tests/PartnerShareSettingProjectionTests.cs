using System;
using System.Collections.Generic;
using Microsoft.Crm.Sdk.Messages;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingProjectionTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ApproverId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ApproverTeamId = Guid.Parse("55555555-5555-5555-8555-555555555555");
        private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid OtherUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [Fact]
        public void 新規principalへのCreateはGrantAccess計画になる()
        {
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.InitialSetup,
                Approver(),
                Input(UserId, PartnerShareAccessLevel.Read, "create-1"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.None));

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.Grant, plan.Operation);
            Assert.Equal(AccessRights.ReadAccess, plan.AccessMask);
            Assert.True(plan.IsManagedProjection);
            Assert.Equal("systemuser", plan.PrincipalReference.LogicalName);
            Assert.Equal(UserId, plan.PrincipalId);
        }

        [Fact]
        public void 同じ要求の再送は共有メッセージを再実行しない()
        {
            var current = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "replay-1");
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(UserId, PartnerShareAccessLevel.Read, "replay-1"),
                new[] { current },
                Direct(AccessRights.ReadAccess));

            Assert.True(plan.IsAllowed);
            Assert.True(plan.IsReplay);
            Assert.Equal(PartnerShareProjectionOperation.None, plan.Operation);
        }

        [Fact]
        public void 同じ要求キーの別内容は拒否する()
        {
            var current = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "same-key");
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(UserId, PartnerShareAccessLevel.Write, "same-key"),
                new[] { current },
                Direct(AccessRights.ReadAccess));

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.RequestKeyConflict, plan.Error);
        }

        [Fact]
        public void 登録後の閲覧者はWrite共有を投影できない()
        {
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Member(PartnerShareAccessLevel.Read),
                Input(UserId, PartnerShareAccessLevel.Write, "reader-write"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.None));

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.ContractDenied, plan.Error);
            Assert.Equal(PartnerShareSettingError.AccessExceedsCaller, plan.ContractError);
        }

        [Fact]
        public void 複数のActive設定は最大権利へ合成する()
        {
            var current = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "union-current");
            var other = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Write, "union-other");
            var desired = Input(UserId, PartnerShareAccessLevel.Read, "union-update");

            var plan = PartnerShareSettingProjection.PlanUpdate(
                PartnerSharePhase.Ready,
                Approver(),
                current,
                desired,
                new[] { current, other },
                Direct(AccessRights.ReadAccess, PartnerShareDirectShareProvenance.ManagedByPartnerLedger));

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.Modify, plan.Operation);
            Assert.Equal(AccessRights.ReadAccess | AccessRights.WriteAccess, plan.AccessMask);
        }

        [Fact]
        public void 同じprincipalの別設定が残る取消はModifyで権利を残す()
        {
            var target = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Write, "revoke-target");
            var remaining = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "revoke-remaining");

            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] { target, remaining },
                Direct(
                    AccessRights.ReadAccess | AccessRights.WriteAccess,
                    PartnerShareDirectShareProvenance.ManagedByPartnerLedger));

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.Modify, plan.Operation);
            Assert.Equal(AccessRights.ReadAccess, plan.AccessMask);
        }

        [Fact]
        public void 最後の管理対象共有はRevoke計画になる()
        {
            var target = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "revoke-last");

            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] { target },
                Direct(
                    AccessRights.ReadAccess,
                    PartnerShareDirectShareProvenance.ManagedByPartnerLedger));

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.Revoke, plan.Operation);
            Assert.Equal(AccessRights.None, plan.AccessMask);
        }

        [Fact]
        public void 旧経路由来の最後の共有権は勝手に剥がさず保留する()
        {
            var target = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "revoke-untracked");

            var plan = PartnerShareSettingProjection.PlanRevoke(
                PartnerSharePhase.Ready,
                Approver(),
                target,
                new[] { target },
                Direct(
                    AccessRights.ReadAccess,
                    PartnerShareDirectShareProvenance.UntrackedOrExternal));

            Assert.False(plan.IsAllowed);
            Assert.True(plan.RequiresReconciliation);
            Assert.Equal(PartnerShareProjectionError.UntrackedDirectShare, plan.Error);
        }

        [Fact]
        public void 旧経路のWriteをReadへ縮小する変更は保留する()
        {
            var current = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Write, "demote-old");
            var plan = PartnerShareSettingProjection.PlanUpdate(
                PartnerSharePhase.Ready,
                Approver(),
                current,
                Input(UserId, PartnerShareAccessLevel.Read, "demote-old-update"),
                new[] { current },
                Direct(
                    AccessRights.ReadAccess | AccessRights.WriteAccess,
                    PartnerShareDirectShareProvenance.UntrackedOrExternal));

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.UntrackedDirectShare, plan.Error);
        }

        [Fact]
        public void 旧経路由来の既存共有へ加算するCreateはPL管理対象印を立てない()
        {
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(UserId, PartnerShareAccessLevel.Read, "add-to-old-share"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.ReadAccess, PartnerShareDirectShareProvenance.UntrackedOrExternal));

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.None, plan.Operation);
            Assert.False(plan.IsManagedProjection);
        }

        [Fact]
        public void 初期承認者TeamのBootstrapReadだけはPL管理対象として引き継ぐ()
        {
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.InitialSetup,
                Approver(),
                TeamInput(ApproverTeamId, PartnerShareAccessLevel.Read, "initial-approver-team"),
                Array.Empty<PartnerShareSettingRecord>(),
                Direct(AccessRights.ReadAccess, PartnerShareDirectShareProvenance.UntrackedOrExternal),
                ApproverTeamId);

            Assert.True(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionOperation.None, plan.Operation);
            Assert.True(plan.IsManagedProjection);
        }

        [Fact]
        public void 不正な既存設定を検知したら投影しない()
        {
            var invalid = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "invalid-existing");
            invalid.AccessLevel = (PartnerShareAccessLevel)999;

            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(OtherUserId, PartnerShareAccessLevel.Read, "invalid-plan"),
                new[] { invalid },
                Direct(AccessRights.None));

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.InvalidExistingSetting, plan.Error);
        }

        [Fact]
        public void Userの保護経路を既存設定として受け入れない()
        {
            var invalid = Record(Guid.NewGuid(), UserId, ApproverId, PartnerShareAccessLevel.Read, "invalid-protected-user");
            invalid.IsProtectedManagementPath = true;

            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(OtherUserId, PartnerShareAccessLevel.Read, "invalid-protected-plan"),
                new[] { invalid },
                Direct(AccessRights.None));

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.InvalidExistingSetting, plan.Error);
        }

        [Fact]
        public void 直接共有権が未解決なら投影しない()
        {
            var plan = PartnerShareSettingProjection.PlanCreate(
                PartnerSharePhase.Ready,
                Approver(),
                Input(UserId, PartnerShareAccessLevel.Read, "unresolved-share"),
                Array.Empty<PartnerShareSettingRecord>(),
                new PartnerShareDirectShareState());

            Assert.False(plan.IsAllowed);
            Assert.Equal(PartnerShareProjectionError.DirectShareStateRequired, plan.Error);
        }

        private static PartnerShareCaller Approver()
            => new PartnerShareCaller { UserId = ApproverId, IsApprover = true };

        private static PartnerShareCaller Member(PartnerShareAccessLevel access)
            => new PartnerShareCaller { UserId = ApproverId, EffectiveAccess = access };

        private static PartnerShareSettingInput Input(
            Guid userId,
            PartnerShareAccessLevel access,
            string requestKey)
            => new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = userId,
                AccessLevel = access,
                RequestKey = requestKey,
            };

        private static PartnerShareSettingInput TeamInput(
            Guid teamId,
            PartnerShareAccessLevel access,
            string requestKey)
            => new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                TeamId = teamId,
                AccessLevel = access,
                RequestKey = requestKey,
            };

        private static PartnerShareSettingRecord Record(
            Guid id,
            Guid principalId,
            Guid createdBy,
            PartnerShareAccessLevel access,
            string requestKey)
        {
            var input = Input(principalId, access, requestKey);
            return new PartnerShareSettingRecord
            {
                Id = id,
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                PrincipalId = principalId,
                AccessLevel = access,
                State = PartnerShareSettingState.Active,
                CreatedByUserId = createdBy,
                IsProtectedManagementPath = false,
                IsManagedProjection = true,
                RequestKey = requestKey,
                ContentHash = PartnerShareSettingFingerprint.Compute(input),
            };
        }

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
