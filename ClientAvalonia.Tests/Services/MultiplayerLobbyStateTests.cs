using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Services;

/// <summary>
/// MultiplayerLobbyState mirrors CnCNetLobbyState and copies its public fields via SyncFrom(core).
/// Tests verify the mirror contract: every observable on the source surfaces on the destination,
/// plus SelectedGameIndex self-heals when the underlying game list shrinks/grows.
/// </summary>
public sealed class MultiplayerLobbyStateTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var state = new MultiplayerLobbyState();
        state.ConnectionStatus.Should().Be("Offline");
        state.ChatChannelDisplay.Should().BeEmpty();
        state.AvailableChannelNames.Should().BeEmpty();
        state.ChannelPlayers.Should().BeEmpty();
        state.HostedGames.Should().BeEmpty();
        state.HostedGameDetails.Should().BeEmpty();
        state.OnlinePlayerCount.Should().Be(-1);
        state.SelectedGameIndex.Should().Be(-1);
    }

    [Fact]
    public void SyncFrom_CopiesEveryField_FromCore()
    {
        var core = new CnCNetLobbyState();
        core.SetConnectionStatus("Connected");
        core.SetChannelName("Test Channel", "#test");
        core.SetAvailableChannels(new[] { "#a", "#b" }, 1);
        core.SetChannelPlayers(new[] { "Alice", "Bob" });
        core.SetOnlinePlayerCount(42);
        core.AppendConnectionLog("Hello");

        var dest = new MultiplayerLobbyState();
        dest.SyncFrom(core);

        dest.ConnectionStatus.Should().Be("Connected");
        dest.ChatChannelDisplay.Should().Be("Test Channel");
        dest.AvailableChannelNames.Should().BeEquivalentTo(new[] { "#a", "#b" });
        dest.SelectedChannelIndex.Should().Be(1);
        dest.ChannelPlayers.Should().BeEquivalentTo(new[] { "Alice", "Bob" });
        dest.OnlinePlayerCount.Should().Be(42);
        dest.ConnectionLog.Should().HaveCount(1);
    }

    [Fact]
    public void SyncFrom_ClampsSelectedGameIndex_ToZero_WhenOutOfRange()
    {
        // DX-aligned clamp: out-of-range index resets to 0 (NOT to last) when games exist.
        var core = new CnCNetLobbyState();
        core.SetHostedGames(new[]
        {
            MakeGame("Game1"),
            MakeGame("Game2"),
        });

        var dest = new MultiplayerLobbyState { SelectedGameIndex = 99 };

        dest.SyncFrom(core);

        dest.SelectedGameIndex.Should().Be(0);
    }

    [Fact]
    public void SyncFrom_PromotesNegativeIndex_ToZero_WhenGamesExist()
    {
        var core = new CnCNetLobbyState();
        core.SetHostedGames(new[] { MakeGame("Game1") });

        var dest = new MultiplayerLobbyState { SelectedGameIndex = -1 };

        dest.SyncFrom(core);

        dest.SelectedGameIndex.Should().Be(0);
    }

    [Fact]
    public void SyncFrom_PreservesValidSelectedGameIndex()
    {
        var core = new CnCNetLobbyState();
        core.SetHostedGames(new[] { MakeGame("G1"), MakeGame("G2"), MakeGame("G3") });

        var dest = new MultiplayerLobbyState { SelectedGameIndex = 1 };

        dest.SyncFrom(core);

        dest.SelectedGameIndex.Should().Be(1);
    }

    [Fact]
    public void SyncFrom_ResetsIndexToMinusOne_WhenGameListEmpty()
    {
        var core = new CnCNetLobbyState();
        var dest = new MultiplayerLobbyState { SelectedGameIndex = 0 };

        dest.SyncFrom(core);

        dest.SelectedGameIndex.Should().Be(-1);
    }

    [Fact]
    public void GetSelectedGame_ReturnsGame_WhenIndexValid()
    {
        var core = new CnCNetLobbyState();
        var g1 = MakeGame("G1");
        var g2 = MakeGame("G2");
        core.SetHostedGames(new[] { g1, g2 });

        var dest = new MultiplayerLobbyState { SelectedGameIndex = 1 };
        dest.SyncFrom(core);

        dest.GetSelectedGame()!.RoomName.Should().Be("G2");
    }

    [Fact]
    public void RefreshFromCore_UsesLocalName_WhenProvided()
    {
        var dest = new MultiplayerLobbyState();
        dest.RefreshFromCore("Alice");
        dest.LocalPlayerName.Should().Be("Alice");
    }

    private static CnCNetHostedGameSummary MakeGame(string room)
        => new()
        {
            HostName = "Host",
            RoomName = room,
            ChannelName = "#" + room,
            MaxPlayers = 4,
            PlayerCount = 1,
            Players = new List<string> { "Host" },
        };
}
