using System;
using System.Security.Cryptography;
using System.Text;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 新規取引先登録の再送内容を、既存OperationLog.pl_Nameへ安全に保存するための
    /// 固定長指紋。専用ハッシュ列を増やさず、監査名から対象IDも復元できる形にする。
    /// </summary>
    public static class PartnerRegistrationFingerprint
    {
        public static string Compute(ValidatedPartnerRegistrationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var canonical = string.Join("\u001f", new[]
            {
                input.InitiatingUserId.ToString("D"),
                input.MainOwnerId.ToString("D"),
                input.Name,
                input.NormalizedName,
                input.Industry ?? string.Empty,
                input.Address ?? string.Empty,
                input.Phone ?? string.Empty,
            });

            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static bool TryParse(string value, string prefix, out Guid id, out string fingerprint)
        {
            id = Guid.Empty;
            fingerprint = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = value.Substring(prefix.Length);
            var separator = rest.IndexOf(":fingerprint:", StringComparison.Ordinal);
            if (separator <= 0 || separator + ":fingerprint:".Length >= rest.Length)
            {
                return false;
            }

            if (!Guid.TryParseExact(rest.Substring(0, separator), "D", out id))
            {
                id = Guid.Empty;
                return false;
            }

            fingerprint = rest.Substring(separator + ":fingerprint:".Length);
            return fingerprint.Length == 64;
        }
    }
}
