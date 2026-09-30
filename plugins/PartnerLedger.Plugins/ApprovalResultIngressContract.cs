using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public static class ApprovalResultIngressError
    {
        public const string InvalidInput = "invalid-input";
        public const string DataIntegrityError = "data-integrity-error";
        public const string DecisionUnsupported = "decision-unsupported";
        public const string LinkNotReady = "link-not-ready";
        public const string StateCannotIngest = "state-cannot-ingest";
        public const string IdempotencyKeyConflict = "idempotency-key-conflict";
    }

    public sealed class ApprovalResultIngressResult
    {
        public bool Success { get; set; }
        public bool IsReplay { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public Guid ApprovalLinkId { get; set; }
        public ApprovalLinkStatus LinkStatus { get; set; }
        public bool ResultKnown { get; set; }
    }
}
