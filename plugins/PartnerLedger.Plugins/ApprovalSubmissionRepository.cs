using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 承認提出で必要な申請／提出版の最小読取りと、競合制御付き更新をまとめる。
    /// クライアントから渡された版情報や提出者を採用せず、Dataverseから再取得した値だけを使う。
    /// </summary>
    internal static class ApprovalSubmissionRepository
    {
        public const string RequestEntityName = "pl_request";
        public const string SubmissionVersionEntityName = "pl_submissionversion";

        public sealed class RequestRecord
        {
            public Guid Id { get; set; }
            public ApprovalRequestStatus Status { get; set; }
            public int? StateCode { get; set; }
            public string RequestTypeCode { get; set; } = string.Empty;
            public Guid? RequestingUserId { get; set; }
            public Guid? PartnerId { get; set; }
            public string PartnerLookupLogicalName { get; set; } = string.Empty;
            public Guid? ContractId { get; set; }
            public bool ContractLookupPresent { get; set; }
            public string ContractLookupLogicalName { get; set; } = string.Empty;
            public Guid? ApproverTeamId { get; set; }
            public string PolicyVersion { get; set; } = string.Empty;
            public DateTime? RequestedAt { get; set; }
            public string RowVersion { get; set; } = string.Empty;
        }

        public sealed class SubmissionVersionRecord
        {
            public Guid Id { get; set; }
            public Guid? RequestId { get; set; }
            public ApprovalSubmissionVersionStatus Status { get; set; }
            public int? StateCode { get; set; }
            public Guid? SubmittedById { get; set; }
            public DateTime? SubmittedAt { get; set; }
            public string PolicyVersion { get; set; } = string.Empty;
            public string TargetRowVersion { get; set; } = string.Empty;
            public int? VersionNumber { get; set; }
            public string ChangeSetJson { get; set; } = string.Empty;
            public string RowVersion { get; set; } = string.Empty;
        }

        public static RequestRecord RetrieveRequest(IOrganizationService service, Guid requestId)
        {
            var entity = service.Retrieve(
                RequestEntityName,
                requestId,
                new ColumnSet(
                    "pl_requeststatuscode",
                    "pl_requesttypecode",
                    "pl_requestinguserlookup",
                    "pl_partnerlookup",
                    "pl_contractlookup",
                    "pl_approverteamlookup",
                    "pl_approvalpolicyversion",
                    "pl_requestedat",
                    "statecode",
                    "versionnumber"));
            var partnerReference = entity.GetAttributeValue<EntityReference>("pl_partnerlookup");
            var contractReference = entity.GetAttributeValue<EntityReference>("pl_contractlookup");

            return new RequestRecord
            {
                Id = requestId,
                Status = ParseRequestStatus(entity.GetAttributeValue<string>("pl_requeststatuscode")),
                StateCode = ReadOptionSetValue(entity, "statecode"),
                RequestTypeCode = entity.GetAttributeValue<string>("pl_requesttypecode") ?? string.Empty,
                RequestingUserId = entity.GetAttributeValue<EntityReference>("pl_requestinguserlookup")?.Id,
                PartnerId = GetReferenceId(partnerReference),
                PartnerLookupLogicalName = partnerReference?.LogicalName ?? string.Empty,
                ContractId = GetReferenceId(contractReference),
                ContractLookupPresent = contractReference != null,
                ContractLookupLogicalName = contractReference?.LogicalName ?? string.Empty,
                ApproverTeamId = entity.GetAttributeValue<EntityReference>("pl_approverteamlookup")?.Id,
                PolicyVersion = entity.GetAttributeValue<string>("pl_approvalpolicyversion") ?? string.Empty,
                RequestedAt = entity.GetAttributeValue<DateTime?>("pl_requestedat"),
                RowVersion = entity.RowVersion ?? string.Empty,
            };
        }

        public static SubmissionVersionRecord RetrieveSubmissionVersion(IOrganizationService service, Guid submissionVersionId)
        {
            var entity = service.Retrieve(
                SubmissionVersionEntityName,
                submissionVersionId,
                new ColumnSet(
                    "pl_requestlookup",
                    "pl_submissionstatuscode",
                    "pl_submittedbylookup",
                    "pl_submittedat",
                    "pl_policyversion",
                    "pl_rowversiontoken",
                    "pl_versionnumber",
                    "pl_changesetjson",
                    "statecode",
                    "versionnumber"));

            return new SubmissionVersionRecord
            {
                Id = submissionVersionId,
                RequestId = entity.GetAttributeValue<EntityReference>("pl_requestlookup")?.Id,
                Status = ParseSubmissionVersionStatus(entity.GetAttributeValue<string>("pl_submissionstatuscode")),
                StateCode = ReadOptionSetValue(entity, "statecode"),
                SubmittedById = entity.GetAttributeValue<EntityReference>("pl_submittedbylookup")?.Id,
                SubmittedAt = entity.GetAttributeValue<DateTime?>("pl_submittedat"),
                PolicyVersion = entity.GetAttributeValue<string>("pl_policyversion") ?? string.Empty,
                TargetRowVersion = entity.GetAttributeValue<string>("pl_rowversiontoken") ?? string.Empty,
                VersionNumber = entity.GetAttributeValue<int?>("pl_versionnumber"),
                ChangeSetJson = entity.GetAttributeValue<string>("pl_changesetjson") ?? string.Empty,
                RowVersion = entity.RowVersion ?? string.Empty,
            };
        }

        public static void UpdateRequestStatus(
            IOrganizationService service,
            RequestRecord request,
            ApprovalRequestStatus status,
            bool releaseActiveRequestKey = false)
        {
            var entity = new Entity(RequestEntityName, request.Id)
            {
                ["pl_requeststatuscode"] = status.ToString(),
                RowVersion = request.RowVersion,
            };
            if (releaseActiveRequestKey)
            {
                entity[ApprovalActiveRequestKey.AttributeName] = null;
            }
            UpdateAsApprovalServer(service, entity);
        }

        public static void MarkSubmissionVersionSubmitted(
            IOrganizationService service,
            SubmissionVersionRecord submissionVersion,
            Guid submittedById,
            DateTime submittedAtUtc)
            => MarkSubmissionVersionSubmitted(
                service,
                submissionVersion,
                submittedById,
                submittedAtUtc,
                submissionVersion.PolicyVersion,
                submissionVersion.TargetRowVersion,
                submissionVersion.VersionNumber);

        public static void MarkSubmissionVersionSubmitted(
            IOrganizationService service,
            SubmissionVersionRecord submissionVersion,
            Guid submittedById,
            DateTime submittedAtUtc,
            string policyVersion,
            string targetRowVersion,
            int? versionNumber,
            string? changeSummary = null,
            string? approvalTitle = null)
        {
            var entity = new Entity(SubmissionVersionEntityName, submissionVersion.Id)
            {
                ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.提出済み.ToString(),
                ["pl_submittedbylookup"] = new EntityReference("systemuser", submittedById),
                ["pl_submittedat"] = submittedAtUtc,
                ["pl_policyversion"] = policyVersion,
                ["pl_rowversiontoken"] = targetRowVersion,
                RowVersion = submissionVersion.RowVersion,
            };
            if (versionNumber.HasValue)
            {
                entity["pl_versionnumber"] = versionNumber.Value;
            }
            if (changeSummary != null)
            {
                entity[ApprovalChangeSummary.AttributeName] = changeSummary;
            }
            if (approvalTitle != null)
            {
                entity[ApprovalChangeSummary.TitleAttributeName] = approvalTitle;
            }
            UpdateAsApprovalServer(service, entity);
        }

        public static void UpdateSubmissionVersionStatus(
            IOrganizationService service,
            SubmissionVersionRecord submissionVersion,
            ApprovalSubmissionVersionStatus status)
        {
            var entity = new Entity(SubmissionVersionEntityName, submissionVersion.Id)
            {
                ["pl_submissionstatuscode"] = status.ToString(),
                RowVersion = submissionVersion.RowVersion,
            };
            UpdateAsApprovalServer(service, entity);
        }

        /// <summary>
        /// 承認系Plugin（提出・判定・取消・反映）がSYSTEMのサービスで行うサーバー内部更新専用。
        /// 対象テーブルの入力ガードStepを飛ばす指定を付けるため、利用者の要求を処理する経路からは呼ばない。
        /// 渡したサービスの主体がprvBypassCustomBusinessLogicを持たない場合、Dataverseが拒否する。
        /// </summary>
        internal static void UpdateAsApprovalServer(IOrganizationService service, Entity entity)
        {
            var request = new Microsoft.Xrm.Sdk.Messages.UpdateRequest
            {
                Target = entity,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
            };
            ApprovalServerWriteBypass.Apply(request);
            service.Execute(request);
        }

        private static ApprovalRequestStatus ParseRequestStatus(string? value)
        {
            if (Enum.TryParse(value, false, out ApprovalRequestStatus status)
                && Enum.IsDefined(typeof(ApprovalRequestStatus), status))
            {
                return status;
            }

            throw new InvalidPluginExecutionException("申請状態が不正です。");
        }

        private static ApprovalSubmissionVersionStatus ParseSubmissionVersionStatus(string? value)
        {
            if (Enum.TryParse(value, false, out ApprovalSubmissionVersionStatus status)
                && Enum.IsDefined(typeof(ApprovalSubmissionVersionStatus), status))
            {
                return status;
            }

            throw new InvalidPluginExecutionException("提出版状態が不正です。");
        }

        private static int? ReadOptionSetValue(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var raw))
                return null;
            if (raw is OptionSetValue optionSet)
                return optionSet.Value;
            if (raw is int integer)
                return integer;
            return null;
        }

        private static Guid? GetReferenceId(EntityReference? reference)
        {
            return reference == null || reference.Id == Guid.Empty
                ? (Guid?)null
                : reference.Id;
        }
    }
}
