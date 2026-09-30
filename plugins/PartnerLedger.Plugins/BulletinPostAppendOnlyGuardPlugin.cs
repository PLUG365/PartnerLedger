using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 掲示板投稿を追記専用に固定する標準Update/Deleteガード。
    /// </summary>
    public sealed class BulletinPostAppendOnlyGuardPlugin : PluginBase
    {
        public BulletinPostAppendOnlyGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(BulletinPostAppendOnlyGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.PrimaryEntityName, BulletinPostStandardCreateContract.EntityName, StringComparison.Ordinal)
                || (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(context.MessageName, "Delete", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidPluginExecutionException("掲示板投稿の追記専用ガードの実行コンテキストが不正です。");
            }

            RejectMutation(context.MessageName);
        }

        public static void RejectMutation(string messageName)
        {
            throw new InvalidPluginExecutionException(
                "掲示板投稿は追記専用です。Update/Deleteは許可されていません。"
                + " message=" + (messageName ?? ""));
        }
    }
}
