using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public enum ApprovalSettingsReadError
    {
        None,
        ActiveSettingTooMany,
        SettingsVersionInvalid,
        ActiveSettingAmbiguous,
        PolicyInvalid,
        ReadFailed,
    }

    public sealed class ApprovalSettingsRecord
    {
        public Guid Id { get; internal set; }
        public int SettingsVersion { get; internal set; }
        public string PolicyVersion { get; internal set; } = string.Empty;
        public string PolicyJson { get; internal set; } = string.Empty;
        public ApprovalPolicy Policy { get; internal set; } = null!;
        public string RowVersion { get; internal set; } = string.Empty;
    }

    public sealed class ApprovalSettingsReadResult
    {
        private ApprovalSettingsReadResult(
            bool isValid,
            ApprovalSettingsReadError error,
            ApprovalSettingsRecord? settings,
            ApprovalPolicyValidationError policyError)
        {
            IsValid = isValid;
            Error = error;
            Settings = settings;
            PolicyError = policyError;
        }

        public bool IsValid { get; }
        public ApprovalSettingsReadError Error { get; }
        public ApprovalSettingsRecord? Settings { get; }
        public ApprovalPolicyValidationError PolicyError { get; }

        public static ApprovalSettingsReadResult Valid(ApprovalSettingsRecord settings)
            => new ApprovalSettingsReadResult(true, ApprovalSettingsReadError.None, settings, ApprovalPolicyValidationError.None);

        public static ApprovalSettingsReadResult Invalid(
            ApprovalSettingsReadError error,
            ApprovalPolicyValidationError policyError = ApprovalPolicyValidationError.None)
            => new ApprovalSettingsReadResult(false, error, null, policyError);
    }

    /// <summary>
    /// 有効な導入設定版を一つだけ解決する。設定の欠損・版の不整合・同一最大版の重複は
    /// 反映経路を安全側へ倒すため、現在版として採用しない。
    /// </summary>
    public static class ApprovalSettingsRepository
    {
        public const string EntityName = "pl_settings";

        public static ApprovalSettingsReadResult RetrieveActive(IOrganizationService service)
        {
            if (service == null)
                return ApprovalSettingsReadResult.Invalid(ApprovalSettingsReadError.ReadFailed);

            EntityCollection result;
            try
            {
                var query = new QueryExpression(EntityName)
                {
                    ColumnSet = new ColumnSet(
                        "pl_policyjson",
                        "pl_settingsversion",
                        "statecode",
                        "versionnumber"),
                    TopCount = 101,
                };
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.AddOrder("pl_settingsversion", OrderType.Descending);
                result = service.RetrieveMultiple(query);
            }
            catch (InvalidPluginExecutionException)
            {
                return ApprovalSettingsReadResult.Invalid(ApprovalSettingsReadError.ReadFailed);
            }

            if (result.MoreRecords)
                return ApprovalSettingsReadResult.Invalid(ApprovalSettingsReadError.ActiveSettingTooMany);
            // 有効な行が0件のときだけ既定値（版0）を使う。インポート直後に行を作る手順を不要にするため
            // （2026-09-27）。不正な行がある場合は下で失敗させ、既定値へは逃がさない。
            if (result.Entities.Count == 0)
                return ApprovalSettingsReadResult.Valid(DefaultSettings());

            var versions = result.Entities
                .Select(entity => entity.GetAttributeValue<int?>("pl_settingsversion"))
                .ToList();
            if (versions.Any(version => !version.HasValue || version.Value <= 0))
                return ApprovalSettingsReadResult.Invalid(ApprovalSettingsReadError.SettingsVersionInvalid);

            var maxVersion = versions.Max(version => version!.Value);
            var currentRows = result.Entities
                .Where(entity => entity.GetAttributeValue<int?>("pl_settingsversion") == maxVersion)
                .ToList();
            if (currentRows.Count != 1)
                return ApprovalSettingsReadResult.Invalid(ApprovalSettingsReadError.ActiveSettingAmbiguous);

            var current = currentRows[0];
            var policyJson = current.GetAttributeValue<string>("pl_policyjson") ?? string.Empty;
            var policyResult = ApprovalPolicyContract.Validate(policyJson);
            if (!policyResult.IsValid)
            {
                return ApprovalSettingsReadResult.Invalid(
                    ApprovalSettingsReadError.PolicyInvalid,
                    policyResult.Error);
            }

            return ApprovalSettingsReadResult.Valid(new ApprovalSettingsRecord
            {
                Id = current.Id,
                SettingsVersion = maxVersion,
                PolicyVersion = maxVersion.ToString(CultureInfo.InvariantCulture),
                PolicyJson = policyJson,
                Policy = policyResult.Policy!,
                RowVersion = current.RowVersion ?? string.Empty,
            });
        }

        private static ApprovalSettingsRecord DefaultSettings()
        {
            var policy = ApprovalPolicyContract.Validate(ApprovalPolicyContract.DefaultPolicyJson);
            if (!policy.IsValid)
                throw new InvalidPluginExecutionException("承認設定の既定値が不正です。");
            return new ApprovalSettingsRecord
            {
                Id = Guid.Empty,
                SettingsVersion = 0,
                PolicyVersion = "0",
                PolicyJson = ApprovalPolicyContract.DefaultPolicyJson,
                Policy = policy.Policy!,
                RowVersion = string.Empty,
            };
        }
    }
}
