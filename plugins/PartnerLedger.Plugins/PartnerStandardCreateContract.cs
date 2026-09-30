using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum PartnerStandardCreateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        InitiatingUserRequired,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        NameTypeInvalid,
        IndustryTypeInvalid,
        AddressTypeInvalid,
        PhoneTypeInvalid,
        MainOwnerTypeInvalid,
        MainOwnerRequired,
        RegistrationKeyTypeInvalid,
        RegistrationKeyRequired,
        RegistrationKeyTooLong,
        NameRequired,
        NameTooLong,
        NameInvalid,
        NormalizedEmpty,
        NormalizedTooLong,
        IndustryTooLong,
        AddressTooLong,
        PhoneTooLong,
    }

    /// <summary>
    /// Standard Dataverse Create (pl_partner) に渡されるTargetの境界。
    /// Custom APIのパラメータではなく、本人接続の標準CRUDを入口にする。
    ///
    /// クライアントから受け付けるのは業務入力と登録要求キーだけで、状態・登録者・
    /// 正規化名・ACL版・内容ハッシュはサーバー側で設定する。新しい列名は、クラウド
    /// スキーマ適用前の設計契約としてこのクラスに固定している。
    /// </summary>
    public static class PartnerStandardCreateContract
    {
        public const string EntityName = "pl_partner";
        public const string NameAttribute = "pl_name";
        public const string IndustryAttribute = "pl_industry";
        public const string AddressAttribute = "pl_address";
        public const string PhoneAttribute = "pl_phone";
        public const string MainOwnerAttribute = "pl_mainownerlookup";
        public const string OwnerAttribute = "ownerid";
        public const string RegistrationKeyAttribute = "pl_registrationkey";
        public const string RegistrationContentHashAttribute = "pl_registrationcontenthash";
        public const string NormalizedNameAttribute = "pl_normalizedname";
        public const string TradingStatusAttribute = "pl_tradingstatuscode";
        public const string RegisteredAtAttribute = "pl_registeredat";
        public const string RegisteredByAttribute = "pl_registeredbylookup";
        public const string AclVersionAttribute = "pl_aclversion";
        public const string ShareSetupStatusAttribute = "pl_sharesetupstatuscode";
        public const int InitialShareSetupStatusCode = 100000000;
        public const int ReadyShareSetupStatusCode = 100000001;

        private static readonly HashSet<string> AllowedInputAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            IndustryAttribute,
            AddressAttribute,
            PhoneAttribute,
            MainOwnerAttribute,
            RegistrationKeyAttribute,
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            RegistrationContentHashAttribute,
            NormalizedNameAttribute,
            TradingStatusAttribute,
            RegisteredAtAttribute,
            RegisteredByAttribute,
            AclVersionAttribute,
            ShareSetupStatusAttribute,
            OwnerAttribute,
            "statecode",
            "statuscode",
            "createdby",
            "createdon",
            "modifiedby",
            "modifiedon",
        };

        public static PartnerStandardCreateValidationResult Validate(
            Entity? target,
            Guid initiatingUserId,
            DateTime registeredAtUtc)
        {
            if (target == null)
            {
                return Invalid(PartnerStandardCreateValidationError.TargetRequired);
            }

            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(PartnerStandardCreateValidationError.TargetEntityInvalid);
            }

            if (initiatingUserId == Guid.Empty)
            {
                return Invalid(PartnerStandardCreateValidationError.InitiatingUserRequired);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedInputAttributes.Contains(attributeName))
                {
                    continue;
                }

                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(PartnerStandardCreateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(PartnerStandardCreateValidationError.UnsupportedAttribute);
            }

            if (!TryGetString(target, NameAttribute, out var name, out var nameTypeError))
            {
                return Invalid(nameTypeError);
            }

            if (!TryGetString(target, IndustryAttribute, out var industry, out var industryTypeError))
            {
                return Invalid(industryTypeError);
            }

            if (!TryGetString(target, AddressAttribute, out var address, out var addressTypeError))
            {
                return Invalid(addressTypeError);
            }

            if (!TryGetString(target, PhoneAttribute, out var phone, out var phoneTypeError))
            {
                return Invalid(phoneTypeError);
            }

            if (!TryGetString(target, RegistrationKeyAttribute, out var registrationKey, out var keyTypeError))
            {
                return Invalid(keyTypeError);
            }

            if (!target.Attributes.Contains(MainOwnerAttribute) || target[MainOwnerAttribute] == null)
            {
                return Invalid(PartnerStandardCreateValidationError.MainOwnerRequired);
            }

            if (!TryGetSystemUserReference(target, out var mainOwnerId))
            {
                return Invalid(PartnerStandardCreateValidationError.MainOwnerTypeInvalid);
            }

            var registration = PartnerRegistrationContract.Validate(
                initiatingUserId,
                new PartnerRegistrationInput
                {
                    Name = name ?? string.Empty,
                    Industry = industry,
                    Address = address,
                    Phone = phone,
                    MainOwnerId = mainOwnerId,
                    IdempotencyKey = registrationKey ?? string.Empty,
                });

            if (!registration.IsValid || registration.Value == null)
            {
                return Invalid(MapRegistrationError(registration.Error));
            }

            var normalizedRegisteredAt = NormalizeUtc(registeredAtUtc);
            return new PartnerStandardCreateValidationResult
            {
                IsValid = true,
                Error = PartnerStandardCreateValidationError.None,
                Value = new ValidatedPartnerStandardCreateInput
                {
                    Registration = registration.Value,
                    RegisteredAtUtc = normalizedRegisteredAt,
                    ContentHash = PartnerRegistrationFingerprint.Compute(registration.Value),
                },
            };
        }

        /// <summary>
        /// PreOperationでTargetへサーバー導出値を適用する。Validateが成功した結果だけを受け、
        /// クライアントから渡された権威値を採用しない。Targetを別Createすることはない。
        ///
        /// owneridは主担当（pl_mainownerlookup、業務項目）ではなく登録実行者を設定する。
        /// 主担当は作成者と別人になり得るため、owneridを主担当に合わせると作成者が
        /// 自分で作った行への権限を失い、再送検知も作成者の権限で動くため機能しなくなる
        /// （独立監査で確認）。Dataverse標準Createがownerid省略時に呼び出しユーザーへ
        /// 自動割当てする挙動を明示化するだけで、新しい挙動は導入しない。
        /// </summary>
        public static void ApplyServerDerivedAttributes(
            Entity target,
            ValidatedPartnerStandardCreateInput value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (value == null) throw new ArgumentNullException(nameof(value));

            var registration = value.Registration;
            target[NameAttribute] = registration.Name;
            target[MainOwnerAttribute] = new EntityReference("systemuser", registration.MainOwnerId);
            target[OwnerAttribute] = new EntityReference("systemuser", registration.InitiatingUserId);
            target[RegistrationKeyAttribute] = registration.IdempotencyKey;
            target[NormalizedNameAttribute] = registration.NormalizedName;
            target[TradingStatusAttribute] = new OptionSetValue(ValidatedPartnerRegistrationInput.InitialTradingStatusCode);
            target[RegisteredAtAttribute] = value.RegisteredAtUtc;
            target[RegisteredByAttribute] = new EntityReference("systemuser", registration.InitiatingUserId);
            target[AclVersionAttribute] = PartnerRegistrationRecordFactory.InitialAclVersion;
            target[ShareSetupStatusAttribute] = new OptionSetValue(InitialShareSetupStatusCode);
            target[RegistrationContentHashAttribute] = value.ContentHash;

            SetOptional(target, IndustryAttribute, registration.Industry);
            SetOptional(target, AddressAttribute, registration.Address);
            SetOptional(target, PhoneAttribute, registration.Phone);
        }

        /// <summary>
        /// Targetを検査し、不正なら例外で拒否、正しければ同じTargetへサーバー導出値を設定する。
        /// pl_partner Createの検査（PartnerCreateGuardPlugin）から呼ぶ。
        /// </summary>
        public static void ValidateAndApply(Entity? target, Guid initiatingUserId, DateTime registeredAtUtc)
        {
            var result = Validate(target, initiatingUserId, registeredAtUtc);
            if (!result.IsValid || result.Value == null)
            {
                throw new InvalidPluginExecutionException(
                    "標準取引先Createの入力が不正です: " + result.Error);
            }

            ApplyServerDerivedAttributes(target!, result.Value);
        }

        public static bool IsAllowedInputAttribute(string attributeName)
            => attributeName != null && AllowedInputAttributes.Contains(attributeName);

        public static bool IsForbiddenAuthorityAttribute(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static bool TryGetString(
            Entity target,
            string attributeName,
            out string? value,
            out PartnerStandardCreateValidationError error)
        {
            value = null;
            error = PartnerStandardCreateValidationError.None;
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
            {
                return true;
            }

            if (!(target[attributeName] is string stringValue))
            {
                error = attributeName == NameAttribute
                    ? PartnerStandardCreateValidationError.NameTypeInvalid
                    : attributeName == IndustryAttribute
                        ? PartnerStandardCreateValidationError.IndustryTypeInvalid
                        : attributeName == AddressAttribute
                            ? PartnerStandardCreateValidationError.AddressTypeInvalid
                            : attributeName == PhoneAttribute
                                ? PartnerStandardCreateValidationError.PhoneTypeInvalid
                                : PartnerStandardCreateValidationError.RegistrationKeyTypeInvalid;
                return false;
            }

            value = stringValue;
            return true;
        }

        private static bool TryGetSystemUserReference(Entity target, out Guid mainOwnerId)
        {
            mainOwnerId = Guid.Empty;
            if (!target.Attributes.Contains(MainOwnerAttribute) || target[MainOwnerAttribute] == null)
            {
                return false;
            }

            if (!(target[MainOwnerAttribute] is EntityReference reference)
                || reference.Id == Guid.Empty
                || !string.Equals(reference.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            mainOwnerId = reference.Id;
            return true;
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

        private static PartnerStandardCreateValidationError MapRegistrationError(
            PartnerRegistrationValidationError error)
            => error switch
            {
                PartnerRegistrationValidationError.InitiatingUserRequired => PartnerStandardCreateValidationError.InitiatingUserRequired,
                PartnerRegistrationValidationError.MainOwnerRequired => PartnerStandardCreateValidationError.MainOwnerRequired,
                PartnerRegistrationValidationError.NameRequired => PartnerStandardCreateValidationError.NameRequired,
                PartnerRegistrationValidationError.NameTooLong => PartnerStandardCreateValidationError.NameTooLong,
                PartnerRegistrationValidationError.NameInvalid => PartnerStandardCreateValidationError.NameInvalid,
                PartnerRegistrationValidationError.NormalizedEmpty => PartnerStandardCreateValidationError.NormalizedEmpty,
                PartnerRegistrationValidationError.NormalizedTooLong => PartnerStandardCreateValidationError.NormalizedTooLong,
                PartnerRegistrationValidationError.IdempotencyKeyRequired => PartnerStandardCreateValidationError.RegistrationKeyRequired,
                PartnerRegistrationValidationError.IdempotencyKeyTooLong => PartnerStandardCreateValidationError.RegistrationKeyTooLong,
                PartnerRegistrationValidationError.IndustryTooLong => PartnerStandardCreateValidationError.IndustryTooLong,
                PartnerRegistrationValidationError.AddressTooLong => PartnerStandardCreateValidationError.AddressTooLong,
                PartnerRegistrationValidationError.PhoneTooLong => PartnerStandardCreateValidationError.PhoneTooLong,
                _ => PartnerStandardCreateValidationError.UnsupportedAttribute,
            };

        private static PartnerStandardCreateValidationResult Invalid(
            PartnerStandardCreateValidationError error)
            => new PartnerStandardCreateValidationResult
            {
                IsValid = false,
                Error = error,
            };

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    public sealed class ValidatedPartnerStandardCreateInput
    {
        public ValidatedPartnerRegistrationInput Registration { get; internal set; } = null!;
        public DateTime RegisteredAtUtc { get; internal set; }
        public string ContentHash { get; internal set; } = string.Empty;
    }

    public sealed class PartnerStandardCreateValidationResult
    {
        public bool IsValid { get; internal set; }
        public PartnerStandardCreateValidationError Error { get; internal set; }
        public ValidatedPartnerStandardCreateInput? Value { get; internal set; }
    }
}
