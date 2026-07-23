using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Protocol contract: default stock DX R13, with wire-shape fallback for older MG DX peers
/// (R10 / 11-field) — not a LocalGame identity pin.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetProtocolR13AlignmentTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public CnCNetProtocolR13AlignmentTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    [Trait("DXContract", "DX-PROTOCOL-R13")]
    [Trait("Category", "Regression")]
    public void ProgramConstants_CnCNetProtocolRevision_IsCompileTimeConstR13()
    {
        FieldInfo? field = typeof(ProgramConstants).GetField(
            "CNCNET_PROTOCOL_REVISION",
            BindingFlags.Public | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.IsLiteral.Should().BeTrue("CNCNET_PROTOCOL_REVISION must be a compile-time const like DX");
        field.GetRawConstantValue().Should().Be(DxAliases.CurrentProtocolRevision);

        ProgramConstants.CNCNET_PROTOCOL_REVISION.Should().Be("R13");
        typeof(ProgramConstants).GetMethod("ApplyCnCNetProtocolRevision").Should().BeNull();
    }

    [Fact]
    [Trait("DXContract", "DX-PROTOCOL-R13")]
    [Trait("Category", "Regression")]
    public void Packaging_MgClientDefinitions_DoesNotForceR10Emit()
    {
        // Adaptive emit: packaging must not pin CnCNetProtocolRevision=R10.
        string packagingIni = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Packaging", "MG-Avalonia", "ClientDefinitions.ini"));

        if (!File.Exists(packagingIni))
        {
            packagingIni = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "Packaging", "MG-Avalonia", "ClientDefinitions.ini"));
        }

        File.Exists(packagingIni).Should().BeTrue($"expected packaging ini at {packagingIni}");
        string text = File.ReadAllText(packagingIni);

        text.Should().NotContain("CnCNetProtocolRevision=R10");
    }

    [Fact]
    [Trait("Category", "Usability")]
    [Trait("DXContract", "DX-PROTOCOL-R13")]
    public void Usability_MoStyleR13Broadcast_IsAccepted_WhenTunnelsAvailable()
    {
        string payload = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: DxAliases.CurrentProtocolRevision,
                gameVersion: "3.3.6",
                maxPlayers: 8,
                channel: "#cncnet-mo-game1234567",
                roomName: "MO Public Room",
                flags: "00000",
                players: new[] { "MoHost", "Alice" },
                map: "Coastal Interference",
                gameMode: "Standard",
                tunnelHost: "mo.tunnel.example",
                tunnelPort: 50000,
                skillLevel: 2,
                mapHash: "MOHASH"));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "MoHost",
            payload,
            SampleGameMessages.SampleTunnels("mo.tunnel.example", 50000),
            sourceGameId: "mo",
            out CnCNetHostedGameSummary? game,
            out string? rejectReason);

        ok.Should().BeTrue(because: rejectReason);
        game!.Revision.Should().Be("R13");
        game.RoomName.Should().Be("MO Public Room");
    }

    [Fact]
    [Trait("Category", "Usability")]
    public void Usability_MgDxStyleR10ElevenField_IsAccepted()
    {
        // Exact live shape from #yuanming-cg-games (trimmed).
        string payload = SampleGameMessages.BuildGameCtcp(string.Join(';',
            "R10", "1.0.4.2", "8", "#yuanming-games-游戏6021669", "shiguang玩家的游戏", "00000",
            "shiguang", "(2) 阿拉斯加油田", "常规作战", "45.76.154.140:50000", "0"));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "shiguang",
            payload,
            tunnels: null,
            sourceGameId: "mg",
            out CnCNetHostedGameSummary? game,
            out string? rejectReason);

        ok.Should().BeTrue(because: rejectReason);
        game!.Revision.Should().Be("R10");
        game.RoomName.Should().Be("shiguang玩家的游戏");
        game.TunnelAddress.Should().Be("45.76.154.140");
        game.TunnelPort.Should().Be(50000);
    }

    [Fact]
    [Trait("Category", "Usability")]
    public void Dialect_DefaultsToR13_ThenLocksAfterFirstPeerObservation()
    {
        var dialect = new CnCNetGameBroadcastDialect();
        dialect.EnterChannel("#yuanming-cg-games");
        dialect.IsWireModeLocked("#yuanming-cg-games").Should().BeFalse();
        dialect.ResolveEmitShape("#yuanming-cg-games")
            .Should().Be(CnCNetGameBroadcastDialect.WireShape.ModernR13);

        string legacy = SampleGameMessages.BuildGameCtcp(string.Join(';',
            "R10", "1.0", "4", "#ch", "room", "00000", "H", "m", "g", "t.example.com:50000", "0"));
        dialect.ObserveInbound("#yuanming-cg-games", legacy);

        dialect.IsWireModeLocked("#yuanming-cg-games").Should().BeTrue();
        dialect.PrefersLegacyEmit("#yuanming-cg-games").Should().BeTrue();
        dialect.ResolveEmitShape("#cncnet-mo-games")
            .Should().Be(CnCNetGameBroadcastDialect.WireShape.ModernR13,
                "MO channel was never observed as legacy");
    }

    [Fact]
    [Trait("Category", "Usability")]
    public void Dialect_LockIsSticky_AndClearedOnLeave()
    {
        var dialect = new CnCNetGameBroadcastDialect();
        dialect.EnterChannel("#yuanming-cg-games");

        string legacy = SampleGameMessages.BuildGameCtcp(string.Join(';',
            "R10", "1.0", "4", "#ch", "room", "00000", "H", "m", "g", "t.example.com:50000", "0"));
        string modern = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                gameVersion: "1.0",
                maxPlayers: 4,
                channel: "#ch",
                roomName: "room",
                flags: "00000",
                players: new[] { "H" },
                map: "m",
                gameMode: "g",
                tunnelHost: "t.example.com",
                tunnelPort: 50000,
                skillLevel: 0,
                mapHash: "HASH"));

        dialect.ObserveInbound("#yuanming-cg-games", legacy);
        dialect.ObserveInbound("#yuanming-cg-games", modern);
        dialect.PrefersLegacyEmit("#yuanming-cg-games").Should().BeTrue(
            "later R13 traffic must not unlock an R10 channel");

        dialect.ObserveInbound("#yuanming-cg-games", modern, fromLocalSender: true);
        dialect.PrefersLegacyEmit("#yuanming-cg-games").Should().BeTrue(
            "local echo is ignored for locking");

        dialect.LeaveChannel("#yuanming-cg-games");
        dialect.EnterChannel("#yuanming-cg-games");
        dialect.IsWireModeLocked("#yuanming-cg-games").Should().BeFalse();
        dialect.ResolveEmitShape("#yuanming-cg-games")
            .Should().Be(CnCNetGameBroadcastDialect.WireShape.ModernR13,
                "re-enter re-probes from R13 preference");
    }

    [Fact]
    [Trait("Category", "Usability")]
    public void BuildPayload_DefaultIsR13_LegacyFlagOmitsSkillAndHash()
    {
        var room = new CnCNetActiveGameRoom
        {
            RoomName = "创世房间",
            ChannelName = "#yuanming-cg-game7654321",
            Password = "",
            Tunnel = SampleGameMessages.SampleTunnel("mg.tunnel.example", 50001),
            MaxPlayers = 4,
            SkillLevel = 1,
        };

        string modern = CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room, "00000", new[] { "MgHost" }, "MG Map", "Standard", "MGHASH");
        modern.Split(';')[0].Should().EndWith("R13");
        modern.Split(';').Should().HaveCount(13);

        string legacy = CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room, "00000", new[] { "MgHost" }, "MG Map", "Standard", "MGHASH",
            useLegacyElevenField: true);
        legacy.Split(';')[0].Should().EndWith("R10");
        // "GAME " + 11 fields → Split(';') yields 11 parts after removing prefix... 
        // Actually whole string is "GAME R10;..." so Split(';') count is 11.
        legacy.Should().StartWith("GAME R10;");
        legacy[5..].Split(';').Should().HaveCount(11);
    }

    [Fact]
    [Trait("Category", "Usability")]
    public void Usability_GameCollection_ResolvesMoBroadcastChannel()
    {
        var collection = new CnCNetGameCollection();
        collection.Initialize();

        CnCNetGameEntry? mo = collection.Games.FirstOrDefault(g =>
            g.InternalName.Equals("mo", StringComparison.OrdinalIgnoreCase));

        mo.Should().NotBeNull();
        mo!.GameBroadcastChannel.Should().Be("#cncnet-mo-games");
        collection.FindByBroadcastChannel("#cncnet-mo-games")!
            .InternalName.Should().Be("mo");
    }
}
