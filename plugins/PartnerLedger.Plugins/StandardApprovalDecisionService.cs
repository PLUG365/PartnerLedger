using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    public static class StandardApprovalDecisionError
    {
        public const string InvalidInput = "invalid-input";
        public const string DataIntegrityError = "data-integrity-error";
        public const string PairMismatch = "pair-mismatch";
        public const string LinkNotReady = "link-not-ready";
        public const string ResultUnknown = "result-unknown";
        public const string DecisionUnsupported = "decision-unsupported";
        public const string StateCannotRecord = "state-cannot-record";
        public const string IdempotencyKeyConflict = "idempotency-key-conflict";
    }

    public sealed class StandardApprovalDecisionResult
    {
        public bool Success { get; set; }
        public bool IsReplay { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public Guid RequestId { get; set; }
        public Guid SubmissionVersionId { get; set; }
        public ApprovalRequestStatus RequestStatus { get; set; }
        public ApprovalSubmissionVersionStatus SubmissionVersionStatus { get; set; }
    }

    /// <summary>
    /// 標準pl_ApprovalLink Updateで確定した承認結果を、申請・提出版・対象行へ反映する。
    /// 入力値は受けず、保存済みLinkの対応・結果・時刻だけを正本として扱う。
    /// PL-031改訂により申請単位の閲覧権は操作しない。
    /// </summary>
    public static class StandardApprovalDecisionService
    {
        public const string OperationCode = "ApplyApprovalDecision";

        public static StandardApprovalDecisionResult Apply(
            IOrganizationService service,
            Guid approvalLinkId,
            Guid initiatingUserId)
        {
            if (service == null || approvalLinkId == Guid.Empty || initiatingUserId == Guid.Empty)
                return Failure(Guid.Empty, Guid.Empty, StandardApprovalDecisionError.InvalidInput);

            ApprovalDecisionRepository.ApprovalLinkRecord link;
            ApprovalSubmissionRepository.RequestRecord request;
            ApprovalSubmissionRepository.SubmissionVersionRecord version;
            try
            {
                link = ApprovalDecisionRepository.Retrieve(service, approvalLinkId);
                if (!link.RequestId.HasValue || !link.SubmissionVersionId.HasValue)
                    return Failure(Guid.Empty, Guid.Empty, StandardApprovalDecisionError.DataIntegrityError);

                request = ApprovalSubmissionRepository.RetrieveRequest(service, link.RequestId.Value);
                version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(service, link.SubmissionVersionId.Value);
            }
            catch (Exception)
            {
                return Failure(Guid.Empty, Guid.Empty, StandardApprovalDecisionError.DataIntegrityError);
            }

            var requestId = link.RequestId.Value;
            var versionId = link.SubmissionVersionId.Value;
            if (version.RequestId != requestId)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.PairMismatch);
            if (!ApprovalResultSourceCodes.IsSupported(link.Source)
                || string.IsNullOrWhiteSpace(link.ExternalRequestKey)
                || string.IsNullOrWhiteSpace(link.RowVersion)
                || string.IsNullOrWhiteSpace(request.RowVersion)
                || string.IsNullOrWhiteSpace(version.RowVersion))
            {
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DataIntegrityError);
            }

            if (link.Status == ApprovalLinkStatus.送信待ち)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.LinkNotReady);
            if (link.Status == ApprovalLinkStatus.連携不明)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.ResultUnknown);
            if (link.Status == ApprovalLinkStatus.連携失敗)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.StateCannotRecord);
            if (link.Status != ApprovalLinkStatus.連携済み
                && link.Status != ApprovalLinkStatus.結果確認済み)
            {
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.LinkNotReady);
            }
            if (!link.ResultKnown)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DataIntegrityError);
            if (!link.DecidedAt.HasValue || !link.ResponseAt.HasValue)
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DataIntegrityError);
            if (!TryParseDecision(link.DecisionCode, out var decision))
                return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DecisionUnsupported);

            var expected = ExpectedStatuses(decision);
            var logName = DecisionLogName(link, decision);
            var idempotencyKey = BuildIdempotencyKey(link.Id, decision);
            var replay = OperationLogRepository.FindApproval(service, OperationCode, idempotencyKey);
            if (link.Status == ApprovalLinkStatus.結果確認済み)
            {
                // 承認は、反映済み／反映失敗（反映前に確定した理由で止めた）のどちらでも確定済みとして扱う。
                var expectedRequestStatus = expected.RequestStatus;
                var expectedVersionStatus = expected.SubmissionVersionStatus;
                if (decision == ApprovalDecision.承認
                    && request.Status == ApprovalRequestStatus.反映済み
                    && version.Status == ApprovalSubmissionVersionStatus.反映済み)
                {
                    expectedRequestStatus = ApprovalRequestStatus.反映済み;
                    expectedVersionStatus = ApprovalSubmissionVersionStatus.反映済み;
                }
                else if (decision == ApprovalDecision.承認
                    && request.Status == ApprovalRequestStatus.反映失敗
                    && version.Status == ApprovalSubmissionVersionStatus.反映失敗)
                {
                    expectedRequestStatus = ApprovalRequestStatus.反映失敗;
                    expectedVersionStatus = ApprovalSubmissionVersionStatus.反映失敗;
                }
                var replayIsConsistent = replay != null
                    && replay.TargetRequestId == requestId
                    && replay.TargetSubmissionVersionId == versionId
                    && string.Equals(replay.Name, logName, StringComparison.Ordinal)
                    && request.Status == expectedRequestStatus
                    && version.Status == expectedVersionStatus;
                return replayIsConsistent
                    ? Success(requestId, versionId, request.Status, version.Status, isReplay: true)
                    : Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DataIntegrityError);
            }

            if (replay != null)
            {
                if (replay.ResultCode == "Success"
                    && replay.TargetRequestId == requestId
                    && replay.TargetSubmissionVersionId == versionId
                    && string.Equals(replay.Name, logName, StringComparison.Ordinal))
                {
                    return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.DataIntegrityError);
                }
                if (replay.ResultCode == "Success")
                    return Reject(service, requestId, versionId, initiatingUserId, link, StandardApprovalDecisionError.IdempotencyKeyConflict);
            }

            var contract = ApprovalContract.ValidateDecision(new ApprovalDecisionInput
            {
                RequestStatus = request.Status,
                SubmissionVersionStatus = version.Status,
                RequestId = requestId.ToString("D"),
                SubmissionVersionId = versionId.ToString("D"),
                VerifiedRequestId = requestId.ToString("D"),
                VerifiedSubmissionVersionId = versionId.ToString("D"),
                Decision = decision,
                Source = link.Source,
                ResultKnown = link.ResultKnown,
            });
            if (!contract.IsValid)
                return Reject(service, requestId, versionId, initiatingUserId, link, MapContractError(contract.Error));

            if (decision == ApprovalDecision.承認)
            {
                var reflection = ApprovalReflectionApplyService.ApplyStandardApproved(
                    service,
                    approvalLinkId,
                    initiatingUserId,
                    DateTime.UtcNow);
                if (!reflection.Success)
                {
                    return Reject(service, requestId, versionId, initiatingUserId, link, reflection.ErrorCode);
                }

                OperationLogRepository.RecordApproval(
                    service,
                    OperationCode,
                    "Success",
                    null,
                    initiatingUserId,
                    requestId,
                    versionId,
                    logName,
                    idempotencyKey);
                return Success(
                    requestId,
                    versionId,
                    reflection.RequestStatus,
                    reflection.SubmissionVersionStatus,
                    isReplay: reflection.IsReplay);
            }

            ApprovalSubmissionRepository.UpdateRequestStatus(
                service,
                request,
                expected.RequestStatus,
                releaseActiveRequestKey: decision == ApprovalDecision.却下);
            ApprovalSubmissionRepository.UpdateSubmissionVersionStatus(service, version, expected.SubmissionVersionStatus);
            MarkLinkDecisionApplied(service, link);
            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                "Success",
                null,
                initiatingUserId,
                requestId,
                versionId,
                logName,
                idempotencyKey);

            return Success(requestId, versionId, expected.RequestStatus, expected.SubmissionVersionStatus, isReplay: false);
        }

        private static void MarkLinkDecisionApplied(
            IOrganizationService service,
            ApprovalDecisionRepository.ApprovalLinkRecord link)
        {
            var entity = new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, link.Id)
            {
                ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                RowVersion = link.RowVersion,
            };
            ApprovalSubmissionRepository.UpdateAsApprovalServer(service, entity);
        }

        private static StandardApprovalDecisionResult Reject(
            IOrganizationService service,
            Guid requestId,
            Guid versionId,
            Guid initiatingUserId,
            ApprovalDecisionRepository.ApprovalLinkRecord link,
            string errorCode)
        {
            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                "Rejected",
                errorCode,
                initiatingUserId,
                requestId == Guid.Empty ? (Guid?)null : requestId,
                versionId == Guid.Empty ? (Guid?)null : versionId,
                $"decision:{link.DecisionCode}",
                string.IsNullOrWhiteSpace(link.ExternalRequestKey) ? null : link.ExternalRequestKey);
            return Failure(requestId, versionId, errorCode);
        }

        private static (ApprovalRequestStatus RequestStatus, ApprovalSubmissionVersionStatus SubmissionVersionStatus)
            ExpectedStatuses(ApprovalDecision decision)
            => decision switch
            {
                ApprovalDecision.承認 => (ApprovalRequestStatus.承認済み, ApprovalSubmissionVersionStatus.承認済み),
                ApprovalDecision.差戻し => (ApprovalRequestStatus.差戻し, ApprovalSubmissionVersionStatus.差戻し),
                ApprovalDecision.却下 => (ApprovalRequestStatus.却下, ApprovalSubmissionVersionStatus.却下),
                _ => throw new InvalidOperationException("未対応の承認結果です。"),
            };

        private static string MapContractError(ApprovalContractError error)
            => error switch
            {
                ApprovalContractError.PairMismatch => StandardApprovalDecisionError.PairMismatch,
                ApprovalContractError.ResultUnknown => StandardApprovalDecisionError.ResultUnknown,
                ApprovalContractError.DecisionUnsupported => StandardApprovalDecisionError.DecisionUnsupported,
                ApprovalContractError.StateCannotRecord => StandardApprovalDecisionError.StateCannotRecord,
                _ => StandardApprovalDecisionError.DataIntegrityError,
            };

        private static bool TryParseDecision(string value, out ApprovalDecision decision)
            => Enum.TryParse(value, false, out decision)
               && Enum.IsDefined(typeof(ApprovalDecision), decision);

        private static string BuildIdempotencyKey(Guid linkId, ApprovalDecision decision)
            => $"standard-decision-{linkId:N}-{decision}";

        private static string DecisionLogName(
            ApprovalDecisionRepository.ApprovalLinkRecord link,
            ApprovalDecision decision)
            => $"decision:{decision}:link:{link.Id}";

        private static StandardApprovalDecisionResult Success(
            Guid requestId,
            Guid versionId,
            ApprovalRequestStatus requestStatus,
            ApprovalSubmissionVersionStatus versionStatus,
            bool isReplay)
            => new StandardApprovalDecisionResult
            {
                Success = true,
                IsReplay = isReplay,
                RequestId = requestId,
                SubmissionVersionId = versionId,
                RequestStatus = requestStatus,
                SubmissionVersionStatus = versionStatus,
            };

        private static StandardApprovalDecisionResult Failure(Guid requestId, Guid versionId, string errorCode)
            => new StandardApprovalDecisionResult
            {
                Success = false,
                RequestId = requestId,
                SubmissionVersionId = versionId,
                ErrorCode = errorCode,
            };
    }
}
