using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

[Collection("ProgramConstantsSerial")]
public sealed class WafUnitCoverageTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public WafUnitCoverageTests()
    {
        _root.BindToProgramConstants();
        WafRulePackLoader.InvalidateCache();
    }

    public void Dispose() => _root.Dispose();

    [Theory]
    [InlineData("\u000304广告\u200b推广", "广告推广")]
    [InlineData("Ｄａｉｌｉａｎ", "代练")] // fullwidth latin → NFKC → fold
    [InlineData("n.m.s.l", "你妈死了")] // compact then fold
    public void Normalizer_Strips_And_Folds(string raw, string expectedSubstring)
    {
        string n = WafTextNormalizer.Normalize(raw);
        string c = WafTextNormalizer.CompactForMatch(n);
        (n + c).Should().Contain(expectedSubstring);
    }

    [Fact]
    public void BodyFingerprint_Ignores_Zwsp_And_Spaces()
    {
        string a = WafBodyFingerprint.KeyFromText("加群领代练");
        string b = WafBodyFingerprint.KeyFromText("加\u200b群\u200b领\u200b代\u200b练");
        string c = WafBodyFingerprint.KeyFromText("加 群 领 代 练");
        a.Should().StartWith("body=");
        b.Should().Be(a);
        c.Should().Be(a);
    }

    [Fact]
    public void BodyFingerprint_Empty_For_Too_Short()
    {
        WafBodyFingerprint.KeyFromText("a").Should().BeEmpty();
        WafBodyFingerprint.KeyFromText("").Should().BeEmpty();
    }

    [Fact]
    public void StrategyPrefs_RoundTrip_File()
    {
        var prefs = new WafStrategyPrefs();
        prefs.SetMode("content.abuse", WafStrategyMode.Drop);
        prefs.SetMode("content.promo", WafStrategyMode.Off);
        prefs.Save();

        var loaded = new WafStrategyPrefs();
        loaded.Load();
        loaded.GetMode("content.abuse").Should().Be(WafStrategyMode.Drop);
        loaded.GetMode("content.promo").Should().Be(WafStrategyMode.Off);
        loaded.GetMode("content.url").Should().Be(WafStrategyMode.Warn);
    }

    [Fact]
    public void Strategy_Off_Allows_Otherwise_Risky_Text()
    {
        var prefs = new WafStrategyPrefs();
        prefs.SetMode("content.contact", WafStrategyMode.Off);
        prefs.SetMode("content.promo", WafStrategyMode.Off);
        prefs.SetMode("content.url", WafStrategyMode.Off);
        prefs.SetMode("content.pm.first_contact_promo", WafStrategyMode.Off);
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, strategyPrefs: prefs);

        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("X", "加群 QQ:123456789 http://a.com 代练"))
            .Severity.Should().Be(WafSeverity.Allow);
    }

    [Fact]
    public void ListStrategies_Includes_Content_And_Protocol()
    {
        // Issue #37: protocol rules are absent from the shipped default pack —
        // the strategy list needs an explicit pack to include proto.* rows.
        var waf = new CnCNetIngressWaf(
            () => new WafSettings(),
            persistUserList: false,
            rules: WafTestPacks.HangFarmWithDefaultContent());
        IReadOnlyList<WafStrategyRow> rows = waf.ListStrategies();
        rows.Should().Contain(r => r.Id == "content.abuse" && r.Kind == "content");
        rows.Should().Contain(r => r.Id.StartsWith("proto.", StringComparison.OrdinalIgnoreCase));
        rows.Should().Contain(r => r.Id.Contains("pm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Block_Unblock_Clear_Persist_Async_Does_Not_Throw()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        waf.Block(WafBlockEntry.FromKey("nick=AsyncPersist", note: "t"));
        waf.IsBlocked("nick=AsyncPersist").Should().BeTrue();
        waf.Unblock("nick=AsyncPersist");
        waf.IsBlocked("nick=AsyncPersist").Should().BeFalse();
        waf.Block("tunnel=1.1.1.1:50000");
        waf.ClearBlocklist();
        waf.ListBlockedEntries().Should().BeEmpty();

        System.Threading.Thread.Sleep(80);
        string json = Path.Combine(_root.GameRoot, "Client", "WafBlockList.json");
        if (File.Exists(json))
            File.ReadAllText(json).Should().Match(s => s.Contains("Entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_Toggles_Disable_Surfaces()
    {
        var waf = new CnCNetIngressWaf(
            () => new WafSettings { CheckChannelChat = false, CheckPrivateChat = true },
            persistUserList: false);

        waf.Evaluate(WafAttackFixtures.LobbyPromoChat("A", "你妈死了"))
            .Severity.Should().Be(WafSeverity.Allow);
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("A", "你妈死了"))
            .Severity.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void TemplateFingerprint_Stable_For_Same_Fields()
    {
        var g = WafAttackFixtures.HostBotGame();
        string a = WafTemplateFingerprint.Compute(g);
        string b = WafTemplateFingerprint.Compute(g);
        a.Should().NotBeNullOrEmpty();
        a.Should().Be(b);
    }

    [Fact]
    public void RulePack_Default_Has_Abuse_Regexes()
    {
        WafCompiledRulePack pack = WafRulePackLoader.Default;
        WafCompiledContentClass abuse = pack.ContentClasses.First(c => c.Id == "content.abuse");
        abuse.Regexes.Should().NotBeEmpty();
        abuse.Keywords.Should().Contain("你妈死了");
    }
}
