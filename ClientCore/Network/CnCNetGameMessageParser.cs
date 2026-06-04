using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.Tools;

namespace ClientCore.Network;

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

    public string DisplayLine => Locked
        ? $"{RoomName} ({PlayerCount}/{MaxPlayers}) — {HostName} [locked]"
        : $"{RoomName} ({PlayerCount}/{MaxPlayers}) — {HostName}";
}

public static class CnCNetGameMessageParser
{
    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnelEntry>? tunnels,
        out string? rejectReason)
    {
        rejectReason = null;

        if (!ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal))
            return null;

        string[] parts = ctcpMessage[5..].Split(';');
        if (parts.Length != 13)
        {
            rejectReason = $"invalid field count ({parts.Length}/13)";
            Logger.Log($"CnCNetGameMessageParser: ignoring GAME from {hostName}: {rejectReason}.");
            return null;
        }

        if (parts[0] != ProgramConstants.CNCNET_PROTOCOL_REVISION)
        {
            rejectReason = $"protocol {parts[0]} != {ProgramConstants.CNCNET_PROTOCOL_REVISION}";
            return null;
        }

        string flags = parts[5];
        if (flags.Length < 3)
        {
            rejectReason = "invalid flags field";
            return null;
        }

        bool locked = flags.Length > 0 && flags[0] == '1';
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
            TunnelAddress = tunnelAddress,
            TunnelPort = tunnelPort,
            MapName = parts[7],
            GameMode = parts[8],
            SkillLevel = int.TryParse(parts[11], out int skill) ? skill : 0,
        };
    }

    public static CnCNetHostedGameSummary? TryParse(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnelEntry>? tunnels = null)
        => TryParse(hostName, ctcpMessage, tunnels, out _);
}
