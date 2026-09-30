using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 新規取引先登録の入力契約を、Dataverse Createへ渡す属性集合へ変換する。
    /// 呼び出し元、状態、登録日時、ACL版、所有者はクライアント入力から受けず、
    /// この境界でサーバー側の値を設定する。主担当は検証済み業務入力から設定し、
    /// 登録者は呼び出し元から設定する。owneridや標準createdbyを業務項目の代用として設定しない。
    /// </summary>
    public static class PartnerRegistrationRecordFactory
    {
        public const int InitialAclVersion = 1;

        public static Entity Build(
            ValidatedPartnerRegistrationInput input,
            DateTime registeredAtUtc)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var normalizedRegisteredAt = registeredAtUtc.Kind == DateTimeKind.Utc
                ? registeredAtUtc
                : registeredAtUtc.ToUniversalTime();

            var entity = new Entity("pl_partner")
            {
                ["pl_name"] = input.Name,
                ["pl_normalizedname"] = input.NormalizedName,
                ["pl_tradingstatuscode"] = new OptionSetValue(ValidatedPartnerRegistrationInput.InitialTradingStatusCode),
                ["pl_registeredat"] = normalizedRegisteredAt,
                ["pl_aclversion"] = InitialAclVersion,
                ["pl_mainownerlookup"] = new EntityReference("systemuser", input.MainOwnerId),
                ["pl_registeredbylookup"] = new EntityReference("systemuser", input.InitiatingUserId),
            };

            AddOptional(entity, "pl_industry", input.Industry);
            AddOptional(entity, "pl_address", input.Address);
            AddOptional(entity, "pl_phone", input.Phone);
            return entity;
        }

        private static void AddOptional(Entity entity, string attributeName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                entity[attributeName] = value;
            }
        }
    }
}
