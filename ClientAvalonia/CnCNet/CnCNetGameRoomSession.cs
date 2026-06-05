using ClientCore;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>In-room CnCNet game lobby logic (XNA CnCNetGameLobby CTCP subset).</summary>
public sealed class CnCNetGameRoomSession
{
    private readonly object _sync = new();
    private readonly List<CnCNetGameRoomPlayer> _players = [];
    private readonly HashSet<string> _channelUsers = new(StringComparer.OrdinalIgnoreCase);

    private CnCNetIrcConnection? _connection;
    private CnCNetGameBroadcastService? _gameBroadcast;
    private CnCNetGameChannels? _channels;
    private bool _locked;
    private int _uniqueGameId;
    private string _localNick = ProgramConstants.PLAYERNAME;

    public CnCNetGameRoomSession(CnCNetActiveGameRoom room)
    {
        Room = room;
        HostName = room.IsHost ? ProgramConstants.PLAYERNAME : room.HostName;
    }

    public CnCNetActiveGameRoom Room { get; }

    public bool IsHost => Room.IsHost;

    public string HostName { get; private set; }

    public bool Locked => _locked;

    public int UniqueGameId => _uniqueGameId;

    public IReadOnlyList<CnCNetGameRoomPlayer> Players
    {
        get
        {
            lock (_sync)
                return _players.ToList();
        }
    }

    public event Action? StateChanged;

    public event Action<string>? NoticeLogged;

    public event Action<CnCNetStartGameInfo>? GameStarting;

    public void Attach(
        CnCNetIrcConnection connection,
        CnCNetGameBroadcastService gameBroadcast,
        CnCNetGameChannels? channels)
    {
        _connection = connection;
        _gameBroadcast = gameBroadcast;
        _channels = channels;
        _localNick = connection.CurrentNick;
        if (IsHost)
        {
            HostName = _localNick;
            lock (_sync)
                EnsureHostPlayerLocked();
        }
    }

    public void OnLocalJoined()
    {
        lock (_sync)
        {
            _uniqueGameId = Random.Shared.Next(1_000_000, int.MaxValue);
            _channelUsers.Add(_localNick);

            if (IsHost)
            {
                HostName = _localNick;
                EnsureHostPlayerLocked();
                _gameBroadcast?.StartHost(_connection!, _channels!, Room);
                BroadcastPlayerOptionsLocked();
            }
            else
            {
                SendCtcp("FHSH 0");
            }
        }

        LogNotice(IsHost ? $"Hosting \"{Room.RoomName}\"." : $"Joined \"{Room.RoomName}\".");
        StateChanged?.Invoke();
    }

    public void OnUserJoined(string channel, string user)
    {
        if (!IsGameChannel(channel))
            return;

        string name = StripPrefixes(user);
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_sync)
        {
            _channelUsers.Add(name);

            if (!IsHost && string.IsNullOrEmpty(HostName))
            {
                // First non-local joiner on channel is treated as host nick until PO arrives.
            }

            if (IsHost)
            {
                AddOrRefreshHumanPlayerLocked(name, name.Equals(_localNick, StringComparison.OrdinalIgnoreCase));
                BroadcastPlayerOptionsLocked();
            }
        }

        StateChanged?.Invoke();
    }

    public void OnUserLeft(string channel, string user)
    {
        if (!IsGameChannel(channel))
            return;

        string name = StripPrefixes(user);
        lock (_sync)
        {
            _channelUsers.Remove(name);
            _players.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (IsHost)
                BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    public void OnChannelCtcp(string channel, string sender, string ctcp)
    {
        if (!IsGameChannel(channel))
            return;

        if (ctcp.StartsWith("PO ", StringComparison.Ordinal))
        {
            ApplyPlayerOptions(sender, ctcp[3..]);
            return;
        }

        if (ctcp.StartsWith("GO ", StringComparison.Ordinal))
        {
            ApplyGameOptions(sender, ctcp[3..]);
            return;
        }

        if (ctcp.StartsWith("START ", StringComparison.Ordinal))
        {
            HandleStart(sender, ctcp[6..]);
            return;
        }

        if (ctcp.StartsWith("R ", StringComparison.Ordinal) && IsHost)
        {
            HandleReadyRequest(sender, ctcp[2..].Trim());
            return;
        }

        if (ctcp.Equals("STRTD", StringComparison.Ordinal))
            LogNotice($"{sender} started the game.");
    }

    public void SetLocked(bool locked)
    {
        lock (_sync)
        {
            _locked = locked;
            if (IsHost)
                BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    public void SetLocalReady(bool ready)
    {
        if (IsHost)
            return;

        SendCtcp($"R {(ready ? 1 : 0)}");
        lock (_sync)
        {
            CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
            if (local != null)
                local.Ready = ready;
        }

        StateChanged?.Invoke();
    }

    public void UpdateHostListing(string mapName, string gameModeName, string mapSha1)
    {
        if (!IsHost)
            return;

        var names = Players.Select(p => p.Name).ToList();
        _gameBroadcast?.UpdateListing(mapName, gameModeName, mapSha1, names, _locked, closed: false);

        lock (_sync)
            BroadcastGameOptionsLocked(mapName, gameModeName, mapSha1);
    }

    public void KickPlayer(string playerName)
    {
        if (!IsHost || _connection == null || string.IsNullOrWhiteSpace(playerName))
            return;

        if (playerName.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
            return;

        LogNotice($"Kicking {playerName} from the game...");
        _connection.KickFromChannel(Room.ChannelName, playerName);
    }

    public void UpdateHumanFromSlot(LobbyPlayerSlot slot)
    {
        if (!IsHost)
            return;

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(slot.Name);
            if (player == null)
                return;

            player.SideId = slot.SideIndex;
            player.ColorId = slot.ColorIndex;
            player.TeamId = slot.TeamIndex;
            player.StartingLocation = slot.StartIndex + 1;
        }
    }

    public void SyncPlayersFromLobby(LobbyPlayerState state, string hostName)
    {
        if (!IsHost)
            return;

        lock (_sync)
        {
            List<CnCNetGameRoomPlayer> entries = MultiplayerSlotLayout.BuildPoListFromState(state, hostName);
            var readyByName = _players.Where(p => !p.IsAi)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            _players.Clear();
            foreach (CnCNetGameRoomPlayer entry in entries)
            {
                if (!entry.IsAi && readyByName.TryGetValue(entry.Name, out CnCNetGameRoomPlayer? existing))
                    entry.Ready = existing.Ready;

                if (entry.IsHost || (!entry.IsAi && entry.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase) && IsHost))
                    entry.Ready = true;

                _players.Add(entry);
            }

            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    public bool TryHostLaunch(out string message)
    {
        message = string.Empty;
        if (!IsHost)
        {
            message = "Only the host can launch.";
            return false;
        }

        if (_connection == null || !_connection.IsConnected)
        {
            message = "IRC not connected.";
            return false;
        }

        lock (_sync)
        {
            var humans = _players.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (humans.Count == 0)
                EnsureHostPlayerLocked();

            humans = _players.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (humans.Count == 0)
            {
                message = "No players in the room.";
                return false;
            }

            if (humans.Count > 1)
            {
                IReadOnlyList<int> ports = Room.Tunnel.RequestPlayerPorts(humans.Count);
                if (ports.Count < humans.Count)
                {
                    message = "Could not contact the CnCNet tunnel server. Try another tunnel.";
                    return false;
                }

                var sb = new StringBuilder("START ");
                sb.Append(_uniqueGameId);
                for (int i = 0; i < humans.Count; i++)
                {
                    humans[i].Port = ports[i];
                    sb.Append(';');
                    sb.Append(humans[i].Name);
                    sb.Append(';');
                    sb.Append(Room.Tunnel.Address);
                    sb.Append(':');
                    sb.Append(ports[i]);
                }

                SendCtcp(sb.ToString());
            }

            int localPort = FindPlayerLocked(_localNick)?.Port ?? 0;
            GameStarting?.Invoke(new CnCNetStartGameInfo
            {
                UniqueGameId = _uniqueGameId,
                Tunnel = Room.Tunnel,
                LocalPlayerPort = localPort,
                IsHost = true,
            });
        }

        message = "Starting game...";
        return true;
    }

    public void Leave()
    {
        if (_connection == null)
            return;

        string channel = NormalizeChannel(Room.ChannelName);
        _gameBroadcast?.Stop();
        _connection.PartChannel(channel);
        _connection = null;
    }

    private void HandleStart(string sender, string payload)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        string[] parts = payload.Split(';');
        if (parts.Length < 1 || !int.TryParse(parts[0], out int gameId) || gameId < 0)
            return;

        int localPort = 0;
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            string playerName = parts[i];
            string[] ipPort = parts[i + 1].Split(':');
            if (ipPort.Length < 2 || !int.TryParse(ipPort[1], out int port))
                return;

            if (playerName.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                localPort = port;
        }

        if (localPort <= 0)
        {
            LogNotice("START received but local port was not assigned.");
            return;
        }

        _uniqueGameId = gameId;
        GameStarting?.Invoke(new CnCNetStartGameInfo
        {
            UniqueGameId = gameId,
            Tunnel = Room.Tunnel,
            LocalPlayerPort = localPort,
            IsHost = false,
        });
    }

    private void HandleReadyRequest(string sender, string payload)
    {
        if (!int.TryParse(payload, out int readyState))
            return;

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(sender);
            if (player == null)
                return;

            player.Ready = readyState > 0;
            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    private void ApplyPlayerOptions(string sender, string message)
    {
        if (IsHost && !sender.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsHost && !string.IsNullOrEmpty(HostName) && !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsHost && string.IsNullOrEmpty(HostName))
            HostName = sender;

        string[] parts = message.Split(';', StringSplitOptions.RemoveEmptyEntries);
        lock (_sync)
        {
            _players.Clear();
            for (int i = 0; i < parts.Length;)
            {
                string nameOrLevel = parts[i++];
                if (i >= parts.Length)
                    break;

                if (!int.TryParse(parts[i++], out int packed))
                    break;

                UnpackOptions(packed, out int team, out int start, out int color, out int side);

                if (int.TryParse(nameOrLevel, out int aiLevel) && aiLevel >= 0)
                {
                    _players.Add(new CnCNetGameRoomPlayer
                    {
                        IsAi = true,
                        AiLevel = aiLevel,
                        Name = AiLevelToName(aiLevel),
                        Ready = true,
                        TeamId = team,
                        StartingLocation = start,
                        ColorId = color,
                        SideId = side,
                    });
                    continue;
                }

                bool ready = false;
                if (i < parts.Length && int.TryParse(parts[i], out int readyState))
                {
                    ready = readyState > 0;
                    i++;
                }

                _players.Add(new CnCNetGameRoomPlayer
                {
                    Name = nameOrLevel,
                    IsHost = nameOrLevel.Equals(HostName, StringComparison.OrdinalIgnoreCase),
                    Ready = ready,
                    TeamId = team,
                    StartingLocation = start,
                    ColorId = color,
                    SideId = side,
                });
            }
        }

        StateChanged?.Invoke();
    }

    private static string AiLevelToName(int aiLevel)
    {
        IReadOnlyList<string> names = ProgramConstants.AI_PLAYER_NAMES;
        if (aiLevel >= 0 && aiLevel < names.Count)
            return names[aiLevel];

        return names.Count > 0 ? names[0] : "AI";
    }

    private void ApplyGameOptions(string sender, string message)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        // Minimal GO handling: last fields are map sha1, mode, map name in XNA order.
        // Joiners log receipt; map selection stays local until full GO port.
        LogNotice("Game options updated by host.");
        StateChanged?.Invoke();
    }

    private void BroadcastPlayerOptionsLocked()
    {
        if (!IsHost || _connection == null)
            return;

        if (_players.Count == 0)
            EnsureHostPlayerLocked();

        var sb = new StringBuilder("PO ");
        foreach (CnCNetGameRoomPlayer player in _players)
        {
            if (player.IsAi)
                sb.Append(player.AiLevel);
            else
                sb.Append(player.Name);

            sb.Append(';');
            sb.Append(PackOptions(player.TeamId, player.StartingLocation, player.ColorId, player.SideId));
            sb.Append(';');

            if (!player.IsAi)
            {
                sb.Append(player.Ready ? 1 : 0);
                sb.Append(';');
            }
        }

        SendCtcp(sb.ToString());
    }

    private void BroadcastGameOptionsLocked(string mapName, string gameModeName, string mapSha1)
    {
        if (!IsHost || _connection == null)
            return;

        int seed = Random.Shared.Next();
        var sb = new StringBuilder("GO ");
        sb.Append('0').Append(';');
        sb.Append('0').Append(';');
        sb.Append('0').Append(';');
        sb.Append('1').Append(';');
        sb.Append(mapSha1).Append(';');
        sb.Append(gameModeName).Append(';');
        sb.Append('6').Append(';');
        sb.Append('0').Append(';');
        sb.Append('2').Append(';');
        sb.Append(seed).Append(';');
        sb.Append('0').Append(';');
        sb.Append(mapName);
        SendCtcp(sb.ToString());
    }

    private void EnsureHostPlayerLocked()
    {
        if (_players.Any(p => p.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase)))
            return;

        _players.Insert(0, new CnCNetGameRoomPlayer
        {
            Name = _localNick,
            IsHost = true,
            Ready = true,
        });
    }

    private void AddOrRefreshHumanPlayerLocked(string name, bool isLocal)
    {
        CnCNetGameRoomPlayer? existing = FindPlayerLocked(name);
        if (existing != null)
            return;

        _players.Add(new CnCNetGameRoomPlayer
        {
            Name = name,
            IsHost = isLocal && IsHost,
            Ready = isLocal && IsHost,
        });
    }

    private CnCNetGameRoomPlayer? FindPlayerLocked(string name)
        => _players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private bool IsGameChannel(string channel)
        => NormalizeChannel(channel).Equals(NormalizeChannel(Room.ChannelName), StringComparison.OrdinalIgnoreCase);

    private void SendCtcp(string ctcpMessage)
    {
        if (_connection == null)
            return;

        _connection.SendCtcpNotice(NormalizeChannel(Room.ChannelName), ctcpMessage);
    }

    private void LogNotice(string message) => NoticeLogged?.Invoke(message);

    private static string NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        string normalized = channel.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        return normalized.ToLowerInvariant();
    }

    private static string StripPrefixes(string user)
    {
        int index = 0;
        while (index < user.Length && (user[index] == '@' || user[index] == '+' || user[index] == '%' || user[index] == '~' || user[index] == '&'))
            index++;

        return user[index..];
    }

    private static int PackOptions(int team, int start, int color, int side)
    {
        byte[] bytes = [(byte)team, (byte)start, (byte)color, (byte)side];
        return BitConverter.ToInt32(bytes, 0);
    }

    private static void UnpackOptions(int packed, out int team, out int start, out int color, out int side)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        team = bytes[0];
        start = bytes[1];
        color = bytes[2];
        side = bytes[3];
    }
}
