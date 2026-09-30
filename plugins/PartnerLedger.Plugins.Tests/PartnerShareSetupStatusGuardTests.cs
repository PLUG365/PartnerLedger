using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerShareSetupStatusGuardTests
    {
        private static readonly Guid PartnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid TeamId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void Readyだけの内部Updateを許可する()
        {
            PartnerShareSetupStatusGuardPlugin.ValidateReadyTransition(
                Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
                PartnerStandardCreateContract.EntityName);
        }

        [Fact]
        public void 初期設定中へ戻す内部Updateを拒否する()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupStatusGuardPlugin.ValidateReadyTransition(
                    Target(PartnerStandardCreateContract.InitialShareSetupStatusCode),
                    PartnerStandardCreateContract.EntityName));
        }

        [Fact]
        public void 状態以外の列を含む内部Updateを拒否する()
        {
            var target = Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode);
            target[PartnerStandardCreateContract.NameAttribute] = "unexpected";

            Assert.Throws<InvalidPluginExecutionException>(() =>
                PartnerShareSetupStatusGuardPlugin.ValidateReadyTransition(
                    target,
                    PartnerStandardCreateContract.EntityName));
        }

        [Fact]
        public void Dataverseが補う監査列は許可する()
        {
            var target = Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode);
            target["modifiedon"] = DateTime.UtcNow;
            target["modifiedby"] = new EntityReference("systemuser", Guid.NewGuid());

            PartnerShareSetupStatusGuardPlugin.ValidateReadyTransition(
                target,
                PartnerStandardCreateContract.EntityName);
        }

        [Fact]
        public void 初期共有の承認者に親行Readがなくても保護行とACLのある内部遷移を許可する()
        {
            var initiatingService = new FakeOrganizationService
            {
                CanRetrieve = (_, _) => false,
            };
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedReadyEvidence(pluginService);

            ExecuteTrustedTransition(initiatingService, pluginService, trusted: false);
        }

        [Fact]
        public void 直接Ready更新も保存済み証拠があれば両ガードを通る()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedReadyEvidence(pluginService);
            var target = Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode);

            ExecutePartnerStandardUpdate(null, pluginService, target, depth: 1);
            ExecuteTrustedTransition(
                new FakeOrganizationService { CanRetrieve = (_, _) => false },
                pluginService,
                trusted: false,
                target: target,
                depth: 1);
        }

        [Fact]
        public void 直接Ready更新は保護行がなければ拒否する()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedTeamSetting(pluginService);

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecuteTrustedTransition(
                    new FakeOrganizationService(), pluginService,
                    trusted: false, depth: 1));
            Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
        }

        [Fact]
        public void 標準Update側も保護行のない直接Readyを拒否する()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedTeamSetting(pluginService);

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecutePartnerStandardUpdate(null, pluginService,
                    Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode), depth: 1));
            Assert.Contains("保護共有設定", Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException).Message);
        }

        [Fact]
        public void 標準Update側は承認マーカーがあってもReadyと業務列の同時変更を拒否する()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedReadyEvidence(pluginService);
            var parent = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
            ((PropertyProxy)parent).Values[nameof(IPluginExecutionContext.SharedVariables)] =
                new ParameterCollection
                {
                    [PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable] = true,
                };
            var target = Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode);
            target[PartnerStandardCreateContract.NameAttribute] = "forged";

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecutePartnerStandardUpdate(parent, pluginService, target));
            Assert.Contains("許可されていない列", Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException).Message);
        }

        [Fact]
        public void 実行主体が現状態を読めない内部遷移は失敗する()
        {
            var initiatingService = new FakeOrganizationService();
            initiatingService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            var pluginService = new FakeOrganizationService
            {
                CanRetrieve = (_, _) => false,
            };

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecuteTrustedTransition(initiatingService, pluginService));
            Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
        }

        [Fact]
        public void 保護行のないネストUpdateはマーカーがあっても拒否する()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedTeamSetting(pluginService);

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecuteTrustedTransition(new FakeOrganizationService(), pluginService, trusted: true));
            Assert.Contains("保護共有設定", Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException).Message);
        }

        [Fact]
        public void 設定完了後の再遷移は拒否する()
        {
            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode));
            SeedReadyEvidence(pluginService);

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                ExecuteTrustedTransition(new FakeOrganizationService(), pluginService));
            Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
        }

        [Fact]
        public void 投影Pluginからの子Updateはマーカー有無に依存せず保護行とACLで両ガードを通る()
        {
            var parent = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
            ((PropertyProxy)parent).Values[nameof(IPluginExecutionContext.SharedVariables)] =
                new ParameterCollection();

            var pluginService = new FakeOrganizationService();
            pluginService.Seed(Target(PartnerStandardCreateContract.InitialShareSetupStatusCode));
            SeedReadyEvidence(pluginService);
            pluginService.BeforeUpdate = update =>
            {
                ExecutePartnerStandardUpdate(parent, pluginService, update);
                ExecuteTrustedTransition(
                    new FakeOrganizationService { CanRetrieve = (_, _) => false },
                    pluginService,
                    trusted: false,
                    parentContext: parent,
                    target: update);
            };

            var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
            var localValues = ((PropertyProxy)local).Values;
            localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = parent;
            localValues[nameof(ILocalPluginContext.SystemService)] = pluginService;

            var method = typeof(PartnerShareSettingProjectionPlugin).GetMethod(
                "CompleteInitialShareSetup",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(null, new object[] { local, PartnerId });

            Assert.Equal(
                PartnerStandardCreateContract.ReadyShareSetupStatusCode,
                pluginService.Retrieve(
                    PartnerStandardCreateContract.EntityName,
                    PartnerId,
                    new Microsoft.Xrm.Sdk.Query.ColumnSet(
                        PartnerStandardCreateContract.ShareSetupStatusAttribute))
                    .GetAttributeValue<OptionSetValue>(PartnerStandardCreateContract.ShareSetupStatusAttribute)!.Value);
        }

        private static void ExecutePartnerStandardUpdate(
            IPluginExecutionContext? parent,
            FakeOrganizationService pluginService,
            Entity target,
            int depth = 2)
        {
            var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
            var contextValues = ((PropertyProxy)context).Values;
            contextValues[nameof(IPluginExecutionContext.MessageName)] = "Update";
            contextValues[nameof(IPluginExecutionContext.PrimaryEntityName)] = PartnerStandardCreateContract.EntityName;
            contextValues[nameof(IPluginExecutionContext.Stage)] = 20;
            contextValues[nameof(IPluginExecutionContext.Depth)] = depth;
            contextValues[nameof(IPluginExecutionContext.IsInTransaction)] = true;
            if (parent != null)
            {
                contextValues[nameof(IPluginExecutionContext.ParentContext)] = parent;
            }
            contextValues[nameof(IPluginExecutionContext.SharedVariables)] = new ParameterCollection();
            contextValues[nameof(IPluginExecutionContext.InputParameters)] = new ParameterCollection
            {
                { "Target", target },
            };

            var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
            var localValues = ((PropertyProxy)local).Values;
            localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = context;
            localValues[nameof(ILocalPluginContext.SystemService)] = pluginService;

            var method = typeof(PartnerStandardUpdatePlugin).GetMethod(
                "ExecuteDataversePlugin",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(new PartnerStandardUpdatePlugin("", ""), new object[] { local });
        }

        private static void ExecuteTrustedTransition(
            FakeOrganizationService initiatingService,
            FakeOrganizationService pluginService,
            bool trusted = true,
            IPluginExecutionContext? parentContext = null,
            Entity? target = null,
            int depth = 2)
        {
            var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
            var contextValues = ((PropertyProxy)context).Values;
            contextValues[nameof(IPluginExecutionContext.MessageName)] = "Update";
            contextValues[nameof(IPluginExecutionContext.PrimaryEntityName)] = PartnerStandardCreateContract.EntityName;
            contextValues[nameof(IPluginExecutionContext.Stage)] = 20;
            contextValues[nameof(IPluginExecutionContext.Depth)] = depth;
            contextValues[nameof(IPluginExecutionContext.IsInTransaction)] = true;
            contextValues[nameof(IPluginExecutionContext.InputParameters)] = new ParameterCollection
            {
                { "Target", target ?? Target(PartnerStandardCreateContract.ReadyShareSetupStatusCode) },
            };
            if (parentContext != null)
            {
                contextValues[nameof(IPluginExecutionContext.ParentContext)] = parentContext;
            }
            var sharedVariables = new ParameterCollection();
            if (trusted)
            {
                sharedVariables[PartnerShareSetupStatusGuardPlugin.TrustedInternalWriteSharedVariable] = true;
            }
            contextValues[nameof(IPluginExecutionContext.SharedVariables)] = sharedVariables;

            var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
            var localValues = ((PropertyProxy)local).Values;
            localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = context;
            localValues[nameof(ILocalPluginContext.InitiatingUserService)] = initiatingService;
            localValues[nameof(ILocalPluginContext.SystemService)] = pluginService;

            var method = typeof(PartnerShareSetupStatusGuardPlugin).GetMethod(
                "ExecuteDataversePlugin",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(new PartnerShareSetupStatusGuardPlugin("", ""), new object[] { local });
        }

        public class PropertyProxy : DispatchProxy
        {
            public Dictionary<string, object> Values { get; } = new Dictionary<string, object>();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod == null || !targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(targetMethod?.Name);
                }
                Values.TryGetValue(targetMethod.Name.Substring(4), out var value);
                return value;
            }
        }

        private static Entity Target(int status)
            => new Entity(PartnerStandardCreateContract.EntityName, PartnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] = new OptionSetValue(status),
            };

        private static void SeedReadyEvidence(FakeOrganizationService service)
        {
            SeedTeamSetting(service);
            var row = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                new PartnerShareSettingInput
                {
                    PartnerId = PartnerId,
                    PrincipalKind = PartnerSharePrincipalKind.Team,
                    TeamId = TeamId,
                    AccessLevel = PartnerShareAccessLevel.Read,
                    RequestKey = "ready-evidence",
                },
                TeamId,
                isManagedProjection: true);
            row["statecode"] = new OptionSetValue(0);
            row["createdby"] = new EntityReference("systemuser", Guid.NewGuid());
            row["createdon"] = DateTime.UtcNow;
            service.Seed(row);
            service.RetrievedSharedPrincipalAccesses = new[]
            {
                new PrincipalAccess
                {
                    Principal = new EntityReference("team", TeamId),
                    AccessMask = AccessRights.ReadAccess,
                },
            };
        }

        private static void SeedTeamSetting(FakeOrganizationService service)
        {
            var definitionId = Guid.NewGuid();
            service.Seed(new Entity("environmentvariabledefinition", definitionId)
            {
                ["schemaname"] = PartnerLedgerEnvironmentVariableNames.ApproverTeamId,
            });
            service.Seed(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] =
                    new EntityReference("environmentvariabledefinition", definitionId),
                ["value"] = TeamId.ToString("D"),
            });
            service.Seed(new Entity("team", TeamId) { ["teamtype"] = new OptionSetValue(3) });
        }
    }
}
