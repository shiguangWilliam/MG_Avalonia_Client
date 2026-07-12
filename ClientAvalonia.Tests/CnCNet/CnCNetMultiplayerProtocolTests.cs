using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Round-trip and protocol-internal tests for CnCNetMultiplayerProtocol beyond the
/// GAME-parsing suite in <see cref="CnCNetGameMessageParserTests"/>:
///   - BuildGameBroadcastPayload produces a CTCP that parses back to the same fields.
///   - HumanPlayerOptionsLength / AiPlayerOptionsLength constants are stable.
///
/// Serial because we mutate ProgramConstants.CNCNET_PROTOCOL_REVISION / GAME_VERSION.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetMultiplayerProtocolTests : System.IDisposable
{
    private readonly TempGameRoot _root = new();
    private readonly string _originalRevision = ProgramConstants.CNCNET_PROTOCOL_REVISION;
    private readonly string _originalGameVersion = ProgramConstants.GAME_VERSION;

    public CnCNetMultiplayerProtocolTests()
    {
        _root.BindToProgramConstants();
        ProgramConstants.ApplyCnCNetProtocolRevision(DxAliases.CurrentProtocolRevision);
        ProgramConstants.GAME_VERSION = "2.0.0";
    }

    public void Dispose()
    {
        ProgramConstants.ApplyCnCNetProtocolRevision(_originalRevision);
        ProgramConstants.GAME_VERSION = _originalGameVersion;
        _root.Dispose();
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FIELDS")]
    public void BuildGameBroadcastPayload_Roundtrips_ThroughParse()
    {
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "RoundTrip Room",
            ChannelName = DxAliases.SampleChannel,
            Password = "secret",
            Tunnel = SampleGameMessages.SampleTunnel("tunnel.example.com", 50000),
            MaxPlayers = 6,
            SkillLevel = 3,
        };
        string flags = CnCNetGameFlags.Build(locked: false, passworded: true, closed: false);
        var players = new List<string> { "Host", "Alice" };

        string payload = CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room, flags, players, "MapX", "Standard", "HASH123");

        // Round-trip: parse the built payload.
        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "Host",
            payload,
            SampleGameMessages.SampleTunnels("tunnel.example.com", 50000),
            sourceGameId: "mg",
            out CnCNetHostedGameSummary? game,
            out _);

        ok.Should().BeTrue();
        game.Should().NotBeNull();
        game!.Revision.Should().Be(DxAliases.CurrentProtocolRevision);
        game.GameVersion.Should().Be("2.0.0");
        game.MaxPlayers.Should().Be(6);
        game.RoomName.Should().Be("RoundTrip Room");
        game.ChannelName.Should().Be(DxAliases.SampleChannel);
        game.Players.Should().BeEquivalentTo(players);
        game.MapName.Should().Be("MapX");
        game.GameMode.Should().Be("Standard");
        game.MapHash.Should().Be("HASH123");
        game.TunnelAddress.Should().Be("tunnel.example.com");
        game.TunnelPort.Should().Be(50000);
        game.SkillLevel.Should().Be(3);
        game.RequiresPassword.Should().BeTrue();
        game.Locked.Should().BeFalse();
        game.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void BuildGameBroadcastPayload_AlwaysHasLoadedGameId_Zero()
    {
        // Index 10 is always '0' in fresh broadcasts — only loaded games set a non-zero id.
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "X",
            ChannelName = "#c",
            Password = "",
            Tunnel = SampleGameMessages.SampleTunnel("h", 1),
        };

        string payload = CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room, "00000", new[] { "P" }, "M", "GM", "H");

        // Split on ';' — index 10 (after stripping "GAME " prefix from index 0) is the loaded-game id.
        string[] parts = payload["GAME ".Length..].Split(';');
        parts[DxAliases.IdxLoadedGameId].Should().Be("0");
    }

    [Fact]
    public void BuildGameBroadcastPayload_PreservesChannelCase_ViaPreserve()
    {
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "X",
            ChannelName = "MixedCase-Channel", // no leading #
            Password = "",
            Tunnel = SampleGameMessages.SampleTunnel("h", 1),
        };

        string payload = CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room, "00000", new[] { "P" }, "M", "GM", "H");

        string[] parts = payload["GAME ".Length..].Split(';');
        parts[DxAliases.IdxChannel].Should().Be("#MixedCase-Channel");
    }

    [Fact]
    public void Constants_PlayerOptionsLength_AreStable()
    {
        // These mirror DX non-standard but stable player-options layout.
        CnCNetMultiplayerProtocol.HumanPlayerOptionsLength.Should().Be(3);
        CnCNetMultiplayerProtocol.AiPlayerOptionsLength.Should().Be(2);
        CnCNetMultiplayerProtocol.MaxPlayerCount.Should().Be(8);
        CnCNetMultiplayerProtocol.GameBroadcastFieldCount.Should().Be(DxAliases.GameFieldCount);
        CnCNetMultiplayerProtocol.LegacyGameBroadcastFieldCount.Should().Be(DxAliases.LegacyGameFieldCount);
    }
}
