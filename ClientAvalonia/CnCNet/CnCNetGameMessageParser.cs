using ClientAvalonia.CnCNet.Protocol;
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

    public string DisplayLine => Locked
        ? $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName} [locked]"
        : $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName}";
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
