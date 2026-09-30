using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Assigns each flow-created Review row to the owner of its parent Capture.
    /// Register synchronously in PostOperation. The internal Assign runs through the
    /// SYSTEM service (no step Run-as, no Application User). The caller's permission
    /// to initiate its root operation remains independently enforced.
    /// </summary>
    public sealed class BusinessCardReviewOwnerPlugin : PluginBase
    {
        public BusinessCardReviewOwnerPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardReviewOwnerPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, BusinessCardRecordOwnerContract.ReviewEntityName, StringComparison.Ordinal)
                || context.Stage != 40
                || context.Mode != 0
                || context.PrimaryEntityId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("名刺確認版所有者Pluginの実行コンテキストが不正です。");
            }

            BusinessCardRecordOwnerContract.EnsureReviewOwnerMatchesCapture(
                localPluginContext.SystemService,
                context.PrimaryEntityId);
        }
    }
}
