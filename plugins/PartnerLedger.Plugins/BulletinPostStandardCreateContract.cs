using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum BulletinPostStandardCreateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        InitiatingUserRequired,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        BodyTypeInvalid,
        BodyRequired,
        BodyTooLong,
        RequestKeyTypeInvalid,
        RequestKeyRequired,
        RequestKeyTooLong,
        PartnerRequired,
        PartnerTypeInvalid,
        ContactKeyTypeInvalid,
        ContactKeyInvalid,
    }

    /// <summary>
    /// 標準 pl_bulletinpost Create の入力契約。
    /// 掲示板は追記専用とし、作成者・登録日時・表示名はサーバーが導出する。
    /// </summary>
    public static class BulletinPostStandardCreateContract
    {
        public const string EntityName = "pl_bulletinpost";
        public const string PrimaryIdAttribute = "pl_bulletinpostid";
        public const string NameAttribute = "pl_name";
        public const string BodyAttribute = "pl_body";
        public const string RegisteredAtAttribute = "pl_registeredat";
        public const string RequestKeyAttribute = "pl_requestkey";
        public const string PartnerLookupAttribute = "pl_partnerlookup";
        public const string ContactKeyAttribute = "pl_contactkey";

        private const int BodyMaxLength = 200;
        private const int RequestKeyMaxLength = 200;

        private static readonly HashSet<string> AllowedInputAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            BodyAttribute,
            RequestKeyAttribute,
            PartnerLookupAttribute,
            ContactKeyAttribute,
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            RegisteredAtAttribute,
            PrimaryIdAttribute,
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

        public static BulletinPostStandardCreateValidationResult Validate(Entity? target, Guid initiatingUserId)
        {
            if (target == null) return Invalid(BulletinPostStandardCreateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(BulletinPostStandardCreateValidationError.TargetEntityInvalid);
            }

            if (initiatingUserId == Guid.Empty)
            {
                return Invalid(BulletinPostStandardCreateValidationError.InitiatingUserRequired);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedInputAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(BulletinPostStandardCreateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(BulletinPostStandardCreateValidationError.UnsupportedAttribute);
            }

            if (!TryGetRequiredText(target, BodyAttribute, BodyMaxLength, out var body, out var bodyError))
            {
                return Invalid(bodyError);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Invalid(BulletinPostStandardCreateValidationError.BodyRequired);
            }

            if (!TryGetRequiredText(target, RequestKeyAttribute, RequestKeyMaxLength, out var requestKey, out var requestKeyError))
            {
                return Invalid(requestKeyError);
            }

            if (string.IsNullOrWhiteSpace(requestKey))
            {
                return Invalid(BulletinPostStandardCreateValidationError.RequestKeyRequired);
            }

            if (!TryGetPartnerReference(target, out var partnerId, out var partnerError))
            {
                return Invalid(partnerError);
            }

            if (!TryGetOptionalContactKey(target, out var contactId, out var contactError))
            {
                return Invalid(contactError);
            }

            return new BulletinPostStandardCreateValidationResult
            {
                IsValid = true,
                Error = BulletinPostStandardCreateValidationError.None,
                Value = new ValidatedBulletinPostStandardCreateInput
                {
                    Body = body!,
                    RequestKey = requestKey!,
                    PartnerId = partnerId,
                    ContactId = contactId,
                },
            };
        }

        public static bool MatchesExisting(Entity existing, ValidatedBulletinPostStandardCreateInput input)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!string.Equals(existing.LogicalName, EntityName, StringComparison.Ordinal)) return false;

            var partner = existing.GetAttributeValue<EntityReference>(PartnerLookupAttribute);
            var existingContactKey = NormalizeContactKey(existing.GetAttributeValue<string>(ContactKeyAttribute));
            return string.Equals(existing.GetAttributeValue<string>(BodyAttribute), input.Body, StringComparison.Ordinal)
                && string.Equals(existing.GetAttributeValue<string>(RequestKeyAttribute), input.RequestKey, StringComparison.Ordinal)
                && partner != null && partner.Id == input.PartnerId
                && existingContactKey == NormalizeContactKey(input.ContactId?.ToString("D"));
        }

        public static void ApplyServerDerivedAttributes(Entity target, ValidatedBulletinPostStandardCreateInput input, DateTime registeredAtUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (input == null) throw new ArgumentNullException(nameof(input));

            target[BodyAttribute] = input.Body;
            target[RequestKeyAttribute] = input.RequestKey;
            target[PartnerLookupAttribute] = new EntityReference("pl_partner", input.PartnerId);
            target[NameAttribute] = "掲示板投稿-" + input.RequestKey;
            target[RegisteredAtAttribute] = NormalizeUtc(registeredAtUtc);
            if (input.ContactId.HasValue)
            {
                target[ContactKeyAttribute] = input.ContactId.Value.ToString("D");
            }
            else
            {
                target.Attributes.Remove(ContactKeyAttribute);
            }
        }

        public static bool IsAllowedInputAttribute(string attributeName)
            => attributeName != null && AllowedInputAttributes.Contains(attributeName);

        public static bool IsForbiddenAuthorityAttribute(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static BulletinPostStandardCreateValidationResult Invalid(BulletinPostStandardCreateValidationError error)
            => new BulletinPostStandardCreateValidationResult { IsValid = false, Error = error };

        private static bool TryGetRequiredText(
            Entity target,
            string attributeName,
            int maxLength,
            out string? value,
            out BulletinPostStandardCreateValidationError error)
        {
            value = null;
            error = BulletinPostStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                error = attributeName == BodyAttribute
                    ? BulletinPostStandardCreateValidationError.BodyRequired
                    : BulletinPostStandardCreateValidationError.RequestKeyRequired;
                return false;
            }

            if (!(target[attributeName] is string text))
            {
                error = attributeName == BodyAttribute
                    ? BulletinPostStandardCreateValidationError.BodyTypeInvalid
                    : BulletinPostStandardCreateValidationError.RequestKeyTypeInvalid;
                return false;
            }

            value = text.Trim();
            if (value.Length > maxLength)
            {
                error = attributeName == BodyAttribute
                    ? BulletinPostStandardCreateValidationError.BodyTooLong
                    : BulletinPostStandardCreateValidationError.RequestKeyTooLong;
                return false;
            }

            return true;
        }

        private static bool TryGetPartnerReference(
            Entity target,
            out Guid partnerId,
            out BulletinPostStandardCreateValidationError error)
        {
            partnerId = Guid.Empty;
            error = BulletinPostStandardCreateValidationError.None;
            if (!target.Attributes.Contains(PartnerLookupAttribute) || target[PartnerLookupAttribute] == null)
            {
                error = BulletinPostStandardCreateValidationError.PartnerRequired;
                return false;
            }

            if (!(target[PartnerLookupAttribute] is EntityReference reference)
                || !string.Equals(reference.LogicalName, "pl_partner", StringComparison.Ordinal)
                || reference.Id == Guid.Empty)
            {
                error = BulletinPostStandardCreateValidationError.PartnerTypeInvalid;
                return false;
            }

            partnerId = reference.Id;
            return true;
        }

        private static bool TryGetOptionalContactKey(
            Entity target,
            out Guid? contactId,
            out BulletinPostStandardCreateValidationError error)
        {
            contactId = null;
            error = BulletinPostStandardCreateValidationError.None;
            if (!target.Attributes.Contains(ContactKeyAttribute) || target[ContactKeyAttribute] == null)
            {
                return true;
            }

            if (!(target[ContactKeyAttribute] is string text))
            {
                error = BulletinPostStandardCreateValidationError.ContactKeyTypeInvalid;
                return false;
            }

            var normalized = text.Trim();
            if (normalized.Length == 0) return true;
            if (!Guid.TryParse(normalized, out var parsed) || parsed == Guid.Empty)
            {
                error = BulletinPostStandardCreateValidationError.ContactKeyInvalid;
                return false;
            }

            contactId = parsed;
            return true;
        }

        private static string? NormalizeContactKey(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value!.Trim().ToLowerInvariant();

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    public sealed class BulletinPostStandardCreateValidationResult
    {
        public bool IsValid { get; set; }
        public BulletinPostStandardCreateValidationError Error { get; set; }
        public ValidatedBulletinPostStandardCreateInput? Value { get; set; }
    }

    public sealed class ValidatedBulletinPostStandardCreateInput
    {
        public string Body { get; set; } = string.Empty;
        public string RequestKey { get; set; } = string.Empty;
        public Guid PartnerId { get; set; }
        public Guid? ContactId { get; set; }
    }
}
