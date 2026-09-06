using System;
using System.IO;
using ClientAvalonia.Tests.Fixture;
using ClientAvalonia.Services;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Services;

/// <summary>
/// Issue #22: campaign side tabs are derived from GameOptions.ini [General]
/// Sides= (the same list the skirmish dropdown uses) instead of the
/// hard-coded GDI/Nod/ThirdSide trio.
/// </summary>
[Collection("EnvironmentServicesSerial")]
public sealed class CampaignSideTabCatalogTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public CampaignSideTabCatalogTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose()
    {
        // Restore statics for the next class in this serial collection.
        ClientConfiguration.ResetInstance();
        ClientAvalonia.GlobalState.Environment.EnvironmentServices.Reset();
        ProgramConstants.ClearHostedGameRoot();
        CampaignSideTabCatalog.InvalidateCache();
        _root.Dispose();
    }

    [Fact]
    public void Tabs_Derive_From_GameOptions_Sides()
    {
        WriteGameOptions("Sides=Allied,Soviet,Ackville");

        var tabs = CampaignSideTabCatalog.GetTabs();

        tabs.Should().HaveCount(3);
        tabs[0].ControlId.Should().Be("GDI");
        tabs[0].SideName.Should().Be("Allied");
        tabs[1].ControlId.Should().Be("Nod");
        tabs[2].ControlId.Should().Be("ThirdSide");
    }

    [Fact]
    public void Fourth_Side_Maps_To_FourthSide_Control()
    {
        WriteGameOptions("Sides=Allied,Soviet,Ackville,Yuri");

        var tabs = CampaignSideTabCatalog.GetTabs();

        tabs.Should().HaveCount(4);
        tabs[3].ControlId.Should().Be("FourthSide");
        tabs[3].SideName.Should().Be("Yuri");
    }

    [Fact]
    public void Fifth_Side_Falls_Back_To_SideN_Control()
    {
        WriteGameOptions("Sides=Allied,Soviet,Ackville,Yuri,Order");

        var tabs = CampaignSideTabCatalog.GetTabs();

        tabs.Should().HaveCount(5);
        tabs[4].ControlId.Should().Be("Side4");
    }

    private void WriteGameOptions(string sidesLine)
    {
        CampaignSideTabCatalog.InvalidateCache();
        File.WriteAllText(
            Path.Combine(_root.ResourcesPath, "GameOptions.ini"),
            $"[General]\r\n{sidesLine}\r\n");
        ClientConfiguration.ResetInstance();
    }
}
