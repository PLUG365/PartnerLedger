using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum ContractStandardUpdateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        TargetIdRequired,
        InitiatingUserRequired,
        UpdateEmpty,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        NameTypeInvalid,
        NameRequired,
        NameTooLong,
        ContractStatusTypeInvalid,
        ContractStatusValueInvalid,
        DateTypeInvalid,
        AutoRenewTypeInvalid,
        LinkTypeInvalid,
        LinkTooLong,
        PreImageRequired,
        PreImageEntityInvalid,
        CurrentStateUnavailable,
        CurrentStateInvalid,
        CurrentStatusUnavailable,
        CurrentStatusInvalid,
        ContractAlreadyEnded,
        SettingsUnavailable,
        ApprovalRequired,
    }

    /// <summary>
    /// Standard pl_contract Update の入力契約。
    /// 標準Write権限は入口にすぎず、ポリシーと契約の現在状態はサーバー側で再検査する。
    /// </summary>
    public static class ContractStandardUpdateContract
    {
        public const string EntityName = "pl_contract";
        public const string PrimaryIdAttribute = "pl_contractid";
        public const string NameAttribute = "pl_name";
        public const string ContractStatusAttribute = "pl_contractstatuscode";
        public const string EndDateAttribute = "pl_enddate";
        public const string NoticeDateAttribute = "pl_noticedate";
        public const string DecisionDateAttribute = "pl_decisiondate";
        public const string AutoRenewAttribute = "pl_autorenew";
        public const string LinkAttribute = "pl_link";
        public const int ActiveStateCode = 0;
        public const int ExecutedContractStatusCode = 100000000;
        public const int EndedContractStatusCode = 100000001;

        private const int TextMaxLength = 200;

        private static readonly HashSet<string> AllowedUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            NameAttribute,
            ContractStatusAttribute,
            EndDateAttribute,
            NoticeDateAttribute,
            DecisionDateAttribute,
            AutoRenewAttribute,
            LinkAttribute,
        };

        // Dataverse adds these platform-managed columns to the SDK Update Target
        // even when the Web API payload contains only an allowed business column.
        // They are not part of the business change set and must not be audited.
        private static readonly HashSet<string> PlatformManagedUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            PrimaryIdAttribute,
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
        };

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            PrimaryIdAttribute,
            "pl_partnerlookup",
            "pl_contracttypelookup",
            "pl_registrationkey",
            "ownerid",
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
            "versionnumber",
            "importsequencenumber",
            "timezoneruleversionnumber",
            "utcconversiontimezonecode",
        };

        public static ContractStandardUpdateValidationResult Validate(
            Entity? target,
            Entity? preImage,
            Guid initiatingUserId,
            ApprovalSettingsReadResult settings)
        {
            var basic = ValidateTargetShape(target, requireId: true);
            if (!basic.IsValid) return basic;
            if (initiatingUserId == Guid.Empty)
            {
                return Invalid(ContractStandardUpdateValidationError.InitiatingUserRequired);
            }

            if (preImage == null)
            {
                return Invalid(ContractStandardUpdateValidationError.PreImageRequired);
            }
            if (!string.Equals(preImage.LogicalName, EntityName, StringComparison.Ordinal)
                || preImage.Id == Guid.Empty
                || preImage.Id != target!.Id)
            {
                return Invalid(ContractStandardUpdateValidationError.PreImageEntityInvalid);
            }

            var state = preImage.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null) return Invalid(ContractStandardUpdateValidationError.CurrentStateUnavailable);
            if (state.Value != ActiveStateCode) return Invalid(ContractStandardUpdateValidationError.CurrentStateInvalid);

            var currentStatus = preImage.GetAttributeValue<OptionSetValue>(ContractStatusAttribute);
            if (currentStatus == null) return Invalid(ContractStandardUpdateValidationError.CurrentStatusUnavailable);
            if (currentStatus.Value == EndedContractStatusCode)
            {
                return Invalid(ContractStandardUpdateValidationError.ContractAlreadyEnded);
            }
            if (currentStatus.Value != ExecutedContractStatusCode)
            {
                return Invalid(ContractStandardUpdateValidationError.CurrentStatusInvalid);
            }

            if (settings == null || !settings.IsValid || settings.Settings == null)
            {
                return Invalid(ContractStandardUpdateValidationError.SettingsUnavailable);
            }
            if (settings.Settings.Policy.ContractUpdate)
            {
                return Invalid(ContractStandardUpdateValidationError.ApprovalRequired);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedUpdateAttributes.Contains(attributeName)
                    || PlatformManagedUpdateAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(ContractStandardUpdateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(ContractStandardUpdateValidationError.UnsupportedAttribute);
            }

            if (target.Attributes.Count == 0)
            {
                return Invalid(ContractStandardUpdateValidationError.UpdateEmpty);
            }

            var changedAttributes = target.Attributes.Keys
                .Where(attributeName => AllowedUpdateAttributes.Contains(attributeName))
                .ToList();
            if (changedAttributes.Count == 0)
            {
                return Invalid(ContractStandardUpdateValidationError.UpdateEmpty);
            }

            var valueResult = ValidateAndNormalizeValues(target);
            if (!valueResult.IsValid) return valueResult;

            return new ContractStandardUpdateValidationResult
            {
                IsValid = true,
                Error = ContractStandardUpdateValidationError.None,
                ChangedAttributes = changedAttributes,
            };
        }

        /// <summary>
        /// 承認反映のネストUpdateは、既にApprovalChangeSetContractで検証済みだが、
        /// 契約Updateのサーバー境界でも属性と型を再検査する。ポリシーONを迂回する
        /// のではなく、承認反映のサーバー処理（SYSTEMのネストUpdate）からだけ呼び出す。
        /// </summary>
        public static void ValidateTrustedInternalTarget(Entity? target, string primaryEntityName)
        {
            if (target == null || !string.Equals(primaryEntityName, EntityName, StringComparison.Ordinal)
                || !string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal)
                || target.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("標準契約Updateの内部対象が不正です。");
            }

            var result = ValidateTargetShape(target, requireId: true);
            if (!result.IsValid) throw new InvalidPluginExecutionException("標準契約Updateの内部対象が不正です: " + result.Error);
            var valueResult = ValidateAndNormalizeValues(target);
            if (!valueResult.IsValid)
            {
                throw new InvalidPluginExecutionException("標準契約Updateの内部値が不正です: " + valueResult.Error);
            }
        }

        public static bool IsAllowedUpdateAttribute(string attributeName)
            => attributeName != null && AllowedUpdateAttributes.Contains(attributeName);

        public static bool IsForbiddenAuthorityAttribute(string attributeName)
            => attributeName != null && ForbiddenAuthorityAttributes.Contains(attributeName);

        private static ContractStandardUpdateValidationResult ValidateTargetShape(Entity? target, bool requireId)
        {
            if (target == null) return Invalid(ContractStandardUpdateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(ContractStandardUpdateValidationError.TargetEntityInvalid);
            }
            if (requireId && target.Id == Guid.Empty)
            {
                return Invalid(ContractStandardUpdateValidationError.TargetIdRequired);
            }
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (AllowedUpdateAttributes.Contains(attributeName)
                    || PlatformManagedUpdateAttributes.Contains(attributeName)) continue;
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(ContractStandardUpdateValidationError.ForbiddenAuthorityInput);
                }

                return Invalid(ContractStandardUpdateValidationError.UnsupportedAttribute);
            }
            if (target.Attributes.Count == 0)
            {
                return Invalid(ContractStandardUpdateValidationError.UpdateEmpty);
            }
            return Valid();
        }

        private static ContractStandardUpdateValidationResult ValidateAndNormalizeValues(Entity target)
        {
            if (target.Attributes.TryGetValue(NameAttribute, out var nameValue))
            {
                if (!(nameValue is string name)) return Invalid(ContractStandardUpdateValidationError.NameTypeInvalid);
                var normalized = name.Trim();
                if (normalized.Length == 0) return Invalid(ContractStandardUpdateValidationError.NameRequired);
                if (normalized.Length > TextMaxLength) return Invalid(ContractStandardUpdateValidationError.NameTooLong);
                target[NameAttribute] = normalized;
            }

            if (target.Attributes.TryGetValue(ContractStatusAttribute, out var statusValue))
            {
                if (!(statusValue is OptionSetValue status)) return Invalid(ContractStandardUpdateValidationError.ContractStatusTypeInvalid);
                if (status.Value != ExecutedContractStatusCode && status.Value != EndedContractStatusCode)
                {
                    return Invalid(ContractStandardUpdateValidationError.ContractStatusValueInvalid);
                }
            }

            foreach (var dateAttribute in new[] { EndDateAttribute, NoticeDateAttribute, DecisionDateAttribute })
            {
                if (target.Attributes.TryGetValue(dateAttribute, out var dateValue)
                    && dateValue != null
                    && !(dateValue is DateTime))
                {
                    return Invalid(ContractStandardUpdateValidationError.DateTypeInvalid);
                }
            }

            if (target.Attributes.TryGetValue(AutoRenewAttribute, out var autoRenewValue)
                && !(autoRenewValue is bool))
            {
                return Invalid(ContractStandardUpdateValidationError.AutoRenewTypeInvalid);
            }

            if (target.Attributes.TryGetValue(LinkAttribute, out var linkValue))
            {
                if (linkValue == null) return Valid();
                if (!(linkValue is string link)) return Invalid(ContractStandardUpdateValidationError.LinkTypeInvalid);
                var normalized = link.Trim();
                if (normalized.Length > TextMaxLength) return Invalid(ContractStandardUpdateValidationError.LinkTooLong);
                target[LinkAttribute] = normalized.Length == 0 ? null : normalized;
            }

            return Valid();
        }

        private static ContractStandardUpdateValidationResult Valid()
            => new ContractStandardUpdateValidationResult
            {
                IsValid = true,
                Error = ContractStandardUpdateValidationError.None,
            };

        private static ContractStandardUpdateValidationResult Invalid(ContractStandardUpdateValidationError error)
            => new ContractStandardUpdateValidationResult
            {
                IsValid = false,
                Error = error,
            };
    }

    public sealed class ContractStandardUpdateValidationResult
    {
        public bool IsValid { get; set; }
        public ContractStandardUpdateValidationError Error { get; set; }
        public IList<string> ChangedAttributes { get; set; } = new List<string>();
    }
}
