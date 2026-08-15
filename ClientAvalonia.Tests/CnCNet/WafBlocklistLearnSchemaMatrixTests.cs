using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Learn / schema matrix (~100): Warn → BlockFromAlert → Drop; body fingerprint;
/// compact variants; host=/ident=; unblock independence; persist body=; listing body keys.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class WafBlocklistLearnSchemaMatrixTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public WafBlocklistLearnSchemaMatrixTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    public static IEnumerable<object[]> Cases()
    {
        foreach (string id in CaseIds())
            yield return new object[] { id };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Learn_Case(string id)
    {
        id.Should().NotBeNullOrWhiteSpace();
        RunCase(id);
    }

    [Fact]
    public void Learn_Matrix_Has_At_Least_100_Cases()
        => CaseIds().Count.Should().BeGreaterThanOrEqualTo(100);

    private static IReadOnlyList<string>? _ids;

    private static IReadOnlyList<string> CaseIds()
        => _ids ??= BuildIds();

    private static IReadOnlyList<string> BuildIds()
    {
        var ids = new List<string>(120);

        for (int i = 0; i < 20; i++)
            ids.Add($"learn.nick_silent_drop_{i}");

        for (int i = 0; i < 15; i++)
            ids.Add($"learn.body_cross_nick_{i}");

        for (int i = 0; i < 12; i++)
            ids.Add($"learn.compact_variant_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"learn.host_block_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"learn.ident_block_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"learn.unblock_independence_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"learn.persist_body_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"learn.listing_body_{i}");

        int n = 0;
        while (ids.Count < 100)
            ids.Add($"learn.schema_key_{n++}");

        return ids;
    }

    private void RunCase(string id)
    {
        if (id.StartsWith("learn.nick_silent_drop_", StringComparison.Ordinal))
        {
            NickSilentDrop(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.body_cross_nick_", StringComparison.Ordinal))
        {
            BodyCrossNick(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.compact_variant_", StringComparison.Ordinal))
        {
            CompactVariant(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.host_block_", StringComparison.Ordinal))
        {
            HostBlock(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.ident_block_", StringComparison.Ordinal))
        {
            IdentBlock(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.unblock_independence_", StringComparison.Ordinal))
        {
            UnblockIndependence(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.persist_body_", StringComparison.Ordinal))
        {
            PersistBody(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.listing_body_", StringComparison.Ordinal))
        {
            ListingBody(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        if (id.StartsWith("learn.schema_key_", StringComparison.Ordinal))
        {
            SchemaKey(int.Parse(id.AsSpan(id.LastIndexOf('_') + 1)));
            return;
        }

        throw new InvalidOperationException("Unknown case " + id);
    }

    private static CnCNetIngressWaf CreateWaf(bool persist = false)
        => new(() => new WafSettings(), persistUserList: persist);

    private static void NickSilentDrop(int i)
    {
        var waf = CreateWaf();
        string nick = "Toxic" + i;
        string body = i % 2 == 0 ? "你妈死了" : "傻逼弱智";
        var evt = WafAttackFixtures.LobbyPromoChat(nick, body);

        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        waf.BlockFromAlert(evt, warn, "learn-nick");
        alerts = 0;

        WafDecision again = waf.Evaluate(WafAttackFixtures.LobbyPromoChat(nick, "hello gg wp"));
        again.Severity.Should().Be(WafSeverity.Drop);
        again.MatchedRuleIds.Should().Contain("user.blocklist");
        alerts.Should().Be(0);
    }

    private static void BodyCrossNick(int i)
    {
        var waf = CreateWaf();
        string body = Bodies[i % Bodies.Length];
        var evt = WafAttackFixtures.PromoPrivateMessage("Ad" + i, body);
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "learn-body");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        WafDecision other = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Other" + i, body));
        other.Severity.Should().Be(WafSeverity.Drop);
        other.MatchedRuleIds.Should().Contain("user.blocklist.body");
        alerts.Should().Be(0);
    }

    private static readonly string[] Bodies =
    [
        "q群 76543210 工作室招人",
        "加群领免费代练 http://spam.vip/x",
        "QQ群：987654321 内部优惠",
        "discord.gg/abcd free boosting",
        "把密码发给我验证码",
        "约炮裸聊色情",
        "弄死你上门砍",
        "自杀教程分享",
        "未成年约炮",
        "elo boost cheap hire",
        "t.me/boostshop",
        "代练低价加群",
        "fuck you asshole",
        "heil hitler",
        "转账汇款中奖",
    ];

    private static void CompactVariant(int i)
    {
        string canonical = "加群领免费代练 http://spam.vip/x";
        string[] variants =
        [
            canonical,
            "加\u200b群领\u200b免\u200b费\u200b代\u200b练 http://spam.vip/x",
            "加 群 领 免 费 代 练 http://spam.vip/x",
            "加\u200b群\u200b领\u200b免\u200b费\u200b代\u200b练\u200b http://spam.vip/x",
            WafSemanticCorpus.InsertZwsp(canonical),
            WafSemanticCorpus.InsertSpaces("加群领免费代练") + " http://spam.vip/x",
            canonical,
            "加群领免费代练  http://spam.vip/x",
            "加群领免费代练 http://spam.vip/x ",
            WafSemanticCorpus.InsertZwsp("加群领免费代练") + " http://spam.vip/x",
            "加群领免费代练http://spam.vip/x",
            "加群领免费代练 http://spam.vip/x",
        ];

        string variant = variants[i % variants.Length];
        var waf = CreateWaf();
        var evt = WafAttackFixtures.LobbyPromoChat("Seed" + i, canonical);
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "compact");

        string keyCanon = WafBodyFingerprint.KeyFromText(canonical);
        string keyVariant = WafBodyFingerprint.KeyFromText(variant);
        keyVariant.Should().Be(keyCanon, because: "normalize+compact should collapse high-similarity variants");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Other" + i, variant))
            .Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);
    }

    private static void HostBlock(int i)
    {
        var waf = CreateWaf();
        string host = "evil" + i + ".gamesurge.net";
        waf.Block("host=" + host);

        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = "AnyNick" + i,
            SenderHost = host,
            DisplayText = "hello",
            RawBody = "hello",
        });
        d.Severity.Should().Be(WafSeverity.Drop);
        d.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    private static void IdentBlock(int i)
    {
        var waf = CreateWaf();
        string ident = "mo.ident" + i;
        waf.Block("ident=" + ident);

        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.PrivateChat,
            Surface = WafSurface.PrivateMessage,
            SenderNick = "Any" + i,
            SenderIdent = ident,
            DisplayText = "hello",
            RawBody = "hello",
        });
        d.Severity.Should().Be(WafSeverity.Drop);
        d.MatchedRuleIds.Should().Contain("user.blocklist");
    }

    private static void UnblockIndependence(int i)
    {
        var waf = CreateWaf();
        string nick = "Indep" + i;
        string body = Bodies[i % Bodies.Length];
        var evt = new WafIngressEvent
        {
            Kind = WafIngressKind.ChannelChat,
            Surface = WafSurface.LobbyChat,
            SenderNick = nick,
            SenderIdent = "id" + i,
            SenderHost = "h" + i + ".net",
            DisplayText = body,
            RawBody = body,
        };

        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "indep");

        string bodyKey = WafBodyFingerprint.KeyFromEvent(evt);
        bodyKey.Should().NotBeNullOrEmpty();
        waf.IsBlocked("nick=" + nick).Should().BeTrue();
        waf.IsBlocked(bodyKey).Should().BeTrue();

        // Unblock nick only — body fingerprint should still Drop other nicks
        waf.Unblock("nick=" + nick);
        waf.IsBlocked("nick=" + nick).Should().BeFalse();
        waf.Evaluate(WafAttackFixtures.LobbyPromoChat("SomeoneElse" + i, body))
            .Severity.Should().Be(WafSeverity.Drop);

        // Unblock body — clean nick traffic allows (unless content still warns)
        waf.Unblock(bodyKey);
        waf.IsBlocked(bodyKey).Should().BeFalse();
    }

    private void PersistBody(int i)
    {
        string body = Bodies[i % Bodies.Length] + " #" + i;
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        waf.ClearBlocklist();
        Thread.Sleep(100);

        var evt = WafAttackFixtures.PromoPrivateMessage("SeedBody" + i, body);
        WafDecision warn = waf.Evaluate(evt);
        // May Warn on content; if Allow (edge), force BlockFromAlert still needs a decision with body
        if (warn.Severity < WafSeverity.Warn)
        {
            // Ensure we still learn body via manual path when content misses
            waf.BlockFromAlert(evt, new WafDecision
            {
                Severity = WafSeverity.Warn,
                Score = 50,
                MatchedRuleIds = ["content.promo"],
                Reasons = ["forced"],
                SuggestedBlockKeys = ["nick=SeedBody" + i],
            }, "persist-body");
        }
        else
        {
            waf.BlockFromAlert(evt, warn, "persist-body");
        }

        Thread.Sleep(150);
        string bodyKey = WafBodyFingerprint.KeyFromEvent(evt);
        bodyKey.Should().NotBeNullOrEmpty();

        var reloaded = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        reloaded.LoadUserList();
        reloaded.IsBlocked(bodyKey).Should().BeTrue(because: "body= keys must survive new instance LoadUserList");

        reloaded.Evaluate(WafAttackFixtures.LobbyPromoChat("OtherBody" + i, body))
            .Severity.Should().Be(WafSeverity.Drop);

        File.Exists(Path.Combine(_root.GameRoot, "Client", "WafBlockList.json")).Should().BeTrue();
    }

    private static void ListingBody(int i)
    {
        var waf = CreateWaf();
        string room = "加群代练房" + i;
        var evt = new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "ListHost" + i,
            DisplayText = room,
            RawBody = room,
            Game = new WafGameBroadcastFields
            {
                Revision = "R13",
                FieldCount = 13,
                ChannelName = "#list" + i,
                RoomName = room,
                MapName = "Map",
                GameMode = "Mode",
                TunnelHost = "tn.example.org",
                TunnelPort = 50000,
                Players = ["ListHost" + i],
            },
        };

        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "listing");

        string bodyKey = WafBodyFingerprint.KeyFromEvent(evt);
        bodyKey.Should().StartWith("body=");
        waf.IsBlocked(bodyKey).Should().BeTrue();

        // Same listing text from another host → body Drop
        var clone = new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "OtherHost" + i,
            DisplayText = room,
            RawBody = room,
            Game = new WafGameBroadcastFields
            {
                Revision = "R13",
                FieldCount = 13,
                ChannelName = "#other" + i,
                RoomName = room,
                MapName = "Map",
                GameMode = "Mode",
                TunnelHost = "tn.example.org",
                TunnelPort = 50000,
                Players = ["OtherHost" + i],
            },
        };
        waf.Evaluate(clone).Severity.Should().Be(WafSeverity.Drop);
    }

    private static void SchemaKey(int i)
    {
        string[] raws =
        [
            "Alice", "nick=Bob", "#room", "1.2.3.4:50000", "host=x.net",
            "ident=mo.a", "tunnel=9.9.9.9:1", "room=#z", "fingerprint=ab", "body=cd",
            "Carol", "nick=Dave", "#farm", "8.8.8.8:50000",
        ];
        string raw = raws[i % raws.Length];
        string key = WafBlockEntry.NormalizeManualKey(raw);
        key.Should().NotBeNullOrWhiteSpace();
        WafBlockEntry.InferKind(key).Should().NotBeNullOrWhiteSpace();

        var entry = WafBlockEntry.FromKey(key, note: "schema");
        entry.DisplayLine.Should().Contain("[");
        entry.Key.Should().Be(key);
    }
}
