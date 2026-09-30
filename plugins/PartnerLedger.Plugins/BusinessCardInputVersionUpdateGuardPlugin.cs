using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// InputVersion is immutable after its validated Create. There is no supported
    /// application update route; reject Update before it can change server-owned state.
    /// </summary>
    public static class BusinessCardInputVersionUpdateGuardContract
    {
        public const string EntityName = "pl_cardinputversion";

        public static void RejectUpdate(string? messageName, string? primaryEntityName, int stage, int mode)
        {
            if (!string.Equals(messageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(primaryEntityName, EntityName, StringComparison.Ordinal)
                || stage != 10
                || mode != 0)
            {
                throw new InvalidPluginExecutionException("名刺入力版Updateガードの実行コンテキストが不正です。");
            }

            throw new InvalidPluginExecutionException("名刺入力版は作成後に変更できません。画像または入力情報を修正する場合は、新しい入力版を作成してください。");
        }
    }

    public sealed class BusinessCardInputVersionUpdateGuardPlugin : PluginBase
    {
        public BusinessCardInputVersionUpdateGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BusinessCardInputVersionUpdateGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            BusinessCardInputVersionUpdateGuardContract.RejectUpdate(
                context.MessageName,
                context.PrimaryEntityName,
                context.Stage,
                context.Mode);
        }
    }
}
