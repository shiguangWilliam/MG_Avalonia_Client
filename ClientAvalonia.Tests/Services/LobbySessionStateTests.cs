using System.Collections.Generic;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Services;

/// <summary>
/// LobbySessionState is pure UI selection state 鈥?favorite-filter index, visible maps/missions,
/// selection lookup by list index. Tests verify the const + bounds-clamp contract that the
/// map/mission UI relies on.
/// </summary>
public sealed class LobbySessionStateTests
{
    [Fact]
    public void FavoriteFilterIndex_IsZero()
    {
        LobbySessionState.FavoriteFilterIndex.Should().Be(0);
    }

    [Fact]
    public void IsFavoriteFilterSelected_True_WhenFilterIndexIsZero()
    {
        var state = new LobbySessionState { FilterIndex = 0 };
        state.IsFavoriteFilterSelected.Should().BeTrue();
    }

    [Fact]
    public void IsFavoriteFilterSelected_False_WhenFilterIndexNonZero()
    {
        var state = new LobbySessionState { FilterIndex = 2 };
        state.IsFavoriteFilterSelected.Should().BeFalse();
    }

    [Fact]
    public void SetVisibleMaps_ReplacesList_AndGetSelectedMap_LooksUpByIndex()
    {
        var state = new LobbySessionState();
        var maps = new List<MapEntry>
        {
            MakeMap("Map1"),
            MakeMap("Map2"),
            MakeMap("Map3"),
        };

        state.SetVisibleMaps(maps);

        state.VisibleMaps.Should().HaveCount(3);
        state.GetSelectedMap(0)!.DisplayName.Should().Be("Map1");
        state.GetSelectedMap(2)!.DisplayName.Should().Be("Map3");
    }

    [Fact]
    public void GetSelectedMap_ReturnsNull_OutOfRange()
    {
        var state = new LobbySessionState();
        state.SetVisibleMaps(new[] { MakeMap("Only") });

        state.GetSelectedMap(-1).Should().BeNull();
        state.GetSelectedMap(5).Should().BeNull();
    }

    [Fact]
    public void SetVisibleMissions_ReplacesList_AndGetSelectedMission_LooksUpByIndex()
    {
        var state = new LobbySessionState();
        var missions = new List<MissionEntry>
        {
            MakeMission("M1"),
            MakeMission("M2"),
        };

        state.SetVisibleMissions(missions);

        state.VisibleMissions.Should().HaveCount(2);
        state.GetSelectedMission(0)!.DisplayName.Should().Be("M1");
    }

    [Fact]
    public void GetSelectedMission_ReturnsNull_OutOfRange()
    {
        var state = new LobbySessionState();
        state.SetVisibleMissions(new[] { MakeMission("Only") });

        state.GetSelectedMission(-1).Should().BeNull();
        state.GetSelectedMission(99).Should().BeNull();
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var state = new LobbySessionState();
        state.FilterIndex.Should().Be(0);
        state.MapSearchText.Should().BeEmpty();
        state.CampaignSideFilter.Should().Be(CampaignSideFilter.All);
        state.LastSelectableCampaignIndex.Should().Be(-1);
        state.VisibleMaps.Should().BeEmpty();
        state.VisibleMissions.Should().BeEmpty();
        // Phase 6: PlayerState removed
        state.MultiplayerState.Should().NotBeNull();
    }

    private static MapEntry MakeMap(string display)
        => new()
        {
            BaseFilePath = display + ".map",
            DisplayName = display,
            UntranslatedName = display,
            GameModes = new List<string>(),
        };

    private static MissionEntry MakeMission(string display)
        => new()
        {
            DisplayName = display,
            Scenario = display + ".scn",
            SectionName = display,
            SideName = "Allied",
        };
}
