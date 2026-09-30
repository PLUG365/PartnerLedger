using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;

namespace PartnerLedger.Plugins.Tests;

public sealed class PartnerLedgerEnvironmentVariableReaderTests
{
    private const string SchemaName = "pl_ApproverTeamId";

    [Fact]
    public void ReadRequiredGuid_UsesOnlyCurrentValue_WhenDefaultValueDiffers()
    {
        var service = CreateService(CurrentValue.ToString("D"));

        var actual = PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName);

        Assert.Equal(CurrentValue, actual);
    }

    [Fact]
    public void ReadRequiredGuid_DoesNotFallBackToDefaultValue_WhenCurrentValueIsMissing()
    {
        var service = CreateService(currentValue: null);

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredGuid_RejectsMissingDefinition()
    {
        var service = new FakeOrganizationService();

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredGuid_RejectsDuplicateDefinitions()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("environmentvariabledefinition")
        {
            ["schemaname"] = SchemaName,
        });

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ReadRequiredGuid_RejectsBlankMalformedOrEmptyGuid(string currentValue)
    {
        var service = CreateService(currentValue);

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredGuid_RejectsAmbiguousCurrentValues()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("environmentvariablevalue")
        {
            ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", DefinitionId),
            ["value"] = Guid.NewGuid().ToString("D"),
        });

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredGuid_PropagatesReadFailure_WithoutTryingAnotherService()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.BeforeRetrieveMultiple = _ => new InvalidPluginExecutionException("Read denied");

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredGuid(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_RejectsMissingTeam()
    {
        var service = CreateService(CurrentValue.ToString("D"));

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ReadRequiredActiveTeamId_RejectsNonGroupTeam(int teamType)
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("team", CurrentValue)
        {
            ["teamtype"] = new OptionSetValue(teamType),
        });

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_RejectsMissingTeamType()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("team", CurrentValue));

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_PropagatesTeamReadFailure()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("team", CurrentValue)
        {
            ["teamtype"] = new OptionSetValue(3),
        });
        service.CanRetrieve = (name, _) => name != "team";

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadRequiredActiveTeamId_ReturnsConfiguredGroupTeamWithoutStatecode(int teamType)
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("team", CurrentValue)
        {
            ["teamtype"] = new OptionSetValue(teamType),
        });

        Assert.Equal(
            CurrentValue,
            PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_ResolvesEntraGroupObjectIdToItsTeam()
    {
        // 2026-09-27のインポート試験で、チームIDの欄にEntraグループのオブジェクトIDが入った。
        // その値を持つグループTeamがちょうど1つあれば、そのTeamを使う。
        var service = CreateService(CurrentValue.ToString("D"));
        var teamId = Guid.Parse("29f96113-5c49-4111-8ec6-6045bdd5993c");
        service.Seed(new Entity("team", teamId)
        {
            ["teamtype"] = new OptionSetValue(3),
            ["azureactivedirectoryobjectid"] = CurrentValue,
        });

        Assert.Equal(teamId, PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_RejectsEntraGroupObjectIdWithSeveralTeams()
    {
        // 同じEntraグループのTeamが部署ごとに複数あると、どれか分からないため失敗させる。
        var service = CreateService(CurrentValue.ToString("D"));
        foreach (var id in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            service.Seed(new Entity("team", id)
            {
                ["teamtype"] = new OptionSetValue(3),
                ["azureactivedirectoryobjectid"] = CurrentValue,
            });
        }

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    [Fact]
    public void ReadRequiredActiveTeamId_RejectsEntraObjectIdOfNonGroupTeam()
    {
        var service = CreateService(CurrentValue.ToString("D"));
        service.Seed(new Entity("team", Guid.NewGuid())
        {
            ["teamtype"] = new OptionSetValue(0),
            ["azureactivedirectoryobjectid"] = CurrentValue,
        });

        Assert.Throws<InvalidPluginExecutionException>(
            () => PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(service, SchemaName));
    }

    private static readonly Guid CurrentValue = Guid.Parse("57c419f0-f23f-4b77-b9a7-1881a0d13218");
    private static readonly Guid DefinitionId = Guid.Parse("8a5033ce-ae0d-4892-96ad-6b1015fa8216");

    private static FakeOrganizationService CreateService(string? currentValue)
    {
        var service = new FakeOrganizationService();
        service.Seed(new Entity("environmentvariabledefinition", DefinitionId)
        {
            ["schemaname"] = SchemaName,
            ["defaultvalue"] = "00000000-0000-0000-0000-000000000001",
        });
        if (currentValue != null)
        {
            service.Seed(new Entity("environmentvariablevalue")
            {
                ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", DefinitionId),
                ["value"] = currentValue,
            });
        }
        return service;
    }
}
