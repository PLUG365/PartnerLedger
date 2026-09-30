using System;
using System.Collections.Generic;
using System.Globalization;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 承認基盤のサーバー側状態・版照合契約。
    /// Dataverse永続化、DecisionFlow呼出し、行共有は行わず、
    /// 申請API／反映処理が共通に使う拒否条件だけを固定する。
    /// </summary>
    public enum ApprovalRequestStatus
    {
        下書き,
        提出中,
        承認済み,
        差戻し,
        却下,
        取消,
        反映待ち,
        反映済み,
        反映失敗,
    }

    public enum ApprovalSubmissionVersionStatus
    {
        下書き,
        提出済み,
        承認済み,
        差戻し,
        却下,
        取消,
        反映待ち,
        反映済み,
        反映失敗,
    }

    public enum ApprovalDecision
    {
        承認,
        差戻し,
        却下,
    }

    public enum ApprovalResultSource
    {
        Unknown = 0,
        // 1は撤去したDecisionFlow。値の再利用を避けるため欠番にする。
        PowerAutomateGroup = 2,
    }

    public static class ApprovalResultSourceCodes
    {
        public const string PowerAutomateGroup = "PowerAutomateGroup";

        public static ApprovalResultSource Parse(string? value)
            => value switch
            {
                PowerAutomateGroup => ApprovalResultSource.PowerAutomateGroup,
                _ => ApprovalResultSource.Unknown,
            };

        public static bool IsSupported(ApprovalResultSource source)
            => source == ApprovalResultSource.PowerAutomateGroup;
    }

    public enum ApprovalLinkStatus
    {
        送信待ち,
        連携済み,
        結果確認済み,
        連携不明,
        連携失敗,
    }

    public enum ApprovalContractError
    {
        None,
        RequestAndVersionRequired,
        PairMismatch,
        ResultSourceUntrusted,
        ResultUnknown,
        DecisionUnsupported,
        StateCannotRecord,
    }

    public sealed class ApprovalContractResult
    {
        private ApprovalContractResult(bool isValid, ApprovalContractError error)
        {
            IsValid = isValid;
            Error = error;
        }

        public bool IsValid { get; }
        public ApprovalContractError Error { get; }

        public static ApprovalContractResult Valid()
            => new ApprovalContractResult(true, ApprovalContractError.None);

        public static ApprovalContractResult Invalid(ApprovalContractError error)
            => new ApprovalContractResult(false, error);
    }

    public sealed class ApprovalDecisionInput
    {
        public ApprovalRequestStatus RequestStatus { get; set; }
        public ApprovalSubmissionVersionStatus SubmissionVersionStatus { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string SubmissionVersionId { get; set; } = string.Empty;
        public string VerifiedRequestId { get; set; } = string.Empty;
        public string VerifiedSubmissionVersionId { get; set; } = string.Empty;
        public ApprovalDecision Decision { get; set; }
        public ApprovalResultSource Source { get; set; }
        public bool ResultKnown { get; set; }
    }

    public static class ApprovalContract
    {
        private static readonly IDictionary<ApprovalRequestStatus, ApprovalRequestStatus[]> RequestTransitions
            = new Dictionary<ApprovalRequestStatus, ApprovalRequestStatus[]>
            {
                [ApprovalRequestStatus.下書き] = new[] { ApprovalRequestStatus.提出中, ApprovalRequestStatus.取消 },
                [ApprovalRequestStatus.提出中] = new[] { ApprovalRequestStatus.承認済み, ApprovalRequestStatus.差戻し, ApprovalRequestStatus.却下, ApprovalRequestStatus.取消, ApprovalRequestStatus.反映失敗 },
                [ApprovalRequestStatus.承認済み] = new[] { ApprovalRequestStatus.反映待ち },
                [ApprovalRequestStatus.差戻し] = new[] { ApprovalRequestStatus.提出中, ApprovalRequestStatus.取消 },
                [ApprovalRequestStatus.却下] = Array.Empty<ApprovalRequestStatus>(),
                [ApprovalRequestStatus.取消] = Array.Empty<ApprovalRequestStatus>(),
                [ApprovalRequestStatus.反映待ち] = new[] { ApprovalRequestStatus.反映済み, ApprovalRequestStatus.反映失敗 },
                [ApprovalRequestStatus.反映済み] = Array.Empty<ApprovalRequestStatus>(),
                [ApprovalRequestStatus.反映失敗] = new[] { ApprovalRequestStatus.反映待ち },
            };

        private static readonly IDictionary<ApprovalSubmissionVersionStatus, ApprovalSubmissionVersionStatus[]> VersionTransitions
            = new Dictionary<ApprovalSubmissionVersionStatus, ApprovalSubmissionVersionStatus[]>
            {
                [ApprovalSubmissionVersionStatus.下書き] = new[] { ApprovalSubmissionVersionStatus.提出済み, ApprovalSubmissionVersionStatus.取消 },
                [ApprovalSubmissionVersionStatus.提出済み] = new[] { ApprovalSubmissionVersionStatus.承認済み, ApprovalSubmissionVersionStatus.差戻し, ApprovalSubmissionVersionStatus.却下, ApprovalSubmissionVersionStatus.取消, ApprovalSubmissionVersionStatus.反映失敗 },
                [ApprovalSubmissionVersionStatus.承認済み] = new[] { ApprovalSubmissionVersionStatus.反映待ち },
                [ApprovalSubmissionVersionStatus.差戻し] = new[] { ApprovalSubmissionVersionStatus.提出済み },
                [ApprovalSubmissionVersionStatus.却下] = Array.Empty<ApprovalSubmissionVersionStatus>(),
                [ApprovalSubmissionVersionStatus.取消] = Array.Empty<ApprovalSubmissionVersionStatus>(),
                [ApprovalSubmissionVersionStatus.反映待ち] = new[] { ApprovalSubmissionVersionStatus.反映済み, ApprovalSubmissionVersionStatus.反映失敗 },
                [ApprovalSubmissionVersionStatus.反映済み] = Array.Empty<ApprovalSubmissionVersionStatus>(),
                [ApprovalSubmissionVersionStatus.反映失敗] = new[] { ApprovalSubmissionVersionStatus.反映待ち },
            };

        private static bool HasText(string value) => !string.IsNullOrWhiteSpace(value);

        private static bool CanTransition<T>(IDictionary<T, T[]> transitions, T from, T to)
            where T : struct
            => transitions.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

        public static ApprovalContractResult ValidateDecision(ApprovalDecisionInput input)
        {
            if (!HasText(input.RequestId) || !HasText(input.SubmissionVersionId))
                return ApprovalContractResult.Invalid(ApprovalContractError.RequestAndVersionRequired);
            if (input.RequestId != input.VerifiedRequestId || input.SubmissionVersionId != input.VerifiedSubmissionVersionId)
                return ApprovalContractResult.Invalid(ApprovalContractError.PairMismatch);
            if (!ApprovalResultSourceCodes.IsSupported(input.Source))
                return ApprovalContractResult.Invalid(ApprovalContractError.ResultSourceUntrusted);
            if (!input.ResultKnown)
                return ApprovalContractResult.Invalid(ApprovalContractError.ResultUnknown);
            if (!Enum.IsDefined(typeof(ApprovalDecision), input.Decision))
                return ApprovalContractResult.Invalid(ApprovalContractError.DecisionUnsupported);

            var requestTarget = input.Decision == ApprovalDecision.承認
                ? ApprovalRequestStatus.承認済み
                : input.Decision == ApprovalDecision.差戻し ? ApprovalRequestStatus.差戻し : ApprovalRequestStatus.却下;
            var versionTarget = input.Decision == ApprovalDecision.承認
                ? ApprovalSubmissionVersionStatus.承認済み
                : input.Decision == ApprovalDecision.差戻し ? ApprovalSubmissionVersionStatus.差戻し : ApprovalSubmissionVersionStatus.却下;
            return CanTransition(RequestTransitions, input.RequestStatus, requestTarget)
                && CanTransition(VersionTransitions, input.SubmissionVersionStatus, versionTarget)
                ? ApprovalContractResult.Valid()
                : ApprovalContractResult.Invalid(ApprovalContractError.StateCannotRecord);
        }

        public static string NormalizeIso(string value)
            => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .UtcDateTime
                .ToString("o", CultureInfo.InvariantCulture);
    }
}
