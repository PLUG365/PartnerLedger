using System.Reflection;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

public sealed class CreatePreValidationPrivilegeBoundaryTests
{
    private static readonly Guid InitiatingUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PrivilegedPluginUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartnerCreate_rejects_empty_target_for_both_step_users(bool callerContext)
    {
        AssertMalformedTargetRejected(new PartnerCreateGuardPlugin("", ""), "pl_partner", withInitiatingService: false, callerContext);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartnerCreate_does_not_skip_validation_under_the_retired_register_api(bool withMarker)
    {
        // 旧Custom API（pl_RegisterPartner）の親の下でも、取引先の作成は標準の入力検査を通す（2026-09-27）。
        // 以前は親の操作名や目印で検査を飛ばしており、同名のCustom APIを作れば迂回できた。
        var parent = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var parentValues = ((PropertyProxy)parent).Values;
        parentValues[nameof(IPluginExecutionContext.MessageName)] = "pl_RegisterPartner";
        var shared = new ParameterCollection();
        if (withMarker) shared["PartnerLedger.RegisterPartner.Create"] = "v1";
        parentValues[nameof(IPluginExecutionContext.SharedVariables)] = shared;

        AssertMalformedTargetRejected(new PartnerCreateGuardPlugin("", ""), "pl_partner", withInitiatingService: false, callerContext: true, parent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulletinPostCreate_rejects_empty_target_for_both_step_users(bool callerContext)
    {
        AssertMalformedTargetRejected(new BulletinPostStandardCreatePlugin("", ""), "pl_bulletinpost", withInitiatingService: true, callerContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContactCreate_rejects_empty_target_for_both_step_users(bool callerContext)
    {
        AssertMalformedTargetRejected(new ContactStandardCreatePlugin("", ""), "pl_contact", withInitiatingService: true, callerContext);
    }

    private static void AssertMalformedTargetRejected(PluginBase plugin, string entityName, bool withInitiatingService, bool callerContext, IPluginExecutionContext? parent = null)
    {
        var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var contextValues = ((PropertyProxy)context).Values;
        contextValues[nameof(IPluginExecutionContext.MessageName)] = "Create";
        contextValues[nameof(IPluginExecutionContext.PrimaryEntityName)] = entityName;
        contextValues[nameof(IPluginExecutionContext.Stage)] = 10;
        contextValues[nameof(IPluginExecutionContext.Depth)] = 1;
        contextValues[nameof(IPluginExecutionContext.UserId)] = callerContext ? InitiatingUserId : PrivilegedPluginUserId;
        contextValues[nameof(IPluginExecutionContext.InitiatingUserId)] = InitiatingUserId;
        contextValues[nameof(IPluginExecutionContext.InputParameters)] = new ParameterCollection
        {
            ["Target"] = new Entity(entityName),
        };
        contextValues[nameof(IPluginExecutionContext.SharedVariables)] = new ParameterCollection();
        contextValues[nameof(IPluginExecutionContext.ParentContext)] = parent;

        var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
        var localValues = ((PropertyProxy)local).Values;
        localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = context;
        if (withInitiatingService)
        {
            localValues[nameof(ILocalPluginContext.InitiatingUserService)] = new FakeOrganizationService();
        }

        var execute = plugin.GetType().GetMethod("ExecuteDataversePlugin", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(execute);
        var thrown = Assert.Throws<TargetInvocationException>(() => execute!.Invoke(plugin, new object[] { local }));
        Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
    }

    public class PropertyProxy : DispatchProxy
    {
        public Dictionary<string, object?> Values { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || !targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }
            Values.TryGetValue(targetMethod.Name[4..], out var value);
            return value;
        }
    }
}
