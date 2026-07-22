using System;
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
/// DX CnCNetLobby.cs:1519-1563 — strict 13-field GAME CTCP (R13).
/// Field order is locked by <see cref="DxAliases"/> and exercised via <see cref="SampleGameMessages"/>.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameMessageParserTests : IDisposable
{
    private const string HostName = "TestHost";
    private readonly TempGameRoot _root = new();
    private readonly string _originalGameVersion = ProgramConstants.GAME_VERSION;

    public CnCNetGameMessageParserTests()
    {
        // Bind a real (throwaway) game root so ClientConfiguration.Instance can resolve
        // LocalGame (the protocol reads it to set the Incompatible flag).
        _root.BindToProgramConstants();
    }

    public void Dispose()
    {
        ProgramConstants.GAME_VERSION = _originalGameVersion;
        _root.Dispose();
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FIELDS")]
    public void Parse_Extracts_AllThirteenFields_InDxOrder()
    {
        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                gameVersion: "2.0",
                maxPlayers: 6,
                channel: "#room-abc",
                roomName: "MyRoom",
                flags: "10010",  // locked=true, loaded=true (per DX defaults reversed here)
                players: new[] { "Host", "Alice", "Bob" },
                map: "BigMap",
                gameMode: "FreeForAll",
                tunnelHost: "tn.example.org",
                tunnelPort: 60000,
                loadedGameId: "LD1",
                skillLevel: 7,
                mapHash: "DEAD"));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName,
            payload,
            SampleGameMessages.SampleTunnels("tn.example.org", 60000),
            sourceGameId: "mg",
            out CnCNetHostedGameSummary? game,
            out string? rejectReason);

        ok.Should().BeTrue();
        rejectReason.Should().BeNull();
        game.Should().NotBeNull();
        game!.HostName.Should().Be(HostName);
        game.Revision.Should().Be("R13");
        game.GameVersion.Should().Be("2.0");
        game.MaxPlayers.Should().Be(6);
        game.ChannelName.Should().Be("#room-abc");
        game.RoomName.Should().Be("MyRoom");
        game.Players.Should().BeEquivalentTo(new[] { "Host", "Alice", "Bob" });
        game.PlayerCount.Should().Be(3);
        game.MapName.Should().Be("BigMap");
        game.GameMode.Should().Be("FreeForAll");
        game.TunnelAddress.Should().Be("tn.example.org");
        game.TunnelPort.Should().Be(60000);
        game.LoadedGameId.Should().Be("LD1");
        game.SkillLevel.Should().Be(7);
        game.MapHash.Should().Be("DEAD");
        // flags "10010" → locked=T, passworded=F, closed=F, loaded=T, ladder=F
        game.Locked.Should().BeTrue();
        game.RequiresPassword.Should().BeFalse();
        game.IsClosed.Should().BeFalse();
        game.IsLoadedGame.Should().BeTrue();
        game.IsLadder.Should().BeFalse();
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    [Trait("DXContract", "DX-GAME-REJECT-COUNT")]
    public void Parse_Rejects_WrongFieldCount_Not13Or11(int fieldCount)
    {
        // Build a payload with an arbitrary field count.
        string fields = string.Join(';', Enumerable.Repeat("x", fieldCount));
        string payload = SampleGameMessages.BuildGameCtcp(fields);

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels(), "mg", out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().Contain("field count");
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-REVISION")]
    public void Parse_Rejects_WrongRevision()
    {
        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(revision: "R99"));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels(), "mg", out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().Contain("protocol");
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-NOTUNNEL-REJECT")]
    public void Parse_Rejects_WhenTunnelListEmpty()
    {
        // Empty tunnel list (not null) → DX rejects with "no available tunnels".
        string payload = SampleGameMessages.BuildGameCtcp(SampleGameMessages.BuildGameMessage());

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, new System.Collections.Generic.List<CnCNetTunnel>(), "mg",
            out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().Be(DxAliases.RejectReasonNoTunnels);
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-NOTUNNEL-REJECT")]
    public void Parse_Accepts_WhenTunnelListIsNull_SkipsTunnelValidation()
    {
        // tunnels=null path skips validation entirely — used when caller doesn't have tunnels loaded.
        string payload = SampleGameMessages.BuildGameCtcp(SampleGameMessages.BuildGameMessage());

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, tunnels: null, sourceGameId: "mg", out _, out _);

        ok.Should().BeTrue();
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-NOTUNNEL-REJECT")]
    public void Parse_Rejects_WhenBroadcastTunnelNotInList()
    {
        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(tunnelHost: "rogue.tunnel", tunnelPort: 1234));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels("other.tunnel", 50000), "mg",
            out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().Contain("unavailable");
    }

    [Fact]
    [Trait("Category", "Usability")]
    [Trait("DXContract", "DX-GAME-FIELDS")]
    public void Parse_ElevenFieldR10Layout_IsAccepted_AsWireFallback()
    {
        // Live #yuanming-cg-games MG DX hosts emit R10 / 11 fields. Receive path must fall back.
        string eleven = string.Join(';',
            "R10", "1.0.4.2", "8", "#yuanming-games-x", "Room", "00000", "Host",
            "Map", "Mode", "tunnel.example.com:50000", "0");
        string payload = SampleGameMessages.BuildGameCtcp(eleven);

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels(), "mg",
            out CnCNetHostedGameSummary? game, out string? rejectReason);

        ok.Should().BeTrue(because: rejectReason);
        game!.Revision.Should().Be("R10");
        game.MapHash.Should().BeEmpty();
        game.SkillLevel.Should().Be(0);
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-REVISION")]
    public void Parse_R10Revision_OnThirteenFields_IsRejected()
    {
        // R10 label on a 13-field body is not a known wire dialect.
        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(revision: DxAliases.RejectedLegacyProtocolRevision));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels(), "mg", out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().Contain("protocol");
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FIELDS")]
    public void ParseGameMessageParser_Facade_Delegates_ToProtocol()
    {
        // CnCNetGameMessageParser is a thin facade over the protocol — verify it surfaces the same result.
        string payload = SampleGameMessages.BuildGameCtcp(SampleGameMessages.BuildGameMessage());

        CnCNetHostedGameSummary? game = CnCNetGameMessageParser.TryParse(
            HostName, payload, SampleGameMessages.SampleTunnels());

        game.Should().NotBeNull();
        game!.HostName.Should().Be(HostName);
    }

    [Fact]
    public void Parse_DoesNotStartWithGamePrefix_ReturnsFalse_WithoutRejectReason()
    {
        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, "NOTAGAME foo;bar", null, "mg", out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().BeNull();
    }

    /// <summary>
    /// Defense-in-depth: a hostile/buggy peer must never be able to crash the IRC read loop by
    /// sending a malformed GAME CTCP. These inputs must degrade to a typed rejection, not throw.
    /// </summary>
    [Theory]
    [InlineData("GAME ")]                  // bare prefix, no payload — Substring(5..) would throw
    [InlineData("GAME")]                   // missing trailing space
    [InlineData("GAME \u0000\u0001")]      // prefix + control chars only
    [InlineData("")]                       // empty
    [Trait("Category", "Security")]
    public void Parse_MalformedGameCtcp_NeverThrows(string hostileCtcp)
    {
        Action act = () => CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, hostileCtcp, SampleGameMessages.SampleTunnels(), "mg",
            out _, out _);

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Parse_BareGamePrefix_RejectsWithTooShortReason()
    {
        // "GAME " alone (length 5) is the boundary: our defensive guard returns false with a
        // diagnostic rejectReason so the failure shows up in logs instead of being silently
        // swallowed or propagated as an unhandled exception.
        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, "GAME ", SampleGameMessages.SampleTunnels(), "mg",
            out _, out string? rejectReason);

        ok.Should().BeFalse();
        rejectReason.Should().NotBeNull();
        rejectReason.Should().Contain("too short");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Parse_GamePrefixWithTruncatedFields_DoesNotThrow()
    {
        // 13 fields but one field references an index that fails IPv4 parsing in the past;
        // the protocol should now catch any leftover FormatException/IndexOutOfRangeException
        // via the parser-level fallback and return a typed rejection.
        string payload = "GAME R13;2.0;6;#room;room;00000;Host;map;mode;not_a_host:port;0;5;hash";

        Action act = () => CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            HostName, payload, SampleGameMessages.SampleTunnels(), "mg",
            out _, out _);

        act.Should().NotThrow();
    }
}
