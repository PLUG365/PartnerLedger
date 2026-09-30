using System;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BulletinPostAppendOnlyGuardPluginTests
    {
        [Theory]
        [InlineData("Update")]
        [InlineData("Delete")]
        public void Caller実行でも内部サービスを使わず常に拒否する(string message)
        {
            var context = DispatchProxy.Create<IPluginExecutionContext,
                PartnerShareSetupStatusGuardTests.PropertyProxy>();
            var values = ((PartnerShareSetupStatusGuardTests.PropertyProxy)context).Values;
            values[nameof(IPluginExecutionContext.PrimaryEntityName)] =
                BulletinPostStandardCreateContract.EntityName;
            values[nameof(IPluginExecutionContext.MessageName)] = message;
            values[nameof(IPluginExecutionContext.UserId)] =
                Guid.Parse("11111111-1111-1111-1111-111111111111");
            values[nameof(IPluginExecutionContext.InitiatingUserId)] =
                Guid.Parse("22222222-2222-2222-2222-222222222222");

            var local = DispatchProxy.Create<ILocalPluginContext,
                PartnerShareSetupStatusGuardTests.PropertyProxy>();
            ((PartnerShareSetupStatusGuardTests.PropertyProxy)local).Values[
                nameof(ILocalPluginContext.PluginExecutionContext)] = context;

            var method = typeof(BulletinPostAppendOnlyGuardPlugin).GetMethod(
                "ExecuteDataversePlugin", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var thrown = Assert.Throws<TargetInvocationException>(() =>
                method!.Invoke(new BulletinPostAppendOnlyGuardPlugin("", ""), new object[] { local }));
            var denied = Assert.IsType<InvalidPluginExecutionException>(thrown.InnerException);
            Assert.Contains("追記専用", denied.Message);
        }

        [Theory]
        [InlineData("Update")]
        [InlineData("Delete")]
        public void UpdateとDeleteは常に拒否する(string message)
        {
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BulletinPostAppendOnlyGuardPlugin.RejectMutation(message));

            Assert.Contains("追記専用", exception.Message);
            Assert.Contains(message, exception.Message);
        }
    }
}
