using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    internal static class ApprovalDecisionRepository
    {
        public const string ApprovalLinkEntityName = "pl_approvallink";

        public sealed class ApprovalLinkRecord
        {
            public Guid Id { get; set; }
            public Guid? RequestId { get; set; }
            public Guid? SubmissionVersionId { get; set; }
            public ApprovalResultSource Source { get; set; }
            public ApprovalLinkStatus Status { get; set; }
            public string ExternalRequestKey { get; set; } = string.Empty;
            public string DecisionCode { get; set; } = string.Empty;
            public bool ResultKnown { get; set; }
            public DateTime? DecidedAt { get; set; }
            public DateTime? ResponseAt { get; set; }
            public string RowVersion { get; set; } = string.Empty;
        }

        public static ApprovalLinkRecord Retrieve(IOrganizationService service, Guid approvalLinkId)
        {
            var entity = service.Retrieve(
                ApprovalLinkEntityName,
                approvalLinkId,
                new ColumnSet(
                    "pl_requestlookup",
                    "pl_submissionversionlookup",
                    "pl_approvalsourcecode",
                    "pl_linkstatuscode",
                    "pl_externalrequestkey",
                    "pl_decisioncode",
                    "pl_resultknown",
                    "pl_decidedat",
                    "pl_responseat",
                    "versionnumber"));

            return new ApprovalLinkRecord
            {
                Id = approvalLinkId,
                RequestId = entity.GetAttributeValue<EntityReference>("pl_requestlookup")?.Id,
                SubmissionVersionId = entity.GetAttributeValue<EntityReference>("pl_submissionversionlookup")?.Id,
                Source = ApprovalResultSourceCodes.Parse(entity.GetAttributeValue<string>("pl_approvalsourcecode")),
                Status = ParseStatus(entity.GetAttributeValue<string>("pl_linkstatuscode")),
                ExternalRequestKey = entity.GetAttributeValue<string>("pl_externalrequestkey") ?? string.Empty,
                DecisionCode = entity.GetAttributeValue<string>("pl_decisioncode") ?? string.Empty,
                ResultKnown = entity.GetAttributeValue<bool?>("pl_resultknown") ?? false,
                DecidedAt = entity.GetAttributeValue<DateTime?>("pl_decidedat"),
                ResponseAt = entity.GetAttributeValue<DateTime?>("pl_responseat"),
                RowVersion = entity.RowVersion ?? string.Empty,
            };
        }

        private static ApprovalLinkStatus ParseStatus(string? value)
        {
            if (Enum.TryParse(value, false, out ApprovalLinkStatus status)
                && Enum.IsDefined(typeof(ApprovalLinkStatus), status))
            {
                return status;
            }

            throw new InvalidPluginExecutionException("承認連携状態が不正です。");
        }
    }
}
