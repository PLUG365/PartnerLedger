using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_cardinputversion Create のPreValidation境界。
    /// 親CaptureをCalling Userで再読取りし、画像到着と現行版を確認してから
    /// 入力版の状態をサーバー側で固定する。
    /// </summary>
    public sealed class BusinessCardInputVersionCreatePlugin : PluginBase
    {
        public BusinessCardInputVersionCreatePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardInputVersionCreatePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, BusinessCardInputVersionCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準名刺入力版Createの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            BusinessCardInputVersionCreateContract.ValidateAndApply(
                target,
                localPluginContext.InitiatingUserService,
                DateTime.UtcNow);
        }
    }
}
