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
/// DX CnCNetLobby.cs:1519-1563 — strict 13-field GAME CTCP, with R10 11-field legacy fallback.
/// Field order is locked by <see cref="DxAliases"/> and exercised via <see cref="SampleGameMessages"/>.
///
/// Marked serial because we mutate <see cref="ProgramConstants.CNCNET_PROTOCOL_REVISION"/>
/// (a process-wide static) to exercise R13 vs R10 paths.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetGameMessageParserTests : IDisposable
{
    private const string HostName = "TestHost";
    private readonly TempGameRoot _root = new();
    private readonly string _originalRevision = ProgramConstants.CNCNET_PROTOCOL_REVISION;

    public CnCNetGameMessageParserTests()
    {
        // Bind a real (throwaway) game root so ClientConfiguration.Instance can resolve
        // LocalGame (the protocol reads it to set the Incompatible flag).
        _root.BindToProgramConstants();

        // Default to R13 for the 13-field tests; legacy tests override locally.
        ProgramConstants.ApplyCnCNetProtocolRevision(DxAliases.CurrentProtocolRevision);
    }

    public void Dispose()
    {
        // Restore whatever revision was active before this test ran so we don't leak state
        // into other test classes that happen to read CNCNET_PROTOCOL_REVISION.
        ProgramConstants.ApplyCnCNetProtocolRevision(_originalRevision);
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
    [Trait("DXContract", "DX-GAME-LEGACY-11")]
    public void Parse_Legacy11Fields_R10Path_Accepted()
    {
        // 11 fields + R10 revision → legacy fallback path (MG current protocol uses R10).
        ProgramConstants.ApplyCnCNetProtocolRevision(DxAliases.LegacyProtocolRevision);

        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildLegacyGameMessage(
                revision: DxAliases.LegacyProtocolRevision,
                tunnelHost: "tn.example.org",
                tunnelPort: 60000));

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
        game!.Revision.Should().Be(DxAliases.LegacyProtocolRevision);
        // Legacy path leaves skillLevel=0 and mapHash="" (not present in 11-field layout).
        game.SkillLevel.Should().Be(0);
        game.MapHash.Should().BeEmpty();
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
}
