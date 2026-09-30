using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_capturebatch／pl_cardcaptureの標準Createへ同じ受付境界を適用する。
    /// Run in User's Contextは呼出者のままとし、本人のCreate／Write権限で評価する。
    /// </summary>
    public sealed class BusinessCardCaptureCreatePlugin : PluginBase
    {
        public BusinessCardCaptureCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardCaptureCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            var result = BusinessCardCaptureCreateContract.Validate(target);
            if (!result.IsValid)
            {
                throw new InvalidPluginExecutionException("名刺受付Createの入力が不正です: " + result.Error);
            }

            BusinessCardCaptureCreateContract.ApplyServerDerivedAttributes(target!, DateTime.UtcNow);
        }
    }
}
