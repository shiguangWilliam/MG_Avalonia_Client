using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.Session;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Unit tests for <see cref="DefaultAiSlotPolicy"/> (auto-ai-slots.md v2).
/// </summary>
public sealed class DefaultAiSlotPolicyTests
{
    [Fact]
    public void AutoFill_2PlayerMap_Leaves_1_Local_1_Ai()
    {
        SkirmishSession session = NewSession();

        DefaultAiSlotPolicy.AutoFillToMapCapacity(session, 2, "Local", NewColors(), session.Player.AiNames);

        session.Player.OccupiedSlotCount.Should().Be(2);
        session.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        session.PlayerSlots[1].IsAi.Should().BeTrue();
        session.PlayerSlots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void AutoFill_8PlayerMap_Leaves_1_Local_7_Ai()
    {
        SkirmishSession session = NewSession();

        DefaultAiSlotPolicy.AutoFillToMapCapacity(session, 8, "Local", NewColors(), session.Player.AiNames);

        session.Player.OccupiedSlotCount.Should().Be(8);
        session.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        Enumerable.Range(1, 7).Select(i => session.PlayerSlots[i].IsAi).Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public void AutoFill_Clears_Existing_User_Edits()
    {
        SkirmishSession session = NewSession();
        session.PlayerSlots[1].Name = "ROOKIE";
        session.PlayerSlots[1].IsAi = true;
        session.PlayerSlots[1].ColorIndex = 5;
        session.PlayerSlots[1].AiLevel = 2;

        DefaultAiSlotPolicy.AutoFillToMapCapacity(session, 3, "Local", NewColors(), session.Player.AiNames);

        session.Player.OccupiedSlotCount.Should().Be(3);
        session.PlayerSlots[1].ColorIndex.Should().NotBe(5);
        session.PlayerSlots[1].AiLevel.Should().Be(0);
    }

    [Fact]
    public void AutoFill_Clamps_Illegal_MaxPlayers()
    {
        SkirmishSession session = NewSession();

        DefaultAiSlotPolicy.AutoFillToMapCapacity(session, 0, "Local", NewColors(), session.Player.AiNames);
        session.Player.OccupiedSlotCount.Should().Be(1);

        DefaultAiSlotPolicy.AutoFillToMapCapacity(session, 99, "Local", NewColors(), session.Player.AiNames);
        session.Player.OccupiedSlotCount.Should().Be(LobbyPlayerSlot.MaxSlots);
    }

    [Fact]
    public void AutoFill_Throws_On_Null_Session()
    {
        Action act = () => DefaultAiSlotPolicy.AutoFillToMapCapacity(null!, 4, "Local", NewColors());
        act.Should().Throw<ArgumentNullException>();
    }

    private static SkirmishSession NewSession()
    {
        var state = new LobbyPlayerState();
        state.LoadCatalogs(includeSpectator: false);
        return new SkirmishSession(state);
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
                    G = 2,
                    B = 3,
                })
                .ToList();
    }
}
