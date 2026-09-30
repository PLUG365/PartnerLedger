using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

/// <summary>
/// 承認設定の行が無いときは既定値（版0）で動く（2026-09-27、実装計画の設計表）。
/// 既定値を使うのは有効な行が0件のときだけで、不正な行があるときは現行どおり失敗させる。
/// </summary>
public sealed class ApprovalSettingsRepositoryTests
{
    [Fact]
    public void No_active_setting_uses_the_default_policy_as_version_zero()
    {
        var result = ApprovalSettingsRepository.RetrieveActive(new FakeOrganizationService());

        Assert.True(result.IsValid);
        Assert.Equal("0", result.Settings!.PolicyVersion);
        Assert.Equal(0, result.Settings.SettingsVersion);
        Assert.True(result.Settings.Policy.CompanyName);
        Assert.True(result.Settings.Policy.TradingStatus);
        Assert.False(result.Settings.Policy.Address);
        Assert.False(result.Settings.Policy.Phone);
        Assert.True(result.Settings.Policy.ContractUpdate);
    }

    [Fact]
    public void Only_inactive_settings_also_use_the_default_policy()
    {
        var service = new FakeOrganizationService();
        service.Seed(Setting(1, statecode: 1, "\"companyName\":false,\"tradingStatus\":false,\"address\":true,\"phone\":true,\"contractUpdate\":false"));

        var result = ApprovalSettingsRepository.RetrieveActive(service);

        Assert.True(result.IsValid);
        Assert.Equal("0", result.Settings!.PolicyVersion);
        Assert.True(result.Settings.Policy.CompanyName);
    }

    [Fact]
    public void Active_setting_is_used_as_before()
    {
        var service = new FakeOrganizationService();
        service.Seed(Setting(2, statecode: 0, "\"companyName\":false,\"tradingStatus\":true,\"address\":true,\"phone\":false,\"contractUpdate\":false"));

        var result = ApprovalSettingsRepository.RetrieveActive(service);

        Assert.True(result.IsValid);
        Assert.Equal("2", result.Settings!.PolicyVersion);
        Assert.False(result.Settings.Policy.CompanyName);
        Assert.True(result.Settings.Policy.Address);
    }

    [Fact]
    public void Invalid_active_setting_still_fails_instead_of_falling_back_to_the_default()
    {
        var service = new FakeOrganizationService();
        service.Seed(Setting(1, statecode: 0, "\"companyName\":\"yes\",\"tradingStatus\":true,\"address\":true,\"phone\":false,\"contractUpdate\":false"));

        var result = ApprovalSettingsRepository.RetrieveActive(service);

        Assert.False(result.IsValid);
        Assert.Equal(ApprovalSettingsReadError.PolicyInvalid, result.Error);
    }

    [Fact]
    public void Default_policy_json_is_valid_under_the_strict_contract()
    {
        var parsed = ApprovalPolicyContract.Validate(ApprovalPolicyContract.DefaultPolicyJson);

        Assert.True(parsed.IsValid);
    }

    private static Entity Setting(int version, int statecode, string properties) => new("pl_settings", Guid.NewGuid())
    {
        ["statecode"] = new OptionSetValue(statecode),
        ["pl_settingsversion"] = version,
        ["pl_policyjson"] = "{\"schemaVersion\":1," + properties + "}",
    };
}
