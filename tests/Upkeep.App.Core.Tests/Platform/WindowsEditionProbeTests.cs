using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Platform;

public class WindowsEditionProbeTests
{
    private readonly FakeRegistryProbe _registry = new();

    private WindowsEditionProbe CreateProbe() => new(_registry);

    private void GiveEdition(string editionId) => _registry.AddValue(
        RegistryHiveName.LocalMachine,
        WindowsEditionProbe.CurrentVersionKey,
        WindowsEditionProbe.EditionValueName,
        editionId);

    [Theory]
    [InlineData("Core")]
    [InlineData("CoreN")]
    [InlineData("CoreSingleLanguage")]
    [InlineData("CoreCountrySpecific")]
    public void Classify_HomeInAnyVariant_IsHome(string editionId) =>
        Assert.Equal(WindowsEditionKind.Home, WindowsEditionProbe.Classify(editionId));

    [Theory]
    [InlineData("Professional")]
    [InlineData("ProfessionalN")]
    [InlineData("ProfessionalWorkstation")]
    public void Classify_Professional_IsProfessional(string editionId) =>
        Assert.Equal(WindowsEditionKind.Professional, WindowsEditionProbe.Classify(editionId));

    [Theory]
    [InlineData("Enterprise")]
    [InlineData("EnterpriseS")]
    [InlineData("EnterpriseN")]
    public void Classify_Enterprise_IsEnterprise(string editionId) =>
        Assert.Equal(WindowsEditionKind.Enterprise, WindowsEditionProbe.Classify(editionId));

    [Theory]
    [InlineData("Education")]
    [InlineData("ProfessionalEducation")]
    public void Classify_Education_IsEducation(string editionId) =>
        Assert.Equal(WindowsEditionKind.Education, WindowsEditionProbe.Classify(editionId));

    [Theory]
    [InlineData("ServerStandard")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_AnEditionUpkeepDoesNotKnow_IsOther(string? editionId)
    {
        // Includes editions that didn't exist when this was written; deferral stays hidden there
        // rather than being a control that might do nothing.
        Assert.Equal(WindowsEditionKind.Other, WindowsEditionProbe.Classify(editionId));
    }

    [Fact]
    public void Classify_IoTEnterprise_IsEnterprise() =>
        // It honours the deferral policies, so it belongs with Enterprise rather than in Other.
        Assert.Equal(WindowsEditionKind.Enterprise, WindowsEditionProbe.Classify("IoTEnterpriseK"));

    [Fact]
    public void SupportsUpdateDeferral_Home_IsFalse()
    {
        // Home reads the deferral policy keys and ignores them.
        GiveEdition("Core");

        Assert.False(CreateProbe().Read().SupportsUpdateDeferral);
    }

    [Theory]
    [InlineData("Professional")]
    [InlineData("Enterprise")]
    [InlineData("Education")]
    public void SupportsUpdateDeferral_EditionsWhereThePolicyApplies_IsTrue(string editionId)
    {
        GiveEdition(editionId);

        Assert.True(CreateProbe().Read().SupportsUpdateDeferral);
    }

    [Fact]
    public void Read_KeepsTheRawEditionIdForTheLog()
    {
        GiveEdition("ProfessionalWorkstation");

        var info = CreateProbe().Read();

        Assert.Equal("ProfessionalWorkstation", info.EditionId);
        Assert.Equal(WindowsEditionKind.Professional, info.Kind);
    }

    [Fact]
    public void Read_RegistryValueMissing_DoesNotOfferDeferral()
    {
        // A machine that won't say what it is gets the cautious answer.
        var info = CreateProbe().Read();

        Assert.Equal(string.Empty, info.EditionId);
        Assert.False(info.SupportsUpdateDeferral);
    }
}
