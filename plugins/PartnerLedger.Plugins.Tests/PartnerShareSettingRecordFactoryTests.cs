using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingRecordFactoryTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid SettingId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [Fact]
        public void 初期設定のUser共有は保護経路にしない()
        {
            var entity = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = " share-1 ",
                },
                TeamId);

            Assert.Equal(PartnerShareSettingRecordFactory.EntityName, entity.LogicalName);
            Assert.Equal(PartnerId, entity.GetAttributeValue<EntityReference>(PartnerShareSettingRecordFactory.PartnerLookupAttribute)!.Id);
            Assert.Equal(
                PartnerShareSettingRecordFactory.PrincipalKindUserOption,
                entity.GetAttributeValue<OptionSetValue>(PartnerShareSettingRecordFactory.PrincipalKindAttribute)!.Value);
            Assert.Equal(UserId, entity.GetAttributeValue<EntityReference>(PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute)!.Id);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute, entity.Attributes.Keys);
            Assert.Equal(PartnerShareSettingRecordFactory.SettingStateActiveOption,
                entity.GetAttributeValue<OptionSetValue>(PartnerShareSettingRecordFactory.SettingStateAttribute)!.Value);
            Assert.False(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ProtectedPathAttribute));
            Assert.False(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ManagedProjectionAttribute));
            Assert.Equal("share-1", entity.GetAttributeValue<string>(PartnerShareSettingRecordFactory.RequestKeyAttribute));
            Assert.Equal(
                PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "ignored-by-hash",
                }),
                entity.GetAttributeValue<string>(PartnerShareSettingRecordFactory.ContentHashAttribute));
            Assert.DoesNotContain("createdby", entity.Attributes.Keys);
        }

        [Fact]
        public void 直接共有をPL管理対象としてCreate属性へ明示できる()
        {
            var entity = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.Ready,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "managed-share",
                },
                TeamId,
                isManagedProjection: true);

            Assert.True(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ManagedProjectionAttribute));
        }

        [Fact]
        public void 初期設定で承認者Teamが未解決なら保護フラグを推測しない()
        {
            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "unresolved-approver-team",
                },
                Guid.Empty));
        }

        [Fact]
        public void 初期設定の承認者Team共有だけが保護経路になる()
        {
            var entity = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = TeamId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "approver-team",
                },
                TeamId);

            Assert.True(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ProtectedPathAttribute));
        }

        [Fact]
        public void 初期設定の別Team共有は保護経路にしない()
        {
            var otherTeamId = Guid.Parse("55555555-5555-5555-8555-555555555555");
            var entity = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = otherTeamId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "other-team",
                },
                TeamId);

            Assert.False(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ProtectedPathAttribute));
        }

        [Fact]
        public void 設定完了後のTeam共有は通常経路としてCreate属性へ変換する()
        {
            var entity = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.Ready,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = TeamId,
                    AccessLevel = PartnerShareAccessLevel.Write,
                    RequestKey = "team-1",
                },
                TeamId);

            Assert.Equal(PartnerShareSettingRecordFactory.PrincipalKindTeamOption,
                entity.GetAttributeValue<OptionSetValue>(PartnerShareSettingRecordFactory.PrincipalKindAttribute)!.Value);
            Assert.Equal(TeamId, entity.GetAttributeValue<EntityReference>(PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute)!.Id);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute, entity.Attributes.Keys);
            Assert.False(entity.GetAttributeValue<bool>(PartnerShareSettingRecordFactory.ProtectedPathAttribute));
        }

        [Fact]
        public void Updateはアクセス種別と再送情報だけを含みprincipalを固定する()
        {
            var entity = PartnerShareSettingRecordFactory.BuildUpdate(
                SettingId,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Write,
                    RequestKey = "update-1",
                });

            Assert.Equal(SettingId, entity.Id);
            Assert.Contains(PartnerShareSettingRecordFactory.AccessLevelAttribute, entity.Attributes.Keys);
            Assert.Contains(PartnerShareSettingRecordFactory.RequestKeyAttribute, entity.Attributes.Keys);
            Assert.Contains(PartnerShareSettingRecordFactory.ContentHashAttribute, entity.Attributes.Keys);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.PartnerLookupAttribute, entity.Attributes.Keys);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute, entity.Attributes.Keys);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute, entity.Attributes.Keys);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.ProtectedPathAttribute, entity.Attributes.Keys);
            Assert.DoesNotContain(PartnerShareSettingRecordFactory.SettingStateAttribute, entity.Attributes.Keys);
        }

        [Fact]
        public void 取消はDeleteではなく状態Updateだけを作る()
        {
            var entity = PartnerShareSettingRecordFactory.BuildRevoke(SettingId);

            Assert.Equal(SettingId, entity.Id);
            Assert.Single(entity.Attributes);
            Assert.Equal(PartnerShareSettingRecordFactory.SettingStateRevokedOption,
                entity.GetAttributeValue<OptionSetValue>(PartnerShareSettingRecordFactory.SettingStateAttribute)!.Value);
        }

        [Fact]
        public void 標準ReadのUser行を認可契約Recordへ戻せる()
        {
            var entity = ValidUserEntity();
            var record = PartnerShareSettingRecordFactory.Parse(entity);

            Assert.Equal(SettingId, record.Id);
            Assert.Equal(PartnerId, record.PartnerId);
            Assert.Equal(PartnerSharePrincipalKind.User, record.PrincipalKind);
            Assert.Equal(UserId, record.PrincipalId);
            Assert.Equal(PartnerShareAccessLevel.Read, record.AccessLevel);
            Assert.Equal(PartnerShareSettingState.Active, record.State);
            Assert.Equal(UserId, record.CreatedByUserId);
            Assert.False(record.IsProtectedManagementPath);
            Assert.Equal("share-1", record.RequestKey);
            Assert.Equal(
                PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "ignored-by-hash",
                }),
                record.ContentHash);
        }

        [Fact]
        public void principalが排他的でないRead行はfail_closedで拒否する()
        {
            var entity = ValidUserEntity();
            entity[PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute] = new EntityReference("team", TeamId);

            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.Parse(entity));
        }

        [Fact]
        public void 標準Readの不正Choiceは拒否する()
        {
            var entity = ValidUserEntity();
            entity[PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(999);

            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.Parse(entity));
        }

        [Fact]
        public void 保護フラグまたはcreatedbyが欠損したRead行は拒否する()
        {
            var withoutProtectedFlag = ValidUserEntity();
            withoutProtectedFlag.Attributes.Remove(PartnerShareSettingRecordFactory.ProtectedPathAttribute);
            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.Parse(withoutProtectedFlag));

            var withoutCreatedBy = ValidUserEntity();
            withoutCreatedBy.Attributes.Remove("createdby");
            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.Parse(withoutCreatedBy));

            var protectedUser = ValidUserEntity();
            protectedUser[PartnerShareSettingRecordFactory.ProtectedPathAttribute] = true;
            Assert.Throws<InvalidPluginExecutionException>(() => PartnerShareSettingRecordFactory.Parse(protectedUser));
        }

        [Fact]
        public void 後付けのPL管理フラグが欠損した旧行は未照合として読む()
        {
            var legacy = ValidUserEntity();
            legacy.Attributes.Remove(PartnerShareSettingRecordFactory.ManagedProjectionAttribute);

            var record = PartnerShareSettingRecordFactory.Parse(legacy);

            Assert.False(record.IsManagedProjection);
        }

        private static Entity ValidUserEntity()
            => new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateActiveOption),
                [PartnerShareSettingRecordFactory.ProtectedPathAttribute] = false,
                [PartnerShareSettingRecordFactory.ManagedProjectionAttribute] = false,
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "share-1",
                [PartnerShareSettingRecordFactory.ContentHashAttribute] = PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "ignored-by-hash",
                }),
                ["createdby"] = new EntityReference("systemuser", UserId),
            };
    }
}
