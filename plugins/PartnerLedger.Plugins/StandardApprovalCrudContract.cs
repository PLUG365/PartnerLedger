using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public enum StandardApprovalCrudError
    {
        None,
        TargetRequired,
        EntityMismatch,
        UnexpectedAttribute,
        RequiredAttributeMissing,
        ValueTypeMismatch,
        ValueTooLong,
        LookupMismatch,
        InvalidStatus,
        AuthorityAttributeForbidden,
        TargetIdRequired,
        DecisionRequired,
        DecisionMustBeEmpty,
    }

    public sealed class StandardApprovalCrudResult
    {
        private StandardApprovalCrudResult(bool isValid, StandardApprovalCrudError error)
        {
            IsValid = isValid;
            Error = error;
        }

        public bool IsValid { get; }
        public StandardApprovalCrudError Error { get; }

        public static StandardApprovalCrudResult Valid()
            => new StandardApprovalCrudResult(true, StandardApprovalCrudError.None);

        public static StandardApprovalCrudResult Invalid(StandardApprovalCrudError error)
            => new StandardApprovalCrudResult(false, error);
    }

    /// <summary>
    /// 標準CRUDで承認基盤へ到達する際のクライアント入力allow-list。
    /// 状態遷移、申請者・設定・対象行の再取得、原子性はPlugin側で追加検査するが、
    /// この契約だけでも画面外の直接Create／Updateから権威列を受け入れない。
    /// </summary>
    public static class StandardApprovalCrudContract
    {
        public const string RequestEntityName = "pl_request";
        public const string SubmissionVersionEntityName = "pl_submissionversion";
        public const string PartnerEntityName = "pl_partner";
        public const string ContractEntityName = "pl_contract";
        public const string ApprovalLinkEntityName = "pl_approvallink";
        public const string RequestPrimaryIdAttribute = "pl_requestid";
        public const string SubmissionVersionPrimaryIdAttribute = "pl_submissionversionid";
        public const string ApprovalLinkPrimaryIdAttribute = "pl_approvallinkid";

        private static readonly ISet<string> RequestCreateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_name",
            "pl_requesttypecode",
            "pl_partnerlookup",
            "pl_contractlookup",
            "pl_requestkey",
        };

        private static readonly ISet<string> SubmissionVersionCreateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_name",
            "pl_changesetjson",
            "pl_requestlookup",
        };

        private static readonly ISet<string> RequestUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_name",
            "pl_requesttypecode",
            "pl_partnerlookup",
            "pl_contractlookup",
            "pl_requeststatuscode",
            "pl_cancellationreason",
        };

        private static readonly ISet<string> SubmissionVersionUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_name",
            "pl_changesetjson",
        };

        private static readonly ISet<string> ApprovalLinkResultUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_resultknown",
            "pl_decisioncode",
        };

        private static readonly ISet<string> ApprovalLinkAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_name",
            "pl_requestlookup",
            "pl_submissionversionlookup",
            "pl_linkstatuscode",
            "pl_externalrequestkey",
            "pl_decidedat",
            "pl_responseat",
            "pl_requestkey",
            "pl_idempotencykey",
        };

        private static readonly ISet<string> AuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_requestinguserlookup",
            "pl_approverteamlookup",
            "pl_approvalpolicyversion",
            ApprovalActiveRequestKey.AttributeName,
            "pl_requestedat",
            "pl_policyversion",
            "pl_rowversiontoken",
            ApprovalChangeSummary.AttributeName,
            ApprovalChangeSummary.TitleAttributeName,
            "pl_versionnumber",
            "pl_submittedbylookup",
            "pl_submittedat",
            "pl_fixedat",
            "pl_submissionstatuscode",
            "statecode",
            "statuscode",
            "ownerid",
            "createdby",
            "createdon",
            "createdonbehalfby",
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
            "owningbusinessunit",
            "owningteam",
            "owninguser",
        };

        public static StandardApprovalCrudResult ValidateRequestCreate(Entity? target)
        {
            var common = ValidateTarget(target, RequestEntityName, RequestCreateAttributes, isUpdate: false);
            if (!common.IsValid) return common;

            var request = target!;
            if (!HasText(request, "pl_name") || !HasText(request, "pl_requesttypecode") || !HasText(request, "pl_requestkey"))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.RequiredAttributeMissing);
            if (!IsTextWithin(request, "pl_name", 850)
                || !IsTextWithin(request, "pl_requesttypecode", 200)
                || !IsTextWithin(request, "pl_requestkey", 200))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            if (!IsLookup(request, "pl_partnerlookup", PartnerEntityName, required: true)
                || !IsLookup(request, "pl_contractlookup", ContractEntityName, required: false))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.LookupMismatch);
            return StandardApprovalCrudResult.Valid();
        }

        public static StandardApprovalCrudResult ValidateSubmissionVersionCreate(Entity? target)
        {
            var common = ValidateTarget(target, SubmissionVersionEntityName, SubmissionVersionCreateAttributes, isUpdate: false);
            if (!common.IsValid) return common;

            var version = target!;
            if (!HasText(version, "pl_name") || !HasText(version, "pl_changesetjson"))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.RequiredAttributeMissing);
            if (!IsTextWithin(version, "pl_name", 850) || !IsTextWithin(version, "pl_changesetjson", 1048576))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            if (!IsLookup(version, "pl_requestlookup", RequestEntityName, required: true))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.LookupMismatch);
            return StandardApprovalCrudResult.Valid();
        }

        public static StandardApprovalCrudResult ValidateRequestUpdate(Entity? target)
        {
            var common = ValidateTarget(target, RequestEntityName, RequestUpdateAttributes, isUpdate: true);
            if (!common.IsValid) return common;
            if (target!.Attributes.Contains("pl_requeststatuscode")
                && (!HasText(target, "pl_requeststatuscode")
                    || !IsAllowedPublicRequestStatus(target.GetAttributeValue<string>("pl_requeststatuscode"))))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.InvalidStatus);
            if (target.Attributes.Contains("pl_cancellationreason")
                && !IsTextWithin(target, "pl_cancellationreason", ApprovalCancellationContract.ReasonMaxLength))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            // 取消理由の要否は実行者で変わるため、ここでは判定せず取消サービスが決める（PL-034）。
            if (target.Attributes.Contains("pl_name") && !IsTextWithin(target, "pl_name", 850))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            if (target.Attributes.Contains("pl_requesttypecode") && !IsTextWithin(target, "pl_requesttypecode", 200))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            if (target.Attributes.Contains("pl_partnerlookup")
                && !IsLookup(target, "pl_partnerlookup", PartnerEntityName, required: true))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.LookupMismatch);
            if (target.Attributes.Contains("pl_contractlookup")
                && !IsLookup(target, "pl_contractlookup", ContractEntityName, required: false))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.LookupMismatch);
            return StandardApprovalCrudResult.Valid();
        }

        public static StandardApprovalCrudResult ValidateSubmissionVersionUpdate(Entity? target)
        {
            var common = ValidateTarget(target, SubmissionVersionEntityName, SubmissionVersionUpdateAttributes, isUpdate: true);
            if (!common.IsValid) return common;
            if (target!.Attributes.Contains("pl_name") && !IsTextWithin(target, "pl_name", 850))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            if (target.Attributes.Contains("pl_changesetjson") && !IsTextWithin(target, "pl_changesetjson", 1048576))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            return StandardApprovalCrudResult.Valid();
        }

        /// <summary>
        /// グループ承認の結果を標準のApprovalLink Updateへ渡すための最小allow-list。
        /// 結果、判定値以外のLink列はすべてサーバー導出値として拒否する。
        /// </summary>
        public static StandardApprovalCrudResult ValidateApprovalLinkResultUpdate(Entity? target)
        {
            if (target == null)
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.TargetRequired);
            if (!string.Equals(target.LogicalName, ApprovalLinkEntityName, StringComparison.Ordinal))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.EntityMismatch);
            if (target.Id == Guid.Empty)
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.TargetIdRequired);

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == ApprovalLinkPrimaryIdAttribute)
                {
                    if (!(target[attributeName] is Guid linkId) || linkId != target.Id)
                        return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTypeMismatch);
                    continue;
                }

                if (IsAuthorityAttribute(attributeName) || ApprovalLinkAuthorityAttributes.Contains(attributeName))
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.AuthorityAttributeForbidden);
                if (!ApprovalLinkResultUpdateAttributes.Contains(attributeName))
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.UnexpectedAttribute);
            }

            if (!target.Attributes.Contains("pl_resultknown") || !(target["pl_resultknown"] is bool))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.RequiredAttributeMissing);

            if (target.Attributes.Contains("pl_decisioncode") && target["pl_decisioncode"] != null)
            {
                if (!(target["pl_decisioncode"] is string decisionCode))
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTypeMismatch);
                if (decisionCode.Length > 50)
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTooLong);
            }

            var resultKnown = (bool)target["pl_resultknown"];
            var decision = target.GetAttributeValue<string>("pl_decisioncode");
            if (resultKnown && string.IsNullOrWhiteSpace(decision))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.DecisionRequired);
            if (!resultKnown && !string.IsNullOrWhiteSpace(decision))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.DecisionMustBeEmpty);

            return StandardApprovalCrudResult.Valid();
        }

        public static bool IsAuthorityAttribute(string attributeName)
            => AuthorityAttributes.Contains(attributeName);

        private static StandardApprovalCrudResult ValidateTarget(
            Entity? target,
            string expectedEntityName,
            ISet<string> allowedAttributes,
            bool isUpdate)
        {
            if (target == null) return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.TargetRequired);
            if (!string.Equals(target.LogicalName, expectedEntityName, StringComparison.Ordinal))
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.EntityMismatch);
            if (isUpdate && target.Id == Guid.Empty)
                return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.TargetIdRequired);

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == (expectedEntityName == RequestEntityName ? RequestPrimaryIdAttribute : SubmissionVersionPrimaryIdAttribute))
                {
                    if (!(target[attributeName] is Guid))
                        return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.ValueTypeMismatch);
                    continue;
                }

                if (AuthorityAttributes.Contains(attributeName))
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.AuthorityAttributeForbidden);
                if (!allowedAttributes.Contains(attributeName))
                    return StandardApprovalCrudResult.Invalid(StandardApprovalCrudError.UnexpectedAttribute);
            }

            return StandardApprovalCrudResult.Valid();
        }

        private static bool HasText(Entity target, string attributeName)
            => target.Attributes.Contains(attributeName)
               && target[attributeName] is string value
               && !string.IsNullOrWhiteSpace(value);

        private static bool IsTextWithin(Entity target, string attributeName, int maxLength)
            => target[attributeName] is string value && value.Trim().Length <= maxLength;

        private static bool IsLookup(Entity target, string attributeName, string expectedLogicalName, bool required)
        {
            if (!target.Attributes.Contains(attributeName) || target[attributeName] == null)
                return !required;
            var reference = target[attributeName] as EntityReference;
            return reference != null
                && reference.Id != Guid.Empty
                && string.Equals(reference.LogicalName, expectedLogicalName, StringComparison.Ordinal);
        }

        private static bool IsAllowedPublicRequestStatus(string? status)
            => string.Equals(status, ApprovalRequestStatus.提出中.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalRequestStatus.取消.ToString(), StringComparison.Ordinal);
    }
}
