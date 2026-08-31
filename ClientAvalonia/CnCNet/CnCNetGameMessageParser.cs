using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.CnCNet.Waf;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>Parsed CTCP GAME broadcast (CnCNet lobby game list entry).</summary>
public sealed class CnCNetHostedGameSummary
{
    public required string HostName { get; init; }

    public required string RoomName { get; init; }

    public required string ChannelName { get; init; }

    public string Revision { get; init; } = string.Empty;

    /// <summary>Semicolon field count from the GAME CTCP (11 = R10, 13 = R13).</summary>
    public int FieldCount { get; init; }

    public int MaxPlayers { get; init; }

    public int PlayerCount { get; init; }

    public IReadOnlyList<string> Players { get; init; } = [];

    public bool IsClosed { get; init; }

    public bool Locked { get; init; }

    /// <summary>Join-side: parsed from GAME CTCP flags index 1 (DX <c>isCustomPassword</c>).</summary>
    public bool RequiresPassword { get; init; }

    public bool IsLoadedGame { get; init; }

    public bool IsLadder { get; init; }

    public string GameVersion { get; init; } = string.Empty;

    public string TunnelAddress { get; init; } = string.Empty;

    public ushort TunnelPort { get; init; }

    public string MapName { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;

    public string MapHash { get; init; } = string.Empty;

    public string LoadedGameId { get; init; } = string.Empty;

    public int SkillLevel { get; init; }

    public bool Incompatible { get; init; }

    /// <summary>CnCNet game id for the broadcast channel this entry came from (XNA CnCNetGame.InternalName).</summary>
    public string SourceGameId { get; init; } = string.Empty;

    public DateTime LastRefreshUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Ingress WAF risk (set after parse; default Allow).</summary>
    public WafSeverity RiskLevel { get; set; } = WafSeverity.Allow;

    public string RiskSummary { get; set; } = string.Empty;

    /// <summary>Resolved tunnel RTT for lobby display (-1 = unknown). Not part of GAME CTCP.</summary>
    public int TunnelPingInMs { get; set; } = -1;

    public string DisplayLine
    {
        get
        {
            string baseLine = Locked
                ? $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName} [locked]"
                : $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName}";
            string ping = TunnelPingInMs >= 0
                ? $" · {TunnelPingInMs} ms"
                : string.Empty;
            string line = baseLine + ping;
            return RiskLevel >= WafSeverity.Warn
                ? $"[风险] {line}"
                : line;
        }
    }
}

/// <summary>Parses inbound GAME CTCP via <see cref="CnCNetMultiplayerProtocol"/> (DXMain-aligned).</summary>
public static class CnCNetGameMessageParser
{
    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnel>? tunnels,
        out string? rejectReason,
        string sourceGameId = "")
    {
        rejectReason = null;

        if (CnCNetMultiplayerProtocol.TryParseGameBroadcast(
                hostName,
                ctcpMessage,
                tunnels,
                sourceGameId,
                out CnCNetHostedGameSummary? game,
                out rejectReason))
            return game;

        if (rejectReason != null)
            Logger.Log($"CnCNetGameMessageParser: ignoring GAME from {hostName}: {rejectReason}.");

        return null;
    }

    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnel>? tunnels = null)
        => TryParse(hostName, ctcpMessage, tunnels, out _);
}
