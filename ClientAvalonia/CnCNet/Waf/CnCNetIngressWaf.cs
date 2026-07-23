using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>
/// Ingress WAF between CnCNet Session (IRC truth) and SessionService (UI marshal).
/// Default: Warn. Drop only for user-confirmed blocklist (or optional confirmed samples).
/// </summary>
public sealed class CnCNetIngressWaf : ICnCNetIngressWaf
{
    private readonly ConcurrentDictionary<string, WafBlockEntry> _blocked =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _rateWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _rateWindowLastTouch =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TunnelHostSighting> _tunnelHosts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TemplateNickBucket> _templateNicks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateSync = new();
    private readonly Func<WafSettings> _settings;
    private readonly bool _persistUserList;
    private readonly WafCompiledRulePack _rules;
    private readonly WafStrategyPrefs _strategyPrefs;
    private int _persistVersion;
    private int _persistQueued;

    private const int MaxRateWindowKeys = 512;

    public event Action<WafAlert>? AlertRaised;

    public CnCNetIngressWaf(
        Func<WafSettings>? settings = null,
        bool persistUserList = true,
        WafCompiledRulePack? rules = null,
        WafStrategyPrefs? strategyPrefs = null)
    {
        _settings = settings ?? WafSettings.FromUserIni;
        _persistUserList = persistUserList;
        _rules = rules ?? WafRulePackLoader.Default;
        _strategyPrefs = strategyPrefs ?? new WafStrategyPrefs();
        if (persistUserList)
            _strategyPrefs.Load();
    }

    /// <summary>Active compiled rule pack (embedded / file / injected).</summary>
    public WafCompiledRulePack Rules => _rules;

    public WafStrategyPrefs StrategyPrefs => _strategyPrefs;

    public bool IsEnabled => _settings().Enabled;

    public WafDecision Evaluate(WafIngressEvent e)
    {
        WafSettings settings = _settings();
        if (!settings.Enabled)
            return WafDecision.Allow;

        // User blocklist → Drop (player-confirmed). Silent — no UI alert.
        foreach (string key in BuildActorKeys(e))
        {
            if (_blocked.ContainsKey(key))
            {
                return Finish(e, BlocklistDrop([key]), raiseAlert: false);
            }
        }

        string bodyKey = WafBodyFingerprint.KeyFromEvent(e);
        if (!string.IsNullOrEmpty(bodyKey) && _blocked.ContainsKey(bodyKey))
        {
            return Finish(e, new WafDecision
            {
                Severity = WafSeverity.Drop,
                Score = 1000,
                MatchedRuleIds = ["user.blocklist.body"],
                Reasons = ["已在本地 WAF 屏蔽名单中（同型消息体）"],
                SuggestedBlockKeys = [bodyKey],
            }, raiseAlert: false);
        }

        if (e.Game != null)
        {
            string roomKey = "room=" + e.Game.ChannelName;
            string tunnelKey = "tunnel=" + e.Game.TunnelEndpoint;
            string fingerprintKey = string.Empty;
            string fp = WafTemplateFingerprint.Compute(e.Game);
            if (!string.IsNullOrEmpty(fp))
                fingerprintKey = "fingerprint=" + fp;

            if (_blocked.ContainsKey(roomKey)
                || (!string.IsNullOrEmpty(e.Game.TunnelEndpoint) && _blocked.ContainsKey(tunnelKey))
                || (!string.IsNullOrEmpty(fingerprintKey) && _blocked.ContainsKey(fingerprintKey)))
            {
                return Finish(e, BlocklistDrop([roomKey, tunnelKey, fingerprintKey]), raiseAlert: false);
            }
        }

        var matched = new List<string>();
        var reasons = new List<string>();
        var suggest = new List<string>();
        int score = 0;
        bool forceDrop = false;

        if (settings.CheckProtocol && e.Kind == WafIngressKind.GameBroadcast && e.Game != null)
            score += ScoreGameProtocol(e, matched, reasons, suggest, ref forceDrop);

        if (settings.CheckProtocol
            && e.Kind is WafIngressKind.PrivateCtcp or WafIngressKind.ChannelCtcp
            && e.CtcpCommand.Equals("INVITE", StringComparison.OrdinalIgnoreCase))
        {
            score += ScoreInviteFlood(e, matched, reasons, suggest, ref forceDrop);
        }

        if (settings.CheckListingText && e.Game != null)
            score += ScoreText(e.Game.RoomName + " " + e.Game.MapName + " " + e.Game.GameMode, "listing", matched, reasons, ref forceDrop);

        if (e.Surface is WafSurface.LobbyChat or WafSurface.GameRoomChat)
        {
            if (settings.CheckChannelChat)
                score += ScoreText(e.DisplayText, "chat", matched, reasons, ref forceDrop);
        }
        else if (e.Surface == WafSurface.PrivateMessage)
        {
            if (settings.CheckPrivateChat)
            {
                score += ScoreText(e.DisplayText, "pm", matched, reasons, ref forceDrop);
                score += ScorePrivateBurst(e, matched, reasons, suggest, ref forceDrop);
            }
        }
        else if (e.Kind is WafIngressKind.PrivateCtcp && settings.CheckPrivateChat)
        {
            score += ScoreText(
                string.IsNullOrEmpty(e.CtcpPayload) ? e.DisplayText : e.CtcpPayload,
                "pm",
                matched,
                reasons,
                ref forceDrop);
        }

        if (score > 0 && !string.IsNullOrWhiteSpace(e.SenderNick)
            && e.Surface is WafSurface.PrivateMessage or WafSurface.LobbyChat or WafSurface.GameRoomChat)
        {
            suggest.Add("nick=" + e.SenderNick);
        }

        if (score <= 0)
            return WafDecision.Allow;

        WafSeverity severity;
        if (forceDrop)
        {
            severity = WafSeverity.Drop;
        }
        else
        {
            severity = MapSeverity(score, settings);
            // Default product policy: never auto-Drop from heuristic score (only blocklist / Drop strategies).
            if (severity == WafSeverity.Drop && !settings.AllowHeuristicDrop)
            {
                severity = settings.AutoHideHighRisk
                    ? WafSeverity.Hide
                    : WafSeverity.Warn;
            }
        }

        return Finish(e, new WafDecision
        {
            Severity = severity,
            Score = score,
            MatchedRuleIds = matched,
            Reasons = reasons,
            SuggestedBlockKeys = suggest
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        }, raiseAlert: severity is WafSeverity.Warn or WafSeverity.Hide);
    }

    private static WafDecision BlocklistDrop(IReadOnlyList<string> keys)
        => new()
        {
            Severity = WafSeverity.Drop,
            Score = 1000,
            MatchedRuleIds = ["user.blocklist"],
            Reasons = ["已在本地 WAF 屏蔽名单中"],
            SuggestedBlockKeys = keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
        };

    public bool IsBlocked(string blockKey)
        => !string.IsNullOrWhiteSpace(blockKey) && _blocked.ContainsKey(blockKey);

    public void Block(string blockKey, string? note = null)
        => Block(WafBlockEntry.FromKey(blockKey, note: note));

    public void Block(WafBlockEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
            return;

        string key = entry.Key.Trim();
        if (string.IsNullOrWhiteSpace(entry.Kind))
            entry.Kind = WafBlockEntry.InferKind(key);
        if (entry.AddedUtc == default)
            entry.AddedUtc = DateTime.UtcNow;

        _blocked.AddOrUpdate(
            key,
            entry,
            (_, existing) =>
            {
                // Keep earliest AddedUtc; fill blank actor fields from the newer sample.
                if (string.IsNullOrWhiteSpace(existing.Nick) && !string.IsNullOrWhiteSpace(entry.Nick))
                    existing.Nick = entry.Nick;
                if (string.IsNullOrWhiteSpace(existing.Ident) && !string.IsNullOrWhiteSpace(entry.Ident))
                    existing.Ident = entry.Ident;
                if (string.IsNullOrWhiteSpace(existing.Host) && !string.IsNullOrWhiteSpace(entry.Host))
                    existing.Host = entry.Host;
                if (string.IsNullOrWhiteSpace(existing.Note) && !string.IsNullOrWhiteSpace(entry.Note))
                    existing.Note = entry.Note;
                return existing;
            });

        Logger.Log($"CnCNet WAF: blocklist add key={key} actor={entry.ActorTriple}"
                    + (string.IsNullOrWhiteSpace(entry.Note) ? "" : $" note={entry.Note}"));
        PersistIfNeeded();
    }

    public void BlockFromAlert(WafIngressEvent ingressEvent, WafDecision decision, string? note = null)
    {
        string n = string.IsNullOrWhiteSpace(note) ? decision.Summary : note!;
        if (string.IsNullOrWhiteSpace(n))
            n = "告警加入";

        foreach (string key in decision.SuggestedBlockKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            Block(new WafBlockEntry
            {
                Key = key.Trim(),
                Kind = WafBlockEntry.InferKind(key),
                Nick = ingressEvent.SenderNick,
                Ident = ingressEvent.SenderIdent,
                Host = ingressEvent.SenderHost,
                Note = n,
                AddedUtc = DateTime.UtcNow,
            });
        }

        string bodyKey = WafBodyFingerprint.KeyFromEvent(ingressEvent);
        if (string.IsNullOrEmpty(bodyKey))
            return;

        string preview = ingressEvent.DisplayText;
        if (string.IsNullOrWhiteSpace(preview))
            preview = ingressEvent.RawBody;
        if (!string.IsNullOrEmpty(preview) && preview.Length > 40)
            preview = preview[..37] + "...";

        Block(new WafBlockEntry
        {
            Key = bodyKey,
            Kind = "body",
            Nick = ingressEvent.SenderNick,
            Ident = ingressEvent.SenderIdent,
            Host = ingressEvent.SenderHost,
            Note = string.IsNullOrWhiteSpace(preview) ? "同型消息体" : "同型消息体 · " + preview,
            AddedUtc = DateTime.UtcNow,
        });
    }

    public void Unblock(string blockKey)
    {
        if (string.IsNullOrWhiteSpace(blockKey))
            return;
        string key = blockKey.Trim();
        if (_blocked.TryRemove(key, out _))
            Logger.Log($"CnCNet WAF: blocklist remove key={key}");
        PersistIfNeeded();
    }

    public IReadOnlyList<string> ListBlockedKeys()
        => _blocked.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<WafBlockEntry> ListBlockedEntries()
        => _blocked.Values
            .OrderByDescending(e => e.AddedUtc)
            .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void ClearBlocklist()
    {
        _blocked.Clear();
        Logger.Log("CnCNet WAF: blocklist cleared");
        PersistIfNeeded();
    }

    public IReadOnlyList<WafStrategyRow> ListStrategies()
    {
        var rows = new List<WafStrategyRow>();

        foreach (WafCompiledContentClass cls in _rules.ContentClasses)
        {
            string samples = cls.Keywords.Count == 0
                ? (cls.Regexes.Count > 0 ? $"正则×{cls.Regexes.Count}" : "(无样本)")
                : string.Join("、", cls.Keywords.Take(6))
                  + (cls.Keywords.Count > 6 ? "…" : string.Empty);

            rows.Add(new WafStrategyRow
            {
                Id = cls.Id,
                Kind = "content",
                Content = $"{cls.Reason}｜样本：{samples}",
                Mode = _strategyPrefs.GetMode(cls.Id),
            });
        }

        foreach (WafCompiledProtocolRule rule in _rules.Protocol.Values
                     .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new WafStrategyRow
            {
                Id = rule.Id,
                Kind = "protocol",
                Content = string.IsNullOrWhiteSpace(rule.Reason) ? rule.Id : rule.Reason,
                Mode = _strategyPrefs.GetMode(rule.Id),
            });
        }

        rows.Add(new WafStrategyRow
        {
            Id = _rules.PmBurst.Id,
            Kind = "pm",
            Content = _rules.PmBurst.Reason,
            Mode = _strategyPrefs.GetMode(_rules.PmBurst.Id),
        });
        rows.Add(new WafStrategyRow
        {
            Id = _rules.PmFirstContact.Id,
            Kind = "pm",
            Content = _rules.PmFirstContact.Reason,
            Mode = _strategyPrefs.GetMode(_rules.PmFirstContact.Id),
        });

        return rows;
    }

    public void SetStrategyMode(string strategyId, WafStrategyMode mode)
    {
        _strategyPrefs.SetMode(strategyId, mode);
        if (_persistUserList)
            _strategyPrefs.Save();
        Logger.Log($"CnCNet WAF: strategy {strategyId} → {mode}");
    }

    public void LoadUserList()
    {
        foreach (WafBlockEntry entry in WafUserListStore.LoadEntries())
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;
            _blocked[entry.Key.Trim()] = entry;
        }

        if (_persistUserList)
            _strategyPrefs.Load();
    }

    private void PersistIfNeeded()
    {
        if (!_persistUserList)
            return;

        Interlocked.Increment(ref _persistVersion);
        if (Interlocked.Exchange(ref _persistQueued, 1) == 1)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var waf = (CnCNetIngressWaf)state!;
            try
            {
                while (true)
                {
                    int seen = Volatile.Read(ref waf._persistVersion);
                    WafUserListStore.SaveEntries(waf.ListBlockedEntries());
                    if (Volatile.Read(ref waf._persistVersion) != seen)
                        continue;

                    Interlocked.Exchange(ref waf._persistQueued, 0);
                    if (Volatile.Read(ref waf._persistVersion) == seen)
                        break;

                    if (Interlocked.Exchange(ref waf._persistQueued, 1) == 1)
                        break;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref waf._persistQueued, 0);
                Logger.Log($"CnCNet WAF: async persist failed: {ex.Message}");
            }
        }, this);
    }

    private WafDecision Finish(WafIngressEvent e, WafDecision decision, bool raiseAlert = true)
    {
        // Log any scored decision (including Allow-below-threshold) so misses are diagnosable.
        if (decision.Score > 0 || decision.Severity >= WafSeverity.Warn)
        {
            string rules = decision.MatchedRuleIds.Count > 0
                ? string.Join(",", decision.MatchedRuleIds)
                : "-";
            string preview = e.DisplayText;
            if (string.IsNullOrWhiteSpace(preview))
                preview = e.RawBody;
            if (!string.IsNullOrEmpty(preview) && preview.Length > 80)
                preview = preview[..77] + "...";
            Logger.Log(
                $"CnCNet WAF: {decision.Severity} score={decision.Score} kind={e.Kind} surface={e.Surface}"
                + $" nick={e.SenderNick} channel={e.Channel} rules=[{rules}]"
                + (string.IsNullOrEmpty(decision.Summary) ? "" : $" reasons={decision.Summary}")
                + (string.IsNullOrEmpty(preview) ? "" : $" text={preview}"));
        }

        // Only Warn/Hide prompt the player. Drop stays silent (blocklist / body / strategy Drop).
        if (raiseAlert && decision.Severity is WafSeverity.Warn or WafSeverity.Hide)
            AlertRaised?.Invoke(new WafAlert { Event = e, Decision = decision });

        return decision;
    }

    private bool ApplyStrategyGate(string strategyId, ref bool forceDrop)
    {
        WafStrategyMode mode = _strategyPrefs.GetMode(strategyId);
        if (mode == WafStrategyMode.Off)
            return false;
        if (mode == WafStrategyMode.Drop)
            forceDrop = true;
        return true;
    }

    private int ScoreGameProtocol(
        WafIngressEvent e,
        List<string> matched,
        List<string> reasons,
        List<string> suggest,
        ref bool forceDrop)
    {
        WafGameBroadcastFields g = e.Game!;
        int score = 0;

        if (g.Revision.Equals("R8", StringComparison.OrdinalIgnoreCase)
            && ApplyStrategyGate("proto.game.r8", ref forceDrop))
        {
            score += _rules.ProtocolScore("proto.game.r8", 40);
            matched.Add("proto.game.r8");
            reasons.Add(_rules.ProtocolReason("proto.game.r8", "GAME 使用已弃用的 R8 协议（常见挂房机）"));
        }

        if (g.FieldCount is not (11 or 13) && g.FieldCount > 0
            && ApplyStrategyGate("proto.game.field_count", ref forceDrop))
        {
            score += _rules.ProtocolScore("proto.game.field_count", 50);
            matched.Add("proto.game.field_count");
            reasons.Add($"{_rules.ProtocolReason("proto.game.field_count", "GAME 字段数异常")} ({g.FieldCount})");
        }

        string tunnel = g.TunnelEndpoint;
        if (_rules.IsKnownHostBotTunnel(tunnel)
            && ApplyStrategyGate("proto.tunnel.blacklist", ref forceDrop))
        {
            score += _rules.ProtocolScore("proto.tunnel.blacklist", 80);
            matched.Add("proto.tunnel.blacklist");
            reasons.Add(_rules.ProtocolReason("proto.tunnel.blacklist", "命中已知挂房机隧道样本"));
            suggest.Add("tunnel=" + tunnel);
        }

        if (!string.IsNullOrEmpty(tunnel))
        {
            string hostKey = e.SenderNick;
            if (!string.IsNullOrEmpty(hostKey))
            {
                DateTime now = DateTime.UtcNow;
                _tunnelHosts.AddOrUpdate(
                    hostKey,
                    _ => new TunnelHostSighting(tunnel, now),
                    (_, existing) => new TunnelHostSighting(tunnel, now));

                int distinctHosts = _tunnelHosts
                    .Where(kv => kv.Value.Tunnel.Equals(tunnel, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                WafCompiledProtocolRule? shared = _rules.ProtocolRule("proto.tunnel.shared_hosts");
                int threshold = shared?.Threshold ?? 3;
                if (distinctHosts >= threshold
                    && ApplyStrategyGate("proto.tunnel.shared_hosts", ref forceDrop))
                {
                    score += shared?.Score ?? 30;
                    matched.Add("proto.tunnel.shared_hosts");
                    reasons.Add(shared?.Reason ?? "同一隧道短时出现多个「房主」");
                    suggest.Add("tunnel=" + tunnel);
                }
            }
        }

        WafCompiledProtocolRule? burstRule = _rules.ProtocolRule("proto.game.burst");
        int burstMin = burstRule?.MinCount ?? 4;
        int burstWindow = burstRule?.WindowSeconds ?? 20;
        string rateKey = "game:" + (string.IsNullOrEmpty(g.ChannelName) ? e.SenderNick : g.ChannelName);
        int bursts = NoteAndCount(rateKey, TimeSpan.FromSeconds(burstWindow));
        if (bursts >= burstMin && ApplyStrategyGate("proto.game.burst", ref forceDrop))
        {
            int per = burstRule?.PerBurst ?? burstRule?.Score ?? 15;
            int cap = burstRule?.Cap ?? 60;
            score += Math.Min(cap, per * bursts);
            matched.Add("proto.game.burst");
            reasons.Add(burstRule?.Reason ?? "房间广播刷新异常频繁");
            if (!string.IsNullOrEmpty(g.ChannelName))
                suggest.Add("room=" + g.ChannelName);
        }

        if (LooksLikeSequentialFakePlayers(g.Players)
            && ApplyStrategyGate("proto.game.fake_players", ref forceDrop))
        {
            score += _rules.ProtocolScore("proto.game.fake_players", 35);
            matched.Add("proto.game.fake_players");
            reasons.Add(_rules.ProtocolReason("proto.game.fake_players", "玩家名单呈模板化假名"));
        }

        string fingerprint = WafTemplateFingerprint.Compute(g);
        if (!string.IsNullOrEmpty(fingerprint) && !string.IsNullOrWhiteSpace(e.SenderNick))
        {
            TemplateNickBucket bucket = _templateNicks.GetOrAdd(
                fingerprint,
                _ => new TemplateNickBucket());
            bucket.Touch(e.SenderNick);
            WafCompiledProtocolRule? tpl = _rules.ProtocolRule("proto.game.template_fingerprint");
            int tplThreshold = tpl?.Threshold ?? 2;
            if (bucket.NickCount >= tplThreshold
                && ApplyStrategyGate("proto.game.template_fingerprint", ref forceDrop))
            {
                score += tpl?.Score ?? 35;
                matched.Add("proto.game.template_fingerprint");
                reasons.Add(tpl?.Reason ?? "相同房间模板被不同昵称反复挂出");
                suggest.Add("fingerprint=" + fingerprint);
                suggest.Add("tunnel=" + tunnel);
            }
        }

        suggest.Add("nick=" + e.SenderNick);
        if (!string.IsNullOrEmpty(g.ChannelName))
            suggest.Add("room=" + g.ChannelName);

        return score;
    }

    private int ScoreInviteFlood(
        WafIngressEvent e,
        List<string> matched,
        List<string> reasons,
        List<string> suggest,
        ref bool forceDrop)
    {
        if (!ApplyStrategyGate("proto.invite.flood", ref forceDrop))
            return 0;

        WafCompiledProtocolRule? rule = _rules.ProtocolRule("proto.invite.flood");
        int min = rule?.MinCount ?? 3;
        int window = rule?.WindowSeconds ?? 30;
        string key = "invite:" + e.SenderNick;
        int n = NoteAndCount(key, TimeSpan.FromSeconds(window));
        if (n < min)
            return 0;

        int baseScore = rule?.Score ?? 40;
        int perExtra = rule?.PerExtra ?? 10;
        int cap = rule?.Cap ?? 80;
        matched.Add("proto.invite.flood");
        reasons.Add(rule?.Reason ?? "短时大量游戏邀请（INVITE）");
        suggest.Add("nick=" + e.SenderNick);
        return Math.Min(cap, baseScore + (n - min) * perExtra);
    }

    private int ScorePrivateBurst(
        WafIngressEvent e,
        List<string> matched,
        List<string> reasons,
        List<string> suggest,
        ref bool forceDrop)
    {
        WafCompiledPmBurst burst = _rules.PmBurst;
        if (!ApplyStrategyGate(burst.Id, ref forceDrop))
            return 0;

        string key = "pm:" + e.SenderNick;
        int n = NoteAndCount(key, TimeSpan.FromSeconds(burst.WindowSeconds));
        if (n < burst.MinCount)
            return 0;

        matched.Add(burst.Id);
        reasons.Add(burst.Reason);
        suggest.Add("nick=" + e.SenderNick);
        return Math.Min(burst.Cap, burst.BaseScore + n * burst.PerMessage);
    }

    private int ScoreText(
        string text,
        string surfaceTag,
        List<string> matched,
        List<string> reasons,
        ref bool forceDrop)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        string normalized = WafTextNormalizer.Normalize(text);
        string compact = WafTextNormalizer.CompactForMatch(normalized);
        int score = 0;

        foreach (WafCompiledContentClass cls in _rules.ContentClasses)
        {
            if (!ApplyStrategyGate(cls.Id, ref forceDrop))
                continue;
            if (!cls.Enabled)
                continue;
            if (!cls.Matches(normalized, compact))
                continue;

            score += cls.Score;
            matched.Add(cls.Id);
            reasons.Add(cls.ReasonFor(surfaceTag));
        }

        WafCompiledPmFirstContact first = _rules.PmFirstContact;
        if (surfaceTag == "pm"
            && score >= first.MinScore
            && matched.Any(id => first.TriggerClasses.Contains(id))
            && ApplyStrategyGate(first.Id, ref forceDrop))
        {
            score += first.Score;
            matched.Add(first.Id);
            reasons.Add(first.Reason);
        }

        return score;
    }

    private WafSeverity MapSeverity(int score, WafSettings settings)
    {
        (int warn, int hide, int drop) = _rules.Thresholds(settings.Sensitivity);
        if (score >= drop)
            return WafSeverity.Drop;
        if (score >= hide)
            return settings.AutoHideHighRisk ? WafSeverity.Hide : WafSeverity.Warn;
        if (score >= warn)
            return WafSeverity.Warn;
        return WafSeverity.Allow;
    }

    private int NoteAndCount(string key, TimeSpan window)
    {
        DateTime now = DateTime.UtcNow;
        lock (_rateSync)
        {
            EvictRateWindowsIfNeeded_NoLock(now);

            Queue<DateTime> q = _rateWindows.GetOrAdd(key, _ => new Queue<DateTime>());
            q.Enqueue(now);
            while (q.Count > 0 && now - q.Peek() > window)
                q.Dequeue();
            _rateWindowLastTouch[key] = now;
            return q.Count;
        }
    }

    private void EvictRateWindowsIfNeeded_NoLock(DateTime now)
    {
        if (_rateWindows.Count < MaxRateWindowKeys)
            return;

        // Drop oldest-touched keys until under soft limit (keep headroom for the next insert).
        int removeCount = Math.Max(1, _rateWindows.Count - MaxRateWindowKeys + 32);
        foreach (KeyValuePair<string, DateTime> oldest in _rateWindowLastTouch
                     .OrderBy(kv => kv.Value)
                     .Take(removeCount)
                     .ToList())
        {
            _rateWindows.TryRemove(oldest.Key, out _);
            _rateWindowLastTouch.TryRemove(oldest.Key, out _);
        }

        Logger.Log($"CnCNet WAF: rate-window hard cap eviction removed≈{removeCount} keys (max={MaxRateWindowKeys})");
    }

    /// <inheritdoc />
    public void PruneEphemeralState(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
            maxAge = TimeSpan.FromSeconds(70);

        DateTime cutoff = DateTime.UtcNow - maxAge;
        int removedTunnels = 0;
        int removedTemplates = 0;
        int removedRates = 0;

        foreach (KeyValuePair<string, TunnelHostSighting> kv in _tunnelHosts)
        {
            if (kv.Value.LastSeenUtc >= cutoff)
                continue;
            if (_tunnelHosts.TryRemove(kv.Key, out _))
                removedTunnels++;
        }

        foreach (KeyValuePair<string, TemplateNickBucket> kv in _templateNicks)
        {
            if (kv.Value.LastSeenUtc >= cutoff)
                continue;
            if (_templateNicks.TryRemove(kv.Key, out _))
                removedTemplates++;
        }

        lock (_rateSync)
        {
            foreach (string key in _rateWindows.Keys.ToList())
            {
                if (!_rateWindows.TryGetValue(key, out Queue<DateTime>? q))
                    continue;

                while (q.Count > 0 && q.Peek() < cutoff)
                    q.Dequeue();

                DateTime lastTouch = _rateWindowLastTouch.TryGetValue(key, out DateTime t) ? t : DateTime.MinValue;
                bool staleTouch = lastTouch < cutoff;
                if ((q.Count == 0 || staleTouch) && _rateWindows.TryRemove(key, out _))
                {
                    _rateWindowLastTouch.TryRemove(key, out _);
                    removedRates++;
                }
            }
        }

        if (removedTunnels + removedTemplates + removedRates > 0)
        {
            Logger.Log(
                $"CnCNet WAF: pruned ephemeral state tunnels={removedTunnels}"
                + $" templates={removedTemplates} rateKeys={removedRates} maxAge={maxAge.TotalSeconds:0}s");
        }
    }

    // Test hooks for ephemeral-cache bounds.
    internal int TunnelHostCountForTests => _tunnelHosts.Count;
    internal int TemplateFingerprintCountForTests => _templateNicks.Count;
    internal int RateWindowKeyCountForTests
    {
        get
        {
            lock (_rateSync)
                return _rateWindows.Count;
        }
    }

    private static bool LooksLikeSequentialFakePlayers(IReadOnlyList<string> players)
    {
        if (players.Count < 4)
            return false;

        // A,B,C,D… or A E / B F style single letters
        int singleLetter = players.Count(p => p.Length == 1 && char.IsLetter(p[0]));
        return singleLetter >= 4;
    }

    private static IEnumerable<string> BuildActorKeys(WafIngressEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.SenderNick))
            yield return "nick=" + e.SenderNick;
        if (!string.IsNullOrWhiteSpace(e.SenderHost))
            yield return "host=" + e.SenderHost;
        if (!string.IsNullOrWhiteSpace(e.SenderIdent))
            yield return "ident=" + e.SenderIdent;
    }

    private readonly record struct TunnelHostSighting(string Tunnel, DateTime LastSeenUtc);

    private sealed class TemplateNickBucket
    {
        private readonly ConcurrentDictionary<string, byte> _nicks =
            new(StringComparer.OrdinalIgnoreCase);

        public DateTime LastSeenUtc { get; private set; } = DateTime.UtcNow;

        public int NickCount => _nicks.Count;

        public void Touch(string nick)
        {
            _nicks[nick] = 0;
            LastSeenUtc = DateTime.UtcNow;
        }
    }
}
