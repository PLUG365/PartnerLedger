using System;

namespace PartnerLedger.Plugins
{
    public enum ApprovalCancellationError
    {
        None,
        InvalidInput,
        Unauthorized,
        RequestNotFound,
        RequestInactive,
        RequestStateInvalid,
        ApprovalLinkNotFound,
        ApprovalLinkStateInvalid,
        SubmissionVersionNotFound,
        SubmissionVersionPairMismatch,
        SubmissionVersionStateInvalid,
        DataIntegrityError,
        ConcurrencyConflict,
    }

    /// <summary>取消要求をどの経路で処理するか。</summary>
    public enum ApprovalCancellationPath
    {
        None,
        /// <summary>取消済みへの再送。更新・監査を追加しない。</summary>
        Replay,
        /// <summary>下書き・差戻しの取下げ（申請者本人、または申請者不在時の管理者）。</summary>
        Withdraw,
        /// <summary>提出中の管理者取消（PL-033）。</summary>
        AdministratorCancel,
    }

    public sealed class ApprovalCancellationAuthorization
    {
        private ApprovalCancellationAuthorization(ApprovalCancellationPath path, ApprovalCancellationError error)
        {
            Path = path;
            Error = error;
        }

        public ApprovalCancellationPath Path { get; }
        public ApprovalCancellationError Error { get; }

        public static ApprovalCancellationAuthorization Allow(ApprovalCancellationPath path)
            => new ApprovalCancellationAuthorization(path, ApprovalCancellationError.None);

        public static ApprovalCancellationAuthorization Deny(ApprovalCancellationError error)
            => new ApprovalCancellationAuthorization(ApprovalCancellationPath.None, error);
    }

    public sealed class ApprovalCancellationValidationResult
    {
        private ApprovalCancellationValidationResult(bool isValid, ApprovalCancellationError error)
        {
            IsValid = isValid;
            Error = error;
        }

        public bool IsValid { get; }
        public ApprovalCancellationError Error { get; }

        public static ApprovalCancellationValidationResult Valid()
            => new ApprovalCancellationValidationResult(true, ApprovalCancellationError.None);

        public static ApprovalCancellationValidationResult Invalid(ApprovalCancellationError error)
            => new ApprovalCancellationValidationResult(false, error);
    }

    /// <summary>
    /// 管理者による承認取消の副作用なし契約。
    /// Dataverseの再取得・ロール判定・更新はサービス側で行うが、許可する状態境界は
    /// この契約へ集約して、申請状態と外部状態の組み合わせをテスト可能にする。
    /// </summary>
    public static class ApprovalCancellationContract
    {
        public const int ReasonMaxLength = 2000;

        /// <summary>
        /// 申請状態と実行者から取消の経路を決める（PL-033／PL-034）。
        /// 申請者本人は下書き・差戻しを理由なしで取り下げられる。管理者は提出中を取り消せ、
        /// 申請者不在の下書き・差戻しも取り下げられるが、他人の申請を扱うときは理由を必須にする。
        /// 申請者でも管理者でもない実行者には、状態によらずUnauthorizedを返す。
        /// </summary>
        public static ApprovalCancellationAuthorization Authorize(
            ApprovalRequestStatus requestStatus,
            bool isRequester,
            bool isAdministrator,
            string? reason)
        {
            var normalizedReason = reason?.Trim() ?? string.Empty;
            if (normalizedReason.Length > ReasonMaxLength)
                return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.InvalidInput);
            if (!isRequester && !isAdministrator)
                return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.Unauthorized);

            switch (requestStatus)
            {
                case ApprovalRequestStatus.取消:
                    return ApprovalCancellationAuthorization.Allow(ApprovalCancellationPath.Replay);
                case ApprovalRequestStatus.下書き:
                case ApprovalRequestStatus.差戻し:
                    if (!isRequester && normalizedReason.Length == 0)
                        return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.InvalidInput);
                    return ApprovalCancellationAuthorization.Allow(ApprovalCancellationPath.Withdraw);
                case ApprovalRequestStatus.提出中:
                    if (!isAdministrator)
                        return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.Unauthorized);
                    if (normalizedReason.Length == 0)
                        return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.InvalidInput);
                    return ApprovalCancellationAuthorization.Allow(ApprovalCancellationPath.AdministratorCancel);
                default:
                    return ApprovalCancellationAuthorization.Deny(ApprovalCancellationError.RequestStateInvalid);
            }
        }

        public static ApprovalCancellationValidationResult Validate(
            ApprovalRequestStatus requestStatus,
            int requestStateCode,
            int approvalLinkCount,
            ApprovalLinkStatus? approvalLinkStatus,
            bool approvalResultKnown,
            ApprovalSubmissionVersionStatus submissionVersionStatus,
            bool requestVersionPairMatches,
            string? reason)
        {
            var normalizedReason = reason?.Trim() ?? string.Empty;
            if (normalizedReason.Length == 0 || normalizedReason.Length > ReasonMaxLength)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.InvalidInput);
            if (requestStateCode != 0)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.RequestInactive);
            if (requestStatus == ApprovalRequestStatus.取消)
                return ApprovalCancellationValidationResult.Valid();
            if (requestStatus != ApprovalRequestStatus.提出中)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.RequestStateInvalid);
            if (approvalLinkCount != 1 || !approvalLinkStatus.HasValue)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.ApprovalLinkNotFound);
            if (approvalLinkStatus.Value != ApprovalLinkStatus.送信待ち
                && approvalLinkStatus.Value != ApprovalLinkStatus.連携済み)
            {
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.ApprovalLinkStateInvalid);
            }
            if (!requestVersionPairMatches)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.SubmissionVersionPairMismatch);
            if (submissionVersionStatus != ApprovalSubmissionVersionStatus.提出済み)
                return ApprovalCancellationValidationResult.Invalid(ApprovalCancellationError.SubmissionVersionStateInvalid);

            // A known result may already be waiting for the decision/reflection step. It is
            // still cancellable while the request is 提出中; the late-result guard below
            // prevents that result from changing the target after cancellation.
            _ = approvalResultKnown;
            return ApprovalCancellationValidationResult.Valid();
        }

        public static bool IsAllowedAdministratorRole(string? roleName)
            => string.Equals(roleName, "PL システム管理", StringComparison.Ordinal)
               || string.Equals(roleName, "システム管理者", StringComparison.Ordinal)
               || string.Equals(roleName, "System Administrator", StringComparison.OrdinalIgnoreCase);
    }
}
