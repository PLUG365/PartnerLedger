using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Creates the initial OCR job after Dataverse has persisted a validated input version.
    /// The Job remains server-owned: do not grant Create to end-user roles. The Job is
    /// written through the SYSTEM service, so the step needs no Run-as user.
    /// </summary>
    public sealed class BusinessCardOcrJobCreatePlugin : PluginBase
    {
        public BusinessCardOcrJobCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardOcrJobCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, BusinessCardOcrJobCreateContract.InputVersionEntityName, StringComparison.Ordinal)
                || context.Stage != 40
                || context.Mode != 0
                || context.PrimaryEntityId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("名刺OCR Job作成Pluginの実行コンテキストが不正です。");
            }

            BusinessCardOcrJobCreateContract.EnsureJobExists(
                context.PrimaryEntityId,
                localPluginContext.SystemService);

            localPluginContext.TracingService.Trace(
                "BusinessCardOcrJobCreate completed for InputVersion {0}.",
                context.PrimaryEntityId);
        }
    }
}
