using System;
using System.Collections.Generic;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingContractTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid UserB = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid TeamA = Guid.Parse("44444444-4444-4444-4444-444444444444");

        private static PartnerShareCaller Approver(Guid userId = default)
            => new PartnerShareCaller {UserId = userId == Guid.Empty ? UserA : userId, IsApprover = true};

        private static PartnerShareSettingInput UserInput(
            Guid userId = default,
            PartnerShareAccessLevel access = PartnerShareAccessLevel.Read,
            string key = "share-1")
            => new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                UserId = userId == Guid.Empty ? UserB : userId,
                AccessLevel = access,
                RequestKey = key,
            };

        private static PartnerShareSettingRecord Record(
            Guid id,
            Guid principalId,
            Guid createdBy,
            bool protectedPath = false,
            PartnerShareAccessLevel access = PartnerShareAccessLevel.Read)
            => new PartnerShareSettingRecord
            {
                Id = id,
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                PrincipalId = principalId,
                AccessLevel = access,
                State = PartnerShareSettingState.Active,
                CreatedByUserId = createdBy,
                IsProtectedManagementPath = protectedPath,
                RequestKey = "existing-" + id,
                ContentHash = PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = principalId,
                    AccessLevel = access,
                    RequestKey = "not-part-of-hash",
                }),
            };

        [Fact]
        public void 初期設定は承認者だけが作成できる()
        {
            var input = UserInput();

            Assert.True(PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.InitialSetup, Approver(), input).IsAllowed);
            Assert.Equal(PartnerShareSettingError.InitialSetupApproverRequired,
                PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.InitialSetup, Member(UserA, PartnerShareAccessLevel.Write), input).Error);
        }

        [Fact]
        public void 登録後は閲覧者が閲覧共有を作成できるが編集共有へ昇格できない()
        {
            var reader = Member(UserA, PartnerShareAccessLevel.Read);

            Assert.True(PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.Ready, reader, UserInput()).IsAllowed);
            Assert.Equal(PartnerShareSettingError.AccessExceedsCaller,
                PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.Ready, reader,
                    UserInput(access: PartnerShareAccessLevel.Write)).Error);
            Assert.True(PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.Ready,
                Member(UserA, PartnerShareAccessLevel.Write), UserInput(access: PartnerShareAccessLevel.Write)).IsAllowed);
        }

        [Fact]
        public void 会社アクセスが無い呼出者は登録後の共有設定を作成できない()
        {
            Assert.Equal(PartnerShareSettingError.CompanyAccessRequired,
                PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.Ready,
                    Member(UserA, access: null), UserInput()).Error);
        }

        [Fact]
        public void UserとTeamの参照は排他的に指定する()
        {
            var both = UserInput();
            both.TeamId = TeamA;
            Assert.Equal(PartnerShareSettingError.PrincipalExclusive, PartnerShareSettingContract.Validate(both).Error);

            var team = new PartnerShareSettingInput
            {
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                TeamId = TeamA,
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = "team-1",
            };
            Assert.True(PartnerShareSettingContract.Validate(team).IsAllowed);
            Assert.Equal(PartnerShareSettingError.PrincipalRequired,
                PartnerShareSettingContract.Validate(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "team-2",
                }).Error);
        }

        [Theory]
        [InlineData(PartnerShareSettingError.PartnerRequired)]
        [InlineData(PartnerShareSettingError.PrincipalKindRequired)]
        [InlineData(PartnerShareSettingError.AccessLevelRequired)]
        [InlineData(PartnerShareSettingError.RequestKeyRequired)]
        public void 共有設定に必要な入力を欠かせない(PartnerShareSettingError expected)
        {
            var input = UserInput();
            switch (expected)
            {
                case PartnerShareSettingError.PartnerRequired: input.PartnerId = Guid.Empty; break;
                case PartnerShareSettingError.PrincipalKindRequired: input.PrincipalKind = null; break;
                case PartnerShareSettingError.AccessLevelRequired: input.AccessLevel = null; break;
                case PartnerShareSettingError.RequestKeyRequired: input.RequestKey = " "; break;
            }

            Assert.Equal(expected, PartnerShareSettingContract.Validate(input).Error);
        }

        [Fact]
        public void 共有設定の変更は対象会社とprincipalを固定し会社アクセス保持者または承認者に許可する()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA, access: PartnerShareAccessLevel.Read);
            var desired = UserInput(userId: UserB, access: PartnerShareAccessLevel.Write, key: "update-1");

            Assert.True(PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Member(UserA, PartnerShareAccessLevel.Write), current, desired).IsAllowed);
            Assert.True(PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Member(UserB, PartnerShareAccessLevel.Write), current, desired).IsAllowed);
            Assert.True(PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Approver(), current, desired).IsAllowed);

            desired.UserId = UserA;
            Assert.Equal(PartnerShareSettingError.ImmutableTarget,
                PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Approver(), current, desired).Error);
        }

        [Fact]
        public void 会社アクセスが無い呼出者は既存設定の変更と取消ができない()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA, access: PartnerShareAccessLevel.Read);
            var noAccess = Member(UserA, access: null);

            Assert.Equal(PartnerShareSettingError.CompanyAccessRequired,
                PartnerShareSettingContract.AuthorizeUpdate(
                    PartnerSharePhase.Ready,
                    noAccess,
                    current,
                    UserInput(userId: UserB, key: "no-access-update")).Error);
            Assert.Equal(PartnerShareSettingError.CompanyAccessRequired,
                PartnerShareSettingContract.AuthorizeRevoke(
                    PartnerSharePhase.Ready,
                    noAccess,
                    current,
                    new[] { current }).Error);
        }

        [Fact]
        public void 登録後の会社アクセス保持者は作成者でなくても通常設定を取消できる()
        {
            var current = Record(Guid.NewGuid(), UserA, UserB, access: PartnerShareAccessLevel.Read);

            Assert.True(PartnerShareSettingContract.AuthorizeRevoke(
                PartnerSharePhase.Ready,
                Member(UserB, PartnerShareAccessLevel.Read),
                current,
                new[] { current }).IsAllowed);
        }

        [Fact]
        public void 閲覧だけの人は編集の共有を取消も閲覧への変更もできない()
        {
            // 2026-09-28ユーザー決定（監査#3）：変更・取消できるのは自分と同じか低い共有だけ。
            // 例：閲覧だけ共有された人が、主担当の編集の共有を外したり下げたりできない。
            var current = Record(Guid.NewGuid(), UserB, UserA, access: PartnerShareAccessLevel.Write);
            var readOnly = Member(UserA, PartnerShareAccessLevel.Read);

            Assert.Equal(PartnerShareSettingError.AccessExceedsCaller,
                PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, readOnly, current, new[] { current }).Error);
            Assert.Equal(PartnerShareSettingError.AccessExceedsCaller,
                PartnerShareSettingContract.AuthorizeUpdate(
                    PartnerSharePhase.Ready,
                    readOnly,
                    current,
                    UserInput(userId: UserB, access: PartnerShareAccessLevel.Read, key: "downgrade-1")).Error);
        }

        [Fact]
        public void 編集を持つ人と承認者は編集の共有を取消も閲覧への変更もできる()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA, access: PartnerShareAccessLevel.Write);

            foreach (var caller in new[] { Member(UserA, PartnerShareAccessLevel.Write), Approver() })
            {
                Assert.True(PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, caller, current, new[] { current }).IsAllowed);
                Assert.True(PartnerShareSettingContract.AuthorizeUpdate(
                    PartnerSharePhase.Ready,
                    caller,
                    current,
                    UserInput(userId: UserB, access: PartnerShareAccessLevel.Read, key: "downgrade-2")).IsAllowed);
            }
        }

        [Fact]
        public void 取消済みの共有設定は再変更再取消できない()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA);
            current.State = PartnerShareSettingState.Revoked;

            Assert.Equal(PartnerShareSettingError.SettingNotActive,
                PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Approver(), current, UserInput(userId: UserB, key: "update-2")).Error);
            Assert.Equal(PartnerShareSettingError.SettingNotActive,
                PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, Approver(), current, new[] {current}).Error);
        }

        [Fact]
        public void 非承認者は保護された管理経路を変更取消できない()
        {
            var current = Record(Guid.NewGuid(), UserA, UserA, protectedPath: true, access: PartnerShareAccessLevel.Write);
            var desired = UserInput(userId: UserA, access: PartnerShareAccessLevel.Read, key: "protected-update");

            Assert.Equal(PartnerShareSettingError.ProtectedManagementPath,
                PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, Member(UserA, PartnerShareAccessLevel.Write), current, desired).Error);
            Assert.Equal(PartnerShareSettingError.ProtectedManagementPath,
                PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, Member(UserA, PartnerShareAccessLevel.Write), current, new[] {current}).Error);
        }

        [Fact]
        public void 最後の保護された管理経路は承認者でも取消できない()
        {
            var current = Record(Guid.NewGuid(), UserA, UserA, protectedPath: true);

            Assert.Equal(PartnerShareSettingError.LastManagementPath,
                PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, Approver(), current, new[] {current}).Error);
        }

        [Fact]
        public void 保護された管理経路が別に残れば承認者は対象を取消できる()
        {
            var current = Record(Guid.NewGuid(), UserA, UserA, protectedPath: true);
            var other = Record(Guid.NewGuid(), UserB, UserA, protectedPath: true);

            Assert.True(PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, Approver(), current, new[] {current, other }).IsAllowed);
        }

        [Fact]
        public void 同じ要求の再送は同じ内容だけ再利用できる()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA, access: PartnerShareAccessLevel.Read);
            current.RequestKey = "share-1";
            current.ContentHash = PartnerShareSettingFingerprint.Compute(UserInput());

            Assert.True(PartnerShareSettingContract.IsSameRequest(current, UserInput()));
            Assert.False(PartnerShareSettingContract.IsSameRequest(current, UserInput(access: PartnerShareAccessLevel.Write)));
        }

        [Fact]
        public void 要求キーとハッシュの上限超過を拒否する()
        {
            Assert.Equal(PartnerShareSettingError.RequestKeyTooLong,
                PartnerShareSettingContract.Validate(UserInput(key: new string('x', PartnerShareSettingContract.RequestKeyMaxLength + 1))).Error);
            Assert.Equal(PartnerShareSettingFingerprint.HashLength, PartnerShareSettingFingerprint.Compute(UserInput()).Length);
            Assert.False(PartnerShareSettingFingerprint.IsValid("not-a-hash"));
        }

        [Fact]
        public void 未定義のprincipal種別とアクセス種別は拒否する()
        {
            var principal = UserInput();
            principal.PrincipalKind = (PartnerSharePrincipalKind)999;
            Assert.Equal(PartnerShareSettingError.PrincipalKindInvalid,
                PartnerShareSettingContract.Validate(principal).Error);

            var access = UserInput();
            access.AccessLevel = (PartnerShareAccessLevel)999;
            Assert.Equal(PartnerShareSettingError.AccessLevelInvalid,
                PartnerShareSettingContract.Validate(access).Error);
        }

        [Fact]
        public void 呼出者のUserIdが未解決なら認可しない()
        {
            Assert.Equal(PartnerShareSettingError.CallerUserRequired,
                PartnerShareSettingContract.AuthorizeCreate(PartnerSharePhase.Ready,
                    new PartnerShareCaller { EffectiveAccess = PartnerShareAccessLevel.Write }, UserInput()).Error);
        }

        [Fact]
        public void 空IDの共有設定行は変更や取消の対象にしない()
        {
            var current = Record(Guid.Empty, UserB, UserA);
            var caller = Member(UserA, PartnerShareAccessLevel.Write);

            Assert.Equal(PartnerShareSettingError.SettingIdRequired,
                PartnerShareSettingContract.AuthorizeUpdate(PartnerSharePhase.Ready, caller, current,
                    UserInput(userId: UserB, key: "update-empty-id")).Error);
            Assert.Equal(PartnerShareSettingError.SettingIdRequired,
                PartnerShareSettingContract.AuthorizeRevoke(PartnerSharePhase.Ready, caller, current, new[] { current }).Error);
        }

        [Fact]
        public void 取消済みの共有設定は同一要求の再送として扱わない()
        {
            var current = Record(Guid.NewGuid(), UserB, UserA);
            current.RequestKey = "share-1";
            current.ContentHash = PartnerShareSettingFingerprint.Compute(UserInput());
            current.State = PartnerShareSettingState.Revoked;

            Assert.False(PartnerShareSettingContract.IsSameRequest(current, UserInput()));
        }

        private static PartnerShareCaller Member(Guid userId, PartnerShareAccessLevel? access)
            => new PartnerShareCaller { UserId = userId, EffectiveAccess = access };
    }
}
