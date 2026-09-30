using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_partner CreateのPreValidation境界。すべてのCreateを標準CRUDの入力契約で
    /// 検査してサーバー導出値を適用する。クライアントがstatus／登録者／ACL版などを
    /// 含むEntityを直接作る迂回経路は閉じたままにする。
    /// 旧Custom API（pl_RegisterPartner）の子Createを素通りさせる分岐は、同名のCustom APIを
    /// 作れば検査を迂回できたため2026-09-27に削除した。
    /// </summary>
    public sealed class PartnerCreateGuardPlugin : PluginBase
    {
        public PartnerCreateGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(PartnerCreateGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            PartnerStandardCreateContract.ValidateAndApply(
                target,
                context.InitiatingUserId,
                DateTime.UtcNow);
        }
    }
}
