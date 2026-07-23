using System;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class CnCNetIngressWafTests
{
    private static CnCNetIngressWaf CreateWaf(WafSettings? settings = null)
    {
        WafSettings snap = settings ?? new WafSettings();
        return new CnCNetIngressWaf(() => snap, persistUserList: false);
    }

    [Fact]
    public void Single_Contact_Qq_Number_Warns_On_Medium_Sensitivity()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("shiguang", "QQ 1234567890"));
        d.Severity.Should().Be(WafSeverity.Warn);
        d.Score.Should().BeGreaterThanOrEqualTo(25);
        d.MatchedRuleIds.Should().Contain("content.contact");
    }

    [Fact]
    public void XuanChuanQun_With_Digits_Warns()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("bot", "宣传群：1234567890"));
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.contact");
    }

    [Theory]
    [InlineData("Join my Discord discord.gg/abcd123 for free boosting")]
    [InlineData("Add me on Telegram t.me/boostshop cheap elo boost")]
    [InlineData("DM me for coaching and rank boost special offer")]
    [InlineData("Buy account shop - cheap boosting hire now")]
    [InlineData("WhatsApp wa.me/15551234567 join group promo")]
    public void English_Promo_And_Contact_Variants_Warn(string text)
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("EnSpammer", text));
        d.Severity.Should().Be(WafSeverity.Warn, because: text);
        d.MatchedRuleIds.Should().Contain(id =>
            id == "content.contact" || id == "content.promo" || id == "content.url",
            because: text);
    }

    [Fact]
    public void Known_HostBot_Tunnel_Scores_Warn()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "BotHost",
            Game = new WafGameBroadcastFields
            {
                Revision = "R13",
                FieldCount = 13,
                TunnelHost = "175.178.174.40",
                TunnelPort = 50000,
                ChannelName = "#room1",
                RoomName = "测试房",
                Players = ["BotHost"],
            },
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("proto.tunnel.blacklist");
        d.SuggestedBlockKeys.Should().Contain(k => k.StartsWith("tunnel=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void R8_And_FakePlayers_Accumulate_Score()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "Fake",
            Game = new WafGameBroadcastFields
            {
                Revision = "R8",
                FieldCount = 9,
                ChannelName = "#fake",
                Players = ["A", "B", "C", "D"],
            },
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("proto.game.r8");
        d.MatchedRuleIds.Should().Contain("proto.game.field_count");
        d.MatchedRuleIds.Should().Contain("proto.game.fake_players");
    }

    [Fact]
    public void Private_Message_Fraud_And_Sexual_Content_Warn()
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.PrivateChat,
            Surface = WafSurface.PrivateMessage,
            SenderNick = "Spammer",
            DisplayText = "把密码和验证码发给我，还可约炮加群 http://bad.example.com",
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.fraud");
        d.MatchedRuleIds.Should().Contain("content.sexual");
        d.MatchedRuleIds.Should().Contain("content.url");
    }

    [Fact]
    public void User_Blocklist_Drops()
    {
        var waf = CreateWaf();
        waf.Block("nick=BlockedNick");
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "BlockedNick",
            DisplayText = "hello",
        });

        d.Severity.Should().Be(WafSeverity.Drop);
        d.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    [Fact]
    public void Text_Normalizer_Strips_Irc_Colors_And_Zw()
    {
        string raw = "\u0003" + "04广告\u200b推广";
        string n = WafTextNormalizer.Normalize(raw);
        n.Should().Be("广告推广");
        WafDefaultRules.MatchesAny(n, WafDefaultRules.PromoKeywords).Should().BeTrue();
    }

    [Fact]
    public void Disabled_Waf_Allows_Everything()
    {
        var waf = CreateWaf(new WafSettings { Enabled = false });
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.PrivateChat,
            Surface = WafSurface.PrivateMessage,
            SenderNick = "x",
            DisplayText = "约炮 http://x.com 验证码发给我",
        });
        d.Severity.Should().Be(WafSeverity.Allow);
    }

    [Fact]
    public void Heuristic_Drop_Is_Clamped_To_Warn_By_Default()
    {
        // High sensitivity + huge score still should not Drop unless AllowHeuristicDrop.
        var waf = CreateWaf(new WafSettings { Sensitivity = 2, AllowHeuristicDrop = false, AutoHideHighRisk = false });
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "Bot",
            Game = new WafGameBroadcastFields
            {
                Revision = "R8",
                FieldCount = 9,
                TunnelHost = "175.178.174.40",
                TunnelPort = 50000,
                ChannelName = "#x",
                Players = ["A", "B", "C", "D"],
                RoomName = "代练加群 http://spam.vip",
            },
        });

        d.Score.Should().BeGreaterThan(100);
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().NotBeEmpty();
    }
}
