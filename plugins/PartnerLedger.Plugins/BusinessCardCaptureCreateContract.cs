using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum BusinessCardCaptureCreateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        BatchKeyTypeInvalid,
        BatchKeyRequired,
        BatchKeyTooLong,
        CaptureKeyTypeInvalid,
        CaptureKeyRequired,
        CaptureKeyTooLong,
        BatchLookupRequired,
        BatchLookupTypeInvalid,
        NameTypeInvalid,
        NameTooLong,
    }

    /// <summary>
    /// 名刺受付の標準Create境界。
    /// Batch／Captureの受付キーだけを呼出元から受け、状態・件数・受付時刻・入力版を
    /// サーバー側で導出する。画像バイトはCreateへ混ぜず、行Create成功後の標準File Uploadへ分離する。
    /// </summary>
    public static class BusinessCardCaptureCreateContract
    {
        public const string BatchEntityName = "pl_capturebatch";
        public const string CaptureEntityName = "pl_cardcapture";
        public const string BatchKeyAttribute = "pl_batchkey";
        public const string CaptureKeyAttribute = "pl_capturekey";
        public const string BatchLookupAttribute = "pl_capturebatchlookup";
        public const string NameAttribute = "pl_name";
        public const string BatchStatusAttribute = "pl_batchstatuscode";
        public const string CaptureStatusAttribute = "pl_capturestatuscode";
        public const string BatchItemCountAttribute = "pl_itemcount";
        public const string BatchCompletedCountAttribute = "pl_completedcount";
        public const string BatchRegisteredAtAttribute = "pl_registeredat";
        public const string CaptureReceivedAtAttribute = "pl_receivedat";
        public const string CaptureRegisteredAtAttribute = "pl_registeredat";
        public const string CaptureCurrentVersionAttribute = "pl_currentversionnumber";
        public const string BatchInitialStatus = "受付中";
        public const string CaptureInitialStatus = "未登録";

        private const int KeyMaxLength = 200;
        private const int NameMaxLength = 200;

        private static readonly HashSet<string> CommonForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ownerid",
            "statecode",
            "statuscode",
            "createdby",
            "createdon",
            "createdonbehalfby",
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
            "owningbusinessunit",
            "owningteam",
            "owninguser",
            "overriddencreatedon",
            "importsequencenumber",
            "timezoneruleversionnumber",
            "utcconversiontimezonecode",
        };

        private static readonly HashSet<string> BatchForbiddenAuthorityAttributes = new HashSet<string>(CommonForbiddenAuthorityAttributes, StringComparer.Ordinal)
        {
            BatchStatusAttribute,
            BatchItemCountAttribute,
            BatchCompletedCountAttribute,
            BatchRegisteredAtAttribute,
        };

        private static readonly HashSet<string> CaptureForbiddenAuthorityAttributes = new HashSet<string>(CommonForbiddenAuthorityAttributes, StringComparer.Ordinal)
        {
            CaptureStatusAttribute,
            CaptureReceivedAtAttribute,
            CaptureRegisteredAtAttribute,
            CaptureCurrentVersionAttribute,
            "pl_imagefile",
        };

        public static BusinessCardCaptureCreateValidationResult Validate(Entity? target)
        {
            if (target == null) return Invalid(BusinessCardCaptureCreateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, BatchEntityName, StringComparison.Ordinal)
                && !string.Equals(target.LogicalName, CaptureEntityName, StringComparison.Ordinal))
            {
                return Invalid(BusinessCardCaptureCreateValidationError.TargetEntityInvalid);
            }

            var forbidden = string.Equals(target.LogicalName, BatchEntityName, StringComparison.Ordinal)
                ? BatchForbiddenAuthorityAttributes
                : CaptureForbiddenAuthorityAttributes;
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (CommonForbiddenAuthorityAttributes.Contains(attributeName) || forbidden.Contains(attributeName))
                {
                    return Invalid(BusinessCardCaptureCreateValidationError.ForbiddenAuthorityInput);
                }

                if (string.Equals(target.LogicalName, BatchEntityName, StringComparison.Ordinal))
                {
                    if (!string.Equals(attributeName, BatchKeyAttribute, StringComparison.Ordinal)
                        && !string.Equals(attributeName, NameAttribute, StringComparison.Ordinal))
                    {
                        return Invalid(BusinessCardCaptureCreateValidationError.UnsupportedAttribute);
                    }
                }
                else if (!string.Equals(attributeName, CaptureKeyAttribute, StringComparison.Ordinal)
                    && !string.Equals(attributeName, BatchLookupAttribute, StringComparison.Ordinal)
                    && !string.Equals(attributeName, NameAttribute, StringComparison.Ordinal))
                {
                    return Invalid(BusinessCardCaptureCreateValidationError.UnsupportedAttribute);
                }
            }

            if (string.Equals(target.LogicalName, BatchEntityName, StringComparison.Ordinal))
            {
                return ValidateBatch(target);
            }

            return ValidateCapture(target);
        }

        public static void ApplyServerDerivedAttributes(Entity target, DateTime nowUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var result = Validate(target);
            if (!result.IsValid) throw new InvalidPluginExecutionException("名刺受付Createの入力が不正です: " + result.Error);

            var now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
            if (string.Equals(target.LogicalName, BatchEntityName, StringComparison.Ordinal))
            {
                var batchKey = NormalizeRequiredText(target, BatchKeyAttribute);
                target[BatchKeyAttribute] = batchKey;
                target[NameAttribute] = NormalizeOptionalName(target, $"名刺取込 {batchKey}");
                target[BatchStatusAttribute] = BatchInitialStatus;
                target[BatchItemCountAttribute] = 1;
                target[BatchCompletedCountAttribute] = 0;
                target[BatchRegisteredAtAttribute] = now;
                return;
            }

            var captureKey = NormalizeRequiredText(target, CaptureKeyAttribute);
            target[CaptureKeyAttribute] = captureKey;
            target[NameAttribute] = NormalizeOptionalName(target, $"名刺 {captureKey}");
            target[CaptureStatusAttribute] = CaptureInitialStatus;
            target[CaptureReceivedAtAttribute] = now;
            target[CaptureCurrentVersionAttribute] = 1;
        }

        private static BusinessCardCaptureCreateValidationResult ValidateBatch(Entity target)
        {
            if (!TryGetRequiredText(target, BatchKeyAttribute, out _, out var keyError)) return Invalid(keyError);
            if (!TryGetOptionalName(target, out _, out var nameError)) return Invalid(nameError);
            return Valid();
        }

        private static BusinessCardCaptureCreateValidationResult ValidateCapture(Entity target)
        {
            if (!TryGetRequiredText(target, CaptureKeyAttribute, out _, out var keyError)) return Invalid(keyError);
            if (!target.Attributes.Contains(BatchLookupAttribute) || target[BatchLookupAttribute] == null)
            {
                return Invalid(BusinessCardCaptureCreateValidationError.BatchLookupRequired);
            }

            if (!(target[BatchLookupAttribute] is EntityReference batchReference)
                || batchReference.Id == Guid.Empty
                || !string.Equals(batchReference.LogicalName, BatchEntityName, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(BusinessCardCaptureCreateValidationError.BatchLookupTypeInvalid);
            }

            if (!TryGetOptionalName(target, out _, out var nameError)) return Invalid(nameError);
            return Valid();
        }

        private static bool TryGetRequiredText(
            Entity target,
            string attributeName,
            out string? value,
            out BusinessCardCaptureCreateValidationError error)
        {
            value = null;
            error = BusinessCardCaptureCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                error = attributeName == BatchKeyAttribute
                    ? BusinessCardCaptureCreateValidationError.BatchKeyRequired
                    : BusinessCardCaptureCreateValidationError.CaptureKeyRequired;
                return false;
            }

            if (!(target[attributeName] is string stringValue))
            {
                error = attributeName == BatchKeyAttribute
                    ? BusinessCardCaptureCreateValidationError.BatchKeyTypeInvalid
                    : BusinessCardCaptureCreateValidationError.CaptureKeyTypeInvalid;
                return false;
            }

            value = stringValue.Trim();
            if (value.Length == 0)
            {
                error = attributeName == BatchKeyAttribute
                    ? BusinessCardCaptureCreateValidationError.BatchKeyRequired
                    : BusinessCardCaptureCreateValidationError.CaptureKeyRequired;
                return false;
            }

            if (value.Length > KeyMaxLength)
            {
                error = attributeName == BatchKeyAttribute
                    ? BusinessCardCaptureCreateValidationError.BatchKeyTooLong
                    : BusinessCardCaptureCreateValidationError.CaptureKeyTooLong;
                return false;
            }

            return true;
        }

        private static bool TryGetOptionalName(
            Entity target,
            out string? value,
            out BusinessCardCaptureCreateValidationError error)
        {
            value = null;
            error = BusinessCardCaptureCreateValidationError.None;
            if (!target.Attributes.Contains(NameAttribute) || target[NameAttribute] == null) return true;
            if (!(target[NameAttribute] is string stringValue))
            {
                error = BusinessCardCaptureCreateValidationError.NameTypeInvalid;
                return false;
            }

            value = stringValue.Trim();
            if (value.Length > NameMaxLength)
            {
                error = BusinessCardCaptureCreateValidationError.NameTooLong;
                return false;
            }

            return true;
        }

        private static string NormalizeRequiredText(Entity target, string attributeName)
            => ((string)target[attributeName]).Trim();

        private static string NormalizeOptionalName(Entity target, string fallback)
        {
            if (!target.Attributes.Contains(NameAttribute) || target[NameAttribute] == null) return fallback;
            var name = ((string)target[NameAttribute]).Trim();
            return name.Length == 0 ? fallback : name;
        }

        private static BusinessCardCaptureCreateValidationResult Valid()
            => new BusinessCardCaptureCreateValidationResult {IsValid = true, Error = BusinessCardCaptureCreateValidationError.None};

        private static BusinessCardCaptureCreateValidationResult Invalid(BusinessCardCaptureCreateValidationError error)
            => new BusinessCardCaptureCreateValidationResult {IsValid = false, Error = error};
    }

    public sealed class BusinessCardCaptureCreateValidationResult
    {
        public bool IsValid { get; internal set; }
        public BusinessCardCaptureCreateValidationError Error { get; internal set; }
    }
}
