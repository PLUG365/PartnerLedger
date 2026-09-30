using System;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalActiveRequestKeyTests
    {
        [Fact]
        public void 同じ対象と申請種別は同じキーになる()
        {
            var targetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var first = PartnerLedger.Plugins.ApprovalActiveRequestKey.Build(
                "pl_Partner",
                targetId,
                "partner.phone");
            var second = PartnerLedger.Plugins.ApprovalActiveRequestKey.Build(
                "pl_partner",
                targetId,
                " PARTNER.PHONE ");

            Assert.Equal(first, second);
            Assert.StartsWith(PartnerLedger.Plugins.ApprovalActiveRequestKey.Prefix, first);
            Assert.Equal(PartnerLedger.Plugins.ApprovalActiveRequestKey.Prefix.Length + 64, first.Length);
        }

        [Fact]
        public void 対象または申請種別が異なるとキーが分かれる()
        {
            var targetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var first = PartnerLedger.Plugins.ApprovalActiveRequestKey.Build(
                "pl_partner",
                targetId,
                "partner.phone");
            var differentTarget = PartnerLedger.Plugins.ApprovalActiveRequestKey.Build(
                "pl_partner",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "partner.phone");
            var differentType = PartnerLedger.Plugins.ApprovalActiveRequestKey.Build(
                "pl_partner",
                targetId,
                "partner.address");

            Assert.NotEqual(first, differentTarget);
            Assert.NotEqual(first, differentType);
        }

        [Theory]
        [InlineData(ApprovalRequestStatus.下書き, true)]
        [InlineData(ApprovalRequestStatus.提出中, true)]
        [InlineData(ApprovalRequestStatus.承認済み, true)]
        [InlineData(ApprovalRequestStatus.差戻し, true)]
        [InlineData(ApprovalRequestStatus.反映待ち, true)]
        [InlineData(ApprovalRequestStatus.反映失敗, false)]
        [InlineData(ApprovalRequestStatus.却下, false)]
        [InlineData(ApprovalRequestStatus.取消, false)]
        [InlineData(ApprovalRequestStatus.反映済み, false)]
        public void 終端状態だけ承認中スロットを解放する(ApprovalRequestStatus status, bool holdsSlot)
        {
            Assert.Equal(holdsSlot, PartnerLedger.Plugins.ApprovalActiveRequestKey.HoldsSlot(status));
        }
    }
}
