using System;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class PartnerRegistrationContractTests
    {
        private static readonly Guid RegistrantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MainOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public void 登録可能な入力はトリムし正規化値と初期状態をサーバー側契約へ変換する()
        {
            var result = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput
                {
                    Name = "  株式会社　ＡＢＣ  ",
                    Industry = " ITサービス ",
                    Address = " 東京都千代田区1-1-1 ",
                    Phone = " 03-1234-5678 ",
                    MainOwnerId = MainOwnerId,
                    IdempotencyKey = " register-1 ",
                });

            Assert.True(result.IsValid);
            Assert.Equal(PartnerRegistrationValidationError.None, result.Error);
            Assert.NotNull(result.Value);
            Assert.Equal(RegistrantId, result.Value!.InitiatingUserId);
            Assert.Equal(MainOwnerId, result.Value.MainOwnerId);
            Assert.Equal("株式会社　ＡＢＣ", result.Value.Name);
            Assert.Equal("ABC", result.Value.NormalizedName);
            Assert.Equal("ITサービス", result.Value.Industry);
            Assert.Equal("東京都千代田区1-1-1", result.Value.Address);
            Assert.Equal("03-1234-5678", result.Value.Phone);
            Assert.Equal("register-1", result.Value.IdempotencyKey);
            Assert.Equal(100000000, ValidatedPartnerRegistrationInput.InitialTradingStatusCode);
        }

        [Fact]
        public void 任意項目の空白は未設定として扱う()
        {
            var result = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput
                {
                    Name = "青空ソリューションズ株式会社",
                    Industry = " ",
                    Address = null,
                    Phone = "",
                    MainOwnerId = MainOwnerId,
                    IdempotencyKey = "register-2",
                });

            Assert.True(result.IsValid);
            Assert.Null(result.Value!.Industry);
            Assert.Null(result.Value.Address);
            Assert.Null(result.Value.Phone);
        }

        [Theory]
        [InlineData(PartnerRegistrationValidationError.InitiatingUserRequired, "", "key")]
        [InlineData(PartnerRegistrationValidationError.MainOwnerRequired, "青空株式会社", "key")]
        [InlineData(PartnerRegistrationValidationError.NameRequired, " ", "key")]
        [InlineData(PartnerRegistrationValidationError.IdempotencyKeyRequired, "青空株式会社", " ")]
        public void 必須値が欠けた入力は拒否する(
            PartnerRegistrationValidationError expected,
            string name,
            string idempotencyKey)
        {
            var caller = expected == PartnerRegistrationValidationError.InitiatingUserRequired ? Guid.Empty : RegistrantId;
            var result = PartnerRegistrationContract.Validate(
                caller,
                new PartnerRegistrationInput
                {
                    Name = name,
                    MainOwnerId = expected == PartnerRegistrationValidationError.MainOwnerRequired ? null : MainOwnerId,
                    IdempotencyKey = idempotencyKey,
                });

            Assert.False(result.IsValid);
            Assert.Equal(expected, result.Error);
            Assert.Null(result.Value);
        }

        [Fact]
        public void 法人格だけの名前や長すぎる名前は拒否する()
        {
            var emptyNormalized = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput {Name = "株式会社", MainOwnerId = MainOwnerId, IdempotencyKey = "name-1"});
            var tooLong = PartnerRegistrationContract.Validate(
                RegistrantId,
                new PartnerRegistrationInput {Name = new string('x', 201), MainOwnerId = MainOwnerId, IdempotencyKey = "name-2"});

            Assert.Equal(PartnerRegistrationValidationError.NormalizedEmpty, emptyNormalized.Error);
            Assert.Equal(PartnerRegistrationValidationError.NameTooLong, tooLong.Error);
        }

        [Theory]
        [InlineData(PartnerRegistrationValidationError.IdempotencyKeyTooLong, "IdempotencyKey")]
        [InlineData(PartnerRegistrationValidationError.IndustryTooLong, "Industry")]
        [InlineData(PartnerRegistrationValidationError.AddressTooLong, "Address")]
        [InlineData(PartnerRegistrationValidationError.PhoneTooLong, "Phone")]
        public void 各保存列の上限超過は拒否する(
            PartnerRegistrationValidationError expected,
            string field)
        {
            var input = new PartnerRegistrationInput
            {
                Name = "青空ソリューションズ株式会社",
                MainOwnerId = MainOwnerId,
                IdempotencyKey = "valid-key",
            };
            switch (field)
            {
                case "IdempotencyKey":
                    input.IdempotencyKey = new string('x', 201);
                    break;
                case "Industry":
                    input.Industry = new string('x', 201);
                    break;
                case "Address":
                    input.Address = new string('x', 501);
                    break;
                case "Phone":
                    input.Phone = new string('x', 201);
                    break;
            }

            var result = PartnerRegistrationContract.Validate(RegistrantId, input);

            Assert.False(result.IsValid);
            Assert.Equal(expected, result.Error);
            Assert.Null(result.Value);
        }

        [Theory]
        [InlineData("TradingStatusCode")]
        [InlineData("RegisteredAt")]
        [InlineData("RegisteredById")]
        [InlineData("OwnerId")]
        [InlineData("AclVersion")]
        [InlineData("NormalizedName")]
        [InlineData("StateCode")]
        [InlineData("StatusCode")]
        [InlineData("PartnerId")]
        public void クライアントが権威情報を指定する入力は拒否対象として固定する(string parameterName)
        {
            Assert.True(PartnerRegistrationContract.IsForbiddenAuthorityInput(parameterName));
        }

        [Theory]
        [InlineData("Name")]
        [InlineData("Industry")]
        [InlineData("Address")]
        [InlineData("Phone")]
        [InlineData("MainOwnerId")]
        [InlineData("IdempotencyKey")]
        public void 業務入力と冪等キーは許可された入力である(string parameterName)
        {
            Assert.False(PartnerRegistrationContract.IsForbiddenAuthorityInput(parameterName));
        }
    }
}
