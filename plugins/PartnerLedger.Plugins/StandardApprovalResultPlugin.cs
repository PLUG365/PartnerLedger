using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_ApprovalLink UpdateのPreOperationでグループ承認の結果を保存する。
    /// 内部の読取・書込みはSYSTEMのサービスで行い、結果の整合性をサーバー側で検証する。
    /// 呼出元の真正性は本Pluginでは保証しない。ApprovalLink WriteのRole境界を別途維持する。
    /// </summary>
    public sealed class StandardApprovalResultPlugin : PluginBase
    {
        public const string StandardResultInputSharedVariable =
            "PartnerLedger.StandardApprovalResultPlugin.StandardResultInput";

        private static readonly ISet<string> PlatformEnrichedAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "modifiedon",
            "modifiedby",
            "modifiedonbehalfby",
        };

        private static readonly ISet<string> ServerDerivedAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_linkstatuscode",
            "pl_decisioncode",
            "pl_resultknown",
            "pl_decidedat",
            "pl_responseat",
        };

        public StandardApprovalResultPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(StandardApprovalResultPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, StandardApprovalCrudContract.ApprovalLinkEntityName, StringComparison.Ordinal)
                || context.Stage != 20)
            {
                throw new InvalidPluginExecutionException(
                    "標準結果取込Pluginはpl_ApprovalLink UpdateのPreOperationだけで実行できます。");
            }
            var rawTarget = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;

            // 撤去した旧結果取込APIと同じ形（結果列とサーバー導出列を同時に含む）の
            // ネストUpdateを、allow-list検査より先に除外する。リポジトリ内の現行経路に
            // この形の書込みは無い（判定反映のLink更新はpl_linkstatuscodeだけで、
            // このStepのFilteringAttributesに含まれない）。削除はクラウドE2Eで確認してから行う。
            if (IsLegacyInternalWrite(context, rawTarget))
            {
                return;
            }

            var target = RemovePlatformEnrichment(rawTarget);
            var inputContract = StandardApprovalCrudContract.ValidateApprovalLinkResultUpdate(target);
            if (!inputContract.IsValid)
            {
                throw new InvalidPluginExecutionException(
                    "標準結果取込Pluginの入力契約違反です: " + inputContract.Error);
            }

            // SharedVariables cannot be supplied by a Web API caller. The marker
            // lets the PostOperation decision step distinguish this standard root
            // update from a legacy nested Link update without relying on the
            // environment-specific ParentContext shape.
            context.SharedVariables[StandardResultInputSharedVariable] = true;

            var result = StandardApprovalResultService.Apply(
                localPluginContext.SystemService,
                target,
                context.InitiatingUserId,
                DateTime.UtcNow);
            if (!result.Success)
            {
                throw new InvalidPluginExecutionException(
                    "承認結果の標準保存を拒否しました: " + result.ErrorCode);
            }

            CopyServerDerivedValues(target, rawTarget!);
        }

        private static Entity RemovePlatformEnrichment(Entity? rawTarget)
        {
            if (rawTarget == null)
            {
                return null!;
            }

            var target = new Entity(rawTarget.LogicalName, rawTarget.Id);
            foreach (var attribute in rawTarget.Attributes)
            {
                if (!PlatformEnrichedAttributes.Contains(attribute.Key))
                {
                    target[attribute.Key] = attribute.Value;
                }
            }

            return target;
        }

        private static void CopyServerDerivedValues(Entity source, Entity target)
        {
            foreach (var attributeName in ServerDerivedAttributes)
            {
                if (source.Attributes.Contains(attributeName))
                {
                    target[attributeName] = source[attributeName];
                }
            }
        }

        internal static bool IsLegacyInternalWrite(IPluginExecutionContext context, Entity? target)
            => context.ParentContext != null
               && context.Depth > 1
               && target != null
               && (target.Attributes.Contains("pl_linkstatuscode")
                   || target.Attributes.Contains("pl_decidedat")
                   || target.Attributes.Contains("pl_responseat"));

        internal static bool HasStandardResultInput(IPluginExecutionContext context)
        {
            for (var current = context; current != null; current = current.ParentContext)
            {
                if (current.SharedVariables != null
                    && current.SharedVariables.TryGetValue(StandardResultInputSharedVariable, out var value)
                    && value is bool marked
                    && marked)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
