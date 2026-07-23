using System;
using System.Collections.Generic;

namespace ClientAvalonia.CnCNet.Waf;

public enum WafSeverity
{
    Allow = 0,
    Warn = 1,
    Hide = 2,
    Drop = 3,
}

public enum WafIngressKind
{
    GameBroadcast,
    ChannelChat,
    PrivateChat,
    ChannelAction,
    PrivateAction,
    PrivateCtcp,
    ChannelCtcp,
}

public enum WafSurface
{
    Protocol,
    ListingText,
    LobbyChat,
    GameRoomChat,
    PrivateMessage,
}

/// <summary>Structured IRC/CTCP event presented to the ingress WAF (between Session truth and UI Service).</summary>
public sealed class WafIngressEvent
{
    public required WafIngressKind Kind { get; init; }

    public required WafSurface Surface { get; init; }

    public string Channel { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string SenderNick { get; init; } = string.Empty;

    public string SenderIdent { get; init; } = string.Empty;

    public string SenderHost { get; init; } = string.Empty;

    /// <summary>Normalized display text for content rules (color codes stripped).</summary>
    public string DisplayText { get; init; } = string.Empty;

    public string RawBody { get; init; } = string.Empty;

    public string CtcpCommand { get; init; } = string.Empty;

    public string CtcpPayload { get; init; } = string.Empty;

    public WafGameBroadcastFields? Game { get; init; }

    public DateTime UtcTimestamp { get; init; } = DateTime.UtcNow;
}

public sealed class WafGameBroadcastFields
{
    public string Revision { get; init; } = string.Empty;

    public int FieldCount { get; init; }

    public string Flags { get; init; } = string.Empty;

    public string RoomName { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;

    public string TunnelHost { get; init; } = string.Empty;

    public ushort TunnelPort { get; init; }

    public string ChannelName { get; init; } = string.Empty;

    public IReadOnlyList<string> Players { get; init; } = [];

    public string TunnelEndpoint =>
        string.IsNullOrEmpty(TunnelHost) ? string.Empty : $"{TunnelHost}:{TunnelPort}";
}

public sealed class WafDecision
{
    public static WafDecision Allow { get; } = new() { Severity = WafSeverity.Allow, Score = 0 };

    public WafSeverity Severity { get; init; }

    public int Score { get; init; }

    public IReadOnlyList<string> MatchedRuleIds { get; init; } = [];

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> SuggestedBlockKeys { get; init; } = [];

    public string Summary =>
        Reasons.Count == 0 ? string.Empty : string.Join("; ", Reasons);
}

/// <summary>UI-facing alert raised after a Warn/Hide/Drop decision (marshalled by SessionService).</summary>
public sealed class WafAlert
{
    public required WafIngressEvent Event { get; init; }

    public required WafDecision Decision { get; init; }

    public DateTime UtcTimestamp { get; init; } = DateTime.UtcNow;
}

public interface ICnCNetIngressWaf
{
    bool IsEnabled { get; }

    WafDecision Evaluate(WafIngressEvent ingressEvent);

    bool IsBlocked(string blockKey);

    void Block(string blockKey, string? note = null);

    void Block(WafBlockEntry entry);

    void Unblock(string blockKey);

    IReadOnlyList<string> ListBlockedKeys();

    IReadOnlyList<WafBlockEntry> ListBlockedEntries();

    void ClearBlocklist();

    /// <summary>Block suggested keys plus same-body fingerprint from an alert decision.</summary>
    void BlockFromAlert(WafIngressEvent ingressEvent, WafDecision decision, string? note = null);

    WafStrategyPrefs StrategyPrefs { get; }

    IReadOnlyList<WafStrategyRow> ListStrategies();

    void SetStrategyMode(string strategyId, WafStrategyMode mode);

    /// <summary>
    /// Drop stale tunnel/template/rate-window state. Called from session hosted-game prune.
    /// </summary>
    void PruneEphemeralState(TimeSpan maxAge);

    event Action<WafAlert>? AlertRaised;
}
