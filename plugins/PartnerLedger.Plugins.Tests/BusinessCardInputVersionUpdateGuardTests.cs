using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BusinessCardInputVersionUpdateGuardTests
    {
        [Fact]
        public void 標準InputVersionUpdateは作成後変更として拒否する()
        {
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardInputVersionUpdateGuardContract.RejectUpdate(
                    "Update",
                    BusinessCardInputVersionUpdateGuardContract.EntityName,
                    10,
                    0));

            Assert.Contains("作成後に変更できません", exception.Message);
        }

        [Theory]
        [InlineData("Create", "pl_cardinputversion", 10, 0)]
        [InlineData("Update", "pl_cardcapture", 10, 0)]
        [InlineData("Update", "pl_cardinputversion", 20, 0)]
        [InlineData("Update", "pl_cardinputversion", 10, 1)]
        public void 想定外のStepコンテキストは失敗閉鎖する(
            string messageName,
            string entityName,
            int stage,
            int mode)
        {
            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardInputVersionUpdateGuardContract.RejectUpdate(messageName, entityName, stage, mode));

            Assert.Contains("実行コンテキストが不正", exception.Message);
        }
    }
}
