using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準CRUDの共有設定行をDataverse Entityへ変換する境界。
    /// Choice値・論理列名はクラウド適用前の設計契約として固定しているため、
    /// 実機登録前にメタデータの値と照合する。標準GrantAccess等はここでは実行しない。
    /// </summary>
    public static class PartnerShareSettingRecordFactory
    {
        public const string EntityName = "pl_partnersharesetting";
        public const string PartnerLookupAttribute = "pl_partnerlookup";
        public const string PrincipalKindAttribute = "pl_principalkindcode";
        public const string UserPrincipalLookupAttribute = "pl_userprincipallookup";
        public const string TeamPrincipalLookupAttribute = "pl_teamprincipallookup";
        public const string AccessLevelAttribute = "pl_accesslevelcode";
        public const string SettingStateAttribute = "pl_settingstatuscode";
        public const string ProtectedPathAttribute = "pl_isprotectedmanagementpath";
        public const string ManagedProjectionAttribute = "pl_ismanagedprojection";
        public const string RequestKeyAttribute = "pl_requestkey";
        public const string ContentHashAttribute = "pl_contenthash";

        // DataverseのChoice値は、クラウド作成時にこの設計値と再照合する。
        public const int PrincipalKindUserOption = 100000000;
        public const int PrincipalKindTeamOption = 100000001;
        public const int AccessLevelReadOption = 100000000;
        public const int AccessLevelWriteOption = 100000001;
        public const int SettingStateActiveOption = 100000000;
        public const int SettingStateRevokedOption = 100000001;

        public static Entity BuildCreate(
            PartnerSharePhase phase,
            PartnerShareSettingInput input,
            Guid resolvedApproverTeamId,
            bool isManagedProjection = false)
        {
            var validation = PartnerShareSettingContract.Validate(input);
            if (!validation.IsAllowed)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のCreate入力が不正です: " + validation.Error);
            }

            var entity = new Entity(EntityName)
            {
                [PartnerLookupAttribute] = new EntityReference("pl_partner", input.PartnerId),
                [PrincipalKindAttribute] = new OptionSetValue(ToPrincipalKindOption(input.PrincipalKind!.Value)),
                [AccessLevelAttribute] = new OptionSetValue(ToAccessLevelOption(input.AccessLevel!.Value)),
                [SettingStateAttribute] = new OptionSetValue(SettingStateActiveOption),
                [ProtectedPathAttribute] = PartnerShareManagementPathPolicy.IsProtected(
                    phase,
                    input,
                    resolvedApproverTeamId),
                [ManagedProjectionAttribute] = isManagedProjection,
                [RequestKeyAttribute] = input.RequestKey.Trim(),
                [ContentHashAttribute] = PartnerShareSettingFingerprint.Compute(input),
            };

            if (input.PrincipalKind.Value == PartnerSharePrincipalKind.User)
            {
                entity[UserPrincipalLookupAttribute] = new EntityReference("systemuser", input.UserId!.Value);
            }
            else
            {
                entity[TeamPrincipalLookupAttribute] = new EntityReference("team", input.TeamId!.Value);
            }

            return entity;
        }

        /// <summary>
        /// 変更要求では会社・principal・保護フラグ・状態をTargetへ含めない。
        /// サーバー側で再取得した現行行と desired を比較するのは別の認可契約で行う。
        /// </summary>
        public static Entity BuildUpdate(Guid settingId, PartnerShareSettingInput desired)
        {
            if (settingId == Guid.Empty) throw new ArgumentException("共有設定IDが必要です。", nameof(settingId));
            var validation = PartnerShareSettingContract.Validate(desired);
            if (!validation.IsAllowed)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のUpdate入力が不正です: " + validation.Error);
            }

            return new Entity(EntityName, settingId)
            {
                [AccessLevelAttribute] = new OptionSetValue(ToAccessLevelOption(desired.AccessLevel!.Value)),
                [RequestKeyAttribute] = desired.RequestKey.Trim(),
                [ContentHashAttribute] = PartnerShareSettingFingerprint.Compute(desired),
            };
        }

        /// <summary>取消は物理Deleteではなく、状態だけをRevokedへ進める。</summary>
        public static Entity BuildRevoke(Guid settingId)
        {
            if (settingId == Guid.Empty) throw new ArgumentException("共有設定IDが必要です。", nameof(settingId));
            return new Entity(EntityName, settingId)
            {
                [SettingStateAttribute] = new OptionSetValue(SettingStateRevokedOption),
            };
        }

        /// <summary>
        /// 標準Readで取得した共有設定行を認可契約の型へ変換する。欠損・異常値は、
        /// 保護経路を誤って通常行として扱わないよう例外にする。
        /// </summary>
        public static PartnerShareSettingRecord Parse(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!string.Equals(entity.LogicalName, EntityName, StringComparison.Ordinal)
                || entity.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("共有設定行の識別情報が不正です。");
            }

            var partner = RequireReference(entity, PartnerLookupAttribute, "pl_partner");
            var principalKind = ParsePrincipalKind(RequireOption(entity, PrincipalKindAttribute));
            var accessLevel = ParseAccessLevel(RequireOption(entity, AccessLevelAttribute));
            var state = ParseState(RequireOption(entity, SettingStateAttribute));
            var protectedPath = RequireBoolean(entity, ProtectedPathAttribute);
            // この列は後から追加したサーバー導出列であり、追加前に作成された行には
            // 属性自体が存在しない。欠損した旧行を管理対象と誤認しないよう、falseへ
            // フォールバックして未照合として扱う。明示された値がbool以外なら拒否する。
            var managedProjection = entity.Attributes.Contains(ManagedProjectionAttribute)
                ? RequireBoolean(entity, ManagedProjectionAttribute)
                : false;
            var createdBy = RequireReference(entity, "createdby", "systemuser");
            var requestKey = RequireString(entity, RequestKeyAttribute);
            var contentHash = RequireString(entity, ContentHashAttribute);
            if (!PartnerShareSettingFingerprint.IsValid(contentHash))
            {
                throw new InvalidPluginExecutionException("共有設定行の内容ハッシュが不正です。");
            }

            var user = OptionalReference(entity, UserPrincipalLookupAttribute, "systemuser");
            var team = OptionalReference(entity, TeamPrincipalLookupAttribute, "team");
            if ((user == null) == (team == null))
            {
                throw new InvalidPluginExecutionException("共有設定行のprincipalが排他的ではありません。");
            }

            if (principalKind == PartnerSharePrincipalKind.User && user == null
                || principalKind == PartnerSharePrincipalKind.Team && team == null)
            {
                throw new InvalidPluginExecutionException("共有設定行のprincipal種別と参照が一致しません。");
            }

            if (protectedPath && principalKind != PartnerSharePrincipalKind.Team)
            {
                throw new InvalidPluginExecutionException(
                    "保護された管理経路は承認者Teamの共有設定行にだけ指定できます。");
            }

            return new PartnerShareSettingRecord
            {
                Id = entity.Id,
                PartnerId = partner.Id,
                PrincipalKind = principalKind,
                PrincipalId = (user ?? team)!.Id,
                AccessLevel = accessLevel,
                State = state,
                CreatedByUserId = createdBy.Id,
                IsProtectedManagementPath = protectedPath,
                IsManagedProjection = managedProjection,
                RequestKey = requestKey,
                ContentHash = contentHash,
            };
        }

        private static int ToPrincipalKindOption(PartnerSharePrincipalKind kind)
            => kind == PartnerSharePrincipalKind.User
                ? PrincipalKindUserOption
                : PrincipalKindTeamOption;

        private static int ToAccessLevelOption(PartnerShareAccessLevel level)
            => level == PartnerShareAccessLevel.Read
                ? AccessLevelReadOption
                : AccessLevelWriteOption;

        private static PartnerSharePrincipalKind ParsePrincipalKind(OptionSetValue option)
            => option.Value switch
            {
                PrincipalKindUserOption => PartnerSharePrincipalKind.User,
                PrincipalKindTeamOption => PartnerSharePrincipalKind.Team,
                _ => throw new InvalidPluginExecutionException("共有設定行のprincipal種別が不正です。"),
            };

        private static PartnerShareAccessLevel ParseAccessLevel(OptionSetValue option)
            => option.Value switch
            {
                AccessLevelReadOption => PartnerShareAccessLevel.Read,
                AccessLevelWriteOption => PartnerShareAccessLevel.Write,
                _ => throw new InvalidPluginExecutionException("共有設定行のアクセス種別が不正です。"),
            };

        private static PartnerShareSettingState ParseState(OptionSetValue option)
            => option.Value switch
            {
                SettingStateActiveOption => PartnerShareSettingState.Active,
                SettingStateRevokedOption => PartnerShareSettingState.Revoked,
                _ => throw new InvalidPluginExecutionException("共有設定行の状態が不正です。"),
            };

        private static OptionSetValue RequireOption(Entity entity, string attributeName)
        {
            var value = entity.GetAttributeValue<OptionSetValue>(attributeName);
            if (value == null) throw new InvalidPluginExecutionException(attributeName + "がありません。");
            return value;
        }

        private static bool RequireBoolean(Entity entity, string attributeName)
        {
            if (!entity.Attributes.Contains(attributeName) || !(entity[attributeName] is bool value))
            {
                throw new InvalidPluginExecutionException(attributeName + "がありません。");
            }

            return value;
        }

        private static string RequireString(Entity entity, string attributeName)
        {
            var value = entity.GetAttributeValue<string>(attributeName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(attributeName + "がありません。");
            }

            return value.Trim();
        }

        private static EntityReference RequireReference(Entity entity, string attributeName, string logicalName)
        {
            var value = OptionalReference(entity, attributeName, logicalName);
            if (value == null) throw new InvalidPluginExecutionException(attributeName + "がありません。");
            return value;
        }

        private static EntityReference? OptionalReference(Entity entity, string attributeName, string logicalName)
        {
            if (!entity.Attributes.Contains(attributeName) || entity[attributeName] == null)
            {
                return null;
            }

            if (!(entity[attributeName] is EntityReference reference)
                || reference.Id == Guid.Empty
                || !string.Equals(reference.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException(attributeName + "の参照先が不正です。");
            }

            return reference;
        }
    }
}
