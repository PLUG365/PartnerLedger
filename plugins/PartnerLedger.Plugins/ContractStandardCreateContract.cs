using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum ContractStandardCreateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        InitiatingUserRequired,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        NameTypeInvalid,
        NameRequired,
        NameTooLong,
        PartnerRequired,
        PartnerTypeInvalid,
        ContractTypeRequired,
        ContractTypeTypeInvalid,
        EndDateTypeInvalid,
        NoticeDateTypeInvalid,
        DecisionDateTypeInvalid,
        AutoRenewTypeInvalid,
        LinkTypeInvalid,
        LinkTooLong,
        RegistrationKeyTypeInvalid,
        RegistrationKeyRequired,
        RegistrationKeyTooLong,
    }

    /// <summary>
    /// Standard Dataverse Create (pl_contract) の最小入力契約。
    /// 初回登録の状態と関連行の有効性は、画面ではなくサーバー側で固定する。
    /// </summary>
    public static class ContractStandardCreateContract
    {
        public const string EntityName = "pl_contract";
        public const string PrimaryIdAttribute = "pl_contractid";
        public const string NameAttribute = "pl_name";
        public const string PartnerLookupAttribute = "pl_partnerlookup";
        public const string ContractTypeLookupAttribute = "pl_contracttypelookup";
        public const string ContractStatusAttribute = "pl_contractstatuscode";
        public const string EndDateAttribute = "pl_enddate";
        public const string NoticeDateAttribute = "pl_noticedate";
        public const string DecisionDateAttribute = "pl_decisiondate";
        public const string AutoRenewAttribute = "pl_autorenew";
        public const string LinkAttribute = "pl_link";
        public const string RegistrationKeyAttribute = "pl_registrationkey";
        public const int DefaultContractStatusCode = 100000000;

        private const int TextMaxLength = 200;

        private static readonly HashSet<string> AllowedInputAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            PartnerLookupAttribute,
            ContractTypeLookupAttribute,
            EndDateAttribute,
            NoticeDateAttribute,
            DecisionDateAttribute,
            AutoRenewAttribute,
            LinkAttribute,
            RegistrationKeyAttribute,
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            ContractStatusAttribute,
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

        public static ContractStandardCreateValidationResult Validate(Entity? target, Guid initiatingUserId)
        {
            if (target == null) return Invalid(ContractStandardCreateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(ContractStandardCreateValidationError.TargetEntityInvalid);
            }

            if (initiatingUserId == Guid.Empty)
            {
                return Invalid(ContractStandardCreateValidationError.InitiatingUserRequired);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedInputAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(ContractStandardCreateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(ContractStandardCreateValidationError.UnsupportedAttribute);
            }

            if (!TryGetRequiredText(target, NameAttribute, TextMaxLength, out var name, out var nameError))
            {
                return Invalid(nameError);
            }

            if (string.IsNullOrWhiteSpace(name)) return Invalid(ContractStandardCreateValidationError.NameRequired);

            if (!TryGetLookup(target, PartnerLookupAttribute, "pl_partner", out var partnerId, out var partnerError))
            {
                return Invalid(partnerError);
            }

            if (!TryGetLookup(target, ContractTypeLookupAttribute, "pl_contracttype", out var contractTypeId, out var contractTypeError))
            {
                return Invalid(contractTypeError);
            }

            if (!TryGetOptionalDate(target, EndDateAttribute, out var endDate, out var endDateError))
            {
                return Invalid(endDateError);
            }

            if (!TryGetOptionalDate(target, NoticeDateAttribute, out var noticeDate, out var noticeDateError))
            {
                return Invalid(noticeDateError);
            }

            if (!TryGetOptionalDate(target, DecisionDateAttribute, out var decisionDate, out var decisionDateError))
            {
                return Invalid(decisionDateError);
            }

            if (!TryGetOptionalBoolean(target, AutoRenewAttribute, out var autoRenew, out var autoRenewError))
            {
                return Invalid(autoRenewError);
            }

            if (!TryGetOptionalText(target, LinkAttribute, TextMaxLength, out var link, out var linkError))
            {
                return Invalid(linkError);
            }

            if (!TryGetRequiredText(target, RegistrationKeyAttribute, TextMaxLength, out var registrationKey, out var keyError))
            {
                return Invalid(keyError);
            }

            if (string.IsNullOrWhiteSpace(registrationKey)) return Invalid(ContractStandardCreateValidationError.RegistrationKeyRequired);

            return new ContractStandardCreateValidationResult
            {
                IsValid = true,
                Error = ContractStandardCreateValidationError.None,
                Value = new ValidatedContractStandardCreateInput
                {
                    Name = name!,
                    PartnerId = partnerId,
                    ContractTypeId = contractTypeId,
                    EndDate = endDate,
                    NoticeDate = noticeDate,
                    DecisionDate = decisionDate,
                    AutoRenew = autoRenew,
                    Link = link,
                    RegistrationKey = registrationKey!,
                },
            };
        }

        public static bool MatchesExisting(Entity existing, ValidatedContractStandardCreateInput input)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!string.Equals(existing.LogicalName, EntityName, StringComparison.Ordinal)) return false;

            var partner = existing.GetAttributeValue<EntityReference>(PartnerLookupAttribute);
            var contractType = existing.GetAttributeValue<EntityReference>(ContractTypeLookupAttribute);
            var status = existing.GetAttributeValue<OptionSetValue>(ContractStatusAttribute);
            return string.Equals(existing.GetAttributeValue<string>(NameAttribute), input.Name, StringComparison.Ordinal)
                && partner != null && partner.Id == input.PartnerId
                && contractType != null && contractType.Id == input.ContractTypeId
                && status != null && status.Value == DefaultContractStatusCode
                && OptionalDateEquals(existing, EndDateAttribute, input.EndDate)
                && OptionalDateEquals(existing, NoticeDateAttribute, input.NoticeDate)
                && OptionalDateEquals(existing, DecisionDateAttribute, input.DecisionDate)
                && OptionalBooleanEquals(existing, AutoRenewAttribute, input.AutoRenew)
                && OptionalTextEquals(existing, LinkAttribute, input.Link)
                && string.Equals(existing.GetAttributeValue<string>(RegistrationKeyAttribute), input.RegistrationKey, StringComparison.Ordinal);
        }

        public static void ApplyServerDerivedAttributes(Entity target, ValidatedContractStandardCreateInput input)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (input == null) throw new ArgumentNullException(nameof(input));

            target[NameAttribute] = input.Name;
            target[PartnerLookupAttribute] = new EntityReference("pl_partner", input.PartnerId);
            target[ContractTypeLookupAttribute] = new EntityReference("pl_contracttype", input.ContractTypeId);
            target[ContractStatusAttribute] = new OptionSetValue(DefaultContractStatusCode);
            target[AutoRenewAttribute] = input.AutoRenew;
            target[RegistrationKeyAttribute] = input.RegistrationKey;
            SetOptionalDate(target, EndDateAttribute, input.EndDate);
            SetOptionalDate(target, NoticeDateAttribute, input.NoticeDate);
            SetOptionalDate(target, DecisionDateAttribute, input.DecisionDate);
            SetOptionalText(target, LinkAttribute, input.Link);
        }

        public static bool IsForbiddenAuthorityInput(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static bool TryGetRequiredText(Entity target, string attributeName, int maxLength, out string? value, out ContractStandardCreateValidationError error)
        {
            value = null;
            error = ContractStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                error = attributeName == NameAttribute ? ContractStandardCreateValidationError.NameRequired : ContractStandardCreateValidationError.RegistrationKeyRequired;
                return false;
            }

            if (!(target[attributeName] is string stringValue))
            {
                error = attributeName == NameAttribute ? ContractStandardCreateValidationError.NameTypeInvalid : ContractStandardCreateValidationError.RegistrationKeyTypeInvalid;
                return false;
            }

            value = stringValue.Trim();
            if (value.Length > maxLength)
            {
                error = attributeName == NameAttribute ? ContractStandardCreateValidationError.NameTooLong : ContractStandardCreateValidationError.RegistrationKeyTooLong;
                return false;
            }

            return true;
        }

        private static bool TryGetOptionalText(Entity target, string attributeName, int maxLength, out string? value, out ContractStandardCreateValidationError error)
        {
            value = null;
            error = ContractStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null) return true;
            if (!(target[attributeName] is string stringValue))
            {
                error = ContractStandardCreateValidationError.LinkTypeInvalid;
                return false;
            }

            var normalized = stringValue.Trim();
            if (normalized.Length == 0) return true;
            if (normalized.Length > maxLength)
            {
                error = ContractStandardCreateValidationError.LinkTooLong;
                return false;
            }

            value = normalized;
            return true;
        }

        private static bool TryGetLookup(Entity target, string attributeName, string logicalName, out Guid id, out ContractStandardCreateValidationError error)
        {
            id = Guid.Empty;
            error = ContractStandardCreateValidationError.None;
            var missingError = attributeName == PartnerLookupAttribute ? ContractStandardCreateValidationError.PartnerRequired : ContractStandardCreateValidationError.ContractTypeRequired;
            var invalidError = attributeName == PartnerLookupAttribute ? ContractStandardCreateValidationError.PartnerTypeInvalid : ContractStandardCreateValidationError.ContractTypeTypeInvalid;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                error = missingError;
                return false;
            }

            if (!(target[attributeName] is EntityReference reference)
                || reference.Id == Guid.Empty
                || !string.Equals(reference.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase))
            {
                error = invalidError;
                return false;
            }

            id = reference.Id;
            return true;
        }

        private static bool TryGetOptionalDate(Entity target, string attributeName, out DateTime? value, out ContractStandardCreateValidationError error)
        {
            value = null;
            error = ContractStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null) return true;
            if (!(target[attributeName] is DateTime date))
            {
                error = attributeName == EndDateAttribute ? ContractStandardCreateValidationError.EndDateTypeInvalid : attributeName == NoticeDateAttribute ? ContractStandardCreateValidationError.NoticeDateTypeInvalid : ContractStandardCreateValidationError.DecisionDateTypeInvalid;
                return false;
            }

            value = date;
            return true;
        }

        private static bool TryGetOptionalBoolean(Entity target, string attributeName, out bool value, out ContractStandardCreateValidationError error)
        {
            value = false;
            error = ContractStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null) return true;
            if (!(target[attributeName] is bool booleanValue))
            {
                error = ContractStandardCreateValidationError.AutoRenewTypeInvalid;
                return false;
            }

            value = booleanValue;
            return true;
        }

        private static bool OptionalDateEquals(Entity entity, string attributeName, DateTime? expected)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var value) || value == null) return expected == null;
            return value is DateTime actual && expected.HasValue && actual.Date == expected.Value.Date;
        }

        private static bool OptionalBooleanEquals(Entity entity, string attributeName, bool expected)
            => !entity.Attributes.TryGetValue(attributeName, out var value) || value == null ? !expected : value is bool actual && actual == expected;

        private static bool OptionalTextEquals(Entity entity, string attributeName, string? expected)
        {
            var actual = entity.GetAttributeValue<string>(attributeName);
            return string.Equals(actual ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        }

        private static void SetOptionalDate(Entity target, string attributeName, DateTime? value)
        {
            if (value.HasValue) target[attributeName] = value.Value;
            else target.Attributes.Remove(attributeName);
        }

        private static void SetOptionalText(Entity target, string attributeName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) target.Attributes.Remove(attributeName);
            else target[attributeName] = value;
        }

        private static ContractStandardCreateValidationResult Invalid(ContractStandardCreateValidationError error)
            => new ContractStandardCreateValidationResult {IsValid = false, Error = error};
    }

    public sealed class ContractStandardCreateValidationResult
    {
        public bool IsValid { get; set; }
        public ContractStandardCreateValidationError Error { get; set; }
        public ValidatedContractStandardCreateInput? Value { get; set; }
    }

    public sealed class ValidatedContractStandardCreateInput
    {
        public string Name { get; set; } = string.Empty;
        public Guid PartnerId { get; set; }
        public Guid ContractTypeId { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? NoticeDate { get; set; }
        public DateTime? DecisionDate { get; set; }
        public bool AutoRenew { get; set; }
        public string? Link { get; set; }
        public string RegistrationKey { get; set; } = string.Empty;
    }
}
