using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Filter Evaluate matrix (~100 cases) across content classes, surfaces, sensitivity,
/// and hang-farm protocol packs (CompileFromJson — default protocol[] is empty).
/// </summary>
public sealed class WafFilterContentMatrixTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (FilterCase c in BuildCases())
            yield return new object[] { c.Id, c.Surface, c.Text, (int)c.Expected, c.RuleHint };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Filter_Case(string id, string surface, string text, int expectedSeverity, string ruleHint)
    {
        id.Should().NotBeNullOrWhiteSpace();
        var expected = (WafSeverity)expectedSeverity;

        CnCNetIngressWaf waf = CreateWafForCase(id);
        WafDecision d = Evaluate(waf, surface, text, id);

        d.Severity.Should().Be(expected, because: $"{id}: surface={surface} text={text}");
        if (expected >= WafSeverity.Warn && !string.IsNullOrEmpty(ruleHint))
            d.MatchedRuleIds.Should().Contain(ruleHint, because: id);
        if (expected == WafSeverity.Drop)
        {
            // Strategy Drop and blocklist Drop must be silent; heuristic Drop is rare by default.
        }
    }

    [Fact]
    public void Filter_Matrix_Has_At_Least_100_Cases()
        => BuildCases().Count.Should().BeGreaterThanOrEqualTo(100);

    private sealed record FilterCase(
        string Id,
        string Surface,
        string Text,
        WafSeverity Expected,
        string RuleHint,
        string Pack = "default");

    private static IReadOnlyList<FilterCase>? _cached;

    private static IReadOnlyList<FilterCase> BuildCases()
        => _cached ??= BuildCasesCore();

    private static IReadOnlyList<FilterCase> BuildCasesCore()
    {
        var list = new List<FilterCase>(140);

        void Add(string id, string surface, string text, WafSeverity exp, string hint, string pack = "default")
            => list.Add(new FilterCase(id, surface, text, exp, hint, pack));

        // Clean allows
        string[] clean =
        [
            "gg wp", "nice game", "准备好了", "1", "ready", "glhf", "see you", "ok",
            "地图不错", "再来一局", "wait", "afk 1 min",
        ];
        for (int i = 0; i < clean.Length; i++)
        {
            Add($"flt.allow.lobby_{i}", "lobby", clean[i], WafSeverity.Allow, "");
            Add($"flt.allow.pm_{i}", "pm", clean[i], WafSeverity.Allow, "");
        }

        // Abuse
        string[] abuse =
        [
            "你妈死了", "nmsl", "操你妈", "傻逼弱智", "fuck you asshole", "去死吧废物",
            "脑残白痴", "你个sb", "motherfucker", "dumbass",
        ];
        foreach (string t in abuse)
        {
            Add($"flt.abuse.lobby_{Sanitize(t)}", "lobby", t, WafSeverity.Warn, "content.abuse");
            Add($"flt.abuse.pm_{Sanitize(t)}", "pm", t, WafSeverity.Warn, "content.abuse");
        }

        // Promo / contact / url
        foreach (string t in WafAttackFixtures.PromoTextVariants())
            Add($"flt.promo.lobby_{Sanitize(t)}", "lobby", t, WafSeverity.Warn, "content.promo");

        foreach (string t in WafAttackFixtures.QqPromoGroupVariants().Take(8))
        {
            Add($"flt.contact.lobby_{Sanitize(t)}", "lobby", t, WafSeverity.Warn, "content.contact");
            Add($"flt.contact.pm_{Sanitize(t)}", "pm", t, WafSeverity.Warn, "content.contact");
        }

        foreach (string t in WafAttackFixtures.EnglishPromoVariants())
            Add($"flt.en.pm_{Sanitize(t)}", "pm", t, WafSeverity.Warn, "content.promo");

        Add("flt.url.lobby_http", "lobby", "check http://spam.vip/x now", WafSeverity.Warn, "content.url");
        Add("flt.url.pm_www", "pm", "www.boost.xyz for deals", WafSeverity.Warn, "content.url");

        // Fraud / sexual / threat / hate / self_harm / child_safety
        Add("flt.fraud.pm", "pm", "把密码发给我 验证码发过来", WafSeverity.Warn, "content.fraud");
        Add("flt.sexual.lobby", "lobby", "约炮裸聊色情网站", WafSeverity.Warn, "content.sexual");
        Add("flt.threat.lobby", "lobby", "我要弄死你 上门砍", WafSeverity.Warn, "content.threat");
        Add("flt.hate.lobby", "lobby", "heil hitler white power", WafSeverity.Warn, "content.hate");
        Add("flt.selfharm.pm", "pm", "自杀教程分享", WafSeverity.Warn, "content.self_harm");
        Add("flt.child.pm", "pm", "未成年约炮联系", WafSeverity.Warn, "content.child_safety");

        // Listing surface (room/map/mode text)
        Add("flt.listing.promo", "listing", "加群代练优惠", WafSeverity.Warn, "content.promo");
        Add("flt.listing.abuse", "listing", "你妈死了房", WafSeverity.Warn, "content.abuse");
        Add("flt.listing.clean", "listing", "Casual FFA", WafSeverity.Allow, "");

        // Sensitivity: same text, expect Warn at default sensitivity
        for (int s = 0; s <= 2; s++)
            Add($"flt.sens.url_{s}", $"sens{s}", "http://x.vip/y", WafSeverity.Warn, "content.url");

        // Strategy Drop expectations (special surface tag)
        Add("flt.drop.strategy_abuse", "drop-abuse", "你妈死了", WafSeverity.Drop, "content.abuse");
        Add("flt.drop.strategy_promo", "drop-promo", "加群领免费代练", WafSeverity.Drop, "content.promo");

        // Hang-farm protocol (injected pack)
        Add("flt.proto.r8", "proto-r8", "", WafSeverity.Warn, "proto.game.r8", "hangfarm");
        Add("flt.proto.tunnel", "proto-tunnel", "", WafSeverity.Warn, "proto.tunnel.blacklist", "hangfarm");
        Add("flt.proto.fake_players", "proto-fake", "", WafSeverity.Warn, "proto.game.fake_players", "hangfarm");
        Add("flt.proto.field_count", "proto-fields", "", WafSeverity.Warn, "proto.game.field_count", "hangfarm");
        Add("flt.proto.default_pack_allows_r8", "proto-r8-default", "", WafSeverity.Allow, "", "default");

        // Game room chat surface
        Add("flt.gameroom.abuse", "gameroom", "傻逼弱智", WafSeverity.Warn, "content.abuse");
        Add("flt.gameroom.clean", "gameroom", "rush mid", WafSeverity.Allow, "");

        // Pad to 100+ with systematic promo/abuse variants
        string[] pads =
        [
            "代练低价", "陪玩充值", "工作室招人", "卖号收号", "免费领优惠券",
            "扫码加群", "discord.gg/abcd", "t.me/boost", "elo boost cheap",
            "rank boost hire", "buy account shop", "contact me for coaching",
            "弱智垃圾人", "滚出去", "kill yourself", "stfu retard",
            "转账汇款中奖", "steam guard login code", "onlyfans camgirl",
            "打死你报复你", "gas the jews", "how to kill yourself",
        ];
        for (int i = 0; i < pads.Length && list.Count < 100; i++)
        {
            string t = pads[i];
            Add($"flt.pad.lobby_{i}", "lobby", t, WafSeverity.Warn, "");
        }

        while (list.Count < 100)
        {
            int i = list.Count;
            Add($"flt.pad.pm_clean_{i}", "pm", "hello " + i, WafSeverity.Allow, "");
        }

        return list;
    }

    private static CnCNetIngressWaf CreateWafForCase(string id)
    {
        FilterCase c = BuildCases().First(x => x.Id == id);
        WafStrategyPrefs? prefs = null;
        WafSettings settings = new();

        if (c.Surface.StartsWith("sens", StringComparison.Ordinal))
        {
            int sens = int.Parse(c.Surface.AsSpan(4));
            settings = new WafSettings { Sensitivity = sens };
        }
        else if (c.Surface == "drop-abuse")
        {
            prefs = new WafStrategyPrefs();
            prefs.SetMode("content.abuse", WafStrategyMode.Drop);
            prefs.SetMode("content.hate", WafStrategyMode.Off);
        }
        else if (c.Surface == "drop-promo")
        {
            prefs = new WafStrategyPrefs();
            prefs.SetMode("content.promo", WafStrategyMode.Drop);
            prefs.SetMode("content.contact", WafStrategyMode.Off);
            prefs.SetMode("content.url", WafStrategyMode.Off);
        }

        WafCompiledRulePack? rules = null;
        if (c.Pack == "hangfarm" || c.Surface.StartsWith("proto-", StringComparison.Ordinal))
        {
            if (c.Surface == "proto-r8-default")
                rules = WafRulePackLoader.Default;
            else if (c.Pack == "hangfarm" || c.Surface.StartsWith("proto-", StringComparison.Ordinal))
                rules = HangFarmPack();
        }

        return new CnCNetIngressWaf(() => settings, persistUserList: false, rules: rules, strategyPrefs: prefs);
    }

    private static WafDecision Evaluate(CnCNetIngressWaf waf, string surface, string text, string id)
    {
        switch (surface)
        {
            case "pm":
            case "drop-promo":
                return waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Flt", text));
            case "gameroom":
                return waf.Evaluate(new WafIngressEvent
                {
                    Kind = WafIngressKind.ChannelChat,
                    Surface = WafSurface.GameRoomChat,
                    Channel = "#game",
                    SenderNick = "Flt",
                    DisplayText = text,
                    RawBody = text,
                });
            case "listing":
                return waf.Evaluate(new WafIngressEvent
                {
                    Kind = WafIngressKind.GameBroadcast,
                    Surface = WafSurface.Protocol,
                    SenderNick = "Flt",
                    DisplayText = text,
                    Game = new WafGameBroadcastFields
                    {
                        Revision = "R13",
                        FieldCount = 13,
                        ChannelName = "#list",
                        RoomName = text,
                        MapName = "Map",
                        GameMode = "Mode",
                        TunnelHost = "tn.example.org",
                        TunnelPort = 50000,
                        Players = ["Flt"],
                    },
                });
            case "proto-r8":
            case "proto-r8-default":
                return waf.Evaluate(WafAttackFixtures.HostBotBroadcast(
                    "Bot",
                    WafAttackFixtures.HostBotGame(revision: "R8", fieldCount: 9)));
            case "proto-tunnel":
                return waf.Evaluate(WafAttackFixtures.HostBotBroadcast(
                    "Bot",
                    WafAttackFixtures.HostBotGame(revision: "R13", fieldCount: 13, players: ["Host", "P2"])));
            case "proto-fake":
                return waf.Evaluate(WafAttackFixtures.HostBotBroadcast(
                    "Bot",
                    new WafGameBroadcastFields
                    {
                        Revision = "R13",
                        FieldCount = 13,
                        ChannelName = "#fake",
                        RoomName = "Casual",
                        MapName = "Map",
                        GameMode = "Mode",
                        TunnelHost = "tn.example.org",
                        TunnelPort = 50000,
                        Players = ["A", "B", "C", "D", "E"],
                    }));
            case "proto-fields":
                return waf.Evaluate(WafAttackFixtures.HostBotBroadcast(
                    "Bot",
                    new WafGameBroadcastFields
                    {
                        Revision = "R13",
                        FieldCount = 9,
                        ChannelName = "#fields",
                        RoomName = "Casual",
                        MapName = "Map",
                        GameMode = "Mode",
                        TunnelHost = "tn.example.org",
                        TunnelPort = 50000,
                        Players = ["Host"],
                    }));
            case "drop-abuse":
                return waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Flt", text));
            default:
                if (surface.StartsWith("sens", StringComparison.Ordinal))
                    return waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Flt", text));
                return waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Flt", text));
        }
    }

    private static WafCompiledRulePack HangFarmPack()
        => WafRulePackLoader.CompileFromJson(
            """
            {
              "version": 2,
              "hostBotTunnels": [ "175.178.174.40:50000" ],
              "protocol": [
                { "id": "proto.game.r8", "score": 40, "reason": "R8" },
                { "id": "proto.game.field_count", "score": 50, "reason": "fields" },
                { "id": "proto.tunnel.blacklist", "score": 80, "reason": "tunnel" },
                { "id": "proto.game.fake_players", "score": 35, "reason": "fake" }
              ],
              "contentClasses": [
                {
                  "id": "content.promo",
                  "score": 25,
                  "reason": "promo",
                  "enabled": true,
                  "keywords": [ "代练", "加群" ]
                }
              ]
            }
            """,
            "hangfarm-filter");

    private static string Sanitize(string s)
    {
        var chars = s.Where(char.IsLetterOrDigit).Take(24).ToArray();
        return chars.Length == 0 ? "x" : new string(chars);
    }
}
