using System;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSetupCompletionTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ApproverTeamId = Guid.Parse("55555555-5555-5555-8555-555555555555");
        private static readonly Guid OtherTeamId = Guid.Parse("66666666-6666-6666-8666-666666666666");

        [Fact]
        public void 初期設定中の承認者Team保護共有だけが完了へ進む()
        {
            Assert.True(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.InitialSetup,
                TeamInput(ApproverTeamId),
                ApproverTeamId));
        }

        [Fact]
        public void 設定完了後は再度完了処理を行わない()
        {
            Assert.False(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.Ready,
                TeamInput(ApproverTeamId),
                ApproverTeamId));
        }

        [Fact]
        public void 別Teamの初期共有では完了へ進まない()
        {
            Assert.False(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.InitialSetup,
                TeamInput(OtherTeamId),
                ApproverTeamId));
        }

        [Fact]
        public void User共有では完了へ進まない()
        {
            var input = new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = Guid.Parse("77777777-7777-7777-8777-777777777777"),
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = "completion-user",
            };

            Assert.False(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.InitialSetup,
                input,
                ApproverTeamId));
        }

        [Fact]
        public void 保存済み保護設定のUpdateも完了へ進める()
        {
            var current = new PartnerShareSettingRecord
            {
                Id = Guid.NewGuid(),
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                PrincipalId = ApproverTeamId,
                AccessLevel = PartnerShareAccessLevel.Read,
                State = PartnerShareSettingState.Active,
                IsProtectedManagementPath = true,
            };

            Assert.True(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.InitialSetup,
                current,
                ApproverTeamId));
        }

        [Fact]
        public void 保護フラグがない保存済み設定では完了へ進まない()
        {
            var current = new PartnerShareSettingRecord
            {
                Id = Guid.NewGuid(),
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                PrincipalId = ApproverTeamId,
                AccessLevel = PartnerShareAccessLevel.Read,
                State = PartnerShareSettingState.Active,
                IsProtectedManagementPath = false,
            };

            Assert.False(PartnerShareSetupCompletion.ShouldComplete(
                PartnerSharePhase.InitialSetup,
                current,
                ApproverTeamId));
        }

        private static PartnerShareSettingInput TeamInput(Guid teamId)
            => new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                TeamId = teamId,
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = "completion-team",
            };
    }
}
