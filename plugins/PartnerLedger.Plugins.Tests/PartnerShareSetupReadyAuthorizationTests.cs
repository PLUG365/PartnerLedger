using System;
using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSetupReadyAuthorizationTests
    {
        private static readonly Guid PartnerId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        private static readonly Guid TeamId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        private static readonly Guid OtherTeamId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

        [Fact]
        public void 同期内部更新は保護行と直接Readが一致すれば許可する()
        {
            var service = ReadyService();
            PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId);
        }

        [Fact]
        public void 直接Updateも保護行と直接Readが一致すれば許可する()
        {
            PartnerShareSetupReadyAuthorization.Validate(
                Context(depth: 1), ReadyService(), Target(), TeamId);
        }

        [Fact]
        public void 直接Updateでも保護行欠落ならBootstrapのACLだけでは拒否する()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(
                    Context(depth: 1), ReadyService(includeRow: false), Target(), TeamId));
        }

        [Fact]
        public void 直接UpdateでもACL欠落なら拒否する()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(
                    Context(depth: 1), ReadyService(includeAcl: false), Target(), TeamId));
        }

        [Fact]
        public void トランザクション外は拒否する()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(inTransaction: false), ReadyService(), Target(), TeamId));
        }

        [Fact]
        public void 保護行がない場合はBootstrapのACLだけでは許可しない()
        {
            var service = ReadyService(includeRow: false);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 管理投影でない保護行は拒否する()
        {
            var service = ReadyService(managed: false);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 別Teamの保護行は拒否する()
        {
            var service = ReadyService(rowTeamId: OtherTeamId);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 失効した保護行は拒否する()
        {
            var service = ReadyService(revoked: true);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 直接ACLがなければ保護行があっても拒否する()
        {
            var service = ReadyService(includeAcl: false);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 直接ACLがRead以外なら拒否する()
        {
            var service = ReadyService(aclRights: AccessRights.ReadAccess | AccessRights.WriteAccess);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 保護行が複数なら曖昧として拒否する()
        {
            var service = ReadyService();
            AddRow(service, TeamId, managed: true, revoked: false);
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void Ready後の再送は拒否する()
        {
            var service = ReadyService();
            service.Update(new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
            });
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), service, Target(), TeamId));
        }

        [Fact]
        public void 状態と業務列の同時変更は拒否する()
        {
            var target = Target();
            target[PartnerStandardCreateContract.NameAttribute] = "forged";
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupReadyAuthorization.Validate(Context(), ReadyService(), target, TeamId));
        }

        private static FakeOrganizationService ReadyService(
            bool includeRow = true,
            bool includeAcl = true,
            bool managed = true,
            bool revoked = false,
            Guid? rowTeamId = null,
            AccessRights aclRights = AccessRights.ReadAccess)
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_partner", PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.InitialShareSetupStatusCode),
            });
            if (includeRow) AddRow(service, rowTeamId ?? TeamId, managed, revoked);
            if (includeAcl)
            {
                service.RetrievedSharedPrincipalAccesses = new[]
                {
                    new PrincipalAccess
                    {
                        Principal = new EntityReference("team", TeamId),
                        AccessMask = aclRights,
                    },
                };
            }
            return service;
        }

        private static void AddRow(FakeOrganizationService service, Guid teamId, bool managed, bool revoked)
        {
            var row = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = teamId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = Guid.NewGuid().ToString("N"),
                },
                teamId,
                managed);
            row["statecode"] = new OptionSetValue(0);
            row["createdby"] = new EntityReference("systemuser", Guid.NewGuid());
            row["createdon"] = DateTime.UtcNow;
            if (revoked)
            {
                row[PartnerShareSettingRecordFactory.SettingStateAttribute] =
                    new OptionSetValue(PartnerShareSettingRecordFactory.SettingStateRevokedOption);
            }
            service.Seed(row);
        }

        private static Entity Target() => new Entity("pl_partner", PartnerId)
        {
            [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
        };

        private static IPluginExecutionContext Context(int depth = 2, bool inTransaction = true)
        {
            var context = DispatchProxy.Create<IPluginExecutionContext,
                PartnerShareSetupStatusGuardTests.PropertyProxy>();
            var values = ((PartnerShareSetupStatusGuardTests.PropertyProxy)context).Values;
            values[nameof(IPluginExecutionContext.PrimaryEntityName)] = "pl_partner";
            values[nameof(IPluginExecutionContext.Depth)] = depth;
            values[nameof(IPluginExecutionContext.IsInTransaction)] = inTransaction;
            return context;
        }
    }
}
