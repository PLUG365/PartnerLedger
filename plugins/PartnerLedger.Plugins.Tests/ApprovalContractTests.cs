using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public class ApprovalContractTests
    {
        private const string RequestId = "request-1";
        private const string SubmissionVersionId = "version-1";

        [Fact]
        public void グループ承認で対応が一致した判断だけを記録できる()
        {
            var input = new ApprovalDecisionInput
            {
                RequestStatus = ApprovalRequestStatus.提出中,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.提出済み,
                RequestId = RequestId,
                SubmissionVersionId = SubmissionVersionId,
                VerifiedRequestId = RequestId,
                VerifiedSubmissionVersionId = SubmissionVersionId,
                Decision = ApprovalDecision.承認,
                Source = ApprovalResultSource.PowerAutomateGroup,
                ResultKnown = true,
            };

            Assert.True(ApprovalContract.ValidateDecision(input).IsValid);
            input.Source = ApprovalResultSource.Unknown;
            Assert.Equal(ApprovalContractError.ResultSourceUntrusted, ApprovalContract.ValidateDecision(input).Error);
        }

        [Theory]
        [InlineData("DecisionFlow")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("powerautomategroup")]
        public void 撤去済みDecisionFlowと未知の結果提供元は信頼しない(string? sourceCode)
        {
            var source = ApprovalResultSourceCodes.Parse(sourceCode);

            Assert.Equal(ApprovalResultSource.Unknown, source);
            Assert.False(ApprovalResultSourceCodes.IsSupported(source));
        }

        [Fact]
        public void グループ承認だけを結果提供元として受け付ける()
        {
            Assert.Equal(ApprovalResultSource.PowerAutomateGroup, ApprovalResultSourceCodes.Parse(ApprovalResultSourceCodes.PowerAutomateGroup));
            Assert.True(ApprovalResultSourceCodes.IsSupported(ApprovalResultSource.PowerAutomateGroup));
            Assert.Equal(2, (int)ApprovalResultSource.PowerAutomateGroup);
        }

        [Fact]
        public void 別版または結果不明は判断として確定できない()
        {
            var input = new ApprovalDecisionInput
            {
                RequestStatus = ApprovalRequestStatus.提出中,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.提出済み,
                RequestId = RequestId,
                SubmissionVersionId = SubmissionVersionId,
                VerifiedRequestId = RequestId,
                VerifiedSubmissionVersionId = "version-old",
                Decision = ApprovalDecision.却下,
                Source = ApprovalResultSource.PowerAutomateGroup,
                ResultKnown = true,
            };
            Assert.Equal(ApprovalContractError.PairMismatch, ApprovalContract.ValidateDecision(input).Error);

            input.VerifiedSubmissionVersionId = SubmissionVersionId;
            input.ResultKnown = false;
            Assert.Equal(ApprovalContractError.ResultUnknown, ApprovalContract.ValidateDecision(input).Error);
        }

        [Fact]
        public void 未知の承認結果を成功として扱わない()
        {
            var input = new ApprovalDecisionInput
            {
                RequestStatus = ApprovalRequestStatus.提出中,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.提出済み,
                RequestId = RequestId,
                SubmissionVersionId = SubmissionVersionId,
                VerifiedRequestId = RequestId,
                VerifiedSubmissionVersionId = SubmissionVersionId,
                Decision = (ApprovalDecision)999,
                Source = ApprovalResultSource.PowerAutomateGroup,
                ResultKnown = true,
            };

            Assert.Equal(ApprovalContractError.DecisionUnsupported, ApprovalContract.ValidateDecision(input).Error);
        }

        [Fact]
        public void 差戻しと却下は提出中の版から許可する()
        {
            var input = new ApprovalDecisionInput
            {
                RequestStatus = ApprovalRequestStatus.提出中,
                SubmissionVersionStatus = ApprovalSubmissionVersionStatus.提出済み,
                RequestId = RequestId,
                SubmissionVersionId = SubmissionVersionId,
                VerifiedRequestId = RequestId,
                VerifiedSubmissionVersionId = SubmissionVersionId,
                Source = ApprovalResultSource.PowerAutomateGroup,
                ResultKnown = true,
            };

            input.Decision = ApprovalDecision.差戻し;
            Assert.True(ApprovalContract.ValidateDecision(input).IsValid);
            input.Decision = ApprovalDecision.却下;
            Assert.True(ApprovalContract.ValidateDecision(input).IsValid);
            input.RequestStatus = ApprovalRequestStatus.承認済み;
            Assert.Equal(ApprovalContractError.StateCannotRecord, ApprovalContract.ValidateDecision(input).Error);
        }

        [Fact]
        public void ISO日時はUTCへ正規化する()
        {
            Assert.Equal(
                "2026-09-12T03:00:00.0000000Z",
                ApprovalContract.NormalizeIso("2026-09-12T12:00:00+09:00"));
        }
    }
}
