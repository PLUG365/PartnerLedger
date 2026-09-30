using System;
using System.Security.Cryptography;
using System.Text;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 共有設定の再送内容をサーバー側で固定長化する指紋。
    /// ContentHashはクライアント入力ではなく、会社・principal・アクセス種別から算出する。
    /// </summary>
    public static class PartnerShareSettingFingerprint
    {
        public const int HashLength = 64;

        public static string Compute(PartnerShareSettingInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.PartnerId == Guid.Empty) throw new ArgumentException("会社IDが必要です。", nameof(input));
            if (!input.PrincipalKind.HasValue) throw new ArgumentException("principal種別が必要です。", nameof(input));
            if (!input.AccessLevel.HasValue) throw new ArgumentException("アクセス種別が必要です。", nameof(input));

            var canonical = string.Join("\u001f", new[]
            {
                input.PartnerId.ToString("D"),
                input.PrincipalKind.Value.ToString(),
                PartnerShareSettingContract.PrincipalId(input).ToString("D"),
                input.AccessLevel.Value.ToString(),
            });

            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(HashLength);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != HashLength)
            {
                return false;
            }

            foreach (var character in value)
            {
                var isHex = character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F';
                if (!isHex) return false;
            }

            return true;
        }
    }
}
