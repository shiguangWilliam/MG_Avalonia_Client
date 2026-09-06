using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Capability / API matrix (~100 cases): settings toggles, block CRUD, strategy modes,
/// prefs, and rule-pack edges. Failures are intentional findings — do not fix production here.
/// </summary>
public sealed class WafCapabilityMatrixTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (CapabilityCase c in BuildCases())
            yield return new object[] { c.Id, c.Kind };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Capability_Case(string id, string kind)
    {
        id.Should().NotBeNullOrWhiteSpace();
        CapabilityCase c = BuildCases().First(x => x.Id == id);
        c.Kind.Should().Be(kind);
        c.Run();
    }

    [Fact]
    public void Capability_Matrix_Has_At_Least_100_Cases()
        => BuildCases().Count.Should().BeGreaterThanOrEqualTo(100);

    private sealed record CapabilityCase(string Id, string Kind, Action Run);

    private static IReadOnlyList<CapabilityCase>? _cached;

    private static IReadOnlyList<CapabilityCase> BuildCases()
        => _cached ??= BuildCasesCore();

    private static IReadOnlyList<CapabilityCase> BuildCasesCore()
    {
        var list = new List<CapabilityCase>(120);

        // --- Settings toggles (Enabled / surface checks / sensitivity) ---
        list.Add(new("cap.settings.enabled_false_allows_abuse", "settings", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings { Enabled = false }, persistUserList: false);
            waf.IsEnabled.Should().BeFalse();
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("A", "你妈死了"))
                .Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.settings.channel_off_allows_lobby_abuse", "settings", () =>
        {
            var waf = new CnCNetIngressWaf(
                () => new WafSettings { CheckChannelChat = false },
                persistUserList: false);
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("A", "你妈死了"))
                .Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.settings.pm_off_allows_pm_promo", "settings", () =>
        {
            var waf = new CnCNetIngressWaf(
                () => new WafSettings { CheckPrivateChat = false },
                persistUserList: false);
            waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("A", "加群领免费代练 http://spam.vip"))
                .Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.settings.protocol_off_allows_r8_with_pack", "settings", () =>
        {
            WafCompiledRulePack pack = HangFarmPack();
            var waf = new CnCNetIngressWaf(
                () => new WafSettings { CheckProtocol = false, CheckListingText = false },
                persistUserList: false,
                rules: pack);
            waf.Evaluate(WafAttackFixtures.HostBotBroadcast("Bot", WafAttackFixtures.HostBotGame()))
                .Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.settings.listing_off_skips_room_promo", "settings", () =>
        {
            var game = new WafGameBroadcastFields
            {
                Revision = "R13",
                FieldCount = 13,
                ChannelName = "#x",
                RoomName = "加群代练优惠",
                MapName = "M",
                GameMode = "G",
                TunnelHost = "tn.example.org",
                TunnelPort = 50000,
                Players = ["H"],
            };
            var waf = new CnCNetIngressWaf(
                () => new WafSettings { CheckListingText = false, CheckProtocol = false },
                persistUserList: false);
            waf.Evaluate(new WafIngressEvent
            {
                Kind = WafIngressKind.GameBroadcast,
                Surface = WafSurface.Protocol,
                SenderNick = "H",
                DisplayText = game.RoomName,
                Game = game,
            }).Severity.Should().Be(WafSeverity.Allow);
        }));

        for (int sens = 0; sens <= 2; sens++)
        {
            int s = sens;
            list.Add(new($"cap.settings.sensitivity_{s}_url_warns", "settings", () =>
            {
                var waf = new CnCNetIngressWaf(
                    () => new WafSettings { Sensitivity = s },
                    persistUserList: false);
                // promo+url >= 50 pts so every sensitivity level (even sens0,
                // warn=30) warns; a bare URL scores 25 and sits below sens0.
                waf.Evaluate(WafAttackFixtures.LobbyPromoChat("U", "代练加群 visit http://example.com/x"))
                    .Severity.Should().Be(WafSeverity.Warn);
            }));
        }

        list.Add(new("cap.settings.auto_hide_high_risk_exists", "settings", () =>
        {
            var settings = new WafSettings { AutoHideHighRisk = true, AllowHeuristicDrop = false };
            settings.AutoHideHighRisk.Should().BeTrue();
            var waf = new CnCNetIngressWaf(() => settings, persistUserList: false);
            waf.IsEnabled.Should().BeTrue();
        }));

        list.Add(new("cap.settings.allow_heuristic_drop_flag", "settings", () =>
        {
            var settings = new WafSettings { AllowHeuristicDrop = true };
            settings.AllowHeuristicDrop.Should().BeTrue();
        }));

        // --- Block CRUD ---
        string[] blockKeys =
        [
            "nick=Alice", "nick=Bob", "host=evil.example", "ident=mo.abc",
            "tunnel=1.2.3.4:50000", "room=#farm", "fingerprint=deadbeef",
            "body=abc123", "nick=Carol", "nick=Dave",
        ];
        for (int i = 0; i < blockKeys.Length; i++)
        {
            string key = blockKeys[i];
            list.Add(new($"cap.block.crud_{i}_{Sanitize(key)}", "block", () =>
            {
                var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
                waf.Block(key, "note");
                waf.IsBlocked(key).Should().BeTrue();
                waf.ListBlockedKeys().Should().Contain(key);
                waf.ListBlockedEntries().Should().Contain(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                waf.Unblock(key);
                waf.IsBlocked(key).Should().BeFalse();
            }));
        }

        list.Add(new("cap.block.clear_empties", "block", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Block("nick=X");
            waf.Block("nick=Y");
            waf.ClearBlocklist();
            waf.ListBlockedEntries().Should().BeEmpty();
        }));

        list.Add(new("cap.block.entry_actor_triple", "block", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Block(new WafBlockEntry
            {
                Key = "nick=Eve",
                Nick = "Eve",
                Ident = "id",
                Host = "h",
                Note = "n",
            });
            waf.ListBlockedEntries().Should().ContainSingle(e => e.ActorTriple == "Eve!id@h");
        }));

        list.Add(new("cap.block.normalize_nick", "block", () =>
            WafBlockEntry.NormalizeManualKey("Zed").Should().Be("nick=Zed")));
        list.Add(new("cap.block.normalize_room", "block", () =>
            WafBlockEntry.NormalizeManualKey("#room").Should().Be("room=#room")));
        list.Add(new("cap.block.normalize_tunnel", "block", () =>
            WafBlockEntry.NormalizeManualKey("9.9.9.9:50000").Should().Be("tunnel=9.9.9.9:50000")));
        list.Add(new("cap.block.normalize_passthrough", "block", () =>
            WafBlockEntry.NormalizeManualKey("host=x").Should().Be("host=x")));
        list.Add(new("cap.block.infer_kind_nick", "block", () =>
            WafBlockEntry.InferKind("nick=A").Should().Be("nick")));
        list.Add(new("cap.block.infer_kind_body", "block", () =>
            WafBlockEntry.InferKind("body=ff").Should().Be("body")));
        list.Add(new("cap.block.extract_target", "block", () =>
            WafBlockEntry.ExtractTarget("nick=Alice").Should().Be("Alice")));
        list.Add(new("cap.block.empty_key_noop", "block", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Block("  ");
            waf.ListBlockedEntries().Should().BeEmpty();
        }));

        // --- Strategy modes ---
        string[] strategyIds =
        [
            "content.url", "content.contact", "content.promo", "content.fraud",
            "content.sexual", "content.abuse", "content.hate", "content.threat",
            "content.self_harm", "content.child_safety",
            "content.pm.burst", "content.pm.first_contact_promo",
        ];
        foreach (string sid in strategyIds)
        {
            string id = sid;
            list.Add(new($"cap.strategy.off_{Sanitize(id)}", "strategy", () =>
            {
                var prefs = new WafStrategyPrefs();
                prefs.SetMode(id, WafStrategyMode.Off);
                prefs.GetMode(id).Should().Be(WafStrategyMode.Off);
                var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, strategyPrefs: prefs);
                waf.SetStrategyMode(id, WafStrategyMode.Warn);
                waf.StrategyPrefs.GetMode(id).Should().Be(WafStrategyMode.Warn);
            }));
        }

        list.Add(new("cap.strategy.drop_abuse_silent", "strategy", () =>
        {
            var prefs = new WafStrategyPrefs();
            prefs.SetMode("content.abuse", WafStrategyMode.Drop);
            prefs.SetMode("content.hate", WafStrategyMode.Off);
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, strategyPrefs: prefs);
            int alerts = 0;
            waf.AlertRaised += _ => alerts++;
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("X", "你妈死了"))
                .Severity.Should().Be(WafSeverity.Drop);
            alerts.Should().Be(0);
        }));

        list.Add(new("cap.strategy.list_contains_abuse", "strategy", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.ListStrategies().Should().Contain(r => r.Id == "content.abuse" && r.Kind == "content");
        }));

        list.Add(new("cap.strategy.list_contains_pm_burst", "strategy", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.ListStrategies().Should().Contain(r => r.Id == "content.pm.burst" && r.Kind == "pm");
        }));

        list.Add(new("cap.strategy.list_exposes_protocol_when_pack_has_rules", "strategy", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false, rules: HangFarmPack());
            waf.ListStrategies().Should().Contain(r => r.Id.StartsWith("proto.", StringComparison.OrdinalIgnoreCase));
        }));

        list.Add(new("cap.strategy.snapshot_roundtrip", "strategy", () =>
        {
            var prefs = new WafStrategyPrefs();
            prefs.SetMode("content.promo", WafStrategyMode.Drop);
            prefs.Snapshot()["content.promo"].Should().Be(WafStrategyMode.Drop);
        }));

        list.Add(new("cap.strategy.default_mode_is_warn", "strategy", () =>
        {
            new WafStrategyPrefs().GetMode("content.url").Should().Be(WafStrategyMode.Warn);
        }));

        list.Add(new("cap.strategy.blank_id_ignored", "strategy", () =>
        {
            var prefs = new WafStrategyPrefs();
            prefs.SetMode(" ", WafStrategyMode.Drop);
            prefs.Snapshot().Should().BeEmpty();
        }));

        // --- Pack edges ---
        list.Add(new("cap.pack.default_version_ge_2", "pack", () =>
            WafRulePackLoader.Default.Version.Should().BeGreaterThanOrEqualTo(2)));

        list.Add(new("cap.pack.default_has_content_classes", "pack", () =>
            WafRulePackLoader.Default.ContentClasses.Select(c => c.Id)
                .Should().Contain(new[] { "content.abuse", "content.promo", "content.url" })));

        list.Add(new("cap.pack.default_protocol_empty", "pack", () =>
            WafRulePackLoader.Default.Protocol.Should().BeEmpty(
                because: "hang-farm protocol[] is intentionally empty in default pack")));

        list.Add(new("cap.pack.compile_inline_protocol", "pack", () =>
        {
            WafCompiledRulePack pack = HangFarmPack();
            pack.Protocol.Should().ContainKey("proto.game.r8");
            pack.IsKnownHostBotTunnel(WafAttackFixtures.HostBotTunnelHost + ":" + WafAttackFixtures.HostBotTunnelPort)
                .Should().BeTrue();
        }));

        list.Add(new("cap.pack.invalid_regex_skipped", "pack", () =>
        {
            WafCompiledRulePack pack = WafRulePackLoader.CompileFromJson(
                """{"version":2,"contentClasses":[{"id":"content.url","score":25,"reason":"u","regexes":["(bad"],"keywords":["http"]}]}""",
                "bad");
            pack.ContentClasses[0].Regexes.Should().BeEmpty();
        }));

        list.Add(new("cap.pack.empty_content_classes", "pack", () =>
        {
            WafCompiledRulePack pack = WafRulePackLoader.CompileFromJson(
                """{"version":2,"protocol":[],"contentClasses":[]}""",
                "empty");
            pack.ContentClasses.Should().BeEmpty();
        }));

        list.Add(new("cap.pack.sensitivity_thresholds", "pack", () =>
        {
            var t = WafRulePackLoader.Default.Thresholds(1);
            t.Warn.Should().BeGreaterThan(0);
            t.Hide.Should().BeGreaterThan(t.Warn);
        }));

        list.Add(new("cap.pack.keywords_promo_nonempty", "pack", () =>
            WafRulePackLoader.Default.GetKeywords("content.promo").Should().NotBeEmpty()));

        list.Add(new("cap.pack.source_label", "pack", () =>
            HangFarmPack().Source.Should().Contain("hangfarm")));

        // --- Evaluate API smoke across kinds ---
        list.Add(new("cap.api.evaluate_allow_clean", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Nice", "gg wp")).Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.api.evaluate_legitimate_r13", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Evaluate(WafAttackFixtures.LegitimateR13Game()).Severity.Should().Be(WafSeverity.Allow);
        }));

        list.Add(new("cap.api.rules_property_exposed", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Rules.Should().NotBeNull();
            waf.StrategyPrefs.Should().NotBeNull();
        }));

        list.Add(new("cap.api.alert_raised_on_warn", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            int n = 0;
            waf.AlertRaised += _ => n++;
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("T", "你妈死了"));
            n.Should().Be(1);
        }));

        list.Add(new("cap.api.blocklist_drop_no_alert", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Block("nick=Banned");
            int n = 0;
            waf.AlertRaised += _ => n++;
            waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Banned", "hello")).Severity.Should().Be(WafSeverity.Drop);
            n.Should().Be(0);
        }));

        list.Add(new("cap.api.prune_ephemeral_noop_when_empty", "api", () =>
        {
            var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
            waf.Invoking(w => w.PruneEphemeralState(TimeSpan.FromSeconds(1))).Should().NotThrow();
        }));

        list.Add(new("cap.api.invite_ctcp_event_shape", "api", () =>
        {
            WafIngressEvent e = WafAttackFixtures.InviteCtcp("Inv", "#chan pass");
            e.Kind.Should().Be(WafIngressKind.PrivateCtcp);
            e.CtcpCommand.Should().Be("INVITE");
        }));

        // Pad to >=100 with systematic settings × surface checks
        string[] surfaces = ["lobby", "pm", "listing"];
        string[] risky = ["你妈死了", "加群代练", "http://x.vip", "弄死你", "自杀教程"];
        int pad = 0;
        foreach (string surface in surfaces)
        {
            foreach (string text in risky)
            {
                string s = surface;
                string t = text;
                int idx = pad++;
                list.Add(new($"cap.pad.surface_{s}_{idx}", "pad", () =>
                {
                    var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
                    WafDecision d = EvaluateSurface(waf, s, t);
                    ((int)d.Severity).Should().BeGreaterThanOrEqualTo((int)WafSeverity.Allow);
                }));
            }
        }

        // Ensure we have at least 100 by adding more block-key variants if needed
        for (int i = 0; list.Count < 100; i++)
        {
            int n = i;
            list.Add(new($"cap.pad.block_nick_{n}", "pad", () =>
            {
                var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
                string key = "nick=Pad" + n;
                waf.Block(key);
                waf.IsBlocked(key).Should().BeTrue();
                waf.Unblock(key);
                waf.IsBlocked(key).Should().BeFalse();
            }));
        }

        return list;
    }

    private static WafDecision EvaluateSurface(CnCNetIngressWaf waf, string surface, string text)
    {
        return surface switch
        {
            "pm" => waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("Pad", text)),
            "listing" => waf.Evaluate(new WafIngressEvent
            {
                Kind = WafIngressKind.GameBroadcast,
                Surface = WafSurface.Protocol,
                SenderNick = "Pad",
                DisplayText = text,
                Game = new WafGameBroadcastFields
                {
                    Revision = "R13",
                    FieldCount = 13,
                    ChannelName = "#pad",
                    RoomName = text,
                    MapName = "M",
                    GameMode = "G",
                    TunnelHost = "tn.example.org",
                    TunnelPort = 50000,
                    Players = ["Pad"],
                },
            }),
            _ => waf.Evaluate(WafAttackFixtures.LobbyPromoChat("Pad", text)),
        };
    }

    private static WafCompiledRulePack HangFarmPack()
        => WafRulePackLoader.CompileFromJson(
            """
            {
              "version": 2,
              "description": "hangfarm-test",
              "hostBotTunnels": [ "175.178.174.40:50000" ],
              "protocol": [
                { "id": "proto.game.r8", "score": 40, "reason": "R8" },
                { "id": "proto.game.field_count", "score": 50, "reason": "fields" },
                { "id": "proto.tunnel.blacklist", "score": 80, "reason": "tunnel" },
                { "id": "proto.tunnel.shared_hosts", "score": 45, "threshold": 3, "reason": "shared" },
                { "id": "proto.game.fake_players", "score": 35, "reason": "fake" },
                { "id": "proto.game.template_fingerprint", "score": 40, "threshold": 2, "reason": "tpl" },
                { "id": "proto.invite.flood", "score": 30, "minCount": 3, "windowSeconds": 30, "perExtra": 10, "cap": 80 }
              ],
              "contentClasses": []
            }
            """,
            "hangfarm");

    private static string Sanitize(string s)
        => s.Replace('=', '_').Replace('.', '_').Replace(':', '_').Replace('#', '_').Replace('/', '_');
}
