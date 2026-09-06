using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Integration-style pipeline: GAME CTCP parse/peek → ingress WAF Evaluate → optional block → variant retest.
/// Does not require live IRC; exercises the same decision surface Session uses before writing lobby state.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class CnCNetIngressWafIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public CnCNetIngressWafIntegrationTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    // Protocol fingerprints / tunnels are off in the shipped default pack —
    // these parser→engine integration scenarios run with the explicit pack
    // (Issue #37), keeping default content classes for listing-text scoring.
    private static CnCNetIngressWaf CreateWaf()
        => new(() => new WafSettings(), persistUserList: false, rules: WafTestPacks.HangFarmWithDefaultContent());

    private static List<CnCNetTunnel> TunnelsIncludingHostBot()
        => SampleGameMessages.SampleTunnels(WafAttackFixtures.HostBotTunnelHost, WafAttackFixtures.HostBotTunnelPort);

    [Fact]
    public void Parsed_R13_HostBot_Tunnel_Listing_Is_Warned_Then_Dropped_After_Block()
    {
        string ctcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                channel: "#bot-room",
                roomName: "挂房机样本",
                players: new[] { "A", "B", "C", "D" },
                tunnelHost: WafAttackFixtures.HostBotTunnelHost,
                tunnelPort: WafAttackFixtures.HostBotTunnelPort));

        bool ok = CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "BotHost",
            ctcp,
            TunnelsIncludingHostBot(),
            sourceGameId: "mg",
            out CnCNetHostedGameSummary? game,
            out string? reject);
        ok.Should().BeTrue(reject);
        game.Should().NotBeNull();

        var waf = CreateWaf();
        WafDecision warn = EvaluateParsedGame(waf, "BotHost", ctcp, game!);
        warn.Severity.Should().Be(WafSeverity.Warn);
        warn.MatchedRuleIds.Should().Contain("proto.tunnel.blacklist");
        warn.MatchedRuleIds.Should().Contain("proto.game.fake_players");

        // Player confirms intercept (MVP: block suggested tunnel).
        string tunnelKey = warn.SuggestedBlockKeys.First(k => k.StartsWith("tunnel=", StringComparison.OrdinalIgnoreCase));
        waf.Block(tunnelKey);

        // Variant: new nick + new channel, same tunnel.
        string variantCtcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                channel: "#bot-room-2",
                roomName: "换皮房间",
                players: new[] { "Host", "P2" },
                tunnelHost: WafAttackFixtures.HostBotTunnelHost,
                tunnelPort: WafAttackFixtures.HostBotTunnelPort));
        CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "OtherNick",
            variantCtcp,
            TunnelsIncludingHostBot(),
            "mg",
            out CnCNetHostedGameSummary? variantGame,
            out _).Should().BeTrue();

        WafDecision dropped = EvaluateParsedGame(waf, "OtherNick", variantCtcp, variantGame!);
        dropped.Severity.Should().Be(WafSeverity.Drop);
        dropped.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    [Fact]
    public void Rejected_R8_Peek_Still_Raises_Protocol_Warn_For_HostBot()
    {
        // R8 is rejected by the stock parser; Session peeks fields for WAF alert-only path.
        string ctcp = "GAME R8;1.0;8;#r8room;挂机房;00000;A,B,C,D;Map;Mode;175.178.174.40:50000;";
        WafGameBroadcastPeek.TryPeek(ctcp, out WafGameBroadcastFields fields).Should().BeTrue();
        fields.Revision.Should().Be("R8");
        fields.TunnelEndpoint.Should().Be("175.178.174.40:50000");

        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "R8Bot",
            RawBody = ctcp,
            DisplayText = fields.RoomName,
            Game = fields,
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("proto.game.r8");
        d.MatchedRuleIds.Should().Contain("proto.tunnel.blacklist");
    }

    [Fact]
    public void Listing_Text_Promo_On_Parsed_Game_Is_Scored()
    {
        string ctcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                roomName: "加群代练优惠",
                map: "Map",
                gameMode: "Mode",
                tunnelHost: "tn.example.org",
                tunnelPort: 50000,
                players: new[] { "Host" }));

        CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "PromoHost",
            ctcp,
            SampleGameMessages.SampleTunnels("tn.example.org", 50000),
            "mg",
            out CnCNetHostedGameSummary? game,
            out _).Should().BeTrue();

        var waf = CreateWaf();
        WafDecision d = EvaluateParsedGame(waf, "PromoHost", ctcp, game!);
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.promo");
    }

    [Fact]
    public void EndToEnd_Warn_Then_Nick_Block_Stops_Promo_Pm_Variants()
    {
        var waf = CreateWaf();
        WafDecision warn = waf.Evaluate(
            WafAttackFixtures.PromoPrivateMessage("AdBot", "加群领免费代练 http://spam.vip"));
        warn.Severity.Should().Be(WafSeverity.Warn);
        warn.SuggestedBlockKeys.Should().Contain("nick=AdBot");

        waf.Block("nick=AdBot");

        foreach (string variant in WafAttackFixtures.PromoTextVariants())
        {
            waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("AdBot", variant))
                .Severity.Should().Be(WafSeverity.Drop, because: variant);
        }

        // Unrelated nick with same text still only Warns (not Drop via nick block).
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Other", "加群领免费代练 http://spam.vip"))
            .Severity.Should().Be(WafSeverity.Warn);
    }

    [Theory]
    [MemberData(nameof(QqPromoCases))]
    public void Qq_Promo_Group_Variants_Warn_With_Contact_Rule(string text)
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("QqSpammer", text));
        d.Severity.Should().Be(WafSeverity.Warn, because: text);
        d.MatchedRuleIds.Should().Contain("content.contact", because: text);
    }

    [Fact]
    public void EndToEnd_Qq_Group_Spam_Then_Nick_Block_Drops_Lobby_And_Pm_Variants()
    {
        var waf = CreateWaf();
        WafDecision warn = waf.Evaluate(
            WafAttackFixtures.LobbyPromoChat("QqBot", "QQ群：123456789 加群领代练"));
        warn.Severity.Should().Be(WafSeverity.Warn);
        warn.MatchedRuleIds.Should().Contain("content.contact");
        warn.SuggestedBlockKeys.Should().Contain("nick=QqBot");

        waf.Block("nick=QqBot");

        foreach (string variant in WafAttackFixtures.QqPromoGroupVariants())
        {
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("QqBot", variant))
                .Severity.Should().Be(WafSeverity.Drop, because: "lobby: " + variant);
            waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("QqBot", variant))
                .Severity.Should().Be(WafSeverity.Drop, because: "pm: " + variant);
        }

        // Different nick with QQ group text still Warns (contact), not Drop.
        WafDecision other = waf.Evaluate(
            WafAttackFixtures.PromoPrivateMessage("OtherQq", "QQ群：123456789 加群"));
        other.Severity.Should().Be(WafSeverity.Warn);
        other.MatchedRuleIds.Should().Contain("content.contact");
    }

    [Fact]
    public void Listing_RoomName_With_Qq_Group_Is_Scored_As_Contact()
    {
        string ctcp = SampleGameMessages.BuildGameCtcp(
            SampleGameMessages.BuildGameMessage(
                revision: "R13",
                roomName: "QQ群987654321低价代练",
                map: "Map",
                gameMode: "Mode",
                tunnelHost: "tn.example.org",
                tunnelPort: 50000,
                players: new[] { "Host" }));

        CnCNetMultiplayerProtocol.TryParseGameBroadcast(
            "QqHost",
            ctcp,
            SampleGameMessages.SampleTunnels("tn.example.org", 50000),
            "mg",
            out CnCNetHostedGameSummary? game,
            out _).Should().BeTrue();

        var waf = CreateWaf();
        WafDecision d = EvaluateParsedGame(waf, "QqHost", ctcp, game!);
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.contact");
    }

    public static TheoryData<string> QqPromoCases()
    {
        var data = new TheoryData<string>();
        foreach (string v in WafAttackFixtures.QqPromoGroupVariants())
            data.Add(v);
        return data;
    }

    private static WafDecision EvaluateParsedGame(
        CnCNetIngressWaf waf,
        string sender,
        string ctcp,
        CnCNetHostedGameSummary game)
        => waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = sender,
            RawBody = ctcp,
            DisplayText = game.RoomName,
            CtcpCommand = "GAME",
            CtcpPayload = ctcp,
            Game = new WafGameBroadcastFields
            {
                Revision = game.Revision,
                FieldCount = game.FieldCount,
                RoomName = game.RoomName,
                MapName = game.MapName,
                GameMode = game.GameMode,
                TunnelHost = game.TunnelAddress,
                TunnelPort = game.TunnelPort,
                ChannelName = game.ChannelName,
                Players = game.Players,
            },
        });
}
