using ClientCore;
using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Parsed CTCP GAME broadcast (CnCNet lobby game list entry).</summary>
public sealed class CnCNetHostedGameSummary
{
    public required string HostName { get; init; }

    public required string RoomName { get; init; }

    public required string ChannelName { get; init; }

    public int MaxPlayers { get; init; }

    public int PlayerCount { get; init; }

    public bool IsClosed { get; init; }

    public bool Locked { get; init; }

    public bool CustomPassword { get; init; }

    public bool IsLoadedGame { get; init; }

    public string GameVersion { get; init; } = string.Empty;

    public string TunnelAddress { get; init; } = string.Empty;

    public int TunnelPort { get; init; }

    public string MapName { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;

    public int SkillLevel { get; init; }

    public bool Incompatible { get; init; }

    /// <summary>CnCNet game id for the broadcast channel this entry came from (XNA CnCNetGame.InternalName).</summary>
    public string SourceGameId { get; init; } = string.Empty;

    public DateTime LastRefreshUtc { get; init; } = DateTime.UtcNow;

    public string DisplayLine => Locked
        ? $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName} [locked]"
        : $"{RoomName} ({PlayerCount}/{MaxPlayers}) - {HostName}";
}

public static class CnCNetGameMessageParser
{
    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnelEntry>? tunnels,
        out string? rejectReason,
        string sourceGameId = "")
    {
        rejectReason = null;

        if (!ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal))
            return null;

        string[] parts = ctcpMessage[5..].Split(';');
        if (parts.Length < 11)
        {
            rejectReason = $"invalid field count ({parts.Length}, minimum 11)";
            Logger.Log($"CnCNetGameMessageParser: ignoring GAME from {hostName}: {rejectReason}.");
            return null;
        }

        string revision = parts[0];
        if (!IsSupportedRevision(revision))
        {
            rejectReason = $"unsupported protocol {revision} (configured {ProgramConstants.CNCNET_PROTOCOL_REVISION})";
            Logger.Log($"CnCNetGameMessageParser: ignoring GAME from {hostName}: {rejectReason}.");
            return null;
        }

        int expectedFields = IsLegacyRevision(revision) ? 11 : 13;
        if (parts.Length != expectedFields)
        {
            rejectReason = $"field count {parts.Length} for {revision} (expected {expectedFields})";
            Logger.Log($"CnCNetGameMessageParser: ignoring GAME from {hostName}: {rejectReason}.");
            return null;
        }

        string flags = parts[5];
        if (flags.Length < 3)
        {
            rejectReason = "invalid flags field";
            return null;
        }

        bool locked = flags[0] == '1';
        bool customPassword = flags.Length > 1 && flags[1] == '1';
        bool isClosed = flags.Length > 2 && flags[2] == '1';
        bool isLoadedGame = flags.Length > 3 && flags[3] == '1';

        string[] tunnelParts = parts[9].Split(':');
        if (tunnelParts.Length < 2 || !int.TryParse(tunnelParts[1], out int tunnelPort))
        {
            rejectReason = "invalid tunnel address";
            return null;
        }

        string tunnelAddress = tunnelParts[0];
        if (tunnels != null && tunnels.Count > 0)
        {
            bool tunnelOk = tunnels.Any(t =>
                t.Address.Equals(tunnelAddress, StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);
            if (!tunnelOk)
            {
                rejectReason = $"tunnel {tunnelAddress}:{tunnelPort} unavailable";
                Logger.Log($"CnCNetGameMessageParser: {rejectReason} (from {hostName}).");
                return null;
            }
        }

        string[] players = parts[6].Split(',', StringSplitOptions.RemoveEmptyEntries);
        int skillLevel = 0;
        if (parts.Length == 13 && int.TryParse(parts[11], out int parsedSkill))
            skillLevel = parsedSkill;

        string localGameId = ClientConfiguration.Instance.LocalGame;
        bool incompatible = !string.IsNullOrWhiteSpace(sourceGameId)
            && sourceGameId.Equals(localGameId, StringComparison.OrdinalIgnoreCase)
            && !parts[1].Equals(ProgramConstants.GAME_VERSION, StringComparison.OrdinalIgnoreCase);

        return new CnCNetHostedGameSummary
        {
            HostName = hostName,
            RoomName = parts[4],
            ChannelName = parts[3],
            MaxPlayers = int.TryParse(parts[2], out int parsedMax) ? parsedMax : 0,
            PlayerCount = players.Length,
            IsClosed = isClosed,
            Locked = locked,
            CustomPassword = customPassword,
            IsLoadedGame = isLoadedGame,
            GameVersion = parts[1],
            Incompatible = incompatible,
            TunnelAddress = tunnelAddress,
            TunnelPort = tunnelPort,
            MapName = parts[7],
            GameMode = parts[8],
            SkillLevel = skillLevel,
            SourceGameId = sourceGameId,
            LastRefreshUtc = DateTime.UtcNow,
        };
    }

    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnelEntry>? tunnels = null)
        => TryParse(hostName, ctcpMessage, tunnels, out _);

    private static bool IsSupportedRevision(string revision)
        => revision.Equals(ProgramConstants.CNCNET_PROTOCOL_REVISION, StringComparison.OrdinalIgnoreCase);

    /// <summary>R10 legacy broadcasts omit skillLevel and mapSha1 (11 fields).</summary>
    public static bool IsLegacyRevision(string revision)
        => revision.Equals("R10", StringComparison.OrdinalIgnoreCase)
           || (ProgramConstants.UsesLegacyCnCNetGameBroadcast
               && revision.Equals(ProgramConstants.CNCNET_PROTOCOL_REVISION, StringComparison.OrdinalIgnoreCase));
}
