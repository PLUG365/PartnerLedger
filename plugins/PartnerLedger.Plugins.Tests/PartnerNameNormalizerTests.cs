using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerNameNormalizerTests
    {
        [Fact]
        public void FormKC空白除去大文字化と端の法人種別を適用する()
        {
            var success = PartnerNameNormalizer.TryNormalize(
                " 株式会社　ＡＢＣ㈱ ",
                out var normalized,
                out var error);

            Assert.True(success);
            Assert.Equal(PartnerNameNormalizationError.None, error);
            Assert.Equal("ABC", normalized);
        }

        [Fact]
        public void 端以外の法人種別と記号は保持する()
        {
            var success = PartnerNameNormalizer.TryNormalize(
                "Ａ・Ｂ株式会社Ｘ",
                out var normalized,
                out var error);

            Assert.True(success);
            Assert.Equal(PartnerNameNormalizationError.None, error);
            Assert.Equal("A・B株式会社X", normalized);
        }

        [Fact]
        public void 先頭と末尾の法人種別を除去する()
        {
            var success = PartnerNameNormalizer.TryNormalize(
                "一般社団法人 青空 株式会社",
                out var normalized,
                out var error);

            Assert.True(success);
            Assert.Equal(PartnerNameNormalizationError.None, error);
            Assert.Equal("青空", normalized);
        }

        [Fact]
        public void 法人種別だけの値は空になるため拒否する()
        {
            var success = PartnerNameNormalizer.TryNormalize(
                "株式会社",
                out var normalized,
                out var error);

            Assert.False(success);
            Assert.Equal(PartnerNameNormalizationError.NormalizedEmpty, error);
            Assert.Empty(normalized);
        }

        [Fact]
        public void 空白だけと長すぎる値を拒否する()
        {
            var whitespaceSuccess = PartnerNameNormalizer.TryNormalize(
                "　\t\r\n",
                out _,
                out var whitespaceError);
            var longSuccess = PartnerNameNormalizer.TryNormalize(
                new string('A', PartnerNameNormalizer.MaxNameLength + 1),
                out _,
                out var longError);

            Assert.False(whitespaceSuccess);
            Assert.Equal(PartnerNameNormalizationError.InputRequired, whitespaceError);
            Assert.False(longSuccess);
            Assert.Equal(PartnerNameNormalizationError.InputTooLong, longError);
        }
    }
}
