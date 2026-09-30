using System;
using System.Security.Cryptography;
using System.Text;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 同一対象・同一申請種別の承認中スロットを表すサーバー導出キー。
    /// キー本体へ業務値を露出させず、Dataverse Alternate Keyへ安全に格納できる固定長にする。
    /// </summary>
    public static class ApprovalActiveRequestKey
    {
        public const string AttributeName = "pl_activeapprovalkey";
        public const string Prefix = "ApprovalActiveV1-";

        public static string Build(string targetEntityName, Guid targetId, string requestTypeCode)
        {
            if (string.IsNullOrWhiteSpace(targetEntityName)
                || targetId == Guid.Empty
                || string.IsNullOrWhiteSpace(requestTypeCode))
            {
                throw new ArgumentException("承認中キーの対象情報が不正です。");
            }

            var canonical = string.Join(
                "|",
                targetEntityName.Trim().ToLowerInvariant(),
                targetId.ToString("D"),
                requestTypeCode.Trim().ToLowerInvariant());
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var hex = BitConverter.ToString(digest).Replace("-", string.Empty);
                return Prefix + hex;
            }
        }

        public static bool HoldsSlot(ApprovalRequestStatus status)
            => status != ApprovalRequestStatus.却下
               && status != ApprovalRequestStatus.取消
               && status != ApprovalRequestStatus.反映済み
               // 2026-09-26から反映失敗は終端。申請者が同じ対象ですぐ申請し直せるようにキーを持たない。
               && status != ApprovalRequestStatus.反映失敗;
    }
}
