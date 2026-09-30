using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 承認で変更する項目だけの、提出時点の値の指紋。
    /// 提出時に対象行から作って提出版（pl_rowversiontoken）へ保存し、反映時に同じ項目の今の値から作り直して比べる。
    /// 行全体の版と違い、無関係な項目の更新では変わらない（2026-09-26 設計A）。
    /// </summary>
    public static class ApprovalTargetBaseline
    {
        public const string Prefix = "fields-v1:";

        public static bool IsFieldToken(string? token)
            => token != null && token.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Compute(Entity row, IEnumerable<string> attributeNames)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (attributeNames == null) throw new ArgumentNullException(nameof(attributeNames));
            var names = attributeNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (names.Count == 0) throw new ArgumentException("指紋を作る項目がありません。", nameof(attributeNames));

            var canonical = string.Join("\n", names.Select(name =>
                name + "=" + Canonical(row.Attributes.TryGetValue(name, out var value) ? value : null)));
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return Prefix + BitConverter.ToString(digest).Replace("-", string.Empty);
            }
        }

        private static string Canonical(object? value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string text:
                    // Dataverseは空文字をnullとして返すため、同じ値として扱う。
                    return text.Length == 0 ? "null" : "text:" + text;
                case OptionSetValue choice:
                    return "choice:" + choice.Value.ToString(CultureInfo.InvariantCulture);
                case bool flag:
                    return flag ? "bool:true" : "bool:false";
                case DateTime time:
                    var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
                    return "datetime:" + utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                case int number:
                    return "int:" + number.ToString(CultureInfo.InvariantCulture);
                case decimal number:
                    return "decimal:" + number.ToString(CultureInfo.InvariantCulture);
                case Money money:
                    return "money:" + money.Value.ToString(CultureInfo.InvariantCulture);
                case EntityReference reference:
                    return "ref:" + reference.LogicalName + ":" + reference.Id.ToString("D");
                default:
                    return "other:" + Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
