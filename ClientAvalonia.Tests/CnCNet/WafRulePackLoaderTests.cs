using System;
using System.IO;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

[Collection("ProgramConstantsSerial")]
public sealed class WafRulePackLoaderTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public WafRulePackLoaderTests()
    {
        _root.BindToProgramConstants();
        WafRulePackLoader.InvalidateCache();
    }

    public void Dispose()
    {
        WafRulePackLoader.InvalidateCache();
        _root.Dispose();
    }

    [Fact]
    public void Embedded_Default_Contains_Social_Content_Classes()
    {
        WafCompiledRulePack pack = WafRulePackLoader.Default;
        pack.Version.Should().BeGreaterThanOrEqualTo(2);
        pack.ContentClasses.Select(c => c.Id).Should().Contain(new[]
        {
            "content.url",
            "content.contact",
            "content.promo",
            "content.fraud",
            "content.sexual",
            "content.abuse",
            "content.hate",
            "content.threat",
            "content.self_harm",
            "content.child_safety",
        });

        // Issue #37: protocol fingerprints and the host-bot tunnel list are
        // intentionally absent from the shipped default (stock rooms
        // false-positived; operators re-enable via Client/WafRules.json).
        // Assert that intent instead of the old tunnel entry.
        pack.HostBotTunnels.Should().BeEmpty();
        pack.Protocol.Should().BeEmpty();
    }

    [Fact]
    public void HangFarm_Protocol_Rules_Work_Via_Override_Pack()
    {
        // The documented re-enable path: an override pack restores protocol
        // fingerprints and tunnels on top of the same engine.
        WafCompiledRulePack pack = WafTestPacks.HangFarm();
        pack.HostBotTunnels.Should().Contain("175.178.174.40:50000");
        pack.Protocol.Should().ContainKey("proto.game.r8");
    }

    [Fact]
    public void GamePath_User_Override_Takes_Precedence()
    {
        string clientDir = Path.Combine(_root.GameRoot, "Client");
        Directory.CreateDirectory(clientDir);
        string overridePath = Path.Combine(clientDir, WafRulePackLoader.UserFileName);
        File.WriteAllText(overridePath,
            """
            {
              "version": 2,
              "description": "test override",
              "hostBotTunnels": [ "9.9.9.9:12345" ],
              "sensitivity": { "1": { "warn": 10, "hide": 50, "drop": 9999 } },
              "protocol": [],
              "contentClasses": [
                {
                  "id": "content.abuse",
                  "score": 99,
                  "reason": "override abuse",
                  "enabled": true,
                  "keywords": [ "自定义脏话词" ]
                }
              ],
              "pm": { "burst": { "minCount": 99 }, "firstContactPromo": { "score": 0, "minScore": 999, "triggerClasses": [] } }
            }
            """);

        WafRulePackLoader.InvalidateCache();
        WafCompiledRulePack pack = WafRulePackLoader.LoadFromGamePath();
        pack.Source.Should().Contain("user");
        pack.IsKnownHostBotTunnel("9.9.9.9:12345").Should().BeTrue();
        pack.GetKeywords("content.abuse").Should().Contain("自定义脏话词");

        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, rules: pack);
        WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("x", "你是自定义脏话词吗"));
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.abuse");
        d.Score.Should().Be(99);
    }

    [Fact]
    public void Invalid_Regex_In_Class_Is_Skipped_Not_Crash()
    {
        WafCompiledRulePack pack = WafRulePackLoader.CompileFromJson(
            """
            {
              "version": 2,
              "contentClasses": [
                { "id": "content.url", "score": 25, "reason": "url", "regexes": [ "(unclosed" ], "keywords": ["http"] }
              ]
            }
            """,
            "bad-regex");

        pack.ContentClasses.Should().ContainSingle();
        pack.ContentClasses[0].Regexes.Should().BeEmpty();
        Action act = () => pack.ContentClasses[0].Matches("http://x", "http://x");
        act.Should().NotThrow();
    }
}

public sealed class WafSocialContentPolicyTests
{
    private static CnCNetIngressWaf CreateWaf()
        => new(() => new WafSettings(), persistUserList: false, rules: WafRulePackLoader.Default);

    [Theory]
    [InlineData("你这个傻逼滚开", "content.abuse")]
    [InlineData("nmsl 去死吧", "content.abuse")]
    [InlineData("fuck you asshole", "content.abuse")]
    [InlineData("我要弄死你 上门砍", "content.threat")]
    [InlineData("i will kill you", "content.threat")]
    [InlineData("heil hitler white power", "content.hate")]
    [InlineData("自杀教程分享", "content.self_harm")]
    [InlineData("未成年约炮联系", "content.child_safety")]
    public void Social_Policy_Classes_Warn(string text, string expectedRule)
    {
        WafDecision d = CreateWaf().Evaluate(WafAttackFixtures.PromoPrivateMessage("Troll", text));
        d.Severity.Should().Be(WafSeverity.Warn, because: text);
        d.MatchedRuleIds.Should().Contain(expectedRule, because: text);
    }

    [Fact]
    public void Plain_Chat_Without_Policy_Hits_Allows()
    {
        WafDecision d = CreateWaf().Evaluate(
            WafAttackFixtures.LobbyPromoChat("NicePlayer", "gg wp, see you next game"));
        d.Severity.Should().Be(WafSeverity.Allow);
    }
}
