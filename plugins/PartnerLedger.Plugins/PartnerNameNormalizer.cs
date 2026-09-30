using System;
using System.Collections.Generic;
using System.Text;

namespace PartnerLedger.Plugins
{
    public enum PartnerNameNormalizationError
    {
        None,
        InputRequired,
        InputTooLong,
        InputInvalid,
        NormalizedEmpty,
        NormalizedTooLong,
    }

    /// <summary>
    /// 取引先重複判定と承認反映で共有する会社名正規化 v1。
    /// 入力の表示名は変更せず、派生列 pl_normalizedname にだけこの値を保存する。
    /// </summary>
    public static class PartnerNameNormalizer
    {
        public const int MaxNameLength = 200;
        private static readonly IReadOnlyList<string> EdgeCorporateDesignators
            = new[]
            {
                "特定非営利活動法人",
                "一般社団法人",
                "一般財団法人",
                "公益社団法人",
                "公益財団法人",
                "社会福祉法人",
                "独立行政法人",
                "株式会社",
                "有限会社",
                "合同会社",
                "合資会社",
                "合名会社",
                "医療法人",
                "学校法人",
                "宗教法人",
                "(株)",
                "(有)",
            };

        public static bool TryNormalize(
            string? input,
            out string normalized,
            out PartnerNameNormalizationError error)
        {
            normalized = string.Empty;
            error = PartnerNameNormalizationError.None;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = PartnerNameNormalizationError.InputRequired;
                return false;
            }
            var partnerName = input!;
            if (partnerName.Length > MaxNameLength)
            {
                error = PartnerNameNormalizationError.InputTooLong;
                return false;
            }

            string compatibilityNormalized;
            try
            {
                compatibilityNormalized = partnerName.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                error = PartnerNameNormalizationError.InputInvalid;
                return false;
            }

            var withoutWhitespace = new StringBuilder(compatibilityNormalized.Length);
            foreach (var character in compatibilityNormalized)
            {
                if (!char.IsWhiteSpace(character))
                    withoutWhitespace.Append(character);
            }

            var upperInvariant = withoutWhitespace.ToString().ToUpperInvariant();
            normalized = StripEdgeCorporateDesignators(upperInvariant);
            if (normalized.Length == 0)
            {
                error = PartnerNameNormalizationError.NormalizedEmpty;
                return false;
            }
            if (normalized.Length > MaxNameLength)
            {
                error = PartnerNameNormalizationError.NormalizedTooLong;
                return false;
            }

            return true;
        }

        private static string StripEdgeCorporateDesignators(string value)
        {
            var current = value;
            var changed = true;
            while (changed && current.Length > 0)
            {
                changed = false;
                foreach (var designator in EdgeCorporateDesignators)
                {
                    if (current.StartsWith(designator, StringComparison.Ordinal))
                    {
                        current = current.Substring(designator.Length);
                        changed = true;
                    }
                    if (current.EndsWith(designator, StringComparison.Ordinal))
                    {
                        current = current.Substring(0, current.Length - designator.Length);
                        changed = true;
                    }
                    if (current.Length == 0)
                        break;
                }
            }

            return current;
        }
    }
}
