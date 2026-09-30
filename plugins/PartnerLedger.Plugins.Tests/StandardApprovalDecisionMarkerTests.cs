using System.Reflection;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

public sealed class StandardApprovalDecisionMarkerTests
{
    [Fact]
    public void Known_standard_result_marks_target_updates_before_reflection()
    {
        var (context, local) = Context(resultKnown: true);

        var error = Invoke(local);

        Assert.NotNull(error);
        Assert.True(context.SharedVariables[StandardApprovalDecisionPlugin.TrustedDecisionInternalWriteSharedVariable] is true);
        Assert.True(context.SharedVariables[PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable] is true);
        Assert.True(context.SharedVariables[ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable] is true);
    }

    [Fact]
    public void Unknown_result_does_not_mark_target_updates()
    {
        var (context, local) = Context(resultKnown: false);

        Assert.Null(Invoke(local));
        Assert.False(context.SharedVariables.Contains(PartnerStandardUpdatePlugin.TrustedInternalWriteSharedVariable));
        Assert.False(context.SharedVariables.Contains(ContractStandardUpdatePlugin.TrustedInternalWriteSharedVariable));
    }

    private static (IPluginExecutionContext Context, ILocalPluginContext Local) Context(bool resultKnown)
    {
        var context = DispatchProxy.Create<IPluginExecutionContext, PropertyProxy>();
        var values = ((PropertyProxy)context).Values;
        values[nameof(IPluginExecutionContext.MessageName)] = "Update";
        values[nameof(IPluginExecutionContext.PrimaryEntityName)] = "pl_approvallink";
        values[nameof(IPluginExecutionContext.Stage)] = 40;
        values[nameof(IPluginExecutionContext.InitiatingUserId)] = Guid.Parse("11111111-1111-4111-8111-111111111111");
        values[nameof(IPluginExecutionContext.InputParameters)] = new ParameterCollection
        {
            ["Target"] = new Entity("pl_approvallink", Guid.Parse("22222222-2222-4222-8222-222222222222"))
            {
                ["pl_resultknown"] = resultKnown,
            },
        };
        values[nameof(IPluginExecutionContext.SharedVariables)] = new ParameterCollection
        {
            [StandardApprovalResultPlugin.StandardResultInputSharedVariable] = true,
        };

        var local = DispatchProxy.Create<ILocalPluginContext, PropertyProxy>();
        var localValues = ((PropertyProxy)local).Values;
        localValues[nameof(ILocalPluginContext.PluginExecutionContext)] = context;
        localValues[nameof(ILocalPluginContext.SystemService)] = new FakeOrganizationService();
        return (context, local);
    }

    private static InvalidPluginExecutionException? Invoke(ILocalPluginContext local)
    {
        var plugin = new StandardApprovalDecisionPlugin("", "");
        var execute = plugin.GetType().GetMethod("ExecuteDataversePlugin", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(execute);
        try
        {
            execute!.Invoke(plugin, new object[] { local });
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return Assert.IsType<InvalidPluginExecutionException>(exception.InnerException);
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
