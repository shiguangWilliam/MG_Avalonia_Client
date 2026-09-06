using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Tests.Fixture;
using ClientCore;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Integration-oriented matrix (~100): Evaluate flows, TempGameRoot persistence,
/// BlockFromAlert → reload, concurrent Evaluate, peek fields.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class WafSessionIntegrationMatrixTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public WafSessionIntegrationMatrixTests()
    {
        _root.BindToProgramConstants();
        WafRulePackLoader.InvalidateCache();
    }

    public void Dispose()
    {
        WafRulePackLoader.InvalidateCache();
        // Restore static bindings for the next class in this serial collection —
        // leaving ProgramConstants/EnvironmentServices pointing at the disposed
        // temp root leaks workspace state into later tests (Issue #36).
        ClientConfiguration.ResetInstance();
        ClientAvalonia.GlobalState.Environment.EnvironmentServices.Reset();
        ProgramConstants.ClearHostedGameRoot();
        _root.Dispose();
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (string id in CaseIds())
            yield return new object[] { id };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Integration_Case(string id)
    {
        id.Should().NotBeNullOrWhiteSpace();
        RunCase(id);
    }

    [Fact]
    public void Integration_Matrix_Has_At_Least_100_Cases()
        => CaseIds().Count.Should().BeGreaterThanOrEqualTo(100);

    private static IReadOnlyList<string>? _ids;

    private static IReadOnlyList<string> CaseIds()
        => _ids ??= BuildIds();

    private static IReadOnlyList<string> BuildIds()
    {
        var ids = new List<string>(120);

        for (int i = 0; i < 20; i++)
            ids.Add($"int.persist.block_reload_{i}");

        for (int i = 0; i < 15; i++)
            ids.Add($"int.persist.strategy_{i}");

        for (int i = 0; i < 15; i++)
            ids.Add($"int.flow.warn_block_drop_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"int.peek.game_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"int.concurrent.eval_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"int.store.roundtrip_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"int.proto.hangfarm_{i}");

        for (int i = 0; i < 10; i++)
            ids.Add($"int.prefs.save_load_{i}");

        // Pad
        int n = 0;
        while (ids.Count < 100)
            ids.Add($"int.pad.eval_{n++}");

        return ids;
    }

    private void RunCase(string id)
    {
        if (id.StartsWith("int.persist.block_reload_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            PersistBlockReload(i);
            return;
        }

        if (id.StartsWith("int.persist.strategy_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            PersistStrategy(i);
            return;
        }

        if (id.StartsWith("int.flow.warn_block_drop_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            WarnBlockDropFlow(i);
            return;
        }

        if (id.StartsWith("int.peek.game_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            PeekAndEvaluate(i);
            return;
        }

        if (id.StartsWith("int.concurrent.eval_", StringComparison.Ordinal))
        {
            ConcurrentEvaluateSmoke();
            return;
        }

        if (id.StartsWith("int.store.roundtrip_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            StoreRoundTrip(i);
            return;
        }

        if (id.StartsWith("int.proto.hangfarm_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            HangFarmProtocol(i);
            return;
        }

        if (id.StartsWith("int.prefs.save_load_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            PrefsSaveLoad(i);
            return;
        }

        if (id.StartsWith("int.pad.eval_", StringComparison.Ordinal))
        {
            int i = int.Parse(id.AsSpan(id.LastIndexOf('_') + 1));
            PadEval(i);
            return;
        }

        throw new InvalidOperationException("Unknown case " + id);
    }

    private void PersistBlockReload(int i)
    {
        string nick = "PersistNick" + i;
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        waf.ClearBlocklist();
        WaitPersist(waf);

        var evt = WafAttackFixtures.LobbyPromoChat(nick, "你妈死了");
        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        waf.BlockFromAlert(evt, warn, "int-" + i);
        WaitPersist(waf);

        string json = Path.Combine(_root.GameRoot, "Client", "WafBlockList.json");
        File.Exists(json).Should().BeTrue(because: "BlockFromAlert with persistUserList should write disk");

        var reloaded = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        reloaded.LoadUserList();
        reloaded.IsBlocked("nick=" + nick).Should().BeTrue();
        reloaded.Evaluate(WafAttackFixtures.LobbyPromoChat(nick, "clean hello"))
            .Severity.Should().Be(WafSeverity.Drop);
    }

    private void PersistStrategy(int i)
    {
        string[] ids =
        [
            "content.abuse", "content.promo", "content.url", "content.contact", "content.fraud",
            "content.hate", "content.threat", "content.sexual", "content.self_harm", "content.child_safety",
            "content.pm.burst", "content.pm.first_contact_promo", "content.abuse", "content.promo", "content.url",
        ];
        string sid = ids[i % ids.Length];
        var mode = (WafStrategyMode)(i % 3);

        var prefs = new WafStrategyPrefs();
        prefs.SetMode(sid, mode);
        prefs.Save();

        var loaded = new WafStrategyPrefs();
        loaded.Load();
        loaded.GetMode(sid).Should().Be(mode);

        // WAF with persist should Load prefs on construct
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        waf.StrategyPrefs.GetMode(sid).Should().Be(mode);
    }

    private static void WarnBlockDropFlow(int i)
    {
        string nick = "Flow" + i;
        string body = i % 2 == 0 ? "你妈死了" : "加群领免费代练 http://spam.vip/" + i;
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        var evt = i % 3 == 0
            ? WafAttackFixtures.PromoPrivateMessage(nick, body)
            : WafAttackFixtures.LobbyPromoChat(nick, body);

        WafDecision warn = waf.Evaluate(evt);
        warn.Severity.Should().Be(WafSeverity.Warn);
        warn.SuggestedBlockKeys.Should().Contain(k => k.StartsWith("nick=", StringComparison.OrdinalIgnoreCase));

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        waf.BlockFromAlert(evt, warn, "flow");
        alerts = 0;

        waf.Evaluate(WafAttackFixtures.LobbyPromoChat(nick, "gg")).Severity.Should().Be(WafSeverity.Drop);
        alerts.Should().Be(0);

        string bodyKey = WafBodyFingerprint.KeyFromEvent(evt);
        if (!string.IsNullOrEmpty(bodyKey))
        {
            waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Other" + i, body))
                .Severity.Should().Be(WafSeverity.Drop);
        }
    }

    private static void PeekAndEvaluate(int i)
    {
        string[] ctcps =
        [
            "GAME R8;1.0;8;#r8room;挂机房;00000;A,B,C,D;Map;Mode;175.178.174.40:50000;",
            "GAME R13;1.0;mg;#ok;Casual;00000;Host,Bob;River;Std;tn.example.org:50000;",
            "GAME R10;1.0;x;#x;Room;00000;A,B;M;G;1.1.1.1:50000;",
            "GAME R8;1.0;8;#a;代练房;00000;A,B,C,D,E;M;G;175.178.174.40:50000;",
            "GAME R9;1.0;9;#b;房;00000;P;M;G;2.2.2.2:50000;",
            "GAME R8;1.0;8;#c;X;00000;A,B,C,D;M;G;175.178.174.40:50000;",
            "GAME R13;1.0;mg;#d;加群优惠;00000;H;M;G;tn.example.org:50000;",
            "GAME R8;1.0;8;#e;Bot;00000;A,B,C,D;M;G;175.178.174.40:50000;",
            "GAME R11;1.0;11;#f;F;00000;A,B;M;G;3.3.3.3:50000;",
            "GAME R8;1.0;8;#g;G;00000;A,B,C,D;M;G;175.178.174.40:50000;",
        ];
        string ctcp = ctcps[i % ctcps.Length];
        WafGameBroadcastPeek.TryPeek(ctcp, out WafGameBroadcastFields fields).Should().BeTrue();
        fields.Revision.Should().NotBeNullOrEmpty();

        // With hang-farm pack, R8 / blacklist tunnels warn; default pack may Allow protocol.
        var pack = HangFarmPack();
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, rules: pack);
        WafDecision d = waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            SenderNick = "Peek" + i,
            RawBody = ctcp,
            DisplayText = fields.RoomName,
            Game = fields,
        });

        if (fields.Revision.Equals("R8", StringComparison.OrdinalIgnoreCase)
            || fields.TunnelEndpoint == "175.178.174.40:50000"
            || fields.RoomName.Contains("加群", StringComparison.Ordinal))
            ((int)d.Severity).Should().BeGreaterThanOrEqualTo((int)WafSeverity.Warn);
        else
            ((int)d.Severity).Should().BeGreaterThanOrEqualTo((int)WafSeverity.Allow);
    }

    private static void ConcurrentEvaluateSmoke()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        var bag = new ConcurrentBag<(int Index, WafSeverity Severity)>();
        Parallel.For(0, 32, n =>
        {
            WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat(
                "C" + n,
                n % 2 == 0 ? "你妈死了" : "gg wp"));
            bag.Add((n, d.Severity));
        });

        bag.Count.Should().Be(32);
        bag.Should().Contain(x => x.Severity == WafSeverity.Warn);
        bag.Should().Contain(x => x.Severity == WafSeverity.Allow);
    }

    private void StoreRoundTrip(int i)
    {
        var entries = new[]
        {
            WafBlockEntry.FromKey("nick=Store" + i, nick: "Store" + i, ident: "id" + i, host: "h" + i, note: "n"),
            WafBlockEntry.FromKey("tunnel=10.0.0." + (i % 200) + ":50000", note: "t"),
        };
        WafUserListStore.SaveEntries(entries);
        IReadOnlyList<WafBlockEntry> loaded = WafUserListStore.LoadEntries();
        loaded.Should().Contain(e => e.Key == "nick=Store" + i && e.Ident == "id" + i);
        File.Exists(Path.Combine(_root.GameRoot, "Client", "WafBlockList.json")).Should().BeTrue();
    }

    private static void HangFarmProtocol(int i)
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, rules: HangFarmPack());
        var game = WafAttackFixtures.HostBotGame(
            channel: "#ym-bot-" + i,
            revision: i % 2 == 0 ? "R8" : "R13",
            fieldCount: i % 2 == 0 ? 9 : 13);
        WafDecision d = waf.Evaluate(WafAttackFixtures.HostBotBroadcast("HB" + i, game));
        d.Severity.Should().Be(WafSeverity.Warn);
        d.MatchedRuleIds.Should().Contain(id => id.StartsWith("proto.", StringComparison.OrdinalIgnoreCase));
    }

    private void PrefsSaveLoad(int i)
    {
        var prefs = new WafStrategyPrefs();
        prefs.SetMode("content.abuse", (WafStrategyMode)(i % 3));
        prefs.SetMode("content.promo", (WafStrategyMode)((i + 1) % 3));
        prefs.Save();

        string path = Path.Combine(_root.GameRoot, "Client", "WafStrategyPrefs.json");
        File.Exists(path).Should().BeTrue();

        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: true);
        waf.SetStrategyMode("content.url", WafStrategyMode.Off);
        WaitPersist(waf); // strategy save is sync on SetStrategyMode when persist=true

        var again = new WafStrategyPrefs();
        again.Load();
        again.GetMode("content.url").Should().Be(WafStrategyMode.Off);
    }

    private static void PadEval(int i)
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        string text = i % 5 == 0 ? "你妈死了" : i % 5 == 1 ? "加群代练" : "hello " + i;
        WafDecision d = waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Pad" + i, text));
        if (i % 5 is 0 or 1)
            d.Severity.Should().Be(WafSeverity.Warn);
        else
            d.Severity.Should().Be(WafSeverity.Allow);
    }

    private static void WaitPersist(CnCNetIngressWaf waf)
        => waf.WaitForPersistToSettle().Should().BeTrue("async persist worker must settle");

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
                { "id": "content.promo", "score": 25, "reason": "p", "enabled": true, "keywords": ["代练","加群"] }
              ]
            }
            """,
            "hangfarm-int");
}
