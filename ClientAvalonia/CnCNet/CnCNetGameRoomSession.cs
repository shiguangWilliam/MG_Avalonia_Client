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
    private int _randomSeed;
    private bool _removeStartingLocations;
    private bool _tunnelErrorMode;

    public bool TunnelErrorMode => _tunnelErrorMode;

    public Func<CnCNetGameOptionsState>? GameOptionsProvider { get; set; }

    public Action<CnCNetGameOptionsState>? GameOptionsReceiver { get; set; }

    public Func<(int CheckBoxCount, int DropDownCount)>? GameOptionsControlCounts { get; set; }

    public int RandomSeed => _randomSeed;

    public bool RemoveStartingLocations => _removeStartingLocations;

    public event Action? HostAbandoned;

    private string _gameFilesHash = string.Empty;

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
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();
        _gameFilesHash = fhc.GetCompleteHash();

        lock (_sync)
        {
            _uniqueGameId = Random.Shared.Next(1_000_000, int.MaxValue);
            _randomSeed = Random.Shared.Next();
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
                SendCtcp($"FHSH {_gameFilesHash}");
            }
        }

        LogNotice(IsHost ? $"Hosting \"{Room.RoomName}\"." : $"Joined \"{Room.RoomName}\".");
        if (!IsHost)
            BroadcastLocalTunnelPingLocked();

        StateChanged?.Invoke();
    }

    public void OnChannelUserList(IReadOnlyList<string> users)
    {
        if (!IsGameChannel(Room.ChannelName))
            return;

        lock (_sync)
        {
            foreach (string user in users)
            {
                string name = StripPrefixes(user);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                _channelUsers.Add(name);

                if (IsHost)
                    AddOrRefreshHumanPlayerLocked(name, name.Equals(_localNick, StringComparison.OrdinalIgnoreCase));
            }

            if (IsHost)
                BroadcastPlayerOptionsLocked();
        }

        if (!IsHost && !string.IsNullOrWhiteSpace(HostName))
        {
            bool hostPresent = users.Any(u =>
                StripPrefixes(u).Equals(HostName, StringComparison.OrdinalIgnoreCase));

            if (!hostPresent)
            {
                LogNotice("The game host has abandoned the game.");
                HostAbandoned?.Invoke();
                return;
            }
        }

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

                int humanCount = _players.Count(p => !p.IsAi);
                if (!name.Equals(_localNick, StringComparison.OrdinalIgnoreCase)
                    && humanCount >= Room.MaxPlayers
                    && !_locked)
                {
                    LogNotice("Player limit reached. The game room has been locked.");
                    SetLocked(true);
                }
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
            {
                BroadcastPlayerOptionsLocked();

                if (_locked && !ProgramConstants.IsInGame)
                    SetLocked(false);
            }
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

        if (ctcp.StartsWith("OR ", StringComparison.Ordinal) && IsHost)
        {
            ApplyOptionsRequest(sender, ctcp[3..].Trim());
            return;
        }

        if (ctcp.StartsWith("TNLPNG ", StringComparison.Ordinal))
        {
            HandleTunnelPing(sender, ctcp[7..].Trim());
            return;
        }

        if (ctcp.StartsWith("GSETTINGS ", StringComparison.Ordinal))
        {
            ApplyGameLobbySettings(sender, ctcp[10..]);
            return;
        }

        if (ctcp.StartsWith("FHSH ", StringComparison.Ordinal) && IsHost)
        {
            HandleFileHashNotification(sender, ctcp[5..].Trim());
            return;
        }

        if (ctcp.StartsWith("MM ", StringComparison.Ordinal))
        {
            HandleCheaterNotification(sender, ctcp[3..].Trim());
            return;
        }

        if (ctcp.Equals("CD", StringComparison.Ordinal))
        {
            LogNotice($"{sender} has modified game files during the client session. They are likely attempting to cheat!");
            return;
        }

        if (ctcp.StartsWith("CHTNL ", StringComparison.Ordinal))
        {
            HandleTunnelChange(sender, ctcp[6..].Trim());
            return;
        }

        if (ctcp.Equals("STRTD", StringComparison.Ordinal))
            LogNotice($"{sender} started the game.");
    }

    public void SetLocked(bool locked)
    {
        lock (_sync)
        {
            if (_locked == locked)
                return;

            _locked = locked;

            if (IsHost && _connection != null)
            {
                string channel = NormalizeChannel(Room.ChannelName);
                _connection.SendInstant($"MODE {channel} {(locked ? "+i" : "-i")}");
                BroadcastPlayerOptionsLocked();
            }
        }

        if (IsHost)
            LogNotice(locked ? "You've locked the game room." : "The game room has been unlocked.");

        StateChanged?.Invoke();
    }

    public void SetLocalReady(bool ready, bool autoReady = false)
    {
        if (IsHost)
            return;

        int readyState = autoReady ? 2 : ready ? 1 : 0;
        SendCtcp($"R {readyState}");
        lock (_sync)
        {
            CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
            if (local != null)
            {
                local.Ready = readyState > 0;
                local.AutoReady = autoReady;
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>Joiner sends side/color/start/team (XNA CnCNetGameLobby.RequestPlayerOptions / OR CTCP).</summary>
    public void RequestLocalPlayerOptions(LobbyPlayerSlot slot)
    {
        if (IsHost || _connection == null)
            return;

        int packed = PackOptionsRequest(slot.SideIndex, slot.ColorIndex, slot.StartIndex, slot.TeamIndex);
        SendCtcp($"OR {packed}");
    }

    public void UpdateHostListing(string mapName, string gameModeName, string mapSha1)
    {
        if (!IsHost)
            return;

        _gameBroadcastListingMapName = mapName;
        _gameBroadcastListingGameMode = gameModeName;
        _gameBroadcastListingMapSha1 = mapSha1;

        var names = GetHumanPlayerNamesLocked();
        _gameBroadcast?.UpdateListing(mapName, gameModeName, mapSha1, names, _locked, closed: false);
        BroadcastGameOptionsLocked();
    }

    public void UpdateGameLobbySettings(string roomName, int maxPlayers, int skillLevel, string? password)
    {
        if (!IsHost || _connection == null)
            return;

        int occupiedCount = _players.Count;
        if (maxPlayers < occupiedCount)
        {
            LogNotice($"Cannot reduce maximum players to {maxPlayers} with {occupiedCount} players currently in game.");
            return;
        }

        string oldRoomName = Room.RoomName;
        int oldMaxPlayers = Room.MaxPlayers;
        bool oldCustomPassword = Room.CustomPassword;
        Room.RoomName = roomName;
        Room.MaxPlayers = maxPlayers;
        Room.SkillLevel = skillLevel;

        if (password != null)
        {
            string actualPassword = string.IsNullOrEmpty(password)
                ? Utilities.CalculateSHA1ForString(Room.ChannelName)[..10]
                : password;

            Room.CustomPassword = !string.IsNullOrEmpty(password);
            Room.Password = actualPassword;
            _connection.SendInstant($"MODE {NormalizeChannel(Room.ChannelName)} +k {actualPassword}");
        }

        BroadcastGameLobbySettings();

        if (!oldRoomName.Equals(roomName, StringComparison.Ordinal))
            LogNotice($"Game room name changed from \"{oldRoomName}\" to \"{roomName}\".");

        if (oldMaxPlayers != maxPlayers)
            LogNotice($"Maximum players changed to {maxPlayers}.");

        if (password != null)
        {
            if (string.IsNullOrEmpty(password))
                LogNotice("Password removed from the game.");
            else if (!oldCustomPassword)
                LogNotice("Password added to the game.");
            else
                LogNotice("Password changed.");
        }

        BroadcastPlayerOptionsLocked();
        _gameBroadcast?.UpdateListing(
            _gameBroadcastListingMapName,
            _gameBroadcastListingGameMode,
            _gameBroadcastListingMapSha1,
            GetHumanPlayerNamesLocked(),
            _locked,
            closed: false);

        StateChanged?.Invoke();
    }

    private string _gameBroadcastListingMapName = string.Empty;
    private string _gameBroadcastListingGameMode = string.Empty;
    private string _gameBroadcastListingMapSha1 = string.Empty;

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
            player.StartingLocation = slot.StartIndex;
        }
    }

    public void SyncPlayersFromLobby(LobbyPlayerState state, string hostName)
    {
        if (!IsHost)
            return;

        List<CnCNetGameRoomPlayer> entries;
        lock (_sync)
        {
            entries = MultiplayerSlotLayout.BuildPoListFromState(state, hostName);
            AppendChannelJoinersLocked(entries, hostName);

            var readyByName = _players.Where(p => !p.IsAi)
                .ToDictionary(p => p.Name, p => (p.Ready, p.AutoReady), StringComparer.OrdinalIgnoreCase);

            foreach (CnCNetGameRoomPlayer entry in entries)
            {
                if (entry.IsAi)
                {
                    entry.Ready = true;
                }
                else if (readyByName.TryGetValue(entry.Name, out (bool Ready, bool AutoReady) existing))
                {
                    entry.Ready = existing.Ready;
                    entry.AutoReady = existing.AutoReady;
                }

                if (entry.IsHost || (!entry.IsAi && entry.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase) && IsHost))
                    entry.Ready = true;
            }

            if (PlayerListsEquivalent(_players, entries))
                return;

            _players.Clear();
            foreach (CnCNetGameRoomPlayer entry in entries)
                _players.Add(entry);

            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    public bool TryHostLaunch(out string message)
    {
        message = string.Empty;
        if (!IsHost)
        {
            message = "Only the host can launch the game.";
            return false;
        }

        if (_connection == null || !_connection.IsConnected)
        {
            message = "IRC not connected.";
            return false;
        }

        if (!_locked)
        {
            message = "The host needs to lock the game room before launching the game.";
            return false;
        }

        lock (_sync)
        {
            if (_players.Count == 0)
                EnsureHostPlayerLocked();

            var humans = _players.Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (humans.Count == 0)
            {
                message = "No players in the room.";
                return false;
            }

            foreach (CnCNetGameRoomPlayer human in humans)
            {
                if (human.IsHost || (IsHost && human.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase)))
                {
                    human.Ready = true;
                    continue;
                }

                if (!human.Ready)
                {
                    message = "Not all players are ready.";
                    return false;
                }
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
            else
            {
                Logger.Log("One player MP -- starting!");
            }

            var fhc = new FileHashCalculator();
            fhc.CalculateHashes();
            if (_gameFilesHash != fhc.GetCompleteHash())
            {
                Logger.Log("Game files modified during client session!");
                SendCtcp("CD");
                LogNotice($"{_localNick} has modified game files during the client session. They are likely attempting to cheat!");
            }

            _gameBroadcast?.MarkGameStarting();
            SendCtcp("STRTD");

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

    private List<string> GetHumanPlayerNamesLocked()
    {
        lock (_sync)
        {
            return _players
                .Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .ToList();
        }
    }

    public IReadOnlyList<string> GetHumanPlayerNames()
    {
        lock (_sync)
            return GetHumanPlayerNamesLocked();
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

    private void HandleTunnelPing(string sender, string payload)
    {
        if (!int.TryParse(payload, out int ping))
            return;

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(sender);
            if (player == null || player.IsAi)
                return;

            player.Ping = ping;
        }

        StateChanged?.Invoke();
    }

    private void BroadcastLocalTunnelPingLocked()
    {
        if (_connection == null)
            return;

        int ping = 0;
        SendCtcp($"TNLPNG {ping}");

        CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
        if (local != null)
            local.Ping = ping;
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
            player.AutoReady = readyState > 1;
            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    private void ApplyOptionsRequest(string playerName, string payload)
    {
        if (!IsHost || !int.TryParse(payload, out int packed))
            return;

        UnpackOptionsRequest(packed, out int side, out int color, out int start, out int team);

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(playerName);
            if (player == null || player.IsAi)
                return;

            if (side < 0 || color < 0 || start < 0 || team < 0)
                return;

            if (side != player.SideId || start != player.StartingLocation || team != player.TeamId)
                ClearHumanReadyStatesLocked(exceptName: playerName);

            player.SideId = side;
            player.ColorId = color;
            player.StartingLocation = start;
            player.TeamId = team;
            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    private void ClearHumanReadyStatesLocked(string? exceptName = null)
    {
        foreach (CnCNetGameRoomPlayer player in _players)
        {
            if (player.IsAi)
                continue;

            if (exceptName != null && player.Name.Equals(exceptName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (player.IsHost)
                continue;

            player.Ready = false;
            player.AutoReady = false;
        }
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
        var parsed = new List<CnCNetGameRoomPlayer>();
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
                parsed.Add(new CnCNetGameRoomPlayer
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
            bool autoReady = false;
            if (i < parts.Length && int.TryParse(parts[i], out int readyState))
            {
                ready = readyState > 0;
                autoReady = readyState > 1;
                i++;
            }

            parsed.Add(new CnCNetGameRoomPlayer
            {
                Name = nameOrLevel,
                IsHost = nameOrLevel.Equals(HostName, StringComparison.OrdinalIgnoreCase),
                Ready = ready,
                AutoReady = autoReady,
                TeamId = team,
                StartingLocation = start,
                ColorId = color,
                SideId = side,
            });
        }

        lock (_sync)
        {
            if (PlayerListsEquivalent(_players, parsed))
                return;

            _players.Clear();
            foreach (CnCNetGameRoomPlayer player in parsed)
                _players.Add(player);
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

        (int checkBoxCount, int dropDownCount) = GameOptionsControlCounts?.Invoke() ?? (0, 0);
        if (checkBoxCount == 0 && dropDownCount == 0)
        {
            LogNotice("Game options updated by host.");
            StateChanged?.Invoke();
            return;
        }

        if (!CnCNetGameOptionsCodec.TryParseBody(message, checkBoxCount, dropDownCount, out CnCNetGameOptionsState? parsed, out string? error)
            || parsed == null)
        {
            LogNotice("The game host has sent an invalid game options message! The game host's game version might be different from yours.");
            Logger.Log($"CnCNet GO parse failed: {error}");
            return;
        }

        _randomSeed = parsed.RandomSeed;
        _removeStartingLocations = parsed.RemoveStartingLocations;
        GameOptionsReceiver?.Invoke(parsed);
        LogNotice("Game options updated by host.");
        StateChanged?.Invoke();
    }

    private void ApplyGameLobbySettings(string sender, string message)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        string[] parts = message.Split(';');
        if (parts.Length < 4)
            return;

        string newRoomName = parts[0];
        int newMaxPlayers = Conversions.IntFromString(parts[1], Room.MaxPlayers);
        int newSkillLevel = Conversions.IntFromString(parts[2], Room.SkillLevel);
        bool newCustomPassword = Convert.ToBoolean(Conversions.IntFromString(parts[3], 0));

        bool nameChanged = !Room.RoomName.Equals(newRoomName, StringComparison.Ordinal);
        bool maxChanged = Room.MaxPlayers != newMaxPlayers;
        bool skillChanged = Room.SkillLevel != newSkillLevel;

        Room.RoomName = newRoomName;
        Room.MaxPlayers = newMaxPlayers;
        Room.SkillLevel = newSkillLevel;
        Room.CustomPassword = newCustomPassword;

        if (nameChanged)
            LogNotice($"{sender} changed game room name to \"{newRoomName}\".");

        if (maxChanged)
            LogNotice($"{sender} changed maximum players to {newMaxPlayers}.");

        if (skillChanged)
        {
            string[] options = ClientConfiguration.Instance.SkillLevelOptions.Split(',');
            string skillName = newSkillLevel >= 0 && newSkillLevel < options.Length
                ? options[newSkillLevel]
                : newSkillLevel.ToString();
            LogNotice($"{sender} changed skill level to {skillName}.");
        }

        StateChanged?.Invoke();
    }

    private void HandleFileHashNotification(string sender, string filesHash)
    {
        if (!IsHost)
            return;

        if (filesHash.Equals(_gameFilesHash, StringComparison.OrdinalIgnoreCase))
            return;

        SendCtcp($"MM {sender}");
        HandleCheaterNotification(_localNick, sender);
    }

    private void HandleCheaterNotification(string sender, string cheaterName)
    {
        if (!sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        LogNotice($"Player {cheaterName} has different files compared to the game host. Either {cheaterName} or the game host could be cheating.");
    }

    private void HandleTunnelChange(string sender, string tunnelAddressAndPort)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        string[] split = tunnelAddressAndPort.Split(':');
        if (split.Length < 2 || !int.TryParse(split[1], out int tunnelPort))
            return;

        CnCNetTunnelEntry? tunnel = CnCNetTunnelListLoader.Load()
            .FirstOrDefault(t => t.Address.Equals(split[0], StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);

        if (tunnel == null)
        {
            _tunnelErrorMode = true;
            LogNotice("The game host has selected an invalid tunnel server! The game host needs to change the server or you will be unable to participate in the match.");
            StateChanged?.Invoke();
            return;
        }

        _tunnelErrorMode = false;
        Room.Tunnel = tunnel;
        LogNotice($"The game host has changed the tunnel server to: {tunnel.Name}");

        lock (_sync)
        {
            foreach (CnCNetGameRoomPlayer player in _players)
            {
                if (!player.IsAi)
                    player.Ping = -1;
            }
        }

        StateChanged?.Invoke();
    }

    private void BroadcastGameLobbySettings()
    {
        if (!IsHost || _connection == null)
            return;

        var sb = new StringBuilder("GSETTINGS ");
        sb.Append(Room.RoomName);
        sb.Append(';');
        sb.Append(Room.MaxPlayers);
        sb.Append(';');
        sb.Append(Room.SkillLevel);
        sb.Append(';');
        sb.Append(Convert.ToInt32(Room.CustomPassword));
        SendCtcp(sb.ToString());
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
                int readyState = player.AutoReady ? 2 : player.Ready ? 1 : 0;
                sb.Append(readyState);
                sb.Append(';');
            }
        }

        SendCtcp(sb.ToString());
    }

    private void BroadcastGameOptionsLocked()
    {
        if (!IsHost || _connection == null)
            return;

        CnCNetGameOptionsState? state = GameOptionsProvider?.Invoke();
        if (state == null)
        {
            state = new CnCNetGameOptionsState
            {
                CheckBoxValues = [],
                DropDownIndices = [],
                MapOfficial = false,
                MapSha1 = _gameBroadcastListingMapSha1,
                GameModeName = _gameBroadcastListingGameMode,
                MapUntranslatedName = _gameBroadcastListingMapName,
                FrameSendRate = ClientConfiguration.Instance.DefaultFrameSendRate,
                MaxAhead = ClientConfiguration.Instance.DefaultMaxAhead,
                ProtocolVersion = ClientConfiguration.Instance.DefaultProtocolVersion,
                RandomSeed = _randomSeed,
                RemoveStartingLocations = _removeStartingLocations,
            };
        }
        else
        {
            _randomSeed = state.RandomSeed;
            _removeStartingLocations = state.RemoveStartingLocations;
        }

        SendCtcp("GO " + CnCNetGameOptionsCodec.BuildBody(state));
    }

    private void AppendChannelJoinersLocked(List<CnCNetGameRoomPlayer> entries, string hostName)
    {
        var namesInEntries = new HashSet<string>(
            entries.Where(e => !e.IsAi).Select(e => e.Name),
            StringComparer.OrdinalIgnoreCase);

        int insertAt = entries.FindIndex(e => e.IsAi);
        if (insertAt < 0)
            insertAt = entries.Count;

        foreach (string channelUser in _channelUsers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (channelUser.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                continue;

            if (namesInEntries.Contains(channelUser))
                continue;

            CnCNetGameRoomPlayer? existing = FindPlayerLocked(channelUser);
            entries.Insert(insertAt++, existing ?? new CnCNetGameRoomPlayer
            {
                Name = channelUser,
                IsHost = channelUser.Equals(hostName, StringComparison.OrdinalIgnoreCase),
            });
            namesInEntries.Add(channelUser);
        }
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

    /// <summary>OR CTCP uses side, color, start, team (XNA RequestPlayerOptions).</summary>
    private static int PackOptionsRequest(int side, int color, int start, int team)
    {
        byte[] bytes = [(byte)side, (byte)color, (byte)start, (byte)team];
        return BitConverter.ToInt32(bytes, 0);
    }

    private static void UnpackOptionsRequest(int packed, out int side, out int color, out int start, out int team)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        side = bytes[0];
        color = bytes[1];
        start = bytes[2];
        team = bytes[3];
    }

    private static void UnpackOptions(int packed, out int team, out int start, out int color, out int side)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        team = bytes[0];
        start = bytes[1];
        color = bytes[2];
        side = bytes[3];
    }

    private static bool PlayerListsEquivalent(
        IReadOnlyList<CnCNetGameRoomPlayer> current,
        IReadOnlyList<CnCNetGameRoomPlayer> next)
    {
        if (current.Count != next.Count)
            return false;

        for (int i = 0; i < current.Count; i++)
        {
            CnCNetGameRoomPlayer a = current[i];
            CnCNetGameRoomPlayer b = next[i];
            if (a.IsAi != b.IsAi
                || a.AiLevel != b.AiLevel
                || a.Ready != b.Ready
                || a.AutoReady != b.AutoReady
                || a.IsHost != b.IsHost
                || a.TeamId != b.TeamId
                || a.StartingLocation != b.StartingLocation
                || a.ColorId != b.ColorId
                || a.SideId != b.SideId
                || !a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
