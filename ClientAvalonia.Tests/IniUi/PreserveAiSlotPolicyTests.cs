using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.Session;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Requirement P0-2: switching maps remembers prior AI adjustments —
/// equal capacity keeps rows, shrink drops the tail, growth appends defaults.
/// </summary>
public sealed class PreserveAiSlotPolicyTests
{
    [Fact]
    public void Same_Capacity_Keeps_All_Adjustments()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        FillAi(session, 1, aiLevel: 2, side: 5, color: 3, team: 1, start: 2);
        FillAi(session, 2, aiLevel: 1, side: 4, color: 6, team: 0, start: 1);
        FillAi(session, 3, aiLevel: 3, side: 2, color: 1, team: 2, start: 3);

        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 4, NewColors(), LobbyCatalogService.Instance.AiNames);

        session.OccupiedSlotCount().Should().Be(4);
        session.PlayerSlots[1].AiLevel.Should().Be(2);
        session.PlayerSlots[1].SideIndex.Should().Be(5);
        session.PlayerSlots[1].ColorIndex.Should().Be(3);
        session.PlayerSlots[1].TeamIndex.Should().Be(1);
        session.PlayerSlots[1].StartIndex.Should().Be(2);
        session.PlayerSlots[2].AiLevel.Should().Be(1);
        session.PlayerSlots[3].SideIndex.Should().Be(2);
        session.PlayerSlots[4].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void Smaller_Capacity_Drops_Tail_And_Clamps_Start()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        FillAi(session, 1, aiLevel: 2, side: 5, color: 3, team: 1, start: 2);
        FillAi(session, 2, aiLevel: 1, side: 4, color: 6, team: 0, start: 1);

        // New map only fits the human + 1 AI; the second AI (start=1 survives, tail drops).
        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 2, NewColors(), LobbyCatalogService.Instance.AiNames);

        session.OccupiedSlotCount().Should().Be(2);
        session.PlayerSlots[1].AiLevel.Should().Be(2);
        session.PlayerSlots[1].SideIndex.Should().Be(5);
        session.PlayerSlots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void Larger_Capacity_Appends_Default_Ais()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        FillAi(session, 1, aiLevel: 3, side: 6, color: 2, team: 1, start: 4);

        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 4, NewColors(), LobbyCatalogService.Instance.AiNames);

        session.OccupiedSlotCount().Should().Be(4);
        // Adjusted AI intact.
        session.PlayerSlots[1].AiLevel.Should().Be(3);
        session.PlayerSlots[1].SideIndex.Should().Be(6);
        // Appended rows are defaults.
        session.PlayerSlots[2].IsAi.Should().BeTrue();
        session.PlayerSlots[2].AiLevel.Should().Be(0);
        session.PlayerSlots[3].IsAi.Should().BeTrue();
        session.PlayerSlots[3].AiLevel.Should().Be(0);
    }

    [Fact]
    public void Out_Of_Range_StartIndex_Reset_For_Smaller_Map()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        FillAi(session, 1, aiLevel: 0, side: 0, color: 0, team: 0, start: 7);

        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 3, NewColors(), LobbyCatalogService.Instance.AiNames);

        session.PlayerSlots[1].StartIndex.Should().Be(0);
    }

    [Fact]
    public void Human_Slot_Side_Color_Team_Survive()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        session.PlayerSlots[0].SideIndex = 7;
        session.PlayerSlots[0].ColorIndex = 4;
        session.PlayerSlots[0].TeamIndex = 2;
        session.PlayerSlots[0].StartIndex = 3;
        FillAi(session, 1, aiLevel: 1, side: 0, color: 1, team: 0, start: 0);

        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 2, NewColors(), LobbyCatalogService.Instance.AiNames);

        session.PlayerSlots[0].SideIndex.Should().Be(7);
        session.PlayerSlots[0].ColorIndex.Should().Be(4);
        session.PlayerSlots[0].TeamIndex.Should().Be(2);
        // start=3 does not exist on a 2-player map: reset to random (0), matching
        // the AI-start clamp and DX CheckLoadedPlayerVariableBounds.
        session.PlayerSlots[0].StartIndex.Should().Be(0);
        session.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
    }

    [Fact]
    public void Throws_On_Null_Session()
    {
        Action act = () => PreserveAiSlotPolicy.ResizeToMapCapacity(null!, 4, NewColors());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Restore_Semantics_Trims_Only_Never_Appends()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        FillAi(session, 1, aiLevel: 2, side: 5, color: 3, team: 1, start: 2);

        // Saved 1 AI on a 8-player map: restore keeps that AI and does NOT
        // fill the remaining 7 rows (map-switch semantics would).
        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 8, NewColors(), LobbyCatalogService.Instance.AiNames, fillToCapacity: false);

        session.OccupiedSlotCount().Should().Be(2);
        session.PlayerSlots[1].AiLevel.Should().Be(2);
        session.PlayerSlots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void Restore_Semantics_Trims_Excess_Saved_Rows_To_Map_Capacity()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        for (int i = 1; i <= 8; i++)
            FillAi(session, i, aiLevel: 2, side: 0, color: 0, team: 0, start: 0);

        // Stale 9-slot save (human + 8 AI) restored onto an 8-player map: only 7 AIs fit.
        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 8, NewColors(), LobbyCatalogService.Instance.AiNames, fillToCapacity: false);

        session.OccupiedSlotCount().Should().Be(8);
        session.PlayerSlots[8].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void Human_StartIndex_Clamped_To_New_Capacity()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[0].IsHumanLocal = true;
        session.PlayerSlots[0].StartIndex = 9;
        FillAi(session, 1, aiLevel: 0, side: 0, color: 0, team: 0, start: 0);

        // Saved start=9 onto an 8-player map: reset to random instead of writing
        // a waypoint the map does not define.
        PreserveAiSlotPolicy.ResizeToMapCapacity(
            session, 8, NewColors(), LobbyCatalogService.Instance.AiNames, fillToCapacity: false);

        session.PlayerSlots[0].StartIndex.Should().Be(0);
    }

    private static SkirmishSession NewSession()
    {
        LobbyCatalogService.Instance.Reload(includeSpectator: false);
        return new SkirmishSession();
    }

    private static void FillAi(
        SkirmishSession session,
        int row,
        int aiLevel,
        int side,
        int color,
        int team,
        int start)
    {
        session.PlayerSlots[row].Name = $"AI{row}";
        session.PlayerSlots[row].IsAi = true;
        session.PlayerSlots[row].AiLevel = aiLevel;
        session.PlayerSlots[row].SideIndex = side;
        session.PlayerSlots[row].ColorIndex = color;
        session.PlayerSlots[row].TeamIndex = team;
        session.PlayerSlots[row].StartIndex = start;
    }

    private static IMultiplayerColorCatalog NewColors() => new FixedColorCatalog();

    private sealed class FixedColorCatalog : IMultiplayerColorCatalog
    {
        public IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> Load()
            => Enumerable.Range(0, 8)
                .Select(i => new MultiplayerColorCatalog.MultiplayerColorEntry
                {
                    Name = $"C{i}",
                    GameColorIndex = i,
                    R = 1,
                    G = 1,
                    B = 1,
                })
                .ToList();
    }
}
