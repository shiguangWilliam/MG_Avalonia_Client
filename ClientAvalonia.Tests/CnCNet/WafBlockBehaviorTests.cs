using System;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Design-facing WAF behaviour: after the player confirms a ban, matching traffic
/// is Drop without UI alerts; compact-equivalent bodies are treated as the same
/// high-confidence template and also Drop silently.
/// </summary>
public sealed class WafBlockBehaviorTests
{
    private static CnCNetIngressWaf CreateWaf()
        => new(() => new WafSettings(), persistUserList: false);

    [Fact]
    public void After_Player_Ban_Same_Nick_Is_Silent_Drop_Even_On_Clean_Text()
    {
        var waf = CreateWaf();
        int alerts = 0;
        waf.AlertRaised += _ => alerts++;

        var evt = WafAttackFixtures.LobbyPromoChat("Toxic", "你妈死了");
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        alerts.Should().Be(1);

        waf.BlockFromAlert(evt, warn, "玩家确认屏蔽");
        alerts = 0;

        WafDecision again = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Toxic", "hello gg wp"));
        again.Severity.Should().Be(WafSeverity.Drop);
        again.MatchedRuleIds.Should().Contain("user.blocklist");
        alerts.Should().Be(0, because: "blocklist Drop must not raise UI alerts");
    }

    [Fact]
    public void After_Player_Ban_Same_Body_From_Other_Nick_Is_Silent_Drop()
    {
        var waf = CreateWaf();
        string body = "q群 76543210 工作室招人";
        var evt = WafAttackFixtures.PromoPrivateMessage("AdOne", body);
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);

        waf.BlockFromAlert(evt, warn, "ban");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;

        WafDecision other = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("AdTwo", body));
        other.Severity.Should().Be(WafSeverity.Drop);
        other.MatchedRuleIds.Should().Contain("user.blocklist.body");
        alerts.Should().Be(0);
    }

    [Theory]
    [InlineData("加群领免费代练 http://spam.vip/x")]
    [InlineData("加\u200b群领\u200b免\u200b费\u200b代\u200b练 http://spam.vip/x")]
    [InlineData("加 群 领 免 费 代 练 http://spam.vip/x")]
    public void Compact_Equivalent_Bodies_Share_Fingerprint_And_Drop(string variant)
    {
        var waf = CreateWaf();
        string canonical = "加群领免费代练 http://spam.vip/x";
        var evt = WafAttackFixtures.LobbyPromoChat("Seed", canonical);
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "ban");

        string keyCanon = WafBodyFingerprint.KeyFromText(canonical);
        string keyVariant = WafBodyFingerprint.KeyFromText(variant);
        keyVariant.Should().Be(keyCanon, because: "normalize+compact must collapse high-similarity variants");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Other", variant))
            .Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);
    }

    [Fact]
    public void Different_Body_From_Unblocked_Nick_Still_Warns()
    {
        var waf = CreateWaf();
        var evt = WafAttackFixtures.LobbyPromoChat("Seed", "你妈死了");
        waf.BlockFromAlert(evt, waf.Evaluate(evt), "ban");

        // Different insult body + different nick → still heuristic warn (not blocklist).
        WafDecision other = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Other", "傻逼弱智"));
        other.Severity.Should().Be(WafSeverity.Warn);
        other.MatchedRuleIds.Should().NotContain(id => id.StartsWith("user.blocklist", StringComparison.Ordinal));
    }

    [Fact]
    public void Strategy_Drop_Is_Silent_Like_Blocklist()
    {
        var prefs = new WafStrategyPrefs();
        prefs.SetMode("content.abuse", WafStrategyMode.Drop);
        prefs.SetMode("content.hate", WafStrategyMode.Off);
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, strategyPrefs: prefs);

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;

        WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("X", "你妈死了"));
        d.Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);
    }

    [Fact]
    public void BlockFromAlert_Writes_Nick_And_Body_Keys()
    {
        var waf = CreateWaf();
        var evt = new WafIngressEvent
        {
            Kind = WafIngressKind.PrivateChat,
            Surface = WafSurface.PrivateMessage,
            SenderNick = "Spammer",
            SenderIdent = "mo.abc",
            SenderHost = "user.gamesurge.net",
            DisplayText = "加群领免费代练 http://spam.vip",
            RawBody = "加群领免费代练 http://spam.vip",
        };

        WafDecision d = waf.Evaluate(evt);
        waf.BlockFromAlert(evt, d, "note");

        waf.IsBlocked("nick=Spammer").Should().BeTrue();
        waf.IsBlocked(WafBodyFingerprint.KeyFromEvent(evt)).Should().BeTrue();
        waf.ListBlockedEntries().Should().Contain(e =>
            e.Key.StartsWith("nick=", StringComparison.OrdinalIgnoreCase)
            && e.Ident == "mo.abc"
            && e.Host == "user.gamesurge.net");
    }
}
