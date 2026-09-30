using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum ContactStandardCreateValidationError
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
        PartnerTypeInvalid,
        PartnerRequired,
        StatusTypeInvalid,
        StatusInvalid,
        DepartmentRoleTypeInvalid,
        DepartmentRoleTooLong,
        EmailTypeInvalid,
        EmailTooLong,
        PhoneTypeInvalid,
        PhoneTooLong,
        RegistrationKeyTypeInvalid,
        RegistrationKeyRequired,
        RegistrationKeyTooLong,
    }

    /// <summary>
    /// Standard Dataverse Create (pl_contact) の入力契約。
    ///
    /// 画面の必須指定だけに依存せず、標準CRUDのTargetへ届いた属性をサーバー側で
    /// allow-list検査する。登録者は標準createdby、登録日時はpl_registeredatへ
    /// サーバーが設定し、親取引先の可視性・状態はプラグイン側で検査する。
    /// </summary>
    public static class ContactStandardCreateContract
    {
        public const string EntityName = "pl_contact";
        public const string PrimaryIdAttribute = "pl_contactid";
        public const string NameAttribute = "pl_name";
        public const string PartnerLookupAttribute = "pl_partnerlookup";
        public const string StatusAttribute = "pl_statuscode";
        public const string DepartmentRoleAttribute = "pl_departmentrole";
        public const string EmailAttribute = "pl_email";
        public const string PhoneAttribute = "pl_phone";
        public const string RegisteredAtAttribute = "pl_registeredat";
        public const string RegistrationKeyAttribute = "pl_registrationkey";
        public const string OwnerAttribute = "ownerid";
        public const int DefaultStatusCode = 100000000;

        private const int NameMaxLength = 850;
        private const int OptionalTextMaxLength = 200;
        private const int RegistrationKeyMaxLength = 200;

        private static readonly HashSet<string> AllowedInputAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            PartnerLookupAttribute,
            StatusAttribute,
            DepartmentRoleAttribute,
            EmailAttribute,
            PhoneAttribute,
            RegistrationKeyAttribute,
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            RegisteredAtAttribute,
            PrimaryIdAttribute,
            OwnerAttribute,
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

        public static ContactStandardCreateValidationResult Validate(
            Entity? target,
            Guid initiatingUserId,
            DateTime registeredAtUtc)
        {
            if (target == null) return Invalid(ContactStandardCreateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(ContactStandardCreateValidationError.TargetEntityInvalid);
            }

            if (initiatingUserId == Guid.Empty)
            {
                return Invalid(ContactStandardCreateValidationError.InitiatingUserRequired);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedInputAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(ContactStandardCreateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(ContactStandardCreateValidationError.UnsupportedAttribute);
            }

            if (!TryGetString(target, NameAttribute, out var name, out var nameError))
            {
                return Invalid(nameError);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Invalid(ContactStandardCreateValidationError.NameRequired);
            }

            var normalizedName = (name ?? string.Empty).Trim();
            if (normalizedName.Length > NameMaxLength)
            {
                return Invalid(ContactStandardCreateValidationError.NameTooLong);
            }

            if (!TryGetPartnerReference(target, out var partnerId, out var partnerError))
            {
                return Invalid(partnerError);
            }

            if (!TryGetStatus(target, out var statusCode, out var statusError))
            {
                return Invalid(statusError);
            }

            if (!IsSupportedStatus(statusCode))
            {
                return Invalid(ContactStandardCreateValidationError.StatusInvalid);
            }

            if (!TryGetOptionalText(target, DepartmentRoleAttribute, OptionalTextMaxLength, out var departmentRole, out var departmentRoleError))
            {
                return Invalid(departmentRoleError);
            }

            if (!TryGetOptionalText(target, EmailAttribute, OptionalTextMaxLength, out var email, out var emailError))
            {
                return Invalid(emailError);
            }

            if (!TryGetOptionalText(target, PhoneAttribute, OptionalTextMaxLength, out var phone, out var phoneError))
            {
                return Invalid(phoneError);
            }

            if (!TryGetString(target, RegistrationKeyAttribute, out var registrationKey, out var keyError))
            {
                return Invalid(keyError);
            }

            if (string.IsNullOrWhiteSpace(registrationKey))
            {
                return Invalid(ContactStandardCreateValidationError.RegistrationKeyRequired);
            }

            var normalizedRegistrationKey = (registrationKey ?? string.Empty).Trim();
            if (normalizedRegistrationKey.Length > RegistrationKeyMaxLength)
            {
                return Invalid(ContactStandardCreateValidationError.RegistrationKeyTooLong);
            }

            return new ContactStandardCreateValidationResult
            {
                IsValid = true,
                Error = ContactStandardCreateValidationError.None,
                Value = new ValidatedContactStandardCreateInput
                {
                    Name = normalizedName,
                    PartnerId = partnerId,
                    StatusCode = statusCode,
                    DepartmentRole = departmentRole,
                    Email = email,
                    Phone = phone,
                    RegistrationKey = normalizedRegistrationKey,
                    RegisteredAtUtc = NormalizeUtc(registeredAtUtc),
                },
            };
        }

        /// <summary>
        /// 同じ要求キーを持つ既存行との比較。要求キーそのものの重複判定はAlternate Key
        /// が原子的に担い、ここでは同じ内容の再送か内容違いかを区別する。
        /// </summary>
        public static bool MatchesExisting(
            Entity existing,
            ValidatedContactStandardCreateInput input)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!string.Equals(existing.LogicalName, EntityName, StringComparison.Ordinal)) return false;

            var existingPartner = existing.GetAttributeValue<EntityReference>(PartnerLookupAttribute);
            var existingStatus = existing.GetAttributeValue<OptionSetValue>(StatusAttribute);
            return string.Equals(existing.GetAttributeValue<string>(NameAttribute), input.Name, StringComparison.Ordinal)
                && existingPartner != null
                && existingPartner.Id == input.PartnerId
                && existingStatus != null
                && existingStatus.Value == input.StatusCode
                && OptionalEquals(existing, DepartmentRoleAttribute, input.DepartmentRole)
                && OptionalEquals(existing, EmailAttribute, input.Email)
                && OptionalEquals(existing, PhoneAttribute, input.Phone)
                && string.Equals(existing.GetAttributeValue<string>(RegistrationKeyAttribute), input.RegistrationKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// ownerIdは登録実行者（呼び出し元が渡すinitiatingUserId）を設定する。親取引先の
        /// 所有者や主担当（pl_partnerlookupの先）とは独立。クライアントは担当者の所有者を
        /// 指定する経路を持たない（allow-listにownerid無し）。
        /// </summary>
        public static void ApplyServerDerivedAttributes(
            Entity target,
            ValidatedContactStandardCreateInput input,
            Guid ownerId)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (ownerId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("担当者の所有者が解決されていません。");
            }

            target[NameAttribute] = input.Name;
            target[PartnerLookupAttribute] = new EntityReference("pl_partner", input.PartnerId);
            target[StatusAttribute] = new OptionSetValue(input.StatusCode);
            target[RegistrationKeyAttribute] = input.RegistrationKey;
            target[RegisteredAtAttribute] = input.RegisteredAtUtc;
            target[OwnerAttribute] = new EntityReference("systemuser", ownerId);
            SetOptional(target, DepartmentRoleAttribute, input.DepartmentRole);
            SetOptional(target, EmailAttribute, input.Email);
            SetOptional(target, PhoneAttribute, input.Phone);
        }

        public static bool IsAllowedInputAttribute(string attributeName)
            => attributeName != null && AllowedInputAttributes.Contains(attributeName);

        public static bool IsForbiddenAuthorityAttribute(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static bool TryGetString(
            Entity target,
            string attributeName,
            out string? value,
            out ContactStandardCreateValidationError error)
        {
            value = null;
            error = ContactStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null) return true;
            if (!(target[attributeName] is string stringValue))
            {
                error = attributeName == NameAttribute
                    ? ContactStandardCreateValidationError.NameTypeInvalid
                    : attributeName == RegistrationKeyAttribute
                        ? ContactStandardCreateValidationError.RegistrationKeyTypeInvalid
                        : ContactStandardCreateValidationError.UnsupportedAttribute;
                return false;
            }

            value = stringValue;
            return true;
        }

        private static bool TryGetOptionalText(
            Entity target,
            string attributeName,
            int maxLength,
            out string? value,
            out ContactStandardCreateValidationError error)
        {
            value = null;
            error = ContactStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null) return true;
            if (!(target[attributeName] is string stringValue))
            {
                error = attributeName == DepartmentRoleAttribute
                    ? ContactStandardCreateValidationError.DepartmentRoleTypeInvalid
                    : attributeName == EmailAttribute
                        ? ContactStandardCreateValidationError.EmailTypeInvalid
                        : ContactStandardCreateValidationError.PhoneTypeInvalid;
                return false;
            }

            var normalized = stringValue.Trim();
            if (normalized.Length == 0) return true;
            if (normalized.Length > maxLength)
            {
                error = attributeName == DepartmentRoleAttribute
                    ? ContactStandardCreateValidationError.DepartmentRoleTooLong
                    : attributeName == EmailAttribute
                        ? ContactStandardCreateValidationError.EmailTooLong
                        : ContactStandardCreateValidationError.PhoneTooLong;
                return false;
            }

            value = normalized;
            return true;
        }

        private static bool TryGetPartnerReference(
            Entity target,
            out Guid partnerId,
            out ContactStandardCreateValidationError error)
        {
            partnerId = Guid.Empty;
            error = ContactStandardCreateValidationError.None;
            if (!target.Attributes.Contains(PartnerLookupAttribute) || target[PartnerLookupAttribute] == null)
            {
                error = ContactStandardCreateValidationError.PartnerRequired;
                return false;
            }

            if (!(target[PartnerLookupAttribute] is EntityReference reference)
                || reference.Id == Guid.Empty
                || !string.Equals(reference.LogicalName, "pl_partner", StringComparison.OrdinalIgnoreCase))
            {
                error = ContactStandardCreateValidationError.PartnerTypeInvalid;
                return false;
            }

            partnerId = reference.Id;
            return true;
        }

        private static bool TryGetStatus(
            Entity target,
            out int statusCode,
            out ContactStandardCreateValidationError error)
        {
            statusCode = DefaultStatusCode;
            error = ContactStandardCreateValidationError.None;
            if (!target.Attributes.Contains(StatusAttribute) || target[StatusAttribute] == null) return true;
            if (!(target[StatusAttribute] is OptionSetValue optionSet))
            {
                error = ContactStandardCreateValidationError.StatusTypeInvalid;
                return false;
            }

            statusCode = optionSet.Value;
            return true;
        }

        private static bool IsSupportedStatus(int statusCode)
            => statusCode == 100000000 || statusCode == 100000001 || statusCode == 100000002;

        private static bool OptionalEquals(Entity entity, string attributeName, string? expected)
        {
            var actual = entity.GetAttributeValue<string>(attributeName);
            return string.Equals(actual ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        }

        private static void SetOptional(Entity target, string attributeName, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Attributes.Remove(attributeName);
                return;
            }

            target[attributeName] = value;
        }

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        private static ContactStandardCreateValidationResult Invalid(ContactStandardCreateValidationError error)
            => new ContactStandardCreateValidationResult { IsValid = false, Error = error };
    }

    public sealed class ContactStandardCreateValidationResult
    {
        public bool IsValid { get; set; }
        public ContactStandardCreateValidationError Error { get; set; }
        public ValidatedContactStandardCreateInput? Value { get; set; }
    }

    public sealed class ValidatedContactStandardCreateInput
    {
        public string Name { get; set; } = string.Empty;
        public Guid PartnerId { get; set; }
        public int StatusCode { get; set; }
        public string? DepartmentRole { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string RegistrationKey { get; set; } = string.Empty;
        public DateTime RegisteredAtUtc { get; set; }
    }
}
