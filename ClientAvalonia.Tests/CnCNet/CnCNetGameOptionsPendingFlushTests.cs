using System;
using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Joiner GO apply + pending-body flush when lobby controls are not ready yet.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameOptionsPendingFlushTests : IDisposable
{
    private const string Channel = "#go-test-room";
    private const string HostNick = "HostGuy";

    private readonly TempGameRoot _root = new();

    public CnCNetGameOptionsPendingFlushTests()
    {
        _root.BindToProgramConstants();
        ProgramConstants.PLAYERNAME = "Joiner";
    }

    public void Dispose()
    {
        ProgramConstants.PLAYERNAME = "Player";
        _root.Dispose();
    }

    [Fact]
    public void Go_WhenControlCountsZero_IsDeferred_DoesNotInvokeReceiver()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (0, 0);
        CnCNetGameOptionsState? received = null;
        room.GameOptionsReceiver = s => received = s;

        room.OnChannelCtcp(Channel, HostNick, "GO " + BuildSampleBody(2, 1));

        received.Should().BeNull();
        room.ChatLines.Should().NotContain(l => l.DisplayText.Contains("Game options updated"));
    }

    [Fact]
    public void TryFlushPendingGameOptions_ReplaysDeferredGo_WhenControlsReady()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (0, 0);

        CnCNetGameOptionsState? received = null;
        room.GameOptionsReceiver = s => received = s;

        var expected = SampleState(checkBoxes: [true, false], dropDowns: [2], seed: 777001);
        room.OnChannelCtcp(Channel, HostNick, "GO " + CnCNetGameOptionsCodec.BuildBody(expected, 2, 1));
        received.Should().BeNull("GO must stay pending while counts are 0");

        room.GameOptionsControlCounts = () => (2, 1);
        room.TryFlushPendingGameOptions();

        received.Should().NotBeNull();
        received!.CheckBoxValues.Should().Equal(true, false);
        received.DropDownIndices.Should().Equal(2);
        received.MapSha1.Should().Be(expected.MapSha1);
        room.RandomSeed.Should().Be(777001);
        room.FrameSendRate.Should().Be(expected.FrameSendRate);
        room.MaxAhead.Should().Be(expected.MaxAhead);
        room.ProtocolVersion.Should().Be(expected.ProtocolVersion);
        room.RemoveStartingLocations.Should().Be(expected.RemoveStartingLocations);
        room.ChatLines.Should().Contain(l => l.IsSystem && l.DisplayText.Contains("Game options updated"));
    }

    [Fact]
    public void TryFlushPendingGameOptions_IsNoOp_WhenNothingPending()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (2, 1);
        int calls = 0;
        room.GameOptionsReceiver = _ => calls++;

        room.TryFlushPendingGameOptions();

        calls.Should().Be(0);
    }

    [Fact]
    public void TryFlushPendingGameOptions_ConsumesPending_SecondFlushDoesNothing()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (0, 0);
        room.OnChannelCtcp(Channel, HostNick, "GO " + BuildSampleBody(2, 1));

        int calls = 0;
        room.GameOptionsControlCounts = () => (2, 1);
        room.GameOptionsReceiver = _ => calls++;

        room.TryFlushPendingGameOptions();
        room.TryFlushPendingGameOptions();

        calls.Should().Be(1);
    }

    [Fact]
    public void Go_AppliesImmediately_WhenControlsAlreadyReady()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (2, 1);
        CnCNetGameOptionsState? received = null;
        room.GameOptionsReceiver = s => received = s;

        var expected = SampleState(checkBoxes: [false, true], dropDowns: [1], seed: 42);
        room.OnChannelCtcp(Channel, HostNick, "GO " + CnCNetGameOptionsCodec.BuildBody(expected, 2, 1));

        received.Should().NotBeNull();
        room.RandomSeed.Should().Be(42);
    }

    [Fact]
    public void Go_FromNonHost_IsIgnored()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (2, 1);
        int calls = 0;
        room.GameOptionsReceiver = _ => calls++;

        room.OnChannelCtcp(Channel, "Imposter", "GO " + BuildSampleBody(2, 1));

        calls.Should().Be(0);
    }

    [Fact]
    public void Go_OnHostSession_IsIgnored()
    {
        CnCNetGameRoomSession room = MakeHostRoom();
        room.GameOptionsControlCounts = () => (2, 1);
        int calls = 0;
        room.GameOptionsReceiver = _ => calls++;

        room.OnChannelCtcp(Channel, HostNick, "GO " + BuildSampleBody(2, 1));

        calls.Should().Be(0);
    }

    [Fact]
    public void Go_InvalidBody_AddsNotice_DoesNotInvokeReceiver()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (2, 1);
        int calls = 0;
        room.GameOptionsReceiver = _ => calls++;

        room.OnChannelCtcp(Channel, HostNick, "GO 1;0");

        calls.Should().Be(0);
        room.ChatLines.Should().Contain(l =>
            l.IsSystem && l.DisplayText.Contains("invalid game options", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeferredGo_LatestBodyWins_WhenMultipleArriveBeforeFlush()
    {
        CnCNetGameRoomSession room = MakeJoinerRoom();
        room.GameOptionsControlCounts = () => (0, 0);

        room.OnChannelCtcp(
            Channel,
            HostNick,
            "GO " + CnCNetGameOptionsCodec.BuildBody(SampleState(seed: 1), 2, 1));
        room.OnChannelCtcp(
            Channel,
            HostNick,
            "GO " + CnCNetGameOptionsCodec.BuildBody(SampleState(seed: 99), 2, 1));

        CnCNetGameOptionsState? received = null;
        room.GameOptionsControlCounts = () => (2, 1);
        room.GameOptionsReceiver = s => received = s;
        room.TryFlushPendingGameOptions();

        received.Should().NotBeNull();
        room.RandomSeed.Should().Be(99);
    }

    private static string BuildSampleBody(int checkBoxes, int dropDowns)
        => CnCNetGameOptionsCodec.BuildBody(SampleState(), checkBoxes, dropDowns);

    private static CnCNetGameOptionsState SampleState(
        IReadOnlyList<bool>? checkBoxes = null,
        IReadOnlyList<int>? dropDowns = null,
        int seed = 12345)
        => new()
        {
            CheckBoxValues = checkBoxes ?? [true, false],
            DropDownIndices = dropDowns ?? [1],
            MapOfficial = true,
            MapSha1 = "MAPHASH",
            GameModeName = "Standard",
            FrameSendRate = 5,
            MaxAhead = 8,
            ProtocolVersion = 2,
            RandomSeed = seed,
            RemoveStartingLocations = false,
            MapUntranslatedName = "Test Map",
        };

    private static CnCNetGameRoomSession MakeJoinerRoom()
    {
        return new CnCNetGameRoomSession(new CnCNetActiveGameRoom
        {
            RoomName = "GO Room",
            ChannelName = Channel,
            Password = string.Empty,
            Tunnel = new CnCNetTunnel(),
            HostName = HostNick,
            IsHost = false,
            MaxPlayers = 4,
        });
    }

    private static CnCNetGameRoomSession MakeHostRoom()
    {
        ProgramConstants.PLAYERNAME = HostNick;
        return new CnCNetGameRoomSession(new CnCNetActiveGameRoom
        {
            RoomName = "GO Room",
            ChannelName = Channel,
            Password = string.Empty,
            Tunnel = new CnCNetTunnel(),
            HostName = HostNick,
            IsHost = true,
            MaxPlayers = 4,
        });
    }
}
