using System;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// P0–P2 MVP attack scenarios: hanging-room bot fingerprints, promo/spam PMs,
/// player blocklist Drop, and obfuscated variants.
/// </summary>
public sealed class CnCNetIngressWafMvpScenarioTests
{
    private static CnCNetIngressWaf CreateWaf(WafSettings? settings = null)
        => new(() => settings ?? new WafSettings(), persistUserList: false);

    [Fact]
    public void P0_HostBot_Tunnel_R8_FakePlayers_Warns_And_Suggests_Block_Keys()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast("BotHostA", WafAttackFixtures.HostBotGame()));

        d.Severity.Should().Be(WafSeverity.Warn);
        d.Score.Should().BeGreaterThanOrEqualTo(35);
        d.MatchedRuleIds.Should().Contain("proto.tunnel.blacklist");
        d.MatchedRuleIds.Should().Contain("proto.game.r8");
        d.MatchedRuleIds.Should().Contain("proto.game.fake_players");
        d.SuggestedBlockKeys.Should().Contain("tunnel=175.178.174.40:50000");
        d.SuggestedBlockKeys.Should().Contain(k => k.StartsWith("nick=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void P0_Legitimate_R13_Game_Is_Not_Dropped_By_Default()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.LegitimateR13Game());
        d.Severity.Should().Be(WafSeverity.Allow);
        d.Score.Should().Be(0);
    }

    [Fact]
    public void P0_After_Tunnel_Block_All_HostBot_Variants_Are_Dropped()
    {
        var waf = CreateWaf();
        WafDecision first = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast("BotHostA", WafAttackFixtures.HostBotGame(channel: "#ym-1")));
        first.Severity.Should().Be(WafSeverity.Warn);

        waf.Block("tunnel=175.178.174.40:50000");

        // Same tunnel, different nick/channel/room text → still Drop.
        WafDecision v1 = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast(
                "BotHostB",
                WafAttackFixtures.HostBotGame(nick: "BotHostB", channel: "#ym-2", roomName: "换皮房名")));
        WafDecision v2 = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast(
                "BotHostC",
                WafAttackFixtures.HostBotGame(
                    nick: "BotHostC",
                    channel: "#ym-3",
                    roomName: "另一模板",
                    revision: "R13",
                    fieldCount: 13,
                    players: ["Host", "P2"])));

        v1.Severity.Should().Be(WafSeverity.Drop);
        v2.Severity.Should().Be(WafSeverity.Drop);
        v1.MatchedRuleIds.Should().Contain("user.blocklist");
        v2.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    [Fact]
    public void P1_Promo_Private_Message_Warns_With_Content_Rules()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(
            WafAttackFixtures.PromoPrivateMessage("Spammer", "加群领免费代练 http://spam.vip/x"));

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.promo");
        d.MatchedRuleIds.Should().Contain("content.url");
        d.MatchedRuleIds.Should().Contain("content.pm.first_contact_promo");
    }

    [Theory]
    [MemberData(nameof(PromoVariants))]
    public void P1_Promo_Text_Variants_Still_Warn(string text)
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("VariantBot", text));
        d.Severity.Should().BeOneOf(WafSeverity.Warn, WafSeverity.Hide);
        d.MatchedRuleIds.Should().Contain(id =>
            id == "content.promo"
            || id == "content.contact"
            || id == "content.url"
            || id == "content.fraud");
    }

    public static TheoryData<string> PromoVariants()
    {
        var data = new TheoryData<string>();
        foreach (string v in WafAttackFixtures.PromoTextVariants())
            data.Add(v);
        return data;
    }

    [Fact]
    public void P1_Pm_Burst_Raises_Score_After_Repeated_Messages()
    {
        var waf = CreateWaf();
        WafDecision? last = null;
        for (int i = 0; i < 5; i++)
        {
            last = waf.Evaluate(
                WafAttackFixtures.PromoPrivateMessage("Flooder", "加群推广 " + i));
        }

        last.Should().NotBeNull();
        last!.MatchedRuleIds.Should().Contain("content.pm.burst");
        last.Severity.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void P1_After_Nick_Block_Chat_And_Pm_Are_Dropped()
    {
        var waf = CreateWaf();
        waf.Block("nick=Spammer");

        waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Spammer", "hello"))
            .Severity.Should().Be(WafSeverity.Drop);
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Spammer", "加群"))
            .Severity.Should().Be(WafSeverity.Drop);
    }

    [Fact]
    public void P1_Disabling_Private_Chat_Leaves_Channel_Rules_Active()
    {
        var waf = CreateWaf(new WafSettings { CheckPrivateChat = false, CheckChannelChat = true });
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Spammer", "加群领免费代练 http://x.vip"))
            .Severity.Should().Be(WafSeverity.Allow);
        waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Spammer", "加群领免费代练 http://x.vip"))
            .Severity.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void P2_Template_Fingerprint_Across_Nicks_Warns()
    {
        var waf = CreateWaf();
        var template = WafAttackFixtures.HostBotGame(
            revision: "R13",
            fieldCount: 13,
            roomName: "模板房",
            channel: "#tpl-1",
            players: ["Host", "P2"]);

        WafDecision first = waf.Evaluate(WafAttackFixtures.HostBotBroadcast("NickOne", template));
        // First sighting may already warn via tunnel blacklist; fingerprint needs 2 nicks.
        WafDecision second = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast(
                "NickTwo",
                WafAttackFixtures.HostBotGame(
                    nick: "NickTwo",
                    revision: "R13",
                    fieldCount: 13,
                    roomName: "模板房",
                    channel: "#tpl-2",
                    players: ["Host", "P2"])));

        second.MatchedRuleIds.Should().Contain("proto.game.template_fingerprint");
        second.SuggestedBlockKeys.Should().Contain(k => k.StartsWith("fingerprint=", StringComparison.OrdinalIgnoreCase));
        first.Severity.Should().BeOneOf(WafSeverity.Warn, WafSeverity.Hide);
        second.Severity.Should().BeOneOf(WafSeverity.Warn, WafSeverity.Hide);
    }

    [Fact]
    public void P2_After_Fingerprint_Block_Variant_Nick_Same_Template_Drops()
    {
        var waf = CreateWaf();
        var game = WafAttackFixtures.HostBotGame(
            revision: "R13",
            fieldCount: 13,
            roomName: "模板房",
            channel: "#tpl-a",
            players: ["X", "Y"]);

        // Seed fingerprint with two nicks.
        waf.Evaluate(WafAttackFixtures.HostBotBroadcast("NickOne", game));
        WafDecision second = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast(
                "NickTwo",
                WafAttackFixtures.HostBotGame(
                    revision: "R13",
                    fieldCount: 13,
                    roomName: "模板房",
                    channel: "#tpl-b",
                    players: ["X", "Y"])));

        string fpKey = second.SuggestedBlockKeys.First(k => k.StartsWith("fingerprint=", StringComparison.OrdinalIgnoreCase));
        waf.Block(fpKey);

        WafDecision third = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast(
                "NickThree",
                WafAttackFixtures.HostBotGame(
                    revision: "R13",
                    fieldCount: 13,
                    roomName: "模板房",
                    channel: "#tpl-c",
                    players: ["X", "Y"])));

        third.Severity.Should().Be(WafSeverity.Drop);
        third.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    [Fact]
    public void P2_Invite_Flood_Warns()
    {
        var waf = CreateWaf();
        WafDecision? last = null;
        for (int i = 0; i < 4; i++)
        {
            last = waf.Evaluate(
                WafAttackFixtures.InviteCtcp("Inviter", $"#room-{i};SpamGame"));
        }

        last.Should().NotBeNull();
        last!.MatchedRuleIds.Should().Contain("proto.invite.flood");
        last.Severity.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void P2_Shared_Tunnel_Hosts_Cluster_Warns()
    {
        var waf = CreateWaf();
        // Use non-blacklisted tunnel so shared_hosts is the interesting signal.
        WafDecision? last = null;
        foreach (string nick in new[] { "H1", "H2", "H3" })
        {
            last = waf.Evaluate(new WafIngressEvent
            {
                Kind = WafIngressKind.GameBroadcast,
                Surface = WafSurface.Protocol,
                SenderNick = nick,
                Game = new WafGameBroadcastFields
                {
                    Revision = "R13",
                    FieldCount = 13,
                    ChannelName = "#" + nick,
                    RoomName = "Room",
                    TunnelHost = "shared.tunnel.test",
                    TunnelPort = 50000,
                    Players = [nick],
                },
            });
        }

        last.Should().NotBeNull();
        last!.MatchedRuleIds.Should().Contain("proto.tunnel.shared_hosts");
        last.Severity.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void Master_Switch_Off_Allows_All_Attack_Traffic()
    {
        var waf = CreateWaf(new WafSettings { Enabled = false });
        waf.Evaluate(WafAttackFixtures.HostBotBroadcast("Bot", WafAttackFixtures.HostBotGame()))
            .Severity.Should().Be(WafSeverity.Allow);
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("S", "加群约炮验证码发给我 http://x.com"))
            .Severity.Should().Be(WafSeverity.Allow);
    }

    [Fact]
    public void Heuristic_Never_Auto_Drops_Without_AllowHeuristicDrop()
    {
        var waf = CreateWaf(new WafSettings { Sensitivity = 2, AllowHeuristicDrop = false });
        WafDecision d = waf.Evaluate(
            WafAttackFixtures.HostBotBroadcast("Bot", WafAttackFixtures.HostBotGame()));
        d.Score.Should().BeGreaterThan(100);
        d.Severity.Should().NotBe(WafSeverity.Drop);
    }
}
