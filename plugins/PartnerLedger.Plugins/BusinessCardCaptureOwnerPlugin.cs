using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Moves all OCR child rows with their parent Capture during an ownership change.
    /// Register synchronously in PreOperation. The child rows are assigned and their
    /// former-owner shares reconciled through the SYSTEM service (no step Run-as); the
    /// root Capture Assign is still authorized for the initiating caller. Any child
    /// assignment failure aborts the parent assignment transaction.
    /// </summary>
    public sealed class BusinessCardCaptureOwnerPlugin : PluginBase
    {
        public BusinessCardCaptureOwnerPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardCaptureOwnerPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Assign", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, BusinessCardRecordOwnerContract.CaptureEntityName, StringComparison.Ordinal)
                || context.Stage != 20
                || context.Mode != 0)
            {
                throw new InvalidPluginExecutionException("名刺取込所有者Pluginの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as EntityReference
                : null;
            var assignee = context.InputParameters.Contains("Assignee")
                ? context.InputParameters["Assignee"] as EntityReference
                : null;
            if (target == null
                || target.Id == Guid.Empty
                || !string.Equals(target.LogicalName, BusinessCardRecordOwnerContract.CaptureEntityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException("名刺取込所有者Pluginの対象行が不正です。");
            }

            BusinessCardRecordOwnerContract.EnsureCaptureChildrenOwnedBy(
                localPluginContext.SystemService,
                target.Id,
                assignee!);
        }
    }
}
