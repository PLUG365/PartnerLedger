using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum PartnerStandardUpdateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        TargetIdRequired,
        InitiatingUserRequired,
        PreImageRequired,
        PreImageEntityInvalid,
        CurrentStateUnavailable,
        CurrentStateInvalid,
        SettingsUnavailable,
        ApprovalRequired,
        UpdateEmpty,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        NameTypeInvalid,
        NameRequired,
        NameTooLong,
        TradingStatusTypeInvalid,
        TradingStatusValueInvalid,
        AddressTypeInvalid,
        AddressTooLong,
        PhoneTypeInvalid,
        PhoneTooLong,
        /// <summary>主担当の値がユーザーへの参照でない（2026-09-29）。</summary>
        MainOwnerTypeInvalid,
    }

    /// <summary>
    /// Standard pl_partner Updateの入力境界。
    /// ポリシーONの項目は標準Updateから拒否し、Request／SubmissionVersionへ送る。
    /// ポリシーOFFの項目だけを直接反映し、操作者と変更列を監査する。
    /// </summary>
    public static class PartnerStandardUpdateContract
    {
        public const string EntityName = "pl_partner";
        public const string PrimaryIdAttribute = "pl_partnerid";
        public const string NameAttribute = "pl_name";
        public const string TradingStatusAttribute = "pl_tradingstatuscode";
        public const string AddressAttribute = "pl_address";
        public const string PhoneAttribute = "pl_phone";
        public const string MainOwnerAttribute = "pl_mainownerlookup";
        public const string NormalizedNameAttribute = "pl_normalizedname";

        private const int ActiveStateCode = 0;
        private const int TextMaxLength = 200;

        private static readonly HashSet<string> AllowedUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            TradingStatusAttribute,
            AddressAttribute,
            PhoneAttribute,
            // 主担当は承認設定で承認が不要なときだけ直接変えられる（2026-09-29ユーザー決定）。
            // 受け付けるときは、PartnerStandardUpdatePluginが新しい主担当の共有を整える。
            MainOwnerAttribute,
        };

        private static readonly HashSet<string> PlatformManagedUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            PrimaryIdAttribute,
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizedNameAttribute,
            "pl_industry",
            "pl_registeredbylookup",
            "pl_registrationkey",
            "pl_registrationcontenthash",
            "pl_registeredat",
            "pl_aclversion",
            "pl_sharesetupstatuscode",
            "ownerid",
            "statecode",
            "statuscode",
            "createdby",
            "createdon",
            "createdonbehalfby",
            "owningbusinessunit",
            "owningteam",
            "owninguser",
            "versionnumber",
        };

        public static PartnerStandardUpdateValidationResult Validate(
            Entity? target,
            Entity? preImage,
            Guid initiatingUserId,
            ApprovalSettingsReadResult settings)
        {
            var shape = ValidateTargetShape(target);
            if (!shape.IsValid) return shape;
            if (initiatingUserId == Guid.Empty)
                return Invalid(PartnerStandardUpdateValidationError.InitiatingUserRequired);
            if (preImage == null)
                return Invalid(PartnerStandardUpdateValidationError.PreImageRequired);
            if (!string.Equals(preImage.LogicalName, EntityName, StringComparison.Ordinal)
                || preImage.Id == Guid.Empty
                || preImage.Id != target!.Id)
            {
                return Invalid(PartnerStandardUpdateValidationError.PreImageEntityInvalid);
            }

            var state = preImage.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null) return Invalid(PartnerStandardUpdateValidationError.CurrentStateUnavailable);
            if (state.Value != ActiveStateCode) return Invalid(PartnerStandardUpdateValidationError.CurrentStateInvalid);
            if (settings == null || !settings.IsValid || settings.Settings == null)
                return Invalid(PartnerStandardUpdateValidationError.SettingsUnavailable);

            var values = ValidateAndNormalizeValues(target!);
            if (!values.IsValid) return values;

            var changedAttributes = target!.Attributes.Keys
                .Where(attributeName => AllowedUpdateAttributes.Contains(attributeName))
                .ToList();
            if (changedAttributes.Count == 0)
                return Invalid(PartnerStandardUpdateValidationError.UpdateEmpty);

            if (changedAttributes.Any(attributeName => IsApprovalRequired(attributeName, settings.Settings.Policy)))
                return Invalid(PartnerStandardUpdateValidationError.ApprovalRequired);

            return new PartnerStandardUpdateValidationResult
            {
                IsValid = true,
                Error = PartnerStandardUpdateValidationError.None,
                ChangedAttributes = changedAttributes,
            };
        }

        /// <summary>
        /// 承認反映のネストUpdate専用境界。ポリシー判定は反映サービス側で済ませ、
        /// ここでは許可列・型・派生列だけを二重検査する。
        /// </summary>
        public static void ValidateTrustedInternalTarget(Entity? target, string primaryEntityName)
        {
            if (target == null
                || !string.Equals(primaryEntityName, EntityName, StringComparison.Ordinal)
                || !string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal)
                || target.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("標準取引先Updateの内部対象が不正です。");
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedUpdateAttributes.Contains(attributeName)
                    || PlatformManagedUpdateAttributes.Contains(attributeName)
                    || attributeName == NormalizedNameAttribute)
                {
                    continue;
                }

                throw new InvalidPluginExecutionException("標準取引先Updateの内部対象に許可されていない列があります: " + attributeName);
            }

            var valueResult = ValidateAndNormalizeValues(target);
            if (!valueResult.IsValid)
                throw new InvalidPluginExecutionException("標準取引先Updateの内部値が不正です: " + valueResult.Error);
        }

        public static bool IsAllowedUpdateAttribute(string attributeName)
            => attributeName != null && AllowedUpdateAttributes.Contains(attributeName);

        public static bool IsForbiddenAuthorityAttribute(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static PartnerStandardUpdateValidationResult ValidateTargetShape(Entity? target)
        {
            if (target == null) return Invalid(PartnerStandardUpdateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
                return Invalid(PartnerStandardUpdateValidationError.TargetEntityInvalid);
            if (target.Id == Guid.Empty)
                return Invalid(PartnerStandardUpdateValidationError.TargetIdRequired);

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedUpdateAttributes.Contains(attributeName)
                    || PlatformManagedUpdateAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                    return Invalid(PartnerStandardUpdateValidationError.ForbiddenAuthorityInput);
                return Invalid(PartnerStandardUpdateValidationError.UnsupportedAttribute);
            }

            if (target.Attributes.Count == 0)
                return Invalid(PartnerStandardUpdateValidationError.UpdateEmpty);
            return Valid();
        }

        private static PartnerStandardUpdateValidationResult ValidateAndNormalizeValues(Entity target)
        {
            if (target.Attributes.TryGetValue(NameAttribute, out var nameValue))
            {
                if (!(nameValue is string name)) return Invalid(PartnerStandardUpdateValidationError.NameTypeInvalid);
                var normalized = name.Trim();
                if (normalized.Length == 0) return Invalid(PartnerStandardUpdateValidationError.NameRequired);
                if (normalized.Length > TextMaxLength) return Invalid(PartnerStandardUpdateValidationError.NameTooLong);
                target[NameAttribute] = normalized;
            }

            if (target.Attributes.TryGetValue(TradingStatusAttribute, out var tradingValue)
                && (!(tradingValue is OptionSetValue tradingStatus)
                    || tradingStatus.Value < 100000000
                    || tradingStatus.Value > 100000003))
            {
                return Invalid(PartnerStandardUpdateValidationError.TradingStatusValueInvalid);
            }

            if (target.Attributes.Contains(TradingStatusAttribute)
                && !(target[TradingStatusAttribute] is OptionSetValue))
            {
                return Invalid(PartnerStandardUpdateValidationError.TradingStatusTypeInvalid);
            }

            if (target.Attributes.TryGetValue(MainOwnerAttribute, out var mainOwnerValue)
                && (!(mainOwnerValue is EntityReference mainOwner)
                    || !string.Equals(mainOwner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
                    || mainOwner.Id == Guid.Empty))
            {
                return Invalid(PartnerStandardUpdateValidationError.MainOwnerTypeInvalid);
            }

            if (!ValidateOptionalText(target, AddressAttribute, PartnerStandardUpdateValidationError.AddressTypeInvalid, PartnerStandardUpdateValidationError.AddressTooLong))
                return Invalid(target[AddressAttribute] is string ? PartnerStandardUpdateValidationError.AddressTooLong : PartnerStandardUpdateValidationError.AddressTypeInvalid);
            if (!ValidateOptionalText(target, PhoneAttribute, PartnerStandardUpdateValidationError.PhoneTypeInvalid, PartnerStandardUpdateValidationError.PhoneTooLong))
                return Invalid(target[PhoneAttribute] is string ? PartnerStandardUpdateValidationError.PhoneTooLong : PartnerStandardUpdateValidationError.PhoneTypeInvalid);

            return Valid();
        }

        private static bool ValidateOptionalText(
            Entity target,
            string attributeName,
            PartnerStandardUpdateValidationError typeError,
            PartnerStandardUpdateValidationError lengthError)
        {
            if (!target.Attributes.Contains(attributeName)) return true;
            var value = target[attributeName];
            if (value == null)
            {
                return true;
            }
            if (!(value is string text)) return false;
            var normalized = text.Trim();
            if (normalized.Length > TextMaxLength) return false;
            target[attributeName] = normalized.Length == 0 ? null : normalized;
            return true;
        }

        private static bool IsApprovalRequired(string attributeName, ApprovalPolicy policy)
            => attributeName == NameAttribute ? policy.CompanyName
                : attributeName == TradingStatusAttribute ? policy.TradingStatus
                : attributeName == AddressAttribute ? policy.Address
                : attributeName == MainOwnerAttribute ? policy.MainOwner
                : attributeName == PhoneAttribute && policy.Phone;

        private static PartnerStandardUpdateValidationResult Valid()
            => new PartnerStandardUpdateValidationResult
            {
                IsValid = true,
                Error = PartnerStandardUpdateValidationError.None,
            };

        private static PartnerStandardUpdateValidationResult Invalid(PartnerStandardUpdateValidationError error)
            => new PartnerStandardUpdateValidationResult
            {
                IsValid = false,
                Error = error,
            };
    }

    public sealed class PartnerStandardUpdateValidationResult
    {
        public bool IsValid { get; internal set; }
        public PartnerStandardUpdateValidationError Error { get; internal set; }
        public IList<string> ChangedAttributes { get; internal set; } = new List<string>();
    }
}
