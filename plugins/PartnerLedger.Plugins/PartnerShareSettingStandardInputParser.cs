using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準pl_PartnerShareSetting CRUDのTargetを、業務入力へ変換する境界。
    /// クライアントが送れる列を狭く固定し、サーバー導出列・principal変更・再開を拒否する。
    /// </summary>
    public static class PartnerShareSettingStandardInputParser
    {
        private static readonly HashSet<string> CreateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            // Dataverseの標準CreateパイプラインがTargetへ自動付与する主キー。
            // 業務入力ではなく、値は認可・ハッシュ計算に使わない。
            "pl_partnersharesettingid",
            // UserOwnedテーブルの標準パイプラインが解決する所属BU。
            // クライアント入力としては採用せず、共有設定の業務契約にも渡さない。
            "owningbusinessunit",
            // UserOwnedテーブルのCreateに必要な所有者。Plugin境界で呼出者本人の
            // systemuser参照であることを検証し、Dataverse標準Createへ残す。
            "ownerid",
            // UserOwnedテーブルがowneridから解決する内部所有者列。呼出者本人の参照だけを残す。
            "owninguser",
            // DataverseがCreate時に保持する監査列。業務入力・認可情報には使わない。
            "createdby",
            "modifiedby",
            "createdon",
            "modifiedon",
            PartnerShareSettingRecordFactory.PartnerLookupAttribute,
            PartnerShareSettingRecordFactory.PrincipalKindAttribute,
            PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute,
            PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute,
            PartnerShareSettingRecordFactory.AccessLevelAttribute,
            PartnerShareSettingRecordFactory.RequestKeyAttribute,
        };

        private static readonly HashSet<string> UpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            // DataverseのUpdate Targetへ自動付与される主キー。target.Idとの一致だけを許可する。
            "pl_partnersharesettingid",
            PartnerShareSettingRecordFactory.AccessLevelAttribute,
            PartnerShareSettingRecordFactory.RequestKeyAttribute,
        };

        private static readonly HashSet<string> PlatformUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_partnersharesettingid",
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
        };

        private static readonly HashSet<string> SafeCreateDefaults = new HashSet<string>(StringComparer.Ordinal)
        {
            PartnerShareSettingRecordFactory.SettingStateAttribute,
            PartnerShareSettingRecordFactory.ProtectedPathAttribute,
            PartnerShareSettingRecordFactory.ContentHashAttribute,
            PartnerShareSettingRecordFactory.ManagedProjectionAttribute,
            "modifiedonbehalfby",
            "createdonbehalfby",
            "statecode",
            "statuscode",
        };

        public static PartnerShareSettingInput ParseCreate(Entity target)
        {
            RequireEntity(target, requireId: false);
            RejectUnexpectedAttributes(target, CreateAttributes);

            var input = new PartnerShareSettingInput
            {
                PartnerId = RequireReference(
                    target,
                    PartnerShareSettingRecordFactory.PartnerLookupAttribute,
                    "pl_partner").Id,
                PrincipalKind = ParsePrincipalKind(
                    RequireOption(target, PartnerShareSettingRecordFactory.PrincipalKindAttribute)),
                UserId = OptionalReference(
                    target,
                    PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute,
                    "systemuser")?.Id,
                TeamId = OptionalReference(
                    target,
                    PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute,
                    "team")?.Id,
                AccessLevel = ParseAccessLevel(
                    RequireOption(target, PartnerShareSettingRecordFactory.AccessLevelAttribute)),
                RequestKey = RequireString(target, PartnerShareSettingRecordFactory.RequestKeyAttribute),
            };
            EnsureValid(input);
            return input;
        }

        public static PartnerShareSettingUpdateCommand ParseUpdate(
            Entity target,
            PartnerShareSettingRecord current)
        {
            RequireEntity(target, requireId: true);
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (target.Id != current.Id)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のUpdate対象IDが一致しません。");
            }

            if (target.Attributes.TryGetValue("pl_partnersharesettingid", out var targetIdValue)
                && (!(targetIdValue is Guid targetId) || targetId != target.Id))
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のUpdate主キーが対象IDと一致しません。");
            }

            var status = target.GetAttributeValue<OptionSetValue>(
                PartnerShareSettingRecordFactory.SettingStateAttribute);
            if (status != null)
            {
                if (status.Value != PartnerShareSettingRecordFactory.SettingStateRevokedOption
                    || GetBusinessAttributeCount(target) != 1)
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定の状態遷移が不正です。取消以外の状態変更は許可しません。");
                }

                return new PartnerShareSettingUpdateCommand { IsRevoke = true };
            }

            RejectUnexpectedAttributes(target, UpdateAttributes);
            var desired = new PartnerShareSettingInput
            {
                PartnerId = current.PartnerId,
                PrincipalKind = current.PrincipalKind,
                UserId = current.PrincipalKind == PartnerSharePrincipalKind.User
                    ? current.PrincipalId
                    : (Guid?)null,
                TeamId = current.PrincipalKind == PartnerSharePrincipalKind.Team
                    ? current.PrincipalId
                    : (Guid?)null,
                AccessLevel = ParseAccessLevel(
                    RequireOption(target, PartnerShareSettingRecordFactory.AccessLevelAttribute)),
                RequestKey = RequireString(target, PartnerShareSettingRecordFactory.RequestKeyAttribute),
            };
            EnsureValid(desired);
            return new PartnerShareSettingUpdateCommand { Desired = desired };
        }

        private static void EnsureValid(PartnerShareSettingInput input)
        {
            var validation = PartnerShareSettingContract.Validate(input);
            if (!validation.IsAllowed)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定の標準CRUD入力が不正です: " + validation.Error);
            }
        }

        private static void RejectUnexpectedAttributes(Entity target, HashSet<string> allowed)
        {
            foreach (var name in target.Attributes.Keys)
            {
                if (allowed.Contains(name))
                {
                    continue;
                }

                if (allowed == CreateAttributes && IsSafeCreateDefault(target, name))
                {
                    continue;
                }

                if (allowed == UpdateAttributes && IsSafeUpdatePlatformAttribute(target, name))
                {
                    continue;
                }

                if (!allowed.Contains(name))
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定のクライアント入力列が許可されていません: " + name);
                }
            }
        }

        private static bool IsSafeCreateDefault(Entity target, string attributeName)
        {
            if (!SafeCreateDefaults.Contains(attributeName)) return false;

            if (string.Equals(attributeName, PartnerShareSettingRecordFactory.SettingStateAttribute, StringComparison.Ordinal))
            {
                return target[attributeName] is OptionSetValue option
                    && option.Value == PartnerShareSettingRecordFactory.SettingStateActiveOption;
            }

            if (string.Equals(attributeName, PartnerShareSettingRecordFactory.ProtectedPathAttribute, StringComparison.Ordinal))
            {
                return target[attributeName] is bool value && !value;
            }

            if (string.Equals(attributeName, PartnerShareSettingRecordFactory.ManagedProjectionAttribute, StringComparison.Ordinal))
            {
                return target[attributeName] is bool value && !value;
            }

            if (string.Equals(attributeName, "modifiedonbehalfby", StringComparison.Ordinal)
                || string.Equals(attributeName, "createdonbehalfby", StringComparison.Ordinal))
            {
                return target[attributeName] == null;
            }

            if (string.Equals(attributeName, "statecode", StringComparison.Ordinal))
            {
                return target[attributeName] is OptionSetValue option && option.Value == 0;
            }

            if (string.Equals(attributeName, "statuscode", StringComparison.Ordinal))
            {
                return target[attributeName] is OptionSetValue option && option.Value == 1;
            }

            return target[attributeName] == null
                || target[attributeName] is string text && string.IsNullOrWhiteSpace(text);
        }

        private static int GetBusinessAttributeCount(Entity target)
        {
            var count = 0;
            foreach (var name in target.Attributes.Keys)
            {
                if (!PlatformUpdateAttributes.Contains(name)) count++;
            }
            return count;
        }

        private static bool IsSafeUpdatePlatformAttribute(Entity target, string attributeName)
        {
            if (!PlatformUpdateAttributes.Contains(attributeName)) return false;

            if (string.Equals(attributeName, "pl_partnersharesettingid", StringComparison.Ordinal))
            {
                return target[attributeName] is Guid id && id != Guid.Empty;
            }

            if (string.Equals(attributeName, "modifiedon", StringComparison.Ordinal))
            {
                return target[attributeName] is DateTime;
            }

            if (string.Equals(attributeName, "modifiedby", StringComparison.Ordinal)
                || string.Equals(attributeName, "modifiedonbehalfby", StringComparison.Ordinal))
            {
                return target[attributeName] == null
                    || target[attributeName] is EntityReference reference
                        && reference.Id != Guid.Empty
                        && string.Equals(reference.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void RequireEntity(Entity target, bool requireId)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!string.Equals(
                    target.LogicalName,
                    PartnerShareSettingRecordFactory.EntityName,
                    StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException(
                    "共有設定の標準CRUD対象テーブルが不正です。");
            }
            if (requireId && target.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のUpdate対象IDがありません。");
            }
        }

        private static OptionSetValue RequireOption(Entity target, string attributeName)
        {
            var value = target.GetAttributeValue<OptionSetValue>(attributeName);
            if (value == null)
            {
                throw new InvalidPluginExecutionException(attributeName + "がありません。");
            }
            return value;
        }

        private static string RequireString(Entity target, string attributeName)
        {
            var value = target.GetAttributeValue<string>(attributeName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(attributeName + "がありません。");
            }
            return value.Trim();
        }

        private static EntityReference RequireReference(
            Entity target,
            string attributeName,
            string logicalName)
        {
            var value = OptionalReference(target, attributeName, logicalName);
            if (value == null)
            {
                throw new InvalidPluginExecutionException(attributeName + "がありません。");
            }
            return value;
        }

        private static EntityReference? OptionalReference(
            Entity target,
            string attributeName,
            string logicalName)
        {
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                return null;
            }
            if (!(target[attributeName] is EntityReference reference)
                || reference.Id == Guid.Empty
                || !string.Equals(reference.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException(attributeName + "の参照先が不正です。");
            }
            return reference;
        }

        private static PartnerSharePrincipalKind ParsePrincipalKind(OptionSetValue value)
            => value.Value switch
            {
                PartnerShareSettingRecordFactory.PrincipalKindUserOption => PartnerSharePrincipalKind.User,
                PartnerShareSettingRecordFactory.PrincipalKindTeamOption => PartnerSharePrincipalKind.Team,
                _ => throw new InvalidPluginExecutionException("主体種別のChoiceが不正です。"),
            };

        private static PartnerShareAccessLevel ParseAccessLevel(OptionSetValue value)
            => value.Value switch
            {
                PartnerShareSettingRecordFactory.AccessLevelReadOption => PartnerShareAccessLevel.Read,
                PartnerShareSettingRecordFactory.AccessLevelWriteOption => PartnerShareAccessLevel.Write,
                _ => throw new InvalidPluginExecutionException("アクセスレベルのChoiceが不正です。"),
            };
    }

    public sealed class PartnerShareSettingUpdateCommand
    {
        public bool IsRevoke { get; internal set; }
        public PartnerShareSettingInput? Desired { get; internal set; }
    }
}
