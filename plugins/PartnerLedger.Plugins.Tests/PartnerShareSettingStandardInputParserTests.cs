using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSettingStandardInputParserTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TeamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid SettingId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        [Fact]
        public void Createは業務入力だけを読み取り要求キーをTrimする()
        {
            var input = PartnerShareSettingStandardInputParser.ParseCreate(new Entity(
                PartnerShareSettingRecordFactory.EntityName)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = " create-1 ",
            });

            Assert.Equal(PartnerId, input.PartnerId);
            Assert.Equal(PartnerSharePrincipalKind.User, input.PrincipalKind);
            Assert.Equal(UserId, input.UserId);
            Assert.Equal("create-1", input.RequestKey);
        }

        [Fact]
        public void Createはサーバー導出列をクライアントから受け付けない()
        {
            var target = CreateTarget();
            target[PartnerShareSettingRecordFactory.ContentHashAttribute] = "client-value";

            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseCreate(target));
        }

        [Fact]
        public void CreateのTargetにDataverseが付与する主キーがあっても業務入力としては採用しない()
        {
            var target = CreateTarget();
            target["pl_partnersharesettingid"] = SettingId;
            target["owningbusinessunit"] = new EntityReference("businessunit", Guid.NewGuid());
            target[PartnerShareSettingRecordFactory.SettingStateAttribute] =
                new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateActiveOption);
            target[PartnerShareSettingRecordFactory.ProtectedPathAttribute] = false;
            target[PartnerShareSettingRecordFactory.ManagedProjectionAttribute] = false;
            target[PartnerShareSettingRecordFactory.ContentHashAttribute] = null;
            target["modifiedonbehalfby"] = null;
            target["createdonbehalfby"] = null;
            target["statecode"] = new OptionSetValue(0);
            target["statuscode"] = new OptionSetValue(1);

            var input = PartnerShareSettingStandardInputParser.ParseCreate(target);

            Assert.Equal(PartnerId, input.PartnerId);
            Assert.Equal(UserId, input.UserId);
        }

        [Fact]
        public void Createの保護既定値をtrueへ差し替える入力は拒否する()
        {
            var target = CreateTarget();
            target[PartnerShareSettingRecordFactory.ProtectedPathAttribute] = true;

            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseCreate(target));
        }

        [Fact]
        public void CreateのPL管理フラグをtrueへ差し替える入力は拒否する()
        {
            var target = CreateTarget();
            target[PartnerShareSettingRecordFactory.ManagedProjectionAttribute] = true;

            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseCreate(target));
        }

        [Fact]
        public void Updateは現在行からprincipalを再構成し変更可能列だけを受け付ける()
        {
            var current = Record();
            var target = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelWriteOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = " update-1 ",
            };

            var command = PartnerShareSettingStandardInputParser.ParseUpdate(target, current);

            Assert.False(command.IsRevoke);
            Assert.NotNull(command.Desired);
            Assert.Equal(PartnerId, command.Desired!.PartnerId);
            Assert.Equal(PartnerSharePrincipalKind.User, command.Desired.PrincipalKind);
            Assert.Equal(UserId, command.Desired.UserId);
            Assert.Equal(PartnerShareAccessLevel.Write, command.Desired.AccessLevel);
            Assert.Equal("update-1", command.Desired.RequestKey);
        }

        [Fact]
        public void Updateでprincipalや保護列を変更できない()
        {
            var current = Record();
            var target = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "update-2",
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", TeamId),
            };

            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseUpdate(target, current));
        }

        [Fact]
        public void Updateの取消は状態列だけの一方向遷移として扱う()
        {
            var command = PartnerShareSettingStandardInputParser.ParseUpdate(
                new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
                {
                    [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateRevokedOption),
                },
                Record());

            Assert.True(command.IsRevoke);
            Assert.Null(command.Desired);
        }

        [Fact]
        public void Updateで再有効化や取消と他列の同時変更を受け付けない()
        {
            var reopen = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateActiveOption),
            };
            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseUpdate(reopen, Record()));

            var mixed = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateRevokedOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "mixed",
            };
            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseUpdate(mixed, Record()));
        }

        [Fact]
        public void UpdateはDataverse自動付与列を無視し主キー一致だけを許可する()
        {
            var target = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                ["pl_partnersharesettingid"] = SettingId,
                ["modifiedon"] = DateTime.UtcNow,
                ["modifiedby"] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelWriteOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "update-platform-columns",
            };

            var command = PartnerShareSettingStandardInputParser.ParseUpdate(target, Record());

            Assert.False(command.IsRevoke);
            Assert.Equal(PartnerShareAccessLevel.Write, command.Desired!.AccessLevel);

            target["pl_partnersharesettingid"] = Guid.NewGuid();
            Assert.Throws<InvalidPluginExecutionException>(
                () => PartnerShareSettingStandardInputParser.ParseUpdate(target, Record()));
        }

        [Fact]
        public void 取消はDataverse自動付与列があっても状態だけを受け付ける()
        {
            var target = new Entity(PartnerShareSettingRecordFactory.EntityName, SettingId)
            {
                ["pl_partnersharesettingid"] = SettingId,
                ["modifiedon"] = DateTime.UtcNow,
                [PartnerShareSettingRecordFactory.SettingStateAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateRevokedOption),
            };

            var command = PartnerShareSettingStandardInputParser.ParseUpdate(target, Record());

            Assert.True(command.IsRevoke);
        }

        private static Entity CreateTarget()
            => new Entity(PartnerShareSettingRecordFactory.EntityName)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] = new EntityReference("pl_partner", PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.PrincipalKindUserOption),
                [PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] = new EntityReference("systemuser", UserId),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] = new OptionSetValue(PartnerShareSettingRecordFactory.AccessLevelReadOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = "create-2",
            };

        private static PartnerShareSettingRecord Record()
            => new PartnerShareSettingRecord
            {
                Id = SettingId,
                PartnerId = PartnerId,
                PrincipalKind = PartnerSharePrincipalKind.User,
                PrincipalId = UserId,
                AccessLevel = PartnerShareAccessLevel.Read,
                State = PartnerShareSettingState.Active,
                CreatedByUserId = UserId,
                IsManagedProjection = false,
                RequestKey = "current",
                ContentHash = PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = UserId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "current",
                }),
            };
    }
}
