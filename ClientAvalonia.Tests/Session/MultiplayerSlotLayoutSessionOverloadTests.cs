using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 2 缺口 2.5：MultiplayerSlotLayout.ApplyToSlots / BuildPoList 新重载（吃 IPlayerSlot[]）。
/// 见 phase2-completeness-audit.md §2.5。
/// </summary>
public sealed class MultiplayerSlotLayoutSessionOverloadTests
{
    private static LobbyPlayerSlot[] MakeSlots(int count = LobbyPlayerSlot.MaxSlots)
        => Enumerable.Range(0, count).Select(_ => new LobbyPlayerSlot()).ToArray();

    private static readonly string[] AiNames = { "Easy", "Medium", "Hard" };

    [Fact]
    public void ApplyToSlots_Writes_Humans_And_Ais_In_Order()
    {
        IPlayerSlot[] slots = MakeSlots();
        var entries = new List<CnCNetGameRoomPlayer>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" },
            new() { IsAi = true, Name = "Easy", AiLevel = 0 },
        };

        MultiplayerSlotLayout.ApplyToSlots(slots, entries, localNick: "Alice");

        slots[0].Name.Should().Be("Alice");
        slots[0].IsAi.Should().BeFalse();
        slots[0].IsHumanLocal.Should().BeTrue();
        slots[1].Name.Should().Be("Bob");
        slots[1].IsHumanLocal.Should().BeFalse();
        slots[2].IsAi.Should().BeTrue();
        slots[2].Name.Should().Be("Easy");
        slots[2].AiLevel.Should().Be(0);
    }

    [Fact]
    public void ApplyToSlots_NullSlots_Throws()
    {
        Action act = () => MultiplayerSlotLayout.ApplyToSlots(null!, Array.Empty<CnCNetGameRoomPlayer>(), "x");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyToSlots_Truncates_When_Entries_Exceed_Slot_Count()
    {
        IPlayerSlot[] slots = MakeSlots(count: 2);
        var entries = new List<CnCNetGameRoomPlayer>
        {
            new() { Name = "A" },
            new() { Name = "B" },
            new() { Name = "C" }, // 超出
        };

        MultiplayerSlotLayout.ApplyToSlots(slots, entries, "A");

        slots[0].Name.Should().Be("A");
        slots[1].Name.Should().Be("B");
    }

    [Fact]
    public void BuildPoList_Encodes_Humans_And_Ais_From_IPlayerSlot_Array()
    {
        IPlayerSlot[] slots = MakeSlots();
        slots[0].Name = "Alice";
        slots[0].IsAi = false;
        slots[1].Name = "Bob";
        slots[1].IsAi = false;
        slots[2].Name = "Easy";
        slots[2].IsAi = true;
        slots[2].AiLevel = 0;

        var dto = MultiplayerSlotLayout.BuildPoList(slots, hostName: "Alice", AiNames);

        dto.Should().HaveCount(3);
        dto[0].Name.Should().Be("Alice");
        dto[0].IsHost.Should().BeTrue();
        dto[1].Name.Should().Be("Bob");
        dto[2].IsAi.Should().BeTrue();
        dto[2].Name.Should().Be("Easy");
        dto[2].AiLevel.Should().Be(0);
    }

    [Fact]
    public void BuildPoList_NullArgs_Throw()
    {
        Action act1 = () => MultiplayerSlotLayout.BuildPoList(null!, "h", AiNames);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => MultiplayerSlotLayout.BuildPoList(Array.Empty<IPlayerSlot>(), "h", null!);
        act2.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>
/// Phase 2 缺口 2.5：LobbyPlayerSlotUiRules.ConfigureFor* 新重载（吃 LobbySessionState + Session）。
/// </summary>
public sealed class LobbyPlayerSlotUiRulesSessionOverloadTests
{
    [Fact]
    public void ConfigureForSkirmish_Writes_To_LobbySessionState()
    {
        var ui = new LobbySessionState { UIMode = LobbyPlayerMode.Multiplayer };
        var session = new SkirmishSession();

        LobbyPlayerSlotUiRules.ConfigureForSkirmish(ui, session);

        ui.UIMode.Should().Be(LobbyPlayerMode.Skirmish);
        ui.AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void ConfigureForMultiplayer_Writes_To_LobbySessionState_And_Applies_Entries()
    {
        var ui = new LobbySessionState();
        var session = new CnCNetGameRoomSession(new CnCNetActiveGameRoom
        {
            RoomName = "T",
            ChannelName = "#t",
            Password = "p",
            Tunnel = new CnCNetTunnel { Name = "T", Address = "1.1.1.1", Port = 50000 },
            HostName = "Alice",
            IsHost = true,
            MaxPlayers = 4,
        });

        var entries = new[] { new CnCNetGameRoomPlayer { Name = "Alice" } };

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            ui, session, entries, localNick: "Alice", hostName: "Alice", isHost: true, resetSlots: true);

        ui.UIMode.Should().Be(LobbyPlayerMode.Multiplayer);
        ui.AllowHostPlayerOptions.Should().BeTrue();
        ui.LocalPlayerName.Should().Be("Alice");
        ui.HostPlayerName.Should().Be("Alice");
        session.PlayerSlots[0].Name.Should().Be("Alice");
    }

    [Fact]
    public void ConfigureForMultiplayer_Resets_Slots_When_Mode_Changes()
    {
        var ui = new LobbySessionState { UIMode = LobbyPlayerMode.Skirmish };
        var session = new CnCNetGameRoomSession(new CnCNetActiveGameRoom
        {
            RoomName = "T",
            ChannelName = "#t",
            Password = "p",
            Tunnel = new CnCNetTunnel { Name = "T", Address = "1.1.1.1", Port = 50000 },
            HostName = "Alice",
            IsHost = true,
            MaxPlayers = 4,
        });
        session.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "OldPlayer" });

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            ui, session, Array.Empty<CnCNetGameRoomPlayer>(), "Alice", "Alice", isHost: true, resetSlots: false);

        // Mode 从 Skirmish 切到 Multiplayer 应触发清空
        session.PlayerSlots[0].IsOccupied.Should().BeFalse();
    }
}
