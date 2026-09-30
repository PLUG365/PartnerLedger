using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace PartnerLedger.Plugins
{
    public static class ApprovalReflectionApplyError
    {
        public const string InvalidInput = "invalid-input";
        public const string DataIntegrityError = "data-integrity-error";
        public const string PairMismatch = "pair-mismatch";
        public const string DecisionNotApproved = "decision-not-approved";
        public const string StateCannotApply = "state-cannot-apply";
        public const string IdempotencyKeyConflict = "idempotency-key-conflict";
        public const string SettingsUnavailable = "settings-unavailable";
        public const string PolicyInvalid = "policy-invalid";
        public const string PolicyVersionMismatch = "policy-version-mismatch";
        public const string PolicyResolutionFailed = "policy-resolution-failed";
        public const string TargetResolutionFailed = "target-resolution-failed";
        public const string ChangeSetInvalid = "changeset-invalid";
        public const string UpdatePlanInvalid = "update-plan-invalid";
        public const string TargetChanged = "target-changed";
        public const string TargetInactive = "target-inactive";
        // 主担当の変更（2026-09-29）。どれも最初の書き込みの前に決め、「反映失敗」として記録する。
        public const string MainOwnerUnavailable = "main-owner-unavailable";
        public const string MainOwnerUnchanged = "main-owner-unchanged";
        public const string MainOwnerShareUnavailable = "main-owner-share-unavailable";
        public const string MainOwnerRowOwnerUnavailable = "main-owner-row-owner-unavailable";
    }

    public sealed class ApprovalReflectionApplyResult
    {
        public bool Success { get; set; }
        public bool IsReplay { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        /// <summary>反映せずに「反映失敗」として記録したときの理由コード。反映済みなら空。</summary>
        public string FailureCode { get; set; } = string.Empty;
        public Guid RequestId { get; set; }
        public Guid SubmissionVersionId { get; set; }
        public Guid TargetId { get; set; }
        public string TargetEntityName { get; set; } = string.Empty;
        public ApprovalRequestStatus RequestStatus { get; set; }
        public ApprovalSubmissionVersionStatus SubmissionVersionStatus { get; set; }
    }

    /// <summary>
    /// 承認済みの提出版を、設定・変更セット・対象行の再検証後に反映済みへ進める。
    /// 標準承認判定の同期プラグインから呼ばれ、そのトランザクション内で対象・版・申請・監査を扱う。
    /// </summary>
    public static class ApprovalReflectionApplyService
    {
        public const string OperationCode = "ApplyApprovalReflection";
        public const string FailureResultCode = "ReflectionFailed";

        /// <summary>
        /// 標準pl_ApprovalLink Updateで確定した承認結果から、反映待ち遷移と対象反映を
        /// 同じ同期トランザクション内で完了する。入力に冪等キーや対象情報は持たせず、
        /// Link IDからサーバー側でキーを導出する。
        /// </summary>
        public static ApprovalReflectionApplyResult ApplyStandardApproved(
            IOrganizationService service,
            Guid approvalLinkId,
            Guid initiatingUserId,
            DateTime reflectedAtUtc)
        {
            if (service == null || approvalLinkId == Guid.Empty || initiatingUserId == Guid.Empty)
                return Failure(Guid.Empty, Guid.Empty, ApprovalReflectionApplyError.InvalidInput);

            var idempotencyKey = $"standard-reflection-{approvalLinkId:N}";
            ApprovalDecisionRepository.ApprovalLinkRecord link;
            ApprovalSubmissionRepository.RequestRecord request;
            ApprovalSubmissionRepository.SubmissionVersionRecord version;
            try
            {
                link = ApprovalDecisionRepository.Retrieve(service, approvalLinkId);
                if (!link.RequestId.HasValue
                    || !link.SubmissionVersionId.HasValue
                    || !ApprovalResultSourceCodes.IsSupported(link.Source)
                    || string.IsNullOrWhiteSpace(link.ExternalRequestKey)
                    || string.IsNullOrWhiteSpace(link.RowVersion))
                {
                    return Failure(Guid.Empty, Guid.Empty, ApprovalReflectionApplyError.DataIntegrityError);
                }

                request = ApprovalSubmissionRepository.RetrieveRequest(service, link.RequestId.Value);
                version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(service, link.SubmissionVersionId.Value);
            }
            catch (InvalidPluginExecutionException)
            {
                return Failure(Guid.Empty, Guid.Empty, ApprovalReflectionApplyError.DataIntegrityError);
            }

            var requestId = link.RequestId.Value;
            var versionId = link.SubmissionVersionId.Value;
            if (version.RequestId != requestId)
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.PairMismatch);
            }
            if (!link.ResultKnown
                || !link.DecidedAt.HasValue
                || !link.ResponseAt.HasValue
                || !string.Equals(link.DecisionCode, ApprovalDecision.承認.ToString(), StringComparison.Ordinal))
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.DecisionNotApproved);
            }

            var reflectionLogName = $"reflection-apply:link:{link.Id}";
            var replay = OperationLogRepository.FindSuccessApproval(service, OperationCode, idempotencyKey);
            if (replay != null)
            {
                if (replay.TargetRequestId != requestId
                    || replay.TargetSubmissionVersionId != versionId
                    || !string.Equals(replay.Name, reflectionLogName, StringComparison.Ordinal))
                {
                    return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                        ApprovalReflectionApplyError.IdempotencyKeyConflict);
                }

                return link.Status == ApprovalLinkStatus.結果確認済み
                    && request.Status == ApprovalRequestStatus.反映済み
                    && version.Status == ApprovalSubmissionVersionStatus.反映済み
                    ? Success(requestId, versionId, Guid.Empty, string.Empty, isReplay: true)
                    : Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                        ApprovalReflectionApplyError.DataIntegrityError);
            }

            if (link.Status != ApprovalLinkStatus.連携済み
                || request.Status != ApprovalRequestStatus.提出中
                || version.Status != ApprovalSubmissionVersionStatus.提出済み
                || string.IsNullOrWhiteSpace(request.RowVersion)
                || string.IsNullOrWhiteSpace(version.RowVersion)
                || string.IsNullOrWhiteSpace(request.PolicyVersion)
                || string.IsNullOrWhiteSpace(version.PolicyVersion))
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.StateCannotApply);
            }
            if (!string.Equals(request.PolicyVersion, version.PolicyVersion, StringComparison.Ordinal))
            {
                return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                    reflectionLogName, idempotencyKey, ApprovalReflectionApplyError.PolicyVersionMismatch);
            }

            var settingsResult = ApprovalSettingsRepository.RetrieveActive(service);
            if (!settingsResult.IsValid)
            {
                var settingsError = settingsResult.Error == ApprovalSettingsReadError.PolicyInvalid
                    ? ApprovalReflectionApplyError.PolicyInvalid
                    : ApprovalReflectionApplyError.SettingsUnavailable;
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link, settingsError);
            }
            if (!string.Equals(request.PolicyVersion, settingsResult.Settings!.PolicyVersion, StringComparison.Ordinal))
            {
                return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                    reflectionLogName, idempotencyKey, ApprovalReflectionApplyError.PolicyVersionMismatch);
            }

            var targetResult = ApprovalTargetRepository.RetrieveForRequest(service, requestId);
            if (!targetResult.IsValid && targetResult.Error == ApprovalTargetResolutionError.TargetInactive)
            {
                return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                    reflectionLogName, idempotencyKey, ApprovalReflectionApplyError.TargetInactive);
            }
            if (!targetResult.IsValid)
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.TargetResolutionFailed);
            }

            var policyResolution = ApprovalPolicyContract.Resolve(
                settingsResult.Settings.Policy,
                request.RequestTypeCode,
                targetResult.Target!.EntityName);
            if (!policyResolution.IsValid)
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.PolicyResolutionFailed);
            }

            var changeSetResult = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = version.ChangeSetJson,
                ExpectedEntityName = policyResolution.Resolution!.EntityName,
                ExpectedTargetId = targetResult.Target.Id,
                AllowedAttributes = policyResolution.Resolution.AllowedAttributes,
            });
            if (!changeSetResult.IsValid)
            {
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.ChangeSetInvalid);
            }

            var updatePlan = ApprovalTargetUpdatePlanner.Build(
                targetResult.Target,
                changeSetResult.ChangeSet,
                version.TargetRowVersion);
            if (!updatePlan.IsValid)
            {
                if (updatePlan.Error == ApprovalTargetUpdatePlanError.TargetChangedSinceSubmission)
                {
                    return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                        reflectionLogName, idempotencyKey, ApprovalReflectionApplyError.TargetChanged);
                }
                if (updatePlan.Error == ApprovalTargetUpdatePlanError.TargetStateInvalid)
                {
                    return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                        reflectionLogName, idempotencyKey, ApprovalReflectionApplyError.TargetInactive);
                }
                return Reject(service, requestId, versionId, initiatingUserId, idempotencyKey, link,
                    ApprovalReflectionApplyError.UpdatePlanInvalid);
            }

            // 主担当の変更は、新しい主担当が通常の利用者か・今と違う人か・共有を整えられるかを、
            // 最初の書き込みより前に決める（反映失敗の記録は最初の書き込みの前でしか使えないため）。
            PartnerMainOwnerChangePlan? mainOwnerPlan = null;
            var mainOwnerOperation = changeSetResult.ChangeSet!.Operations
                .FirstOrDefault(operation => operation.AttributeName == PartnerStandardCreateContract.MainOwnerAttribute);
            if (mainOwnerOperation != null)
            {
                var approverTeamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                    service,
                    PartnerLedgerEnvironmentVariableNames.ApproverTeamId);
                mainOwnerPlan = PartnerMainOwnerChange.Plan(
                    service,
                    targetResult.Target.Id,
                    mainOwnerOperation.Value.UserIdValue ?? Guid.Empty,
                    initiatingUserId,
                    approverTeamId);
                if (!mainOwnerPlan.IsValid)
                {
                    return RecordReflectionFailure(service, request, version, link, initiatingUserId,
                        reflectionLogName, idempotencyKey, MainOwnerFailureCode(mainOwnerPlan.Error));
                }
            }

            // The approval result is not accepted as complete until the target update
            // succeeds. The two pending updates are internal, transient states inside
            // the same Dataverse transaction and are guarded by StandardApprovalCrudGuard.
            ApprovalSubmissionRepository.UpdateRequestStatus(service, request, ApprovalRequestStatus.反映待ち);
            ApprovalSubmissionRepository.UpdateSubmissionVersionStatus(
                service,
                version,
                ApprovalSubmissionVersionStatus.反映待ち);
            request = ApprovalSubmissionRepository.RetrieveRequest(service, requestId);
            version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(service, versionId);

            var normalizedReflectedAt = reflectedAtUtc.Kind == DateTimeKind.Utc
                ? reflectedAtUtc
                : reflectedAtUtc.ToUniversalTime();
            var versionUpdate = new UpdateRequest
            {
                Target = new Entity(ApprovalSubmissionRepository.SubmissionVersionEntityName, versionId)
                {
                    ["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.反映済み.ToString(),
                    ["pl_fixedat"] = normalizedReflectedAt,
                    RowVersion = version.RowVersion,
                },
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
            };
            var requestUpdate = new UpdateRequest
            {
                Target = new Entity(ApprovalSubmissionRepository.RequestEntityName, requestId)
                {
                    ["pl_requeststatuscode"] = ApprovalRequestStatus.反映済み.ToString(),
                    [ApprovalActiveRequestKey.AttributeName] = null,
                    RowVersion = request.RowVersion,
                },
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
            };
            var linkUpdate = new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, link.Id)
            {
                ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                RowVersion = link.RowVersion,
            };

            ApprovalServerWriteBypass.Apply(versionUpdate);
            ApprovalServerWriteBypass.Apply(requestUpdate);

            // Do not catch execution failures. Target, status, Link, and audit writes
            // must roll back together at the synchronous plug-in boundary.
            service.Execute(updatePlan.Plan!.UpdateRequest);
            if (mainOwnerPlan != null)
            {
                // 主担当の書き込みの後に、新しい主担当の共有の行→共有の順で書く。前の主担当の共有は変えない。
                PartnerMainOwnerChange.Execute(service, mainOwnerPlan);
            }
            service.Execute(versionUpdate);
            service.Execute(requestUpdate);
            ApprovalSubmissionRepository.UpdateAsApprovalServer(service, linkUpdate);
            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                "Success",
                null,
                initiatingUserId,
                requestId,
                versionId,
                reflectionLogName,
                idempotencyKey);

            return Success(
                requestId,
                versionId,
                targetResult.Target.Id,
                targetResult.Target.EntityName,
                isReplay: false);
        }

        /// <summary>
        /// 最初の書込みより前に確定した「反映できない」理由を、申請者に見える終端の状態として残す。
        /// 対象行へは書かない。例外を投げないので、承認結果（Linkの更新）と一緒に確定する。
        /// 申請の重複防止キーを解放し、申請者が同じ対象で申請し直せるようにする。
        /// </summary>
        private static ApprovalReflectionApplyResult RecordReflectionFailure(
            IOrganizationService service,
            ApprovalSubmissionRepository.RequestRecord request,
            ApprovalSubmissionRepository.SubmissionVersionRecord version,
            ApprovalDecisionRepository.ApprovalLinkRecord link,
            Guid initiatingUserId,
            string reflectionLogName,
            string idempotencyKey,
            string failureCode)
        {
            ApprovalSubmissionRepository.UpdateAsApprovalServer(
                service,
                new Entity(ApprovalSubmissionRepository.RequestEntityName, request.Id)
                {
                    ["pl_requeststatuscode"] = ApprovalRequestStatus.反映失敗.ToString(),
                    [ApprovalActiveRequestKey.AttributeName] = null,
                    ["pl_cancellationreason"] = FailureReasonText(failureCode),
                    RowVersion = request.RowVersion,
                });
            ApprovalSubmissionRepository.UpdateSubmissionVersionStatus(
                service,
                version,
                ApprovalSubmissionVersionStatus.反映失敗);
            ApprovalSubmissionRepository.UpdateAsApprovalServer(
                service,
                new Entity(StandardApprovalCrudContract.ApprovalLinkEntityName, link.Id)
                {
                    ["pl_linkstatuscode"] = ApprovalLinkStatus.結果確認済み.ToString(),
                    RowVersion = link.RowVersion,
                });
            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                FailureResultCode,
                failureCode,
                initiatingUserId,
                request.Id,
                version.Id,
                reflectionLogName,
                idempotencyKey);

            return new ApprovalReflectionApplyResult
            {
                Success = true,
                FailureCode = failureCode,
                RequestId = request.Id,
                SubmissionVersionId = version.Id,
                RequestStatus = ApprovalRequestStatus.反映失敗,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.反映失敗,
            };
        }

        private static string FailureReasonText(string failureCode)
            => failureCode switch
            {
                ApprovalReflectionApplyError.TargetChanged
                    => "承認待ちの間に、申請した項目が別の操作で変更されたため、反映しませんでした。今の内容を確認し、必要なら申請し直してください。",
                ApprovalReflectionApplyError.TargetInactive
                    => "承認待ちの間に、申請の対象が無効になったため、反映しませんでした。",
                ApprovalReflectionApplyError.PolicyVersionMismatch
                    => "承認待ちの間に承認の設定が変わったため、反映しませんでした。必要なら申請し直してください。",
                ApprovalReflectionApplyError.MainOwnerUnavailable
                    => "新しい主担当が通常の利用者ではない（無効になった、など）ため、反映しませんでした。別の人を選んで申請し直してください。",
                ApprovalReflectionApplyError.MainOwnerUnchanged
                    => "主担当がすでに申請した人になっているため、反映しませんでした。",
                ApprovalReflectionApplyError.MainOwnerShareUnavailable
                    => "新しい主担当の共有を整えられなかったため、反映しませんでした。共有設定を確認し、管理者に相談してください。",
                ApprovalReflectionApplyError.MainOwnerRowOwnerUnavailable
                    => "取引先の登録者も新しい主担当も共有設定を使える状態でない（PartnerLedgerのロールが無い、など）ため、反映しませんでした。新しい主担当にロールを割り当ててから申請し直してください。",
                _ => "承認の内容を反映できませんでした。管理者に確認してください。",
            };

        private static string MainOwnerFailureCode(PartnerMainOwnerChangeError error)
            => error switch
            {
                PartnerMainOwnerChangeError.SameAsCurrent => ApprovalReflectionApplyError.MainOwnerUnchanged,
                PartnerMainOwnerChangeError.ShareCannotBeArranged => ApprovalReflectionApplyError.MainOwnerShareUnavailable,
                PartnerMainOwnerChangeError.RowOwnerUnavailable => ApprovalReflectionApplyError.MainOwnerRowOwnerUnavailable,
                _ => ApprovalReflectionApplyError.MainOwnerUnavailable,
            };

        private static ApprovalReflectionApplyResult Reject(
            IOrganizationService service,
            Guid requestId,
            Guid versionId,
            Guid initiatingUserId,
            string idempotencyKey,
            ApprovalDecisionRepository.ApprovalLinkRecord link,
            string errorCode)
        {
            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                "Rejected",
                errorCode,
                initiatingUserId,
                requestId,
                versionId,
                $"reflection-apply:{link.Id}",
                idempotencyKey);
            return Failure(requestId, versionId, errorCode);
        }

        private static ApprovalReflectionApplyResult Success(
            Guid requestId,
            Guid versionId,
            Guid targetId,
            string targetEntityName,
            bool isReplay)
            => new ApprovalReflectionApplyResult
            {
                Success = true,
                IsReplay = isReplay,
                RequestId = requestId,
                SubmissionVersionId = versionId,
                TargetId = targetId,
                TargetEntityName = targetEntityName,
                RequestStatus = ApprovalRequestStatus.反映済み,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.反映済み,
            };

        private static ApprovalReflectionApplyResult Failure(Guid requestId, Guid versionId, string errorCode)
            => new ApprovalReflectionApplyResult
            {
                Success = false,
                RequestId = requestId,
                SubmissionVersionId = versionId,
                ErrorCode = errorCode,
            };
    }
}
