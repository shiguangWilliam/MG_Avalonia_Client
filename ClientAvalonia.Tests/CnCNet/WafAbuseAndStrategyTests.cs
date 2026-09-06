using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class WafAbuseAndStrategyTests
{
    private static CnCNetIngressWaf CreateWaf(WafStrategyPrefs? prefs = null, WafCompiledRulePack? rules = null)
        => new(
            () => new WafSettings(),
            persistUserList: false,
            rules: rules ?? WafTestPacks.HangFarmWithDefaultContent(),
            strategyPrefs: prefs ?? new WafStrategyPrefs());

    [Theory]
    [InlineData("你妈死了")]
    [InlineData("nmsl")]
    [InlineData("操你妈")]
    public void Chinese_Abuse_Phrases_Warn(string text)
    {
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "Toxic",
            DisplayText = text,
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain(id => id == "content.abuse" || id == "content.hate");
    }

    [Fact]
    public void Blocklist_Drop_Does_Not_Raise_Alert()
    {
        var waf = CreateWaf();
        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        waf.Block("nick=Banned");

        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "Banned",
            DisplayText = "QQ:1234567890 工作室招人",
        });

        d.Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);
    }

    [Fact]
    public void Ban_From_Alert_Also_Drops_Same_Body_From_Other_Nicks()
    {
        var waf = CreateWaf();
        var evt = new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "Spammer",
            DisplayText = "q群 76543210 工作室招人",
        };
        WafDecision first = waf.Evaluate(evt);
        first.Severity.Should().Be(WafSeverity.Warn);

        waf.BlockFromAlert(evt, first, "测试");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;

        WafDecision sameBody = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "OtherNick",
            DisplayText = "q群 76543210 工作室招人",
        });

        sameBody.Severity.Should().Be(WafSeverity.Drop);
        sameBody.MatchedRuleIds.Should().Contain("user.blocklist.body");
        alerts.Should().Be(0);
    }

    [Fact]
    public void Strategy_Off_Skips_Class_And_Drop_Forces_Silent_Drop()
    {
        var prefs = new WafStrategyPrefs();
        prefs.SetMode("content.abuse", WafStrategyMode.Off);
        prefs.SetMode("content.hate", WafStrategyMode.Off);
        var wafOff = CreateWaf(prefs);
        wafOff.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "A",
            DisplayText = "你妈死了",
        }).Severity.Should().Be(WafSeverity.Allow);

        prefs.SetMode("content.abuse", WafStrategyMode.Drop);
        prefs.SetMode("content.hate", WafStrategyMode.Off);
        var wafDrop = CreateWaf(prefs);
        int alerts = 0;
        wafDrop.AlertRaised += _ => alerts++;
        WafDecision d = wafDrop.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "B",
            DisplayText = "你妈死了",
        });

        d.Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);
    }

    [Fact]
    public void PruneEphemeralState_Removes_Stale_Tunnel_And_Template_Keys()
    {
        var waf = CreateWaf();
        for (int i = 0; i < 3; i++)
        {
            waf.Evaluate(new WafIngressEvent
            {
                Kind = WafIngressKind.GameBroadcast,
                Surface = WafSurface.Protocol,
                SenderNick = "Host" + i,
                Game = new WafGameBroadcastFields
                {
                    Revision = "R10",
                    FieldCount = 11,
                    RoomName = "SameRoom",
                    MapName = "SameMap",
                    GameMode = "SameMode",
                    TunnelHost = "1.2.3.4",
                    TunnelPort = 50000,
                    ChannelName = "#room" + i,
                    Players = ["A", "B"],
                },
            });
        }

        waf.TunnelHostCountForTests.Should().BeGreaterThan(0);
        waf.TemplateFingerprintCountForTests.Should().BeGreaterThan(0);

        System.Threading.Thread.Sleep(15);
        waf.PruneEphemeralState(TimeSpan.FromMilliseconds(5));

        waf.TunnelHostCountForTests.Should().Be(0);
        waf.TemplateFingerprintCountForTests.Should().Be(0);
    }

    [Fact]
    public void ListStrategies_Exposes_Id_Content_And_Mode()
    {
        var waf = CreateWaf();
        IReadOnlyList<WafStrategyRow> rows = waf.ListStrategies();
        rows.Should().Contain(r => r.Id == "content.abuse");
        rows.First(r => r.Id == "content.abuse").Content.Should().Contain("辱骂");
        rows.First(r => r.Id == "content.abuse").Mode.Should().Be(WafStrategyMode.Warn);
    }

    [Theory]
    [InlineData("dailian 加我")]
    [InlineData("代 练 低价")]
    [InlineData("工-作-室招人")]
    public void Promo_Obfuscation_And_Pinyin_Warn(string text)
    {
        WafRulePackLoader.InvalidateCache();
        var waf = CreateWaf();
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "Ad",
            DisplayText = text,
        });

        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain("content.promo");
    }

    [Fact]
    public void Confusable_Fold_Maps_Nmsl_And_Dailian()
    {
        WafTextNormalizer.Normalize("nmsl").Should().Contain("你妈死了");
        WafTextNormalizer.CompactForMatch("dai***lian").Should().Contain("代练");
    }
}
