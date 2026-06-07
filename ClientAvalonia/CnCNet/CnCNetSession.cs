using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClientCore;
using ClientCore.Settings;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>CnCNet session: IRC connect, channel join, tunnel list, player/game lobby state.</summary>
public sealed class CnCNetSession : IDisposable
{
    public static CnCNetSession Instance { get; } = new();

    private readonly object _sync = new();
    private readonly Dictionary<string, CnCNetHostedGameSummary> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, CnCNetHostedGameSummary>> _gamesByBroadcast = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _channelUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _joinedBroadcastChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _followedGameIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _settingsSavedSubscribed;

    private CnCNetIrcConnection? _connection;
    private CnCNetGameCollection? _gameCollection;
    private CnCNetGameEntry? _currentGame;
    private int _selectedChannelIndex;
    private CnCNetPlayerCountService? _playerCountService;
    private Timer? _hostedGameRefreshTimer;
    private Timer? _tunnelMaintenanceTimer;
    private CnCNetActiveGameRoom? _activeGameRoom;
    private CnCNetGameRoomSession? _gameRoom;
    private readonly CnCNetGameBroadcastService _gameBroadcast = new();
    private string _systemId = string.Empty;
    private bool _autoReconnect;
    private int _reconnectAttempts;
    private int _namesRetryCount;
    private bool _gameRoomJoinPending;
    private bool _tunnelRefreshInProgress;
    private uint _tunnelMaintenanceCycle;

    public bool IsGameRoomJoinPending => _gameRoomJoinPending;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public event Action<string>? GameRoomJoinFailed;

    public CnCNetLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<CnCNetTunnelEntry> Tunnels { get; private set; } = [];

    public CnCNetIrcConnection? Connection => _connection;

    public CnCNetGameCollection? GameCollection => _gameCollection;

    public CnCNetGameEntry? CurrentGame => _currentGame;

    public CnCNetGameChannels? Channels => _currentGame?.ToChannels();

    public int SelectedChannelIndex => _selectedChannelIndex;

    public CnCNetActiveGameRoom? ActiveGameRoom => _activeGameRoom;

    public CnCNetGameRoomSession? GameRoom => _gameRoom;

    public string LocalNick => _connection?.CurrentNick ?? ProgramConstants.PLAYERNAME;

    public event Action<CnCNetStartGameInfo>? GameStarting;

    public event Action? GameRoomHostAbandoned;

    public Func<CnCNetGameOptionsState>? GameOptionsProvider { get; set; }

    public Action<CnCNetGameOptionsState>? GameOptionsReceiver { get; set; }

    public Func<(int CheckBoxCount, int DropDownCount)>? GameOptionsControlCounts { get; set; }

    private const double HostedGameLifetimeSeconds = 35;
    private const double HostedGameRefreshIntervalSeconds = 5;
    private const double CurrentTunnelPingIntervalSeconds = 20;
    private const uint CyclesPerTunnelListRefresh = 6;

    public void EnsureStarted()
    {
        LogActivity($"Protocol revision {ProgramConstants.CNCNET_PROTOCOL_REVISION} (legacy GAME={ProgramConstants.UsesLegacyCnCNetGameBroadcast}).");

        if (_playerCountService == null)
        {
            _playerCountService = new CnCNetPlayerCountService();
            _playerCountService.PlayerCountUpdated += count =>
            {
                OnlinePlayerCount = count;
                LogActivity($"Online players (HTTP): {count}");
                LobbyState.SetOnlinePlayerCount(count);
                StateChanged?.Invoke();
            };
            _playerCountService.Start();
        }

        if (_hostedGameRefreshTimer == null)
        {
            _hostedGameRefreshTimer = new Timer(_ => PruneStaleHostedGames(), null,
                TimeSpan.FromSeconds(HostedGameRefreshIntervalSeconds),
                TimeSpan.FromSeconds(HostedGameRefreshIntervalSeconds));
        }

        if (_tunnelMaintenanceTimer == null)
        {
            _tunnelMaintenanceTimer = new Timer(_ => RunTunnelMaintenance(), null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(CurrentTunnelPingIntervalSeconds));
        }
    }

    private void RunTunnelMaintenance()
    {
        if (_tunnelMaintenanceCycle % CyclesPerTunnelListRefresh == 0)
        {
            _tunnelMaintenanceCycle = 0;
            RefreshTunnelsAsync();
        }
        else
        {
            PingCurrentTunnelAsync(checkTunnelList: true);
        }

        _tunnelMaintenanceCycle++;
    }

    private void RefreshTunnelsAsync()
    {
        lock (_sync)
        {
            if (_tunnelRefreshInProgress)
                return;

            _tunnelRefreshInProgress = true;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                IReadOnlyList<CnCNetTunnelEntry> refreshed = CnCNetTunnelListLoader.Load();
                HandleRefreshedTunnels(refreshed);
            }
            catch (Exception ex)
            {
                LogActivity($"Tunnel list failed: {ex.Message}");
            }
            finally
            {
                lock (_sync)
                    _tunnelRefreshInProgress = false;
            }
        });
    }

    private void HandleRefreshedTunnels(IReadOnlyList<CnCNetTunnelEntry> refreshed)
    {
        if (refreshed.Count == 0)
        {
            LogActivity("Tunnel list refresh returned no available NAT tunnels.");
            StateChanged?.Invoke();
            return;
        }

        List<CnCNetTunnelEntry> updated = [];
        lock (_sync)
        {
            var existing = Tunnels.ToDictionary(t => $"{t.Address}:{t.Port}", StringComparer.OrdinalIgnoreCase);

            foreach (CnCNetTunnelEntry tunnel in refreshed)
            {
                string key = $"{tunnel.Address}:{tunnel.Port}";
                if (existing.TryGetValue(key, out CnCNetTunnelEntry? existingTunnel))
                {
                    existingTunnel.UpdateFrom(tunnel);
                    updated.Add(existingTunnel);
                }
                else
                {
                    updated.Add(tunnel);
                }
            }

            Tunnels = updated;

            if (_activeGameRoom != null)
            {
                CnCNetTunnelEntry? activeTunnel = updated.FirstOrDefault(t =>
                    t.Address.Equals(_activeGameRoom.Tunnel.Address, StringComparison.OrdinalIgnoreCase)
                    && t.Port == _activeGameRoom.Tunnel.Port);

                if (activeTunnel != null)
                    _activeGameRoom.Tunnel = activeTunnel;
            }
        }

        LogActivity($"Loaded {updated.Count} NAT tunnels.");
        RefreshHostedGames();
        PingListedTunnelsAsync(updated);
        PingCurrentTunnelAsync(checkTunnelList: true);
        StateChanged?.Invoke();
    }

    private void PingListedTunnelsAsync(IReadOnlyList<CnCNetTunnelEntry> tunnels)
    {
        foreach (CnCNetTunnelEntry tunnel in tunnels)
        {
            if (!UserINISettings.Instance.PingUnofficialCnCNetTunnels.Value && !tunnel.Official && !tunnel.Recommended)
                continue;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                tunnel.UpdatePing();
                StateChanged?.Invoke();
            });
        }
    }

    private void PingCurrentTunnelAsync(bool checkTunnelList)
    {
        CnCNetTunnelEntry? tunnel = _activeGameRoom?.Tunnel;
        if (tunnel == null)
            return;

        bool canBroadcastPing = _gameRoom is { IsLocalJoined: true } && !_gameRoomJoinPending;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            tunnel.UpdatePing();

            if (checkTunnelList)
            {
                CnCNetTunnelEntry? listedTunnel = Tunnels.FirstOrDefault(t =>
                    t.Address.Equals(tunnel.Address, StringComparison.OrdinalIgnoreCase)
                    && t.Port == tunnel.Port);
                if (listedTunnel != null && !ReferenceEquals(listedTunnel, tunnel))
                    listedTunnel.PingInMs = tunnel.PingInMs;
            }

            if (canBroadcastPing)
                _gameRoom?.BroadcastLocalTunnelPing();

            StateChanged?.Invoke();
        });
    }

    public void ConnectIfNeeded()
    {
        EnsureStarted();
        _autoReconnect = true;

        lock (_sync)
        {
            if (_connection is { IsConnected: true } or { IsConnecting: true })
                return;

            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }

            _gameCollection = new CnCNetGameCollection();
            _gameCollection.Initialize();

            _currentGame = _gameCollection.GetLocalGame();
            if (_currentGame == null)
            {
                LogActivity("No game collection entry for LocalGame ? IRC connect skipped.");
                LobbyState.SetConnectionStatus("No chat channels configured.");
                StateChanged?.Invoke();
                return;
            }

            _selectedChannelIndex = IndexInSelectableGames(_currentGame);
            UpdateChannelListState();
            ApplyPlayerNameFromUserSettings();
            if (NameValidator.IsNameValid(ProgramConstants.PLAYERNAME, out string? nameError) != NameValidationError.None)
            {
                string message = nameError ?? "Invalid CnCNet nickname.";
                LogActivity(message);
                LobbyState.SetConnectionStatus(message);
                StateChanged?.Invoke();
                return;
            }

            LobbyState.ClearConnectionLog();
            LogActivity($"Starting session as {ProgramConstants.PLAYERNAME} ({ClientConfiguration.Instance.LocalGame})");
            LogActivity($"Channels: chat={_currentGame.ChatChannel}, games={_currentGame.GameBroadcastChannel} ({_currentGame.UiName})");

            _systemId = CnCNetIdentity.CreateSystemId();
            _connection = new CnCNetIrcConnection(_systemId);
            WireConnection(_connection);
            EnsureSettingsSavedSubscription();
            LobbyState.SetConnectionStatus("Connecting to CnCNet...");
            LobbyState.SetChannelName(_currentGame.UiName, _currentGame.ChatChannel);
            StateChanged?.Invoke();
            LogActivity("Connecting to CnCNet IRC...");
            _connection.ConnectAsync();
        }
    }

    public void SwitchToGame(int gameIndex)
    {
        if (_gameCollection == null || _connection is not { IsConnected: true })
            return;

        IReadOnlyList<CnCNetGameEntry> selectable = _gameCollection.GetSelectableGames();
        if (gameIndex < 0 || gameIndex >= selectable.Count || gameIndex == _selectedChannelIndex)
            return;

        CnCNetGameEntry? previous = _currentGame;
        CnCNetGameEntry next = selectable[gameIndex];
        string localChat = NormalizeIrcChannel(_gameCollection.GetLocalGame()?.ChatChannel ?? string.Empty);
        string cncnetChat = NormalizeIrcChannel("#cncnet");

        if (previous != null)
        {
            string prevChat = NormalizeIrcChannel(previous.ChatChannel);
            if (prevChat != localChat && prevChat != cncnetChat)
                _connection.PartChannel(previous.ChatChannel);
        }

        _currentGame = next;
        _selectedChannelIndex = gameIndex;
        _channelUsers.Clear();
        _namesRetryCount = 0;

        string nextChat = NormalizeIrcChannel(next.ChatChannel);
        if (nextChat != localChat && nextChat != cncnetChat)
            _connection.JoinChannelInstant(next.ChatChannel, "ra1-derp");

        JoinGameBroadcastChannel(next);
        _connection.RequestChannelNames(next.ChatChannel);
        LobbyState.SetChannelName(next.UiName, next.ChatChannel);
        UpdateChannelListState();
        RefreshHostedGames();
        RefreshLobbyPlayers();
        LogActivity($"Switched to channel {next.UiName} ({next.ChatChannel}).");
        StateChanged?.Invoke();
    }

    private void UpdateChannelListState()
    {
        if (_gameCollection == null)
            return;

        IReadOnlyList<CnCNetGameEntry> selectable = _gameCollection.GetSelectableGames();
        var names = selectable.Select(g => g.UiName).ToList();
        LobbyState.SetAvailableChannels(names, _selectedChannelIndex);
    }

    private int IndexInSelectableGames(CnCNetGameEntry game)
    {
        if (_gameCollection == null)
            return 0;

        IReadOnlyList<CnCNetGameEntry> selectable = _gameCollection.GetSelectableGames();
        for (int i = 0; i < selectable.Count; i++)
        {
            if (selectable[i].InternalName.Equals(game.InternalName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private void JoinGameBroadcastChannel(CnCNetGameEntry game)
    {
        if (_connection == null || !game.HasGameBroadcast)
            return;

        string broadcast = NormalizeIrcChannel(game.GameBroadcastChannel!);
        if (_joinedBroadcastChannels.Contains(broadcast))
            return;

        // XNA: Channel.Join() every welcome; only mark joined after local JOIN echo.
        _connection.JoinChannelInstant(game.GameBroadcastChannel!);
    }

    /// <summary>Re-JOIN listing channels when membership was lost (XNA welcome / channel rejoin).</summary>
    public void EnsureGameBroadcastChannelsJoined()
    {
        if (!_autoReconnect || _connection is not { IsConnected: true } || _currentGame == null)
            return;

        JoinGameBroadcastChannel(_currentGame);
        JoinFollowedBroadcastChannels();
    }

    private bool IsKnownBroadcastChannel(string channel)
    {
        string normalized = NormalizeIrcChannel(channel);
        if (_currentGame?.HasGameBroadcast == true
            && normalized.Equals(NormalizeIrcChannel(_currentGame.GameBroadcastChannel!), StringComparison.OrdinalIgnoreCase))
            return true;

        if (_gameCollection == null)
            return false;

        return _gameCollection.Games.Any(g =>
            g.HasGameBroadcast
            && normalized.Equals(NormalizeIrcChannel(g.GameBroadcastChannel!), StringComparison.OrdinalIgnoreCase));
    }

    private void ConfirmBroadcastChannelJoined(string channel)
    {
        string normalized = NormalizeIrcChannel(channel);
        if (_joinedBroadcastChannels.Add(normalized))
            LogActivity($"Joined game broadcast channel {normalized}.", notifyUi: false);
    }

    private void DropBroadcastChannelMembership(string channel)
    {
        string normalized = NormalizeIrcChannel(channel);
        if (_joinedBroadcastChannels.Remove(normalized))
            LogActivity($"Left game broadcast channel {normalized}.", notifyUi: false);
    }

    private bool IsCurrentGameBroadcast(string normalizedBroadcastChannel)
        => _currentGame?.HasGameBroadcast == true
           && NormalizeIrcChannel(_currentGame.GameBroadcastChannel!)
               .Equals(normalizedBroadcastChannel, StringComparison.OrdinalIgnoreCase);

    public void Disconnect()
    {
        _autoReconnect = false;
        _reconnectAttempts = 0;
        LeaveGameRoom(restoreBroadcastChannels: false);
        _gameBroadcast.Stop();
        lock (_sync)
        {
            if (_connection == null)
                return;

            if (_currentGame != null)
            {
                _connection.PartChannel(_currentGame.ChatChannel);
                if (_currentGame.HasGameBroadcast)
                    _connection.PartChannel(_currentGame.GameBroadcastChannel!);
            }

            foreach (string broadcast in _joinedBroadcastChannels.ToList())
                _connection.PartChannel(broadcast);

            _joinedBroadcastChannels.Clear();
            _connection.Disconnect();
        }
    }

    public void SetActiveGameRoom(CnCNetActiveGameRoom room)
    {
        _gameRoom?.Leave();
        _activeGameRoom = room;
        _gameRoomJoinPending = true;
        _gameRoom = new CnCNetGameRoomSession(room);
        if (_connection != null)
            AttachGameRoomSession();
    }

    private void AttachGameRoomSession()
    {
        if (_gameRoom == null || _connection == null)
            return;

        DetachGameRoomSessionHandlers();

        _gameRoom.Attach(_connection, _gameBroadcast, Channels);
        _gameRoom.GameOptionsProvider = GameOptionsProvider;
        _gameRoom.GameOptionsReceiver = GameOptionsReceiver;
        _gameRoom.GameOptionsControlCounts = GameOptionsControlCounts;
        _gameRoom.HostAbandoned += OnGameRoomHostAbandoned;
        _gameRoom.StateChanged += OnGameRoomStateChanged;
        _gameRoom.NoticeLogged += OnGameRoomNotice;
        _gameRoom.GameStarting += OnGameRoomStarting;
    }

    private void DetachGameRoomSessionHandlers()
    {
        if (_gameRoom == null)
            return;

        _gameRoom.StateChanged -= OnGameRoomStateChanged;
        _gameRoom.NoticeLogged -= OnGameRoomNotice;
        _gameRoom.GameStarting -= OnGameRoomStarting;
        _gameRoom.HostAbandoned -= OnGameRoomHostAbandoned;
    }

    private void OnGameRoomHostAbandoned()
    {
        LogActivity("Game host abandoned — leaving game room.");
        LeaveGameRoom();
        GameRoomHostAbandoned?.Invoke();
    }

    private void OnGameRoomStateChanged() => StateChanged?.Invoke();

    private void OnGameRoomNotice(string msg) => LogActivity(msg);

    private void OnGameRoomStarting(CnCNetStartGameInfo info) => GameStarting?.Invoke(info);

    public void LeaveGameRoom(bool restoreBroadcastChannels = true)
    {
        if (_gameRoom != null)
        {
            DetachGameRoomSessionHandlers();
            _gameRoom.Leave();
        }

        _gameRoom = null;
        _activeGameRoom = null;
        _gameRoomJoinPending = false;

        if (restoreBroadcastChannels
            && _autoReconnect
            && _connection is { IsConnected: true })
        {
            EnsureGameBroadcastChannelsJoined();
        }

        StateChanged?.Invoke();
    }

    public bool TryLaunchHostedGame(out string message)
    {
        if (_gameRoom == null)
        {
            if (_activeGameRoom != null)
            {
                message = "Still joining the CnCNet game room — please wait.";
                return false;
            }

            message = "Not in a CnCNet game room.";
            return false;
        }

        return _gameRoom.TryHostLaunch(out message);
    }

    public void SetGameRoomReady(bool ready, bool autoReady = false)
        => _gameRoom?.SetLocalReady(ready, autoReady);

    public void SetGameRoomLocked(bool locked) => _gameRoom?.SetLocked(locked);

    public void SendChatMessage(string message)
    {
        if (_connection == null || !_connection.IsConnected || _currentGame == null)
            return;

        if (string.IsNullOrWhiteSpace(message))
            return;

        int colorIndex = LobbyState.SelectedChatColorIndex;
        if (colorIndex < 0)
            colorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);

        CnCNetChatColorEntry color = CnCNetChatColorCatalog.GetEntry(colorIndex);
        _connection.SendChatMessage(_currentGame.ChatChannel, message, color.IrcColorId);

        LobbyState.AddChatLine(new CnCNetChatLine
        {
            Sender = LocalNick,
            DisplayText = FormatChatLine(LocalNick, message, DateTime.Now),
        });
        StateChanged?.Invoke();
    }

    public void SetChatColorIndex(int index)
    {
        LobbyState.SelectedChatColorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(index);
        UserINISettings.Instance.ChatColor.Value = LobbyState.SelectedChatColorIndex;
        UserINISettings.Instance.SaveSettings();
    }

    public void UpdateHostedGameListing(
        string mapName,
        string gameModeName,
        string mapSha1,
        IReadOnlyList<string> playerNames,
        bool locked = false,
        bool closed = false)
    {
        _gameBroadcast.UpdateListing(mapName, gameModeName, mapSha1, playerNames, locked, closed);
    }

    public void JoinGameChannel(string channelName, string password, out string? error)
    {
        error = null;
        if (_connection is not { IsConnected: true })
        {
            error = "IRC not connected.";
            return;
        }

        string normalized = CnCNetIrcChannelNames.Preserve(channelName);
        // XNA: JOIN channelName password — preserve exact channel name (no lower-case).
        _connection.SendInstant($"JOIN {normalized} {password}");
        LogActivity($"? JOIN game channel {normalized}", notifyUi: false);
    }

    private void FailGameRoomJoin(string message)
    {
        LogActivity($"Join failed: {message}");
        _gameRoomJoinPending = false;
        GameRoomJoinFailed?.Invoke(message);
        LeaveGameRoom();
    }

    private void CompleteLocalGameRoomJoin()
    {
        if (!_gameRoomJoinPending || _activeGameRoom == null || _gameRoom == null)
            return;

        _gameRoomJoinPending = false;

        if (_activeGameRoom.IsHost)
            EnsureGameBroadcastChannelsJoined();

        _gameRoom.OnLocalJoined();
        LogActivity($"Joined game room \"{_activeGameRoom.RoomName}\".");
        RefreshLobbyPlayers();
        GameRoomJoined?.Invoke(_activeGameRoom);
    }

    private void TryCompleteLocalGameRoomJoinFromNames(string channel, IReadOnlyList<string> users)
    {
        if (!_gameRoomJoinPending || _activeGameRoom == null || _connection == null)
            return;

        if (!channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
            return;

        bool localPresent = users.Any(u => _connection.IsLocalUser(StripIrcPrefixes(u)));
        if (localPresent)
            CompleteLocalGameRoomJoin();
    }

    private void OnChannelJoinFailed(int code, string channel, string detail)
    {
        if (!string.IsNullOrWhiteSpace(channel) && IsKnownBroadcastChannel(channel))
        {
            DropBroadcastChannelMembership(channel);
            if (_autoReconnect)
            {
                LogActivity($"Game broadcast channel join failed (IRC {code}) — will retry.", notifyUi: false);
                EnsureGameBroadcastChannelsJoined();
            }

            return;
        }

        if (!_gameRoomJoinPending || _activeGameRoom == null)
            return;

        if (!string.IsNullOrWhiteSpace(channel)
            && !NormalizeIrcChannel(channel).Equals(
                NormalizeIrcChannel(_activeGameRoom.ChannelName),
                StringComparison.OrdinalIgnoreCase))
            return;

        string message = code switch
        {
            473 => "Cannot join — game room is locked.",
            471 => "Cannot join — game room is full.",
            475 => "Incorrect game room password.",
            474 => "You are banned from this game room.",
            439 => "Cannot join — changing channels too fast. Wait a moment and try again.",
            _ => string.IsNullOrWhiteSpace(detail)
                ? $"Cannot join game room (IRC {code})."
                : detail.TrimEnd('.') + ".",
        };

        FailGameRoomJoin(message);
    }

    public void Dispose()
    {
        _autoReconnect = false;
        _hostedGameRefreshTimer?.Dispose();
        _hostedGameRefreshTimer = null;
        _tunnelMaintenanceTimer?.Dispose();
        _tunnelMaintenanceTimer = null;
        _gameBroadcast.Dispose();
        Disconnect();
        _connection?.Dispose();
        _playerCountService?.Dispose();
    }

    private void WireConnection(CnCNetIrcConnection connection)
    {
        connection.Connected += OnTcpConnected;
        connection.WelcomeReceived += OnWelcomeReceived;
        connection.ConnectionFailed += OnConnectionFailed;
        connection.Disconnected += OnDisconnected;
        connection.ServerMessage += msg =>
        {
            if (msg.Length > 120)
                msg = msg[..117] + "...";
            LogActivity($"? {msg}", notifyUi: false);
        };
        connection.ChannelUserListReceived += OnUserList;
        connection.UserJoined += OnUserJoined;
        connection.UserLeft += OnUserLeft;
        connection.GameBroadcastReceived += OnGameBroadcast;
        connection.ChannelCtcpReceived += OnAnyCtcp;
        connection.ChatMessageReceived += OnChatMessageReceived;
        connection.ChannelNamesComplete += OnChannelNamesComplete;
        connection.ChannelJoinFailed += OnChannelJoinFailed;
        connection.NotOnChannel += OnNotOnChannel;
        connection.ActivityLogged += msg => LogActivity(msg, notifyUi: false);
    }

    private void OnNotOnChannel(string channel)
    {
        if (!_autoReconnect || !IsKnownBroadcastChannel(channel))
            return;

        DropBroadcastChannelMembership(channel);
        LogActivity($"Not on game broadcast channel {NormalizeIrcChannel(channel)} — rejoining.", notifyUi: false);
        EnsureGameBroadcastChannelsJoined();
    }

    private void OnAnyCtcp(string channel, string sender, string ctcp)
    {
        if (ctcp.StartsWith("INVITE ", StringComparison.Ordinal))
        {
            HandleGameInvite(sender, ctcp[7..]);
            return;
        }

        OnChannelCtcp(channel, sender, ctcp);
    }

    private void HandleGameInvite(string sender, string arguments)
    {
        string[] parts = arguments.Split(';');
        if (parts.Length < 2)
            return;

        string channelName = parts[0];
        string gameName = parts[1];
        LogActivity($"Game invite from {sender}: \"{gameName}\" ({channelName}).");
    }

    private void EnsureSettingsSavedSubscription()
    {
        if (_settingsSavedSubscribed)
            return;

        UserINISettings.Instance.SettingsSaved += OnUserSettingsSaved;
        _settingsSavedSubscribed = true;
    }

    private void OnUserSettingsSaved(object? sender, EventArgs e)
    {
        if (_connection is not { IsConnected: true } || _gameCollection == null || _currentGame == null)
            return;

        string localName = _currentGame.InternalName;
        foreach (CnCNetGameEntry game in _gameCollection.Games)
        {
            if (!game.HasGameBroadcast || !game.Supported)
                continue;

            if (game.InternalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                continue;

            bool followed = UserINISettings.Instance.IsGameFollowed(game.InternalName.ToUpperInvariant());
            bool wasFollowed = _followedGameIds.Contains(game.InternalName);

            if (wasFollowed && !followed)
            {
                _connection.PartChannel(game.GameBroadcastChannel!);
                _joinedBroadcastChannels.Remove(NormalizeIrcChannel(game.GameBroadcastChannel!));
                _followedGameIds.Remove(game.InternalName);
            }
            else if (!wasFollowed && followed)
            {
                JoinGameBroadcastChannel(game);
                _followedGameIds.Add(game.InternalName);
            }
        }

        RefreshHostedGames();
    }

    private void JoinFollowedBroadcastChannels()
    {
        if (_gameCollection == null || _connection == null || _currentGame == null)
            return;

        foreach (CnCNetGameEntry game in _gameCollection.Games)
        {
            if (!game.HasGameBroadcast || !game.Supported)
                continue;

            if (game.InternalName.Equals(_currentGame.InternalName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!UserINISettings.Instance.IsGameFollowed(game.InternalName.ToUpperInvariant()))
                continue;

            JoinGameBroadcastChannel(game);
            _followedGameIds.Add(game.InternalName);
        }
    }

    private void OnChannelNamesComplete(string channel)
    {
        if (_activeGameRoom != null
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_currentGame == null || !IsChatChannel(channel))
            return;

        if (_channelUsers.Count == 0 && _connection is { IsConnected: true } && _namesRetryCount < 2)
        {
            _namesRetryCount++;
            LogActivity($"NAMES empty for {channel}, retrying ({_namesRetryCount}/2)...");
            _connection.RequestChannelNames(_currentGame!.ChatChannel);
        }
    }

    private void OnTcpConnected()
    {
        _reconnectAttempts = 0;
        string server = _connection?.ConnectedServer ?? "unknown";
        LobbyState.SetConnectionStatus($"Registering ({server})...");
        LogActivity($"TCP connected to {server}, sending USER/NICK...");
        StateChanged?.Invoke();
    }

    private void OnWelcomeReceived(string welcomeLine)
    {
        LobbyState.SetConnectionStatus("Connected ??joining channels...");
        LogActivity($"IRC welcome: {welcomeLine}");
        StateChanged?.Invoke();

        if (_connection == null || _currentGame == null)
            return;

        string chatChannel = NormalizeIrcChannel(_currentGame.ChatChannel);
        _connection.JoinChannelInstant(chatChannel, "ra1-derp");
        _connection.JoinChannelInstant("#cncnet");
        JoinGameBroadcastChannel(_currentGame);
        JoinFollowedBroadcastChannels();
        _namesRetryCount = 0;
        _connection.RequestChannelNames(_currentGame.ChatChannel);

        LobbyState.SetConnectionStatus("Connected");
        LobbyState.SelectedChatColorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);
        _reconnectAttempts = 0;
        LogActivity($"JOIN {chatChannel}, #cncnet + broadcast channels; NAMES requested.");
        EnsureGameBroadcastChannelsJoined();
        TryRejoinActiveGameRoom();
        StateChanged?.Invoke();
    }

    private void OnConnectionFailed(string message)
    {
        ClearLobbyData(preserveGameRoom: _activeGameRoom != null);
        LobbyState.SetConnectionStatus(message);
        LogActivity($"Connection failed: {message}");
        StateChanged?.Invoke();
        ScheduleReconnect();
    }

    private void OnDisconnected(string message)
    {
        ClearLobbyData(preserveGameRoom: _activeGameRoom != null);
        LobbyState.SetConnectionStatus("Offline");
        LogActivity($"Disconnected: {message}");
        StateChanged?.Invoke();

        lock (_sync)
        {
            _connection?.Dispose();
            _connection = null;
        }

        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (!_autoReconnect || _reconnectAttempts >= 3 || ProgramConstants.IsInGame)
            return;

        _reconnectAttempts++;
        int attempt = _reconnectAttempts;
        LogActivity($"Reconnecting in 5s (attempt {attempt}/3)...");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(5000);
            if (!_autoReconnect)
                return;

            lock (_sync)
            {
                if (_connection is { IsConnected: true } or { IsConnecting: true })
                    return;
            }

            ConnectIfNeeded();
        });
    }

    private void ClearLobbyData(bool preserveGameRoom = false)
    {
        if (!preserveGameRoom)
            _gameBroadcast.Stop();

        _channelUsers.Clear();
        _games.Clear();
        _gamesByBroadcast.Clear();
        _joinedBroadcastChannels.Clear();
        _followedGameIds.Clear();
        LobbyState.SetChannelPlayers([]);
        LobbyState.SetHostedGames([]);

        if (preserveGameRoom)
        {
            LogActivity("IRC disconnected — preserving active game room for reconnect.");
            return;
        }

        _gameRoom = null;
        _activeGameRoom = null;
        _gameRoomJoinPending = false;
    }

    private void TryRejoinActiveGameRoom()
    {
        if (_activeGameRoom == null || _connection is not { IsConnected: true } || ProgramConstants.IsInGame)
            return;

        var gameSummary = new CnCNetHostedGameSummary
        {
            HostName = _activeGameRoom.HostName,
            RoomName = _activeGameRoom.RoomName,
            ChannelName = _activeGameRoom.ChannelName,
            CustomPassword = _activeGameRoom.CustomPassword,
        };

        if (CnCNetLobbyOperations.TryResolveJoinPassword(
                gameSummary,
                _activeGameRoom.CustomPassword ? _activeGameRoom.Password : null,
                out string joinPassword,
                out _))
        {
            _activeGameRoom.Password = joinPassword;
        }

        _gameRoomJoinPending = true;
        JoinGameChannel(_activeGameRoom.ChannelName, _activeGameRoom.Password, out _);
        AttachGameRoomSession();
        LogActivity($"Rejoining game room \"{_activeGameRoom.RoomName}\"...");
    }

    private void OnUserList(string channel, IReadOnlyList<string> users)
    {
        if (_activeGameRoom != null
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            TryCompleteLocalGameRoomJoinFromNames(channel, users);
            _gameRoom?.OnChannelUserList(users);
            return;
        }

        if (_connection != null
            && IsKnownBroadcastChannel(channel)
            && users.Any(u => _connection.IsLocalUser(StripIrcPrefixes(u))))
        {
            ConfirmBroadcastChannelJoined(channel);
        }

        if (_currentGame == null || !IsChatChannel(channel))
            return;

        _channelUsers.Clear();
        foreach (string user in users)
        {
            string name = StripIrcPrefixes(user);
            if (!string.IsNullOrWhiteSpace(name))
                _channelUsers.Add(name);
        }

        LogActivity($"Channel user list ({channel}): {_channelUsers.Count} users.");
        RefreshLobbyPlayers();
    }

    private void OnUserJoined(string channel, string user)
    {
        string name = StripIrcPrefixes(user);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_activeGameRoom != null
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            if (_connection != null && _connection.IsLocalUser(name))
            {
                CompleteLocalGameRoomJoin();
                return;
            }

            _gameRoom?.OnUserJoined(channel, name);
            return;
        }

        if (_currentGame != null
            && _currentGame.HasGameBroadcast
            && channel.Equals(NormalizeIrcChannel(_currentGame.GameBroadcastChannel!), StringComparison.OrdinalIgnoreCase)
            && _connection != null
            && _connection.IsLocalUser(name))
        {
            ConfirmBroadcastChannelJoined(channel);
            return;
        }

        if (_gameCollection != null
            && _connection != null
            && _connection.IsLocalUser(name)
            && IsKnownBroadcastChannel(channel))
        {
            ConfirmBroadcastChannelJoined(channel);
            return;
        }

        if (_currentGame == null || !IsChatChannel(channel))
            return;

        _channelUsers.Add(name);
        if (_connection != null
            && _connection.IsLocalUser(name)
            && _channelUsers.Count(u => u.Equals(name, StringComparison.OrdinalIgnoreCase)) == 1)
        {
            LogActivity($"Joined chat channel {channel} as {name}.");
            EnsureGameBroadcastChannelsJoined();
        }

        RefreshLobbyPlayers();
    }

    private void OnUserLeft(string channel, string user)
    {
        string name = StripIrcPrefixes(user);

        if (_activeGameRoom != null
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            _gameRoom?.OnUserLeft(channel, name);
            return;
        }

        if (_connection != null && _connection.IsLocalUser(name) && IsKnownBroadcastChannel(channel))
        {
            DropBroadcastChannelMembership(channel);
            return;
        }

        if (RemoveHostedGamesByHost(name))
            return;

        if (_currentGame == null)
            return;

        if (!IsChatChannel(channel) && !channel.Equals("*", StringComparison.Ordinal))
            return;

        _channelUsers.Remove(name);
        RefreshLobbyPlayers();
    }

    private bool RemoveHostedGamesByHost(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return false;

        bool changed = false;
        foreach (Dictionary<string, CnCNetHostedGameSummary> bucket in _gamesByBroadcast.Values.ToList())
        {
            var toRemove = bucket.Values
                .Where(g => g.HostName.Equals(hostName, StringComparison.OrdinalIgnoreCase))
                .Select(g => g.ChannelName)
                .ToList();

            foreach (string key in toRemove)
            {
                if (bucket.Remove(key))
                    changed = true;
                _games.Remove(key);
            }
        }

        if (changed)
            RefreshHostedGames();

        return changed;
    }

    private void OnChannelCtcp(string channel, string sender, string ctcp)
        => _gameRoom?.OnChannelCtcp(channel, sender, ctcp);

    private void OnChatMessageReceived(string channel, string sender, string message)
    {
        if (_currentGame == null || !IsChatChannel(channel))
            return;

        string text = ParseIncomingChatText(message);
        LobbyState.AddChatLine(new CnCNetChatLine
        {
            Sender = sender,
            DisplayText = FormatChatLine(sender, text, DateTime.Now),
        });
        StateChanged?.Invoke();
    }

    private static string ParseIncomingChatText(string message)
    {
        if (message.Contains('\u0003') && message.Length >= 3)
        {
            string colorString = message.Substring(1, 2);
            if (int.TryParse(colorString, out _))
                message = message.Length > 3 ? message[3..] : string.Empty;
        }

        if (message.Length > 0 && message[^1] == '\u001f')
            message = message[..^1];

        return message.Replace('\r', ' ').Trim();
    }

    private static string FormatChatLine(string sender, string message, DateTime time)
        => $"[{time:HH:mm}] {sender}: {message}";

    private void OnGameBroadcast(string channel, string sender, string ctcp)
    {
        string normalizedBroadcast = NormalizeIrcChannel(channel);

        CnCNetGameEntry? sourceGame = _gameCollection?.FindByBroadcastChannel(normalizedBroadcast);
        if (sourceGame == null)
        {
            Logger.Log($"CnCNet: ignoring GAME from unknown broadcast channel {normalizedBroadcast}.");
            return;
        }

        CnCNetHostedGameSummary? game = CnCNetGameMessageParser.TryParse(
            sender,
            ctcp,
            Tunnels,
            out string? rejectReason,
            sourceGame.InternalName);
        if (game == null)
        {
            if (ctcp.StartsWith("GAME ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(rejectReason))
                Logger.Log($"CnCNet: GAME from {sender} ignored: {rejectReason}");

            return;
        }

        if (!_gamesByBroadcast.TryGetValue(normalizedBroadcast, out Dictionary<string, CnCNetHostedGameSummary>? bucket))
        {
            bucket = new Dictionary<string, CnCNetHostedGameSummary>(StringComparer.OrdinalIgnoreCase);
            _gamesByBroadcast[normalizedBroadcast] = bucket;
        }

        if (game.IsClosed)
        {
            RemoveHostedGame(bucket, game);
            if (IsCurrentGameBroadcast(normalizedBroadcast))
                LogActivity($"Game closed: {game.RoomName} ({sender})", notifyUi: false);
        }
        else
        {
            bucket[game.ChannelName] = game;
            _games[game.ChannelName] = game;
            if (IsCurrentGameBroadcast(normalizedBroadcast))
            {
                LogActivity(
                    $"Game listed: {game.RoomName} by {sender} ({game.PlayerCount}/{game.MaxPlayers})",
                    notifyUi: false);
            }
        }

        RefreshHostedGames();
    }

    private void RemoveHostedGame(Dictionary<string, CnCNetHostedGameSummary> bucket, CnCNetHostedGameSummary game)
    {
        bucket.Remove(game.ChannelName);
        _games.Remove(game.ChannelName);

        foreach (CnCNetHostedGameSummary hostedGame in bucket.Values
                     .Where(g => g.HostName.Equals(game.HostName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            bucket.Remove(hostedGame.ChannelName);
            _games.Remove(hostedGame.ChannelName);
        }
    }

    private void RefreshHostedGames()
    {
        List<CnCNetHostedGameSummary> list = GetHostedGamesForCurrentChannel();
        LobbyState.SetHostedGames(list);
        StateChanged?.Invoke();
    }

    private void PruneStaleHostedGames()
    {
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-HostedGameLifetimeSeconds);
        bool changed = false;

        foreach (Dictionary<string, CnCNetHostedGameSummary> bucket in _gamesByBroadcast.Values.ToList())
        {
            foreach (CnCNetHostedGameSummary game in bucket.Values.ToList())
            {
                if (game.LastRefreshUtc >= cutoff)
                    continue;

                if (bucket.Remove(game.ChannelName))
                    changed = true;

                _games.Remove(game.ChannelName);
            }
        }

        if (changed)
            RefreshHostedGames();
    }

    private List<CnCNetHostedGameSummary> GetHostedGamesForCurrentChannel()
    {
        if (_currentGame?.HasGameBroadcast != true)
            return [];

        string broadcast = NormalizeIrcChannel(_currentGame.GameBroadcastChannel!);
        if (!_gamesByBroadcast.TryGetValue(broadcast, out Dictionary<string, CnCNetHostedGameSummary>? bucket))
            return [];

        return bucket.Values
            .Where(g => !UserINISettings.Instance.HideIncompatibleGames.Value || !g.Incompatible)
            .OrderBy(g => g.RoomName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshLobbyPlayers()
    {
        var players = _channelUsers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        LobbyState.SetChannelPlayers(players);
        StateChanged?.Invoke();
    }

    private bool IsChatChannel(string channel)
        => _currentGame != null
           && NormalizeIrcChannel(channel).Equals(
               NormalizeIrcChannel(_currentGame.ChatChannel),
               StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIrcChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        string normalized = channel.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        return normalized.ToLowerInvariant();
    }

    private void LogActivity(string message, bool notifyUi = true, bool connectionLog = true)
    {
        Logger.Log(message.StartsWith("CnCNet", StringComparison.Ordinal) ? message : $"CnCNet: {message}");
        if (connectionLog)
            LobbyState.AppendConnectionLog(message);
        if (notifyUi)
            StateChanged?.Invoke();
    }

    private static void ApplyPlayerNameFromUserSettings()
    {
        string? raw = UserINISettings.Instance.PlayerName.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string trimmed = raw.Trim();
        if (NameValidator.IsNameValid(trimmed, out string? errorMessage) == NameValidationError.None)
        {
            ProgramConstants.PLAYERNAME = trimmed;
            return;
        }

        Logger.Log($"CnCNet: saved player name is invalid for CnCNet: {errorMessage}");
    }

    private static string StripIrcPrefixes(string user)
    {
        int index = 0;
        while (index < user.Length && (user[index] == '@' || user[index] == '+' || user[index] == '%' || user[index] == '~' || user[index] == '&'))
            index++;

        return user[index..];
    }
}
