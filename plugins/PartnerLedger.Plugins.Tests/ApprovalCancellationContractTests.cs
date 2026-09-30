using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalCancellationContractTests
    {
        private const string Reason = "承認者不在のため管理者取消";

        [Fact]
        public void 提出中で送信待ちの提出版は取消できる()
        {
            var result = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, ApprovalLinkStatus.送信待ち, false,
                ApprovalSubmissionVersionStatus.提出済み, true, Reason);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void 結果確知済みでも申請が提出中なら取消して遅着結果を止める()
        {
            var result = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, ApprovalLinkStatus.連携済み, true,
                ApprovalSubmissionVersionStatus.提出済み, true, Reason);

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.下書き)]
        [InlineData(ApprovalRequestStatus.承認済み)]
        [InlineData(ApprovalRequestStatus.差戻し)]
        [InlineData(ApprovalRequestStatus.却下)]
        [InlineData(ApprovalRequestStatus.反映待ち)]
        [InlineData(ApprovalRequestStatus.反映済み)]
        public void 提出中の取消検証は提出中以外の未取消状態を通さない(ApprovalRequestStatus status)
        {
            var result = ApprovalCancellationContract.Validate(
                status, 0, 1, ApprovalLinkStatus.送信待ち, false,
                ApprovalSubmissionVersionStatus.提出済み, true, Reason);

            Assert.False(result.IsValid);
            Assert.Equal(ApprovalCancellationError.RequestStateInvalid, result.Error);
        }

        [Theory]
        // 申請者本人は下書き・差戻しを理由なしで取り下げられる。
        [InlineData(ApprovalRequestStatus.下書き, true, false, "", ApprovalCancellationPath.Withdraw, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.差戻し, true, false, "", ApprovalCancellationPath.Withdraw, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.下書き, true, false, "対象を間違えた", ApprovalCancellationPath.Withdraw, ApprovalCancellationError.None)]
        // 申請者不在の下書き・差戻しは、管理者が理由付きで取り消せる。
        [InlineData(ApprovalRequestStatus.下書き, false, true, Reason, ApprovalCancellationPath.Withdraw, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.差戻し, false, true, Reason, ApprovalCancellationPath.Withdraw, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.下書き, false, true, " ", ApprovalCancellationPath.None, ApprovalCancellationError.InvalidInput)]
        [InlineData(ApprovalRequestStatus.下書き, false, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        [InlineData(ApprovalRequestStatus.差戻し, false, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        // 提出中は管理者だけ。申請者本人でも管理者でなければ取り消せない。
        [InlineData(ApprovalRequestStatus.提出中, true, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        [InlineData(ApprovalRequestStatus.提出中, false, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        [InlineData(ApprovalRequestStatus.提出中, false, true, Reason, ApprovalCancellationPath.AdministratorCancel, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.提出中, true, true, Reason, ApprovalCancellationPath.AdministratorCancel, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.提出中, false, true, "", ApprovalCancellationPath.None, ApprovalCancellationError.InvalidInput)]
        [InlineData(ApprovalRequestStatus.提出中, true, true, "", ApprovalCancellationPath.None, ApprovalCancellationError.InvalidInput)]
        // 判断済み・反映系は誰でも不可。無関係者には状態を明かさない。
        [InlineData(ApprovalRequestStatus.承認済み, true, true, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.RequestStateInvalid)]
        [InlineData(ApprovalRequestStatus.却下, true, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.RequestStateInvalid)]
        [InlineData(ApprovalRequestStatus.反映待ち, false, true, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.RequestStateInvalid)]
        [InlineData(ApprovalRequestStatus.反映失敗, true, true, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.RequestStateInvalid)]
        [InlineData(ApprovalRequestStatus.反映済み, false, true, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.RequestStateInvalid)]
        [InlineData(ApprovalRequestStatus.承認済み, false, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        // 取消済みの再送は関係者だけ冪等成功。
        [InlineData(ApprovalRequestStatus.取消, true, false, "", ApprovalCancellationPath.Replay, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.取消, false, true, "", ApprovalCancellationPath.Replay, ApprovalCancellationError.None)]
        [InlineData(ApprovalRequestStatus.取消, false, false, Reason, ApprovalCancellationPath.None, ApprovalCancellationError.Unauthorized)]
        public void 取消の経路は状態と実行者と理由で決まる(
            ApprovalRequestStatus status,
            bool isRequester,
            bool isAdministrator,
            string reason,
            ApprovalCancellationPath expectedPath,
            ApprovalCancellationError expectedError)
        {
            var result = ApprovalCancellationContract.Authorize(status, isRequester, isAdministrator, reason);

            Assert.Equal(expectedPath, result.Path);
            Assert.Equal(expectedError, result.Error);
        }

        [Fact]
        public void 理由の上限を超える取消は実行者を問わず拒否する()
        {
            var tooLong = new string('あ', ApprovalCancellationContract.ReasonMaxLength + 1);

            Assert.Equal(
                ApprovalCancellationError.InvalidInput,
                ApprovalCancellationContract.Authorize(ApprovalRequestStatus.下書き, true, false, tooLong).Error);
            Assert.Equal(
                ApprovalCancellationError.InvalidInput,
                ApprovalCancellationContract.Authorize(ApprovalRequestStatus.提出中, false, true, tooLong).Error);
        }

        [Fact]
        public void 取消済みは同じ要求を冪等成功にできる()
        {
            var result = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.取消, 0, 0, null, false,
                ApprovalSubmissionVersionStatus.取消, true, Reason);

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(ApprovalLinkStatus.連携不明)]
        [InlineData(ApprovalLinkStatus.結果確認済み)]
        [InlineData(ApprovalLinkStatus.連携失敗)]
        public void 不明または完了したApprovalLinkはfail_closedで拒否する(ApprovalLinkStatus status)
        {
            var result = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, status, false,
                ApprovalSubmissionVersionStatus.提出済み, true, Reason);

            Assert.False(result.IsValid);
            Assert.Equal(ApprovalCancellationError.ApprovalLinkStateInvalid, result.Error);
        }

        [Fact]
        public void Link版の組み合わせが不一致なら拒否する()
        {
            var result = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, ApprovalLinkStatus.送信待ち, false,
                ApprovalSubmissionVersionStatus.提出済み, false, Reason);

            Assert.False(result.IsValid);
            Assert.Equal(ApprovalCancellationError.SubmissionVersionPairMismatch, result.Error);
        }

        [Fact]
        public void 理由は必須で2000文字を超えられない()
        {
            var missing = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, ApprovalLinkStatus.送信待ち, false,
                ApprovalSubmissionVersionStatus.提出済み, true, " ");
            var tooLong = ApprovalCancellationContract.Validate(
                ApprovalRequestStatus.提出中, 0, 1, ApprovalLinkStatus.送信待ち, false,
                ApprovalSubmissionVersionStatus.提出済み, true, new string('x', 2001));

            Assert.Equal(ApprovalCancellationError.InvalidInput, missing.Error);
            Assert.Equal(ApprovalCancellationError.InvalidInput, tooLong.Error);
        }

        [Fact]
        public void ロール名はPLシステム管理またはDataverseシステム管理者だけを許可する()
        {
            Assert.True(ApprovalCancellationContract.IsAllowedAdministratorRole("PL システム管理"));
            Assert.True(ApprovalCancellationContract.IsAllowedAdministratorRole("システム管理者"));
            Assert.True(ApprovalCancellationContract.IsAllowedAdministratorRole("System Administrator"));
            Assert.False(ApprovalCancellationContract.IsAllowedAdministratorRole("PL 承認"));
        }
    }
}
