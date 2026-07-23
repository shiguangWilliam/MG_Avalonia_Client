using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Slice 6: <see cref="ICnCNetGameSession.EnsureHostFirst"/> / <see cref="ICnCNetGameSession.MarkLocalHuman"/>
/// 在 <see cref="CnCNetGameRoomSession"/> 上的实现。
/// 见 layered-architecture-progress-report.md §9.5 Slice 6。
/// </summary>
public sealed class CnCNetGameRoomSessionHostSetupTests
{
    private static CnCNetGameRoomSession NewSession(bool isHost = true)
    {
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "Test",
            ChannelName = "#game-test",
            Password = "pw",
            Tunnel = new CnCNetTunnel { Name = "T", Address = "1.1.1.1", Port = 50000 },
            HostName = isHost ? "LocalPlayer" : "RemoteHost",
            IsHost = isHost,
            MaxPlayers = 4,
        };
        return new CnCNetGameRoomSession(room);
    }

    [Fact]
    public void InitHostSlots_Places_LocalPlayer_At_Slot0_And_Bumps_Revision()
    {
        var sut = NewSession(isHost: true);
        long initial = sut.Revision;

        sut.InitHostSlots("Alice");

        sut.Revision.Should().BeGreaterThan(initial);
        sut.PlayerSlots[0].Name.Should().Be("Alice");
        sut.PlayerSlots[0].IsAi.Should().BeFalse();
        sut.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        // 其余槽位空
        sut.PlayerSlots[1].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void InitHostSlots_Clears_All_Other_Slots()
    {
        var sut = NewSession(isHost: true);
        sut.SlotSink.WriteSlotSilent(2, new SlotFieldUpdate { Name = "OldAI", IsAi = true });

        sut.InitHostSlots("Host");

        sut.PlayerSlots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void ReorderHostFirst_Preserves_Existing_Players_And_Moves_Host_To_Slot0()
    {
        var sut = NewSession(isHost: true);
        // 现状：slot[0]=Alice(slot[1]) 之后的；slot[1]=Bob；让 Bob 当 host
        sut.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Alice", IsHumanLocal = false });
        sut.SlotSink.WriteSlotSilent(1, new SlotFieldUpdate { Name = "Bob" });

        sut.ReorderHostFirst(hostName: "Bob", localNick: "Bob");

        sut.PlayerSlots[0].Name.Should().Be("Bob");
        sut.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        sut.PlayerSlots[1].Name.Should().Be("Alice");
    }

    [Fact]
    public void ApplyPlayersFromNetwork_Writes_Entries_And_Marks_LocalHost()
    {
        var sut = NewSession(isHost: true);
        var entries = new List<CnCNetGameRoomPlayer>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" },
            new() { IsAi = true, Name = "EasyAI", AiLevel = 0 },
        };

        sut.ApplyPlayersFromNetwork(entries, hostName: "Alice", localNick: "Alice");

        // host (Alice) 在前
        sut.PlayerSlots[0].Name.Should().Be("Alice");
        sut.PlayerSlots[0].IsHumanLocal.Should().BeTrue();
        sut.PlayerSlots[1].Name.Should().Be("Bob");
        sut.PlayerSlots[2].IsAi.Should().BeTrue();
        sut.PlayerSlots[2].Name.Should().Be("EasyAI");
    }

    [Fact]
    public void UpdateHuman_Updates_Protocol_Fields_For_Named_Player()
    {
        var sut = NewSession(isHost: true);
        sut.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Alice" });

        sut.UpdateHuman("Alice", new SlotFieldUpdate { SideIndex = 2, ColorIndex = 3 });

        // Players 集合内对应记录应被更新
        sut.Players.FirstOrDefault(p => p.Name == "Alice")?.SideId.Should().Be(2);
    }

    [Fact]
    public void UpdateHuman_Noop_When_Player_Not_Found()
    {
        var sut = NewSession(isHost: true);
        long initial = sut.Revision;

        sut.UpdateHuman("Nobody", new SlotFieldUpdate { SideIndex = 2 });

        sut.Revision.Should().Be(initial);
    }

    [Fact]
    public void BroadcastPlayerOptionsFromSlots_Noop_When_Not_Host()
    {
        var sut = NewSession(isHost: false);
        // 不应抛
        Action act = () => sut.BroadcastPlayerOptionsFromSlots("host", new[] { "EasyAI" });
        act.Should().NotThrow();
    }

    [Fact]
    public void MarkLocalHuman_Flags_Only_Matching_Name()
    {
        var sut = NewSession(isHost: true);
        sut.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Alice" });
        sut.SlotSink.WriteSlotSilent(1, new SlotFieldUpdate { Name = "Bob" });

        sut.MarkLocalHuman("Bob");

        sut.PlayerSlots[0].IsHumanLocal.Should().BeFalse();
        sut.PlayerSlots[1].IsHumanLocal.Should().BeTrue();
    }

    [Fact]
    public void MarkLocalHuman_No_Match_Clears_All()
    {
        var sut = NewSession(isHost: true);
        sut.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Alice", IsHumanLocal = true });

        sut.MarkLocalHuman("Nobody");

        sut.PlayerSlots[0].IsHumanLocal.Should().BeFalse();
    }

    [Fact]
    public void MarkLocalHuman_Bumps_Revision()
    {
        var sut = NewSession(isHost: true);
        long initial = sut.Revision;

        sut.MarkLocalHuman("X");

        sut.Revision.Should().BeGreaterThan(initial);
    }

    [Fact]
    public void Mode_Is_Multiplayer()
    {
        var sut = NewSession();
        sut.Mode.Should().Be(LobbyPlayerMode.Multiplayer);
    }
}
