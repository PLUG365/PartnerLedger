using System;

namespace PartnerLedger.Plugins
{
    public enum PartnerRegistrationValidationError
    {
        None,
        InitiatingUserRequired,
        MainOwnerRequired,
        NameRequired,
        NameTooLong,
        NameInvalid,
        NormalizedEmpty,
        NormalizedTooLong,
        IdempotencyKeyRequired,
        IdempotencyKeyTooLong,
        IndustryTooLong,
        AddressTooLong,
        PhoneTooLong,
    }

    /// <summary>
    /// 新規取引先登録APIが受け取る入力のローカル契約。
    /// 登録者・登録日時・状態・ACL版・Dataverse所有者は入力に含めず、サーバー側で導出する。
    /// このクラスはまだDataverseへ書き込まず、Custom API登録前の境界を固定する。
    /// </summary>
    public sealed class PartnerRegistrationInput
    {
        public string Name { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public Guid? MainOwnerId { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    public sealed class ValidatedPartnerRegistrationInput
    {
        public Guid InitiatingUserId { get; internal set; }
        public Guid MainOwnerId { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public string NormalizedName { get; internal set; } = string.Empty;
        public string? Industry { get; internal set; }
        public string? Address { get; internal set; }
        public string? Phone { get; internal set; }
        public string IdempotencyKey { get; internal set; } = string.Empty;

        // Dataverseへの登録時にサーバーが常に設定する初期値。
        public const int InitialTradingStatusCode = 100000000;
    }

    public sealed class PartnerRegistrationValidationResult
    {
        public bool IsValid { get; internal set; }
        public PartnerRegistrationValidationError Error { get; internal set; }
        public ValidatedPartnerRegistrationInput? Value { get; internal set; }
    }

    public static class PartnerRegistrationContract
    {
        public const int MaxIndustryLength = 200;
        public const int MaxAddressLength = 500;
        public const int MaxPhoneLength = 200;
        public const int MaxIdempotencyKeyLength = 200;

        private static readonly string[] ForbiddenAuthorityInputs =
        {
            "TradingStatusCode",
            "RegisteredAt",
            "RegisteredById",
            "OwnerId",
            "AclVersion",
            "NormalizedName",
            "StateCode",
            "StatusCode",
            "PartnerId",
        };

        public static PartnerRegistrationValidationResult Validate(
            Guid initiatingUserId,
            PartnerRegistrationInput? input)
        {
            if (initiatingUserId == Guid.Empty)
                return Invalid(PartnerRegistrationValidationError.InitiatingUserRequired);
            if (input == null)
                return Invalid(PartnerRegistrationValidationError.NameRequired);
            if (!input.MainOwnerId.HasValue || input.MainOwnerId.Value == Guid.Empty)
                return Invalid(PartnerRegistrationValidationError.MainOwnerRequired);
            if (!PartnerNameNormalizer.TryNormalize(input.Name, out var normalizedName, out var nameError))
                return Invalid(MapNameError(nameError));

            var idempotencyKey = NormalizeRequired(input.IdempotencyKey);
            if (idempotencyKey == null)
                return Invalid(PartnerRegistrationValidationError.IdempotencyKeyRequired);
            if (idempotencyKey.Length > MaxIdempotencyKeyLength)
                return Invalid(PartnerRegistrationValidationError.IdempotencyKeyTooLong);

            var industry = NormalizeOptional(input.Industry);
            if (industry != null && industry.Length > MaxIndustryLength)
                return Invalid(PartnerRegistrationValidationError.IndustryTooLong);

            var address = NormalizeOptional(input.Address);
            if (address != null && address.Length > MaxAddressLength)
                return Invalid(PartnerRegistrationValidationError.AddressTooLong);

            var phone = NormalizeOptional(input.Phone);
            if (phone != null && phone.Length > MaxPhoneLength)
                return Invalid(PartnerRegistrationValidationError.PhoneTooLong);

            return new PartnerRegistrationValidationResult
            {
                IsValid = true,
                Error = PartnerRegistrationValidationError.None,
                Value = new ValidatedPartnerRegistrationInput
                {
                    InitiatingUserId = initiatingUserId,
                    MainOwnerId = input.MainOwnerId.Value,
                    Name = input.Name.Trim(),
                    NormalizedName = normalizedName,
                    Industry = industry,
                    Address = address,
                    Phone = phone,
                    IdempotencyKey = idempotencyKey,
                },
            };
        }

        public static bool IsForbiddenAuthorityInput(string parameterName)
            => Array.IndexOf(ForbiddenAuthorityInputs, parameterName) >= 0;

        private static string? NormalizeRequired(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value!.Trim();
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static PartnerRegistrationValidationError MapNameError(PartnerNameNormalizationError error)
            => error switch
            {
                PartnerNameNormalizationError.InputRequired => PartnerRegistrationValidationError.NameRequired,
                PartnerNameNormalizationError.InputTooLong => PartnerRegistrationValidationError.NameTooLong,
                PartnerNameNormalizationError.InputInvalid => PartnerRegistrationValidationError.NameInvalid,
                PartnerNameNormalizationError.NormalizedEmpty => PartnerRegistrationValidationError.NormalizedEmpty,
                PartnerNameNormalizationError.NormalizedTooLong => PartnerRegistrationValidationError.NormalizedTooLong,
                _ => PartnerRegistrationValidationError.NameInvalid,
            };

        private static PartnerRegistrationValidationResult Invalid(PartnerRegistrationValidationError error)
            => new PartnerRegistrationValidationResult
            {
                IsValid = false,
                Error = error,
            };
    }
}
