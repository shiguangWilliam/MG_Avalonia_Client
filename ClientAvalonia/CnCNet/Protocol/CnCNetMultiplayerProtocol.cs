using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientAvalonia.CnCNet.Protocol;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>
/// CnCNet multiplayer CTCP parsing aligned with DXMainClient
/// (<c>CnCNetLobby</c>, <c>CnCNetGameLobby</c>).
/// </summary>
public static class CnCNetMultiplayerProtocol
{
    public const int MaxPlayerCount = 8;

    public const int HumanPlayerOptionsLength = 3;

    public const int AiPlayerOptionsLength = 2;

    public const int GameBroadcastFieldCount = 13;

    public const int LegacyGameBroadcastFieldCount = 11;

    /// <summary>DX <c>CnCNetLobby.GameBroadcastChannel_CTCPReceived</c> (strict 13 fields).</summary>
    public static bool TryParseGameBroadcast(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnel>? tunnels,
        string sourceGameId,
        out CnCNetHostedGameSummary? game,
        out string? rejectReason)
    {
        game = null;
        rejectReason = null;

        if (!ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal))
            return false;

        string[] parts = ctcpMessage[5..].Split(';');
        if (parts.Length != GameBroadcastFieldCount)
        {
            if (parts.Length == LegacyGameBroadcastFieldCount)
                return TryParseLegacyGameBroadcast(hostName, ctcpMessage, tunnels, sourceGameId, out game, out rejectReason);

            rejectReason = $"invalid field count ({parts.Length}, expected {GameBroadcastFieldCount})";
            return false;
        }

        string revision = parts[0];
        if (!revision.Equals(ProgramConstants.CNCNET_PROTOCOL_REVISION, StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = $"unsupported protocol {revision}";
            return false;
        }

        if (parts[5].Length < CnCNetGameFlags.ExpectedLength)
        {
            rejectReason = "invalid flags field";
            return false;
        }

        bool locked = Conversions.BooleanFromString(parts[5].Substring(0, 1), defaultValue: true);
        bool passworded = Conversions.BooleanFromString(parts[5].Substring(1, 1), defaultValue: false);
        bool isClosed = Conversions.BooleanFromString(parts[5].Substring(2, 1), defaultValue: true);
        bool isLoadedGame = Conversions.BooleanFromString(parts[5].Substring(3, 1), defaultValue: false);
        bool isLadder = Conversions.BooleanFromString(parts[5].Substring(4, 1), defaultValue: false);

        if (!CnCNetPortValidator.TryParseEndpoint(parts[9], out string tunnelAddress, out ushort tunnelPort))
        {
            rejectReason = "invalid tunnel address";
            return false;
        }

        if (tunnels != null)
        {
            if (tunnels.Count == 0)
            {
                rejectReason = "no available tunnels";
                return false;
            }

            bool tunnelOk = tunnels.Any(t =>
                t.Address.Equals(tunnelAddress, StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);
            if (!tunnelOk)
            {
                rejectReason = $"tunnel {tunnelAddress}:{tunnelPort} unavailable";
                return false;
            }
        }

        string[] players = parts[6].Split(',', StringSplitOptions.RemoveEmptyEntries);
        string localGameId = ClientConfiguration.Instance.LocalGame;
        bool incompatible = !string.IsNullOrWhiteSpace(sourceGameId)
            && sourceGameId.Equals(localGameId, StringComparison.OrdinalIgnoreCase)
            && !parts[1].Equals(ProgramConstants.GAME_VERSION, StringComparison.OrdinalIgnoreCase);

        bool listingLocked = locked || (isLoadedGame && !players.Contains(ProgramConstants.PLAYERNAME));

        game = new CnCNetHostedGameSummary
        {
            HostName = hostName,
            RoomName = parts[4],
            ChannelName = parts[3],
            Revision = revision,
            MaxPlayers = Conversions.IntFromString(parts[2], 0),
            PlayerCount = players.Length,
            Players = players,
            IsClosed = isClosed,
            Locked = listingLocked,
            Passworded = passworded,
            IsLoadedGame = isLoadedGame,
            IsLadder = isLadder,
            GameVersion = parts[1],
            Incompatible = incompatible,
            TunnelAddress = tunnelAddress,
            TunnelPort = tunnelPort,
            MapName = parts[7],
            GameMode = parts[8],
            LoadedGameId = parts[10],
            SkillLevel = Conversions.IntFromString(parts[11], 0),
            MapHash = parts[12],
            SourceGameId = sourceGameId,
            LastRefreshUtc = DateTime.UtcNow,
        };

        return true;
    }

    /// <summary>R10 legacy 11-field GAME broadcasts.</summary>
    public static bool TryParseLegacyGameBroadcast(
        string hostName,
        string ctcpMessage,
        IReadOnlyList<CnCNetTunnel>? tunnels,
        string sourceGameId,
        out CnCNetHostedGameSummary? game,
        out string? rejectReason)
    {
        game = null;
        rejectReason = null;

        if (!ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal))
            return false;

        string[] parts = ctcpMessage[5..].Split(';');
        if (parts.Length != LegacyGameBroadcastFieldCount)
        {
            rejectReason = $"invalid legacy field count ({parts.Length})";
            return false;
        }

        string revision = parts[0];
        if (!revision.Equals(ProgramConstants.CNCNET_PROTOCOL_REVISION, StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = $"unsupported protocol {revision}";
            return false;
        }

        string flags = parts[5];
        if (flags.Length < CnCNetGameFlags.ExpectedLength)
            flags = flags.PadRight(CnCNetGameFlags.ExpectedLength, '0');

        bool locked = CnCNetGameFlags.ParseLocked(flags);
        bool passworded = CnCNetGameFlags.ParsePassworded(flags);
        bool isClosed = CnCNetGameFlags.ParseClosed(flags);
        bool isLoadedGame = CnCNetGameFlags.ParseLoadedGame(flags);
        bool isLadder = CnCNetGameFlags.ParseLadder(flags);

        if (!CnCNetPortValidator.TryParseEndpoint(parts[9], out string tunnelAddress, out ushort tunnelPort))
        {
            rejectReason = "invalid tunnel address";
            return false;
        }

        if (tunnels != null)
        {
            if (tunnels.Count == 0)
            {
                rejectReason = "no available tunnels";
                return false;
            }

            bool tunnelOk = tunnels.Any(t =>
                t.Address.Equals(tunnelAddress, StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);
            if (!tunnelOk)
            {
                rejectReason = $"tunnel {tunnelAddress}:{tunnelPort} unavailable";
                return false;
            }
        }

        string[] players = parts[6].Split(',', StringSplitOptions.RemoveEmptyEntries);
        string localGameId = ClientConfiguration.Instance.LocalGame;
        bool incompatible = !string.IsNullOrWhiteSpace(sourceGameId)
            && sourceGameId.Equals(localGameId, StringComparison.OrdinalIgnoreCase)
            && !parts[1].Equals(ProgramConstants.GAME_VERSION, StringComparison.OrdinalIgnoreCase);

        bool listingLocked = locked || (isLoadedGame && !players.Contains(ProgramConstants.PLAYERNAME));

        game = new CnCNetHostedGameSummary
        {
            HostName = hostName,
            RoomName = parts[4],
            ChannelName = parts[3],
            Revision = revision,
            MaxPlayers = Conversions.IntFromString(parts[2], 0),
            PlayerCount = players.Length,
            Players = players,
            IsClosed = isClosed,
            Locked = listingLocked,
            Passworded = passworded,
            IsLoadedGame = isLoadedGame,
            IsLadder = isLadder,
            GameVersion = parts[1],
            Incompatible = incompatible,
            TunnelAddress = tunnelAddress,
            TunnelPort = tunnelPort,
            MapName = parts[7],
            GameMode = parts[8],
            LoadedGameId = parts[10],
            SkillLevel = 0,
            MapHash = string.Empty,
            SourceGameId = sourceGameId,
            LastRefreshUtc = DateTime.UtcNow,
        };

        return true;
    }

    /// <summary>DX <c>CnCNetGameLobby.NonHostLaunchGame</c>.</summary>
    public static bool TryParseStartCommand(
        string payload,
        string localPlayerName,
        IReadOnlyList<CnCNetGameRoomPlayer> knownPlayers,
        IReadOnlyList<CnCNetTunnel> tunnels,
        CnCNetTunnel? roomTunnel,
        out CnCNetStartParseResult result,
        out string? error)
    {
        result = default;
        error = null;

        string[] parts = payload.Split(';');
        if (parts.Length < 1)
        {
            error = "START message is empty.";
            return false;
        }

        int uniqueGameId = Conversions.IntFromString(parts[0], -1);
        if (uniqueGameId < 0)
        {
            error = "START game id is invalid.";
            Logger.Log($"CnCNet START: invalid game id in payload: {payload}");
            return false;
        }

        var playerPorts = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        CnCNetTunnel? matchedTunnel = null;
        ushort localPort = CnCNetPortValidator.UnsetPort;
        bool localPlayerSeen = false;

        for (int i = 1; i < parts.Length; i += 2)
        {
            if (parts.Length <= i + 1)
            {
                error = "START player/port pair is incomplete.";
                return false;
            }

            string playerName = parts[i];
            if (!TryParseStartAddressPort(parts[i + 1], out string tunnelAddress, out ushort port))
            {
                error = $"START port for {playerName} is invalid.";
                return false;
            }

            if (IsLocalStartPlayer(playerName, localPlayerName))
            {
                localPlayerSeen = true;
                matchedTunnel = ResolveTunnelForStart(tunnelAddress, tunnels, roomTunnel);
                if (matchedTunnel == null)
                {
                    error = $"Failed to match tunnel address: {tunnelAddress}";
                    Logger.Log($"CnCNet START: failed to match tunnel address {tunnelAddress} (payload: {payload})");
                    return false;
                }

                localPort = port;
            }

            CnCNetGameRoomPlayer? known = knownPlayers.FirstOrDefault(p =>
                p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (known == null)
            {
                error = $"START references unknown player {playerName}.";
                return false;
            }

            playerPorts[playerName] = port;
        }

        if (!localPlayerSeen)
        {
            error = "START message does not include the local player.";
            return false;
        }

        result = new CnCNetStartParseResult
        {
            UniqueGameId = uniqueGameId,
            LocalPlayerPort = localPort,
            PlayerPorts = playerPorts,
            MatchedTunnel = matchedTunnel,
        };
        return true;
    }

    /// <summary>
    /// DX loading lobby sends <c>0.0.0.0:port</c> (port only); regular lobby sends tunnel IP.
    /// </summary>
    private static bool IsPlaceholderStartAddress(string address)
        => string.IsNullOrWhiteSpace(address)
           || address.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalStartPlayer(string playerName, string localPlayerName)
    {
        if (playerName.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase))
            return true;

        return playerName.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase)
               && localPlayerName.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>DX <c>NonHostLaunchGame</c>: port via <c>int.TryParse</c>; address may be <c>0.0.0.0</c>.</summary>
    private static bool TryParseStartAddressPort(string field, out string address, out ushort port)
    {
        address = string.Empty;
        port = CnCNetPortValidator.UnsetPort;

        int colon = field.LastIndexOf(':');
        if (colon <= 0 || colon >= field.Length - 1)
            return false;

        address = field[..colon].Trim();
        return CnCNetPortValidator.TryParse(field[(colon + 1)..], out port);
    }

    private static CnCNetTunnel? ResolveTunnelForStart(
        string startAddress,
        IReadOnlyList<CnCNetTunnel> tunnels,
        CnCNetTunnel? roomTunnel)
    {
        if (!IsPlaceholderStartAddress(startAddress))
        {
            CnCNetTunnel? listed = tunnels.FirstOrDefault(t =>
                t.Address.Equals(startAddress, StringComparison.OrdinalIgnoreCase));
            if (listed != null)
                return listed;

            listed = CnCNetTunnelListLoader.Load().FirstOrDefault(t =>
                t.Address.Equals(startAddress, StringComparison.OrdinalIgnoreCase));
            if (listed != null)
                return listed;
        }

        if (roomTunnel != null && !IsPlaceholderStartAddress(roomTunnel.Address))
        {
            Logger.Log(
                $"CnCNet START: using room tunnel {roomTunnel.Address}:{roomTunnel.Port} (START listed {startAddress}).");
            return roomTunnel;
        }

        return null;
    }

    /// <summary>DX <c>CnCNetGameLobby.ApplyPlayerOptions</c>.</summary>
    public static bool TryParsePlayerOptions(
        string message,
        IReadOnlySet<string> channelUsers,
        int maxSideIndex,
        int maxColorIndex,
        out List<CnCNetGameRoomPlayer> players,
        out string? error)
    {
        players = [];
        error = null;

        string[] parts = message.Split(';');
        for (int i = 0; i < parts.Length;)
        {
            if (string.IsNullOrEmpty(parts[i]))
            {
                i++;
                continue;
            }

            string nameOrLevel = parts[i];
            int converted = Conversions.IntFromString(nameOrLevel, -1);
            bool isAi = converted > -1;

            if (parts.Length <= i + 1)
            {
                error = "PO message truncated at player options.";
                return false;
            }

            int playerOptions = Conversions.IntFromString(parts[i + 1], -1);
            if (playerOptions == -1)
            {
                error = "PO packed options are invalid.";
                return false;
            }

            byte[] byteArray = BitConverter.GetBytes(playerOptions);
            int team = byteArray[0];
            int start = byteArray[1];
            int color = byteArray[2];
            int side = byteArray[3];

            if (side < 0 || side > maxSideIndex)
            {
                error = $"PO side {side} out of range.";
                return false;
            }

            if (color < 0 || color > maxColorIndex)
            {
                error = $"PO color {color} out of range.";
                return false;
            }

            if (start < 0 || start > MaxPlayerCount)
            {
                error = $"PO start {start} out of range.";
                return false;
            }

            if (team < 0 || team > 4)
            {
                error = $"PO team {team} out of range.";
                return false;
            }

            if (isAi)
            {
                players.Add(new CnCNetGameRoomPlayer
                {
                    IsAi = true,
                    AiLevel = converted,
                    Name = AiLevelToName(converted),
                    Ready = true,
                    TeamId = team,
                    StartingLocation = start,
                    ColorId = color,
                    SideId = side,
                });
                i += AiPlayerOptionsLength;
                continue;
            }

            if (!channelUsers.Contains(nameOrLevel))
            {
                i += HumanPlayerOptionsLength;
                continue;
            }

            if (parts.Length <= i + 2)
            {
                error = "PO ready status missing.";
                return false;
            }

            int readyStatus = Conversions.IntFromString(parts[i + 2], -1);
            if (readyStatus == -1)
            {
                error = "PO ready status is invalid.";
                return false;
            }

            players.Add(new CnCNetGameRoomPlayer
            {
                Name = nameOrLevel,
                Ready = readyStatus > 0,
                AutoReady = readyStatus > 1,
                TeamId = team,
                StartingLocation = start,
                ColorId = color,
                SideId = side,
            });

            i += HumanPlayerOptionsLength;
        }

        return true;
    }

    public static string BuildGameBroadcastPayload(
        CnCNetActiveGameRoom room,
        string flags,
        IReadOnlyList<string> playerNames,
        string mapName,
        string gameModeName,
        string mapSha1)
    {
        var sb = new System.Text.StringBuilder("GAME ");
        sb.Append(ProgramConstants.CNCNET_PROTOCOL_REVISION);
        sb.Append(';');
        sb.Append(ProgramConstants.GAME_VERSION);
        sb.Append(';');
        sb.Append(room.MaxPlayers);
        sb.Append(';');
        sb.Append(CnCNetIrcChannelNames.Preserve(room.ChannelName));
        sb.Append(';');
        sb.Append(room.RoomName);
        sb.Append(';');
        sb.Append(flags);
        sb.Append(';');
        sb.Append(string.Join(',', playerNames));
        sb.Append(';');
        sb.Append(mapName);
        sb.Append(';');
        sb.Append(gameModeName);
        sb.Append(';');
        sb.Append(room.Tunnel.Address);
        sb.Append(':');
        sb.Append(room.Tunnel.Port);
        sb.Append(';');
        sb.Append('0');
        sb.Append(';');
        sb.Append(room.SkillLevel);
        sb.Append(';');
        sb.Append(mapSha1);
        return sb.ToString();
    }

    private static string AiLevelToName(int aiLevel)
    {
        IReadOnlyList<string> names = ProgramConstants.AI_PLAYER_NAMES;
        if (aiLevel >= 0 && aiLevel < names.Count)
            return names[aiLevel];

        return names.Count > 0 ? names[0] : "AI";
    }
}

public readonly struct CnCNetStartParseResult
{
    public int UniqueGameId { get; init; }

    public ushort LocalPlayerPort { get; init; }

    public IReadOnlyDictionary<string, ushort> PlayerPorts { get; init; }

    public CnCNetTunnel? MatchedTunnel { get; init; }
}
