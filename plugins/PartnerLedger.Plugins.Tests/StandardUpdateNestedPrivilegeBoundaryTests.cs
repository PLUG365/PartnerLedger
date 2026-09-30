using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

public sealed class StandardUpdateNestedPrivilegeBoundaryTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PrivilegedStepUserId = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid ContractId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SettingsId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Nested_partner_update_without_marker_does_not_bypass_approval_policy()
    {
        var target = new Entity("pl_partner", Guid.NewGuid()) { [PartnerStandardUpdateContract.NameAttribute] = "Changed" };
        var service = ServiceWithPolicy("\"companyName\":true,\"tradingStatus\":false,\"address\":false,\"phone\":false,\"contractUpdate\":false");
        var context = CreateNestedContext("pl_partner", target, PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable, withMarker: false);
        Set(context.LocalValues, nameof(ILocalPluginContext.SystemService), service);
        Set(context.Values, nameof(IPluginExecutionContext.PreEntityImages), new EntityImageCollection
        {
            [PartnerStandardUpdatePlugin.PreImageAlias] = new Entity("pl_partner", target.Id) { ["statecode"] = new OptionSetValue(0) },
        });

        var exception = ExecuteAndCapture(new PartnerStandardUpdatePlugin("", ""), context.Local);

        Assert.Contains(nameof(PartnerStandardUpdateValidationError.ApprovalRequired), exception.Message);
        Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
    }

    [Fact]
    public void Share_setup_marker_cannot_update_partner_business_fields()
    {
        var target = new Entity("pl_partner", Guid.NewGuid())
        {
            [PartnerStandardUpdateContract.NameAttribute] = "Changed",
        };
        var context = CreateNestedContext(
            "pl_partner", target,
            PartnerShareSetupStatusGuardPlugin.TrustedInternalWriteSharedVariable,
            withMarker: true);
        var service = ServiceWithPolicy("\"companyName\":true,\"tradingStatus\":false,\"address\":false,\"phone\":false,\"contractUpdate\":false");
        Set(context.LocalValues, nameof(ILocalPluginContext.SystemService), service);
        Set(context.Values, nameof(IPluginExecutionContext.PreEntityImages), new EntityImageCollection
        {
            [PartnerStandardUpdatePlugin.PreImageAlias] = new Entity("pl_partner", target.Id) { ["statecode"] = new OptionSetValue(0) },
        });

        var exception = ExecuteAndCapture(new PartnerStandardUpdatePlugin("", ""), context.Local);

        Assert.Contains(nameof(PartnerStandardUpdateValidationError.ApprovalRequired), exception.Message);
    }

    [Fact]
    public void Approval_reflection_marker_cannot_change_share_setup_state()
    {
        var target = new Entity("pl_partner", Guid.NewGuid())
        {
            [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
        };
        var context = CreateNestedContext(
            "pl_partner", target,
            PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable,
            withMarker: true);
        var teamId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var service = new FakeOrganizationService();
        service.Seed(new Entity("environmentvariabledefinition", definitionId)
        {
            ["schemaname"] = PartnerLedgerEnvironmentVariableNames.ApproverTeamId,
        });
        service.Seed(new Entity("environmentvariablevalue")
        {
            ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", definitionId),
            ["value"] = teamId.ToString("D"),
        });
        service.Seed(new Entity("team", teamId) { ["teamtype"] = new OptionSetValue(3) });
        service.Seed(new Entity("pl_partner", target.Id)
        {
            [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                new OptionSetValue(PartnerStandardCreateContract.InitialShareSetupStatusCode),
        });
        Set(context.LocalValues, nameof(ILocalPluginContext.SystemService), service);
        Set(context.Values, nameof(IPluginExecutionContext.IsInTransaction), true);

        var exception = ExecuteAndCapture(new PartnerStandardUpdatePlugin("", ""), context.Local);

        Assert.Contains("保護共有設定", exception.Message);
    }

    [Fact]
    public void Nested_contract_update_without_marker_does_not_bypass_approval_policy()
    {
        var target = new Entity("pl_contract", ContractId) { [ContractStandardUpdateContract.NameAttribute] = "Changed" };
        var service = ServiceWithPolicy("\"companyName\":false,\"tradingStatus\":false,\"address\":false,\"phone\":false,\"contractUpdate\":true");
        var context = CreateNestedContext("pl_contract", target, ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable, withMarker: false);
        Set(context.LocalValues, nameof(ILocalPluginContext.SystemService), service);
        Set(context.Values, nameof(IPluginExecutionContext.PreEntityImages), new EntityImageCollection
        {
            [ContractStandardUpdatePlugin.PreImageAlias] = ActiveContract(),
        });

        var exception = ExecuteAndCapture(new ContractStandardUpdatePlugin("", ""), context.Local);

        Assert.Contains(nameof(ContractStandardUpdateValidationError.ApprovalRequired), exception.Message);
        Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
    }

    [Fact]
    public void Nested_contract_update_with_parent_marker_allows_validated_target_even_when_policy_is_on()
    {
        var target = new Entity("pl_contract", ContractId) { [ContractStandardUpdateContract.LinkAttribute] = "  https://example.test/path  " };
        var service = ServiceWithPolicy("\"companyName\":false,\"tradingStatus\":false,\"address\":false,\"phone\":false,\"contractUpdate\":true");
        var context = CreateNestedContext("pl_contract", target, ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable, withMarker: true);
        Set(context.LocalValues, nameof(ILocalPluginContext.SystemService), service);
        Set(context.Values, nameof(IPluginExecutionContext.PreEntityImages), new EntityImageCollection());

        ExecuteSuccessfully(new ContractStandardUpdatePlugin("", ""), context.Local);

        Assert.Equal("https://example.test/path", target.GetAttributeValue<string>(ContractStandardUpdateContract.LinkAttribute));
        Assert.True((bool)context.Context.SharedVariables[ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable]!);
        Assert.Empty(service.RetrieveMultiple(new QueryExpression("pl_operationlog")).Entities);
    }

    [Fact]
    public void Nested_contract_update_with_marker_still_rejects_authority_columns()
    {
        var target = new Entity("pl_contract", ContractId) { ["ownerid"] = new EntityReference("systemuser", CallerId) };
        var context = CreateNestedContext("pl_contract", target, ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable, withMarker: true);

        var exception = ExecuteAndCapture(new ContractStandardUpdatePlugin("", ""), context.Local);

        Assert.Contains(nameof(ContractStandardUpdateValidationError.ForbiddenAuthorityInput), exception.Message);
    }

    private static (IPluginExecutionContext Context, ILocalPluginContext Local, PropertyProxy Values, PropertyProxy LocalValues) CreateNestedContext(
        string entityName,
        Entity target,
        string markerName,
        bool withMarker)
    {
        var parent = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var parentValues = (PropertyProxy)parent;
        Set(parentValues, nameof(IPluginExecutionContext.SharedVariables), withMarker
            ? new ParameterCollection { [markerName] = true }
            : new ParameterCollection());

        var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var values = (PropertyProxy)context;
        var sharedVariables = new ParameterCollection();
        Set(values, nameof(IPluginExecutionContext.MessageName), "Update");
        Set(values, nameof(IPluginExecutionContext.PrimaryEntityName), entityName);
        Set(values, nameof(IPluginExecutionContext.Stage), 20);
        Set(values, nameof(IPluginExecutionContext.Depth), 2);
        Set(values, nameof(IPluginExecutionContext.UserId), PrivilegedStepUserId);
        Set(values, nameof(IPluginExecutionContext.InitiatingUserId), CallerId);
        Set(values, nameof(IPluginExecutionContext.ParentContext), parent);
        Set(values, nameof(IPluginExecutionContext.InputParameters), new ParameterCollection { ["Target"] = target });
        Set(values, nameof(IPluginExecutionContext.SharedVariables), sharedVariables);

        var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
        var localValues = (PropertyProxy)local;
        Set(localValues, nameof(ILocalPluginContext.PluginExecutionContext), context);
        // 内部の読取・書込みはSystemServiceだけを使い、操作者のサービスを通らないことを明示的に守る（2026-09-27、監査指摘）。
        // ここでの操作者（InitiatingUserId）は、利用者が起こしたネストUpdate（例：利用者の同期ワークフロー）を想定する。
        // サーバーのSYSTEM書込みは飛ばす指定（BypassBusinessLogicExecutionStepIds）付きで、これらのStepを通らない。
        Set(localValues, nameof(ILocalPluginContext.InitiatingUserService), new ForbiddenService());
        return (context, local, values, localValues);
    }

    private static FakeOrganizationService ServiceWithPolicy(string policyProperties)
    {
        var service = new FakeOrganizationService();
        service.Seed(new Entity("pl_settings", SettingsId)
        {
            ["statecode"] = new OptionSetValue(0),
            ["pl_settingsversion"] = 1,
            ["pl_policyjson"] = "{\"schemaVersion\":1," + policyProperties + "}",
        });
        return service;
    }

    private static Entity ActiveContract() => new("pl_contract", ContractId)
    {
        ["statecode"] = new OptionSetValue(0),
        ["pl_contractstatuscode"] = new OptionSetValue(ContractStandardUpdateContract.ExecutedContractStatusCode),
    };

    private static InvalidPluginExecutionException ExecuteAndCapture(PluginBase plugin, ILocalPluginContext local)
    {
        var execute = plugin.GetType().GetMethod("ExecuteDataversePlugin", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(execute);
        var thrown = Assert.Throws<TargetInvocationException>(() => execute!.Invoke(plugin, new object[] { local }));
        return Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
    }

    private static void ExecuteSuccessfully(PluginBase plugin, ILocalPluginContext local)
    {
        var execute = plugin.GetType().GetMethod("ExecuteDataversePlugin", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(execute);
        execute!.Invoke(plugin, new object[] { local });
    }

    private static void Set(PropertyProxy proxy, string propertyName, object? value) => proxy.Values[propertyName] = value;

    private sealed class ForbiddenService : IOrganizationService
    {
        private static Exception Forbidden() => new InvalidOperationException("操作者のサービスを内部処理に使ってはいけません。");
        public Guid Create(Entity entity) => throw Forbidden();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw Forbidden();
        public void Update(Entity entity) => throw Forbidden();
        public void Delete(string entityName, Guid id) => throw Forbidden();
        public OrganizationResponse Execute(OrganizationRequest request) => throw Forbidden();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Forbidden();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Forbidden();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw Forbidden();
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
