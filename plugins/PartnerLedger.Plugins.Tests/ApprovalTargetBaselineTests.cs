using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalTargetBaselineTests
    {
        private static Entity Partner(string? name = "現行会社", string? phone = "03-0000-0000", int status = 100000000)
            => new Entity("pl_partner", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"))
            {
                ["pl_name"] = name,
                ["pl_phone"] = phone,
                ["pl_tradingstatuscode"] = new OptionSetValue(status),
            };

        [Fact]
        public void 指紋は版の接頭辞付きで項目の並び順に依存しない()
        {
            var a = ApprovalTargetBaseline.Compute(Partner(), new[] { "pl_name", "pl_tradingstatuscode" });
            var b = ApprovalTargetBaseline.Compute(Partner(), new[] { "pl_tradingstatuscode", "pl_name" });

            Assert.StartsWith(ApprovalTargetBaseline.Prefix, a);
            Assert.Equal(a, b);
            Assert.True(ApprovalTargetBaseline.IsFieldToken(a));
            Assert.False(ApprovalTargetBaseline.IsFieldToken("12345"));
            Assert.False(ApprovalTargetBaseline.IsFieldToken(null));
        }

        [Fact]
        public void 対象の項目が変わると指紋が変わり対象外の項目では変わらない()
        {
            var baseline = ApprovalTargetBaseline.Compute(Partner(), new[] { "pl_name" });

            Assert.NotEqual(baseline, ApprovalTargetBaseline.Compute(Partner(name: "別の会社"), new[] { "pl_name" }));
            Assert.Equal(baseline, ApprovalTargetBaseline.Compute(Partner(phone: "03-9999-9999"), new[] { "pl_name" }));
            Assert.Equal(baseline, ApprovalTargetBaseline.Compute(Partner(status: 100000002), new[] { "pl_name" }));
        }

        [Fact]
        public void 空文字とnullと列なしは同じ値として扱う()
        {
            var withNull = ApprovalTargetBaseline.Compute(Partner(phone: null), new[] { "pl_phone" });
            var withEmpty = ApprovalTargetBaseline.Compute(Partner(phone: string.Empty), new[] { "pl_phone" });
            var missing = new Entity("pl_partner", Guid.NewGuid());

            Assert.Equal(withNull, withEmpty);
            Assert.Equal(withNull, ApprovalTargetBaseline.Compute(missing, new[] { "pl_phone" }));
        }

        [Fact]
        public void 型ごとに正規化し同じ時刻は同じ指紋になる()
        {
            var utc = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc);
            Entity Contract(object? end, bool autoRenew) => new Entity("pl_contract", Guid.NewGuid())
            {
                ["pl_enddate"] = end,
                ["pl_autorenew"] = autoRenew,
                ["pl_contractstatuscode"] = new OptionSetValue(100000000),
            };
            var names = new[] { "pl_enddate", "pl_autorenew", "pl_contractstatuscode" };

            var baseline = ApprovalTargetBaseline.Compute(Contract(utc, true), names);
            Assert.Equal(baseline, ApprovalTargetBaseline.Compute(Contract(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), true), names));
            Assert.NotEqual(baseline, ApprovalTargetBaseline.Compute(Contract(utc.AddDays(1), true), names));
            Assert.NotEqual(baseline, ApprovalTargetBaseline.Compute(Contract(utc, false), names));
            // 文字列の「100000000」と選択肢の100000000は別の値として扱う。
            var asText = new Entity("pl_contract", Guid.NewGuid()) { ["pl_contractstatuscode"] = "100000000" };
            var asChoice = new Entity("pl_contract", Guid.NewGuid()) { ["pl_contractstatuscode"] = new OptionSetValue(100000000) };
            Assert.NotEqual(
                ApprovalTargetBaseline.Compute(asText, new[] { "pl_contractstatuscode" }),
                ApprovalTargetBaseline.Compute(asChoice, new[] { "pl_contractstatuscode" }));
        }

        [Fact]
        public void 対象の項目が空または対象行がないときは使えない()
        {
            Assert.Throws<ArgumentException>(() => ApprovalTargetBaseline.Compute(Partner(), Array.Empty<string>()));
            Assert.Throws<ArgumentNullException>(() => ApprovalTargetBaseline.Compute(null!, new[] { "pl_name" }));
        }
    }
}
