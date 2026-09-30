using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 担当者の標準Updateで、作成時の所属・登録日・再送キー・実行主体を不変にする。
    /// 作成後の氏名・在籍状態・連絡先の編集は標準CRUDの範囲に残すが、所属変更は
    /// 退職＋新規登録の業務導線へ分離する。画面のread-only表示だけに依存しない。
    /// </summary>
    public sealed class ContactStandardUpdateGuardPlugin : PluginBase
    {
        public ContactStandardUpdateGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ContactStandardUpdateGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));
            var context = localPluginContext.PluginExecutionContext;
            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, ContactStandardCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準担当者Updateの実行コンテキストが不正です。");
            }

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            Entity? existing = null;
            if (target != null && target.Attributes.Contains(ContactStandardCreateContract.StatusAttribute))
            {
                if (target.Id == Guid.Empty)
                {
                    throw new InvalidPluginExecutionException("標準担当者Updateの対象IDがありません。");
                }

                // The guard step runs as the Calling User, preserving the same record
                // access boundary as the standard Update. Use a narrow pre-image instead
                // of retrieving the row with elevated privileges. The step registration
                // must provide exactly the columns required by ValidateRetirementTransition.
                if (context.PreEntityImages == null || !context.PreEntityImages.Contains(PreImageAlias))
                {
                    throw new InvalidPluginExecutionException(
                        "担当者の現在状態を確認できるPre Imageがないため、在籍状態を変更できません。");
                }

                existing = context.PreEntityImages[PreImageAlias];
            }

            Validate(target, existing);
        }

        public static void Validate(Entity? target)
            => Validate(target, null);

        public static void Validate(Entity? target, Entity? existing)
        {
            if (target == null)
            {
                throw new InvalidPluginExecutionException("標準担当者UpdateのTargetがありません。");
            }

            if (!string.Equals(target.LogicalName, ContactStandardCreateContract.EntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("標準担当者Updateの対象テーブルが不正です。");
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                // Dataverseの標準Updateは、変更列とは別にTargetへ主キー属性を付与する。
                // これは変更可能な業務列ではないが、標準パイプラインの技術属性として
                // 型だけを検証して受け入れる。主キー以外の権威列は引き続き拒否する。
                if (attributeName == ContactStandardCreateContract.PrimaryIdAttribute)
                {
                    if (!(target[attributeName] is Guid))
                    {
                        throw new InvalidPluginExecutionException(
                            "標準担当者Updateの主キー属性の型が不正です: " + attributeName);
                    }

                    continue;
                }

                if (attributeName == StateAttribute || attributeName == StatusReasonAttribute)
                {
                    continue;
                }

                if (AllowedUpdateAttributes.Contains(attributeName)) continue;
                if (ImmutableAttributes.Contains(attributeName))
                {
                    throw new InvalidPluginExecutionException(
                        "担当者の所属・登録日・登録要求キー・実行主体は作成後に変更できません: " + attributeName);
                }

                throw new InvalidPluginExecutionException(
                    "標準担当者Updateで許可されていない列です: " + attributeName);
            }

            ValidateEditableValue(target, ContactStandardCreateContract.NameAttribute, required: true, maxLength: 850);
            ValidateStatus(target);
            ValidateEditableValue(target, ContactStandardCreateContract.DepartmentRoleAttribute, required: false, maxLength: 200);
            ValidateEditableValue(target, ContactStandardCreateContract.EmailAttribute, required: false, maxLength: 200);
            ValidateEditableValue(target, ContactStandardCreateContract.PhoneAttribute, required: false, maxLength: 200);
            ValidateRetirementTransition(target, existing);
        }

        private static void ValidateEditableValue(Entity target, string attributeName, bool required, int maxLength)
        {
            if (!target.Attributes.Contains(attributeName)) return;
            var value = target[attributeName];
            if (value == null)
            {
                if (required)
                {
                    throw new InvalidPluginExecutionException(attributeName + "は空にできません。");
                }

                return;
            }

            if (!(value is string stringValue))
            {
                throw new InvalidPluginExecutionException(attributeName + "の型が不正です。");
            }

            var normalized = stringValue.Trim();
            if (required && normalized.Length == 0)
            {
                throw new InvalidPluginExecutionException(attributeName + "は空にできません。");
            }

            if (normalized.Length > maxLength)
            {
                throw new InvalidPluginExecutionException(attributeName + "が長すぎます。");
            }

            target[attributeName] = required || normalized.Length > 0 ? normalized : null;
        }

        private static void ValidateStatus(Entity target)
        {
            if (!target.Attributes.Contains(ContactStandardCreateContract.StatusAttribute)) return;
            if (!(target[ContactStandardCreateContract.StatusAttribute] is OptionSetValue status)
                || (status.Value != 100000000 && status.Value != 100000001 && status.Value != 100000002))
            {
                throw new InvalidPluginExecutionException("担当者の在籍状態が不正です。");
            }
        }

        private static void ValidateRetirementTransition(Entity target, Entity? existing)
        {
            var hasContactStatus = target.Attributes.Contains(ContactStandardCreateContract.StatusAttribute);
            var hasState = target.Attributes.Contains(StateAttribute);
            var hasStatusReason = target.Attributes.Contains(StatusReasonAttribute);

            if (!hasState && !hasStatusReason)
            {
                if (hasContactStatus
                    && GetOptionValue(target, ContactStandardCreateContract.StatusAttribute) == RetiredContactStatus)
                {
                    throw new InvalidPluginExecutionException(
                        "退職へ変更する場合はDataverseの非アクティブ状態も同時に指定してください。");
                }

                if (existing != null
                    && GetOptionValue(existing, StateAttribute) == InactiveState
                    && hasContactStatus
                    && GetOptionValue(target, ContactStandardCreateContract.StatusAttribute) != RetiredContactStatus)
                {
                    throw new InvalidPluginExecutionException("退職済み担当者を再有効化できません。");
                }

                return;
            }

            if (!hasContactStatus || !hasState || !hasStatusReason)
            {
                throw new InvalidPluginExecutionException(
                    "担当者を退職にする場合は在籍状態・statecode・statuscodeを同時に指定してください。");
            }

            if (GetOptionValue(target, ContactStandardCreateContract.StatusAttribute) != RetiredContactStatus
                || GetOptionValue(target, StateAttribute) != InactiveState
                || GetOptionValue(target, StatusReasonAttribute) != InactiveStatusReason)
            {
                throw new InvalidPluginExecutionException(
                    "退職時のDataverse状態の組合せが不正です。");
            }
        }

        private static int? GetOptionValue(Entity entity, string attributeName)
            => entity.GetAttributeValue<OptionSetValue>(attributeName)?.Value;

        private static readonly HashSet<string> AllowedUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            ContactStandardCreateContract.NameAttribute,
            ContactStandardCreateContract.StatusAttribute,
            ContactStandardCreateContract.DepartmentRoleAttribute,
            ContactStandardCreateContract.EmailAttribute,
            ContactStandardCreateContract.PhoneAttribute,
        };

        private static readonly HashSet<string> ImmutableAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            ContactStandardCreateContract.PartnerLookupAttribute,
            ContactStandardCreateContract.RegisteredAtAttribute,
            ContactStandardCreateContract.RegistrationKeyAttribute,
            "ownerid",
            "createdby",
            "createdon",
            "createdonbehalfby",
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
            "owningbusinessunit",
            "owningteam",
            "owninguser",
        };

        private const string StateAttribute = "statecode";
        private const string StatusReasonAttribute = "statuscode";
        private const int InactiveState = 1;
        private const int InactiveStatusReason = 2;
        private const int RetiredContactStatus = 100000002;
        public const string PreImageAlias = "ContactStandardUpdateGuardPreImage";
    }
}
