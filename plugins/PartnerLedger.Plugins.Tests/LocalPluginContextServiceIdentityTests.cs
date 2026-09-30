using System.Reflection;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

/// <summary>
/// サーバー処理はSYSTEMの権限で行い、StepのRun-as（実行ユーザー）に依存しない（2026-09-27、実装計画の設計表）。
/// Run-asは環境ごとの設定でSolutionに載らず、無効化されると処理が止まるため、その経路をコードに残さない。
/// </summary>
public sealed class LocalPluginContextServiceIdentityTests
{
    private static readonly Guid InitiatingUserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid StepRunAsUserId = Guid.Parse("99999999-9999-4999-8999-999999999999");

    [Fact]
    public void Plugin_context_has_no_service_bound_to_the_step_run_as_user()
    {
        Assert.Null(typeof(ILocalPluginContext).GetProperty("PluginUserService"));
        Assert.Null(typeof(LocalPluginContext).GetProperty("PluginUserService"));
        Assert.NotNull(typeof(ILocalPluginContext).GetProperty(nameof(ILocalPluginContext.SystemService)));
    }

    [Fact]
    public void System_service_is_created_as_SYSTEM_and_caller_service_as_the_initiating_user()
    {
        var factory = new RecordingServiceFactory();
        var provider = new StubServiceProvider(CreateExecutionContext(), factory);

        var local = new LocalPluginContext(provider);

        Assert.NotNull(local.SystemService);
        Assert.NotNull(local.InitiatingUserService);
        // SYSTEM（null）と操作者だけを要求し、Stepの実行ユーザー（UserId）ではサービスを作らない。
        Assert.Contains(null, factory.RequestedUserIds);
        Assert.Contains(InitiatingUserId, factory.RequestedUserIds.Where(id => id.HasValue).Select(id => id!.Value));
        Assert.DoesNotContain(StepRunAsUserId, factory.RequestedUserIds.Where(id => id.HasValue).Select(id => id!.Value));
        Assert.Same(factory.ServiceFor(null), local.SystemService);
        Assert.Same(factory.ServiceFor(InitiatingUserId), local.InitiatingUserService);
    }

    [Fact]
    public void Plugin_sources_do_not_create_services_for_the_step_run_as_user()
    {
        // PluginBase以外でUserId（Stepの実行ユーザー）からサービスを作る経路を残さない。
        var sources = Directory.EnumerateFiles(PluginSourceDirectory(), "*.cs")
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(sources);
        Assert.DoesNotContain(sources, source => source.Text.Contains("PluginUserService"));
        // 監査指摘：書き方の違いで漏れないよう、実行文脈のUserId（Stepの実行ユーザー）への参照そのものを禁止する。
        var userIdReference = new System.Text.RegularExpressions.Regex(@"\b(context|PluginExecutionContext|executionContext)\.UserId\b");
        Assert.DoesNotContain(sources, source => userIdReference.IsMatch(source.Text));
    }

    private static IPluginExecutionContext CreateExecutionContext()
    {
        var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var values = ((PropertyProxy)context).Values;
        values[nameof(IPluginExecutionContext.InitiatingUserId)] = InitiatingUserId;
        values[nameof(IPluginExecutionContext.UserId)] = StepRunAsUserId;
        values[nameof(IExecutionContext.OperationCreatedOn)] = DateTime.UtcNow;
        return context;
    }

    private static string PluginSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "PartnerLedger.Plugins")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "PartnerLedger.Plugins");
    }

    private sealed class RecordingServiceFactory : IOrganizationServiceFactory
    {
        private readonly Dictionary<string, IOrganizationService> services = new();

        public List<Guid?> RequestedUserIds { get; } = new();

        public IOrganizationService CreateOrganizationService(Guid? userId)
        {
            RequestedUserIds.Add(userId);
            var key = userId?.ToString("D") ?? "SYSTEM";
            if (!services.TryGetValue(key, out var service))
            {
                service = new FakeOrganizationService();
                services[key] = service;
            }

            return service;
        }

        public IOrganizationService ServiceFor(Guid? userId) => services[userId?.ToString("D") ?? "SYSTEM"];
    }

    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly IPluginExecutionContext context;
        private readonly IOrganizationServiceFactory factory;

        public StubServiceProvider(IPluginExecutionContext context, IOrganizationServiceFactory factory)
        {
            this.context = context;
            this.factory = factory;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext)) return context;
            if (serviceType == typeof(IOrganizationServiceFactory)) return factory;
            if (serviceType == typeof(ITracingService)) return new NullTracingService();
            return null;
        }
    }

    private sealed class NullTracingService : ITracingService
    {
        public void Trace(string format, params object[] args)
        {
        }
    }

    public class PropertyProxy : DispatchProxy
    {
        public Dictionary<string, object?> Values { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || !targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
                throw new NotSupportedException(targetMethod?.Name);
            Values.TryGetValue(targetMethod.Name[4..], out var value);
            return value;
        }
    }
}
