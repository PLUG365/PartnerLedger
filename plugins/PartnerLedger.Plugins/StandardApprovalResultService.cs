using System;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// グループ承認の結果を、標準のpl_ApprovalLink Updateから受け取る境界。
    /// 呼出し元は結果の確知性と判定値だけを渡し、外部要求キー・状態・時刻は
    /// 保存済みLinkとサーバー実行時刻から解決する。対象行の更新や承認状態の確定は行わない。
    /// </summary>
    public static class StandardApprovalResultService
    {
        // 監査ログの冪等キー照合に使う既存値。DecisionFlow撤去後も、記録済みログとの再送判定を保つため変更しない。
        public const string OperationCode = "StoreDecisionFlowResult";

        public static ApprovalResultIngressResult Apply(
            IOrganizationService service,
            Entity? target,
            Guid initiatingUserId,
            DateTime respondedAtUtc)
        {
            var contract = StandardApprovalCrudContract.ValidateApprovalLinkResultUpdate(target);
            if (!contract.IsValid)
            {
                return Failure(
                    target?.Id ?? Guid.Empty,
                    ApprovalLinkStatus.送信待ち,
                    false,
                    ApprovalResultIngressError.InvalidInput);
            }
            if (service == null || initiatingUserId == Guid.Empty || respondedAtUtc == DateTime.MinValue)
            {
                return Failure(
                    target!.Id,
                    ApprovalLinkStatus.送信待ち,
                    false,
                    ApprovalResultIngressError.InvalidInput);
            }

            var resultKnown = (bool)target!["pl_resultknown"];
            var decisionCode = target.GetAttributeValue<string>("pl_decisioncode");
            if (decisionCode != null)
            {
                decisionCode = decisionCode.Trim();
            }

            ApprovalDecisionRepository.ApprovalLinkRecord link;
            try
            {
                link = ApprovalDecisionRepository.Retrieve(service, target.Id);
            }
            catch (Exception)
            {
                return Failure(
                    target.Id,
                    ApprovalLinkStatus.送信待ち,
                    false,
                    ApprovalResultIngressError.DataIntegrityError);
            }

            if (!link.RequestId.HasValue
                || !link.SubmissionVersionId.HasValue
                || string.IsNullOrWhiteSpace(link.ExternalRequestKey)
                || string.IsNullOrWhiteSpace(link.RowVersion))
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.DataIntegrityError);
            }

            if (!IsPersistedStateCoherent(link))
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.DataIntegrityError);
            }

            ApprovalSubmissionRepository.RequestRecord request;
            try
            {
                request = ApprovalSubmissionRepository.RetrieveRequest(service, link.RequestId.Value);
            }
            catch (Exception)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.DataIntegrityError);
            }
            if (request.Status == ApprovalRequestStatus.取消)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.StateCannotIngest);
            }
            if (!ApprovalResultSourceCodes.IsSupported(link.Source))
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.DataIntegrityError);
            }

            var decision = default(ApprovalDecision);
            if (resultKnown && !TryParseDecision(decisionCode, out decision))
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.DecisionUnsupported);
            }

            var normalizedDecisionCode = resultKnown ? decision.ToString() : null;
            var idempotencyKey = BuildIdempotencyKey(link.Id, resultKnown, normalizedDecisionCode);
            var logName = ResultLogName(link.Id, normalizedDecisionCode);
            var replay = OperationLogRepository.FindApproval(service, OperationCode, idempotencyKey);
            if (replay != null && (replay.ResultCode == "Success" || replay.ResultCode == "Uncertain"))
            {
                var expectedStatus = resultKnown ? ApprovalLinkStatus.連携済み : ApprovalLinkStatus.連携不明;
                var sameCurrentState = replay.TargetRequestId == link.RequestId.Value
                    && replay.TargetSubmissionVersionId == link.SubmissionVersionId.Value
                    && string.Equals(replay.Name, logName, StringComparison.Ordinal)
                    && IsCurrentResultState(link, expectedStatus, resultKnown, normalizedDecisionCode);
                return sameCurrentState
                    ? Success(link.Id, link.Status, resultKnown, isReplay: true)
                    : Failure(
                        link.Id,
                        link.Status,
                        link.ResultKnown,
                        ApprovalResultIngressError.IdempotencyKeyConflict);
            }

            if (link.Status == ApprovalLinkStatus.結果確認済み
                || link.Status == ApprovalLinkStatus.連携失敗)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.StateCannotIngest);
            }
            if (link.Status != ApprovalLinkStatus.送信待ち
                && link.Status != ApprovalLinkStatus.連携不明
                && link.Status != ApprovalLinkStatus.連携済み)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.LinkNotReady);
            }
            if (link.ResultKnown)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.StateCannotIngest);
            }
            if (!resultKnown && link.Status == ApprovalLinkStatus.連携不明)
            {
                return Failure(
                    link.Id,
                    link.Status,
                    link.ResultKnown,
                    ApprovalResultIngressError.StateCannotIngest);
            }

            var normalizedRespondedAt = NormalizeUtc(respondedAtUtc);
            target["pl_linkstatuscode"] = resultKnown
                ? ApprovalLinkStatus.連携済み.ToString()
                : ApprovalLinkStatus.連携不明.ToString();
            target["pl_decisioncode"] = resultKnown ? normalizedDecisionCode! : string.Empty;
            target["pl_resultknown"] = resultKnown;
            target["pl_decidedat"] = resultKnown ? normalizedRespondedAt : null;
            target["pl_responseat"] = normalizedRespondedAt;

            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                resultKnown ? "Success" : "Uncertain",
                null,
                initiatingUserId,
                link.RequestId.Value,
                link.SubmissionVersionId.Value,
                logName,
                idempotencyKey);

            return Success(
                link.Id,
                resultKnown ? ApprovalLinkStatus.連携済み : ApprovalLinkStatus.連携不明,
                resultKnown,
                isReplay: false);
        }

        private static bool IsPersistedStateCoherent(ApprovalDecisionRepository.ApprovalLinkRecord link)
        {
            if (link.Status == ApprovalLinkStatus.送信待ち || link.Status == ApprovalLinkStatus.連携不明)
            {
                return !link.ResultKnown
                    && string.IsNullOrEmpty(link.DecisionCode)
                    && !link.DecidedAt.HasValue;
            }

            if (link.Status == ApprovalLinkStatus.連携済み
                || link.Status == ApprovalLinkStatus.結果確認済み)
            {
                return link.ResultKnown
                    && link.DecidedAt.HasValue
                    && TryParseDecision(link.DecisionCode, out _);
            }

            // 連携失敗は結果値を再解釈する経路へ渡さないため、ここでは内容を推測しない。
            return true;
        }

        private static bool IsCurrentResultState(
            ApprovalDecisionRepository.ApprovalLinkRecord link,
            ApprovalLinkStatus expectedStatus,
            bool resultKnown,
            string? decisionCode)
            => (link.Status == expectedStatus
                || (resultKnown && link.Status == ApprovalLinkStatus.結果確認済み))
               && link.ResultKnown == resultKnown
               && link.ResponseAt.HasValue
               && (resultKnown
                   ? link.DecidedAt.HasValue && string.Equals(link.DecisionCode, decisionCode, StringComparison.Ordinal)
                   : !link.DecidedAt.HasValue && string.IsNullOrEmpty(link.DecisionCode));

        private static string BuildIdempotencyKey(Guid linkId, bool resultKnown, string? decisionCode)
            => resultKnown
                ? $"standard-result-{linkId:N}-known-{decisionCode}"
                : $"standard-result-{linkId:N}-unknown";

        private static string ResultLogName(Guid linkId, string? decisionCode)
            => decisionCode == null
                ? $"result:unknown:link:{linkId}"
                : $"result:{decisionCode}:link:{linkId}";

        private static bool TryParseDecision(string? value, out ApprovalDecision decision)
            => Enum.TryParse(value, false, out decision)
               && Enum.IsDefined(typeof(ApprovalDecision), decision);

        private static ApprovalResultIngressResult Success(
            Guid linkId,
            ApprovalLinkStatus status,
            bool resultKnown,
            bool isReplay)
            => new ApprovalResultIngressResult
            {
                Success = true,
                IsReplay = isReplay,
                ApprovalLinkId = linkId,
                LinkStatus = status,
                ResultKnown = resultKnown,
            };

        private static ApprovalResultIngressResult Failure(
            Guid linkId,
            ApprovalLinkStatus status,
            bool resultKnown,
            string errorCode)
            => new ApprovalResultIngressResult
            {
                Success = false,
                ApprovalLinkId = linkId,
                LinkStatus = status,
                ResultKnown = resultKnown,
                ErrorCode = errorCode,
            };

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }
}
