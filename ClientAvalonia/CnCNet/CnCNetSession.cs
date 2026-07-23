using ClientAvalonia.Online.EventArguments;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.CnCNet.Tunnels;
using ClientAvalonia.CnCNet.Waf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Media;
using ClientCore;
using ClientCore.Enums;
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
    private readonly HashSet<string> _gameBroadcastRejectHintsShown = new(StringComparer.Ordinal);
    /// <summary>Channels that returned permanent JOIN denial (e.g. IRC 474 +b). Do not auto-retry.</summary>
    private readonly HashSet<string> _joinPermanentlyDenied = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CnCNetPrivateMessageThread> _privateThreads =
        new(StringComparer.OrdinalIgnoreCase);
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
    private readonly CnCNetGameBroadcastDialect _broadcastDialect = new();
    private string _systemId = string.Empty;
    private bool _autoReconnect;
    private int _reconnectAttempts;
    private int _namesRetryCount;
    private bool _gameRoomJoinPending;
    private int _gameRoomJoinRetryCount;
    private IReadOnlyList<string>? _gameRoomJoinPasswordCandidates;
    private int _gameRoomJoinPasswordCandidateIndex;
    private Timer? _gameRoomJoinTimeoutTimer;
    private bool _tunnelRefreshInProgress;
    private uint _tunnelMaintenanceCycle;
    private readonly object _gameRoomGate = new();
    private CnCNetLaunchPresenceKeepAlive? _launchPresenceKeepAlive;

    public bool IsGameRoomJoinPending => _gameRoomJoinPending;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public event Action<string>? GameRoomJoinFailed;

    public CnCNetLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<CnCNetTunnel> Tunnels { get; private set; } = [];

    /// <summary>
    /// Min-heap of tunnels by latency (low-latency-tunnel.md v2). Updated
    /// automatically whenever tunnel pings complete. Raises
    /// <see cref="TunnelSorter.BestTunnelChanged"/> on the calling thread.
    /// </summary>
    public TunnelSorter TunnelSorter { get; } = new();

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

    /// <summary>Ingress WAF between IRC truth and lobby state writes. Owned/configured by SessionService.</summary>
    public ICnCNetIngressWaf? IngressWaf { get; set; }

    /// <summary>Test-only override for <see cref="CnCNetPrivateMessagePolicy.FromUserSettings"/>.</summary>
    internal AllowPrivateMessagesFromEnum? PrivateMessagePolicyOverrideForTests { get; set; }

    private const double HostedGameLifetimeSeconds = 35;
    private const int GameRoomJoinTimeoutSeconds = 45;
    private const int MaxGameRoomJoinRetries = 1;
    private const double HostedGameRefreshIntervalSeconds = 5;
    private const double CurrentTunnelPingIntervalSeconds = 20;
    private const uint CyclesPerTunnelListRefresh = 6;

    public void EnsureStarted()
    {
        _gameBroadcast.Dialect = _broadcastDialect;
        LogActivity($"Protocol revision {ProgramConstants.CNCNET_PROTOCOL_REVISION} (emit falls back per channel dialect).");

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
                IReadOnlyList<CnCNetTunnel> refreshed = CnCNetTunnelListLoader.Load();
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

    private void HandleRefreshedTunnels(IReadOnlyList<CnCNetTunnel> refreshed)
    {
        if (refreshed.Count == 0)
        {
            LogActivity("Tunnel list refresh returned no available NAT tunnels.");
            StateChanged?.Invoke();
            return;
        }

        List<CnCNetTunnel> updated = [];
        lock (_sync)
        {
            var existing = Tunnels.ToDictionary(t => $"{t.Address}:{t.Port}", StringComparer.OrdinalIgnoreCase);

            foreach (CnCNetTunnel tunnel in refreshed)
            {
                string key = $"{tunnel.Address}:{tunnel.Port}";
                if (existing.TryGetValue(key, out CnCNetTunnel? existingTunnel))
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
                CnCNetTunnel? activeTunnel = updated.FirstOrDefault(t =>
                    t.Address.Equals(_activeGameRoom.Tunnel.Address, StringComparison.OrdinalIgnoreCase)
                    && t.Port == _activeGameRoom.Tunnel.Port);

                if (activeTunnel != null)
                    _activeGameRoom.Tunnel = activeTunnel;
            }
        }

        LogActivity($"Loaded {updated.Count} NAT tunnels.");
        _gameBroadcastRejectHintsShown.Clear();
        RevalidateHostedGamesAgainstTunnels();

        // low-latency-tunnel.md v2: reset the heap on each tunnel-list refresh
        // so stale entries from the previous list don't bias BestTunnelChanged.
        TunnelSorter.Clear();

        PingListedTunnelsAsync(updated);

        if (_activeGameRoom != null && _gameRoom is { IsLocalJoined: true })
        {
            CnCNetTunnel? activeTunnel = updated.FirstOrDefault(t =>
                t.Address.Equals(_activeGameRoom.Tunnel.Address, StringComparison.OrdinalIgnoreCase)
                && t.Port == _activeGameRoom.Tunnel.Port);

            if (activeTunnel != null)
            {
                _activeGameRoom.Tunnel = activeTunnel;
                if (_connection?.IsLocalOnChannel(_activeGameRoom.ChannelName) == true)
                    _gameRoom.BroadcastLocalTunnelPing();
            }
            else
            {
                PingCurrentTunnelAsync(checkTunnelList: false);
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>Drop listed games whose tunnel is no longer reachable (DX re-check on tunnel refresh).</summary>
    private void RevalidateHostedGamesAgainstTunnels()
    {
        if (Tunnels.Count == 0)
            return;

        bool changed = false;
        foreach (Dictionary<string, CnCNetHostedGameSummary> bucket in _gamesByBroadcast.Values.ToList())
        {
            foreach (CnCNetHostedGameSummary game in bucket.Values.ToList())
            {
                bool tunnelOk = Tunnels.Any(t =>
                    t.Address.Equals(game.TunnelAddress, StringComparison.OrdinalIgnoreCase)
                    && t.Port == game.TunnelPort);

                if (tunnelOk)
                    continue;

                if (bucket.Remove(game.ChannelName))
                    changed = true;
                _games.Remove(game.ChannelName);
            }
        }

        if (changed)
            RefreshHostedGames();
    }

    private void PingListedTunnelsAsync(IReadOnlyList<CnCNetTunnel> tunnels)
    {
        foreach (CnCNetTunnel tunnel in tunnels)
        {
            if (!UserINISettings.Instance.PingUnofficialCnCNetTunnels.Value && !tunnel.Official && !tunnel.Recommended)
                continue;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                tunnel.UpdatePing();

                // low-latency-tunnel.md v2: feed every successful or failed
                // measurement into the min-heap so the best-tunnel signal fires
                // as soon as the fastest tunnel responds.
                TunnelSorter.Update(tunnel, tunnel.PingInMs);

                if (_activeGameRoom != null
                    && ReferenceEquals(_activeGameRoom.Tunnel, tunnel)
                    && _gameRoom is { IsLocalJoined: true }
                    && _connection?.IsLocalOnChannel(_activeGameRoom.ChannelName) == true)
                {
                    _gameRoom.BroadcastLocalTunnelPing();
                }

                StateChanged?.Invoke();
            });
        }
    }

    private void PingCurrentTunnelAsync(bool checkTunnelList)
    {
        // DX TunnelHandler: only ping/broadcast CurrentTunnel while in a joined game lobby.
        if (_gameRoom is not { IsLocalJoined: true } || _gameRoomJoinPending || _activeGameRoom == null)
            return;

        CnCNetTunnel tunnel = _activeGameRoom.Tunnel;
        CnCNetGameRoomSession gameRoom = _gameRoom;
        bool onGameChannel = _connection?.IsLocalOnChannel(_activeGameRoom.ChannelName) == true;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (checkTunnelList)
            {
                CnCNetTunnel? listedTunnel = Tunnels.FirstOrDefault(t =>
                    t.Address.Equals(tunnel.Address, StringComparison.OrdinalIgnoreCase)
                    && t.Port == tunnel.Port);

                if (listedTunnel != null)
                {
                    if (!ReferenceEquals(listedTunnel, tunnel))
                        _activeGameRoom.Tunnel = listedTunnel;

                    tunnel = _activeGameRoom.Tunnel;
                }
            }

            tunnel.UpdatePing();

            if (onGameChannel)
                gameRoom.BroadcastLocalTunnelPing();

            StateChanged?.Invoke();
        });
    }

    /// <summary>Warm tunnel ICMP ping when entering a game room (DX lobby shows ping before START).</summary>
    public void WarmActiveTunnelPing()
    {
        if (_activeGameRoom == null)
            return;

        CnCNetTunnel tunnel = ResolveListedTunnel(_activeGameRoom.Tunnel) ?? _activeGameRoom.Tunnel;
        _activeGameRoom.Tunnel = tunnel;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            tunnel.UpdatePing();
            if (_gameRoom is { IsLocalJoined: true }
                && _connection?.IsLocalOnChannel(_activeGameRoom.ChannelName) == true)
            {
                _gameRoom.BroadcastLocalTunnelPing();
            }

            StateChanged?.Invoke();
        });
    }

    private CnCNetTunnel? ResolveListedTunnel(CnCNetTunnel tunnel)
        => Tunnels.FirstOrDefault(t =>
            t.Address.Equals(tunnel.Address, StringComparison.OrdinalIgnoreCase)
            && t.Port == tunnel.Port);

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
                LogActivity(
                    $"No game collection entry for LocalGame={ClientConfiguration.Instance.LocalGame}. " +
                    "Add [CustomGames] in GameCollectionConfig.ini, set CnCNetChatChannel / CnCNetGameBroadcastChannel " +
                    "in ClientDefinitions.ini, or use a valid LocalGame id for #cncnet-{{id}} convention fallback.");
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
            _connection.ClearSendQueueForChannel(previous.ChatChannel);
            if (prevChat != localChat && prevChat != cncnetChat)
                _connection.PartChannel(previous.ChatChannel);

            // Leaving a game's listing frequency must PART + cancel pending JOINs; otherwise a
            // banned broadcast (IRC 474) keeps retrying forever after the user switched away.
            if (previous.HasGameBroadcast)
            {
                string prevBroadcast = previous.GameBroadcastChannel!;
                _connection.ClearSendQueueForChannel(prevBroadcast);
                // Always PART: membership may be pending (JOIN in flight / 474 pending).
                _connection.PartChannel(prevBroadcast);
                DropBroadcastChannelMembership(prevBroadcast);
            }
        }

        _currentGame = next;
        _selectedChannelIndex = gameIndex;
        _channelUsers.Clear();
        _namesRetryCount = 0;

        string nextChat = NormalizeIrcChannel(next.ChatChannel);
        if (IsJoinPermanentlyDenied(nextChat))
        {
            LogActivity(
                $"Cannot switch into {next.UiName}: previously banned from {nextChat} (IRC 474).",
                notifyUi: true);
        }
        else if (nextChat != localChat && nextChat != cncnetChat)
        {
            _connection.JoinChannelPersistent(next.ChatChannel, "ra1-derp");
        }

        JoinGameBroadcastChannel(next);
        _connection.RequestChannelNames(next.ChatChannel);
        LobbyState.SetChannelName(next.UiName, next.ChatChannel);
        UpdateChannelListState();
        // C3: refresh the hosted-game list against the new broadcast channel.
        // _gamesByBroadcast is keyed by channel name, so this immediately drops
        // the previous channel's games from the UI. The new channel's games will
        // appear as their GAME CTCPs arrive (OnGameBroadcast → RefreshHostedGames).
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

        if (IsJoinPermanentlyDenied(broadcast))
        {
            LogActivity(
                $"Skipping JOIN {broadcast}: permanently denied (ban/invite). Channel list will stay empty for this game.",
                notifyUi: false);
            return;
        }

        // DX Persistent Channel.Join: random delay + queue (dedupe key JOIN:#channel).
        // Instant welcome bursts previously flooded GameSurge; some hubs silently dropped JOINs.
        bool sent = _connection.JoinChannelPersistent(game.GameBroadcastChannel!);
        if (!sent)
            LogActivity($"Failed to JOIN game broadcast channel {broadcast} (not connected).");
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
        {
            _joinPermanentlyDenied.Remove(normalized);
            // Prefer R13 until the first peer GAME locks this listing channel.
            _broadcastDialect.EnterChannel(normalized);
            LogActivity($"Joined game broadcast channel {normalized}.", notifyUi: false);
        }
    }

    private void DropBroadcastChannelMembership(string channel)
    {
        string normalized = NormalizeIrcChannel(channel);
        if (_joinedBroadcastChannels.Remove(normalized))
        {
            _broadcastDialect.LeaveChannel(normalized);
            LogActivity($"Left game broadcast channel {normalized}.", notifyUi: false);
        }
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
            _joinPermanentlyDenied.Clear();
            _broadcastDialect.Clear();
            _connection.Disconnect();
        }
    }

    public void BeginDefaultPasswordJoinCandidates(IReadOnlyList<string>? candidates)
    {
        lock (_gameRoomGate)
        {
            _gameRoomJoinPasswordCandidates = candidates;
            _gameRoomJoinPasswordCandidateIndex = 0;
        }
    }

    public void SetActiveGameRoom(CnCNetActiveGameRoom room)
    {
        lock (_gameRoomGate)
        {
            if (_gameRoomJoinPending
                && _activeGameRoom != null
                && _activeGameRoom.ChannelName.Equals(room.ChannelName, StringComparison.OrdinalIgnoreCase))
            {
                LogActivity($"Already joining {room.RoomName}...", notifyUi: false);
                return;
            }

            string? previousChannel = _activeGameRoom?.ChannelName;
            bool sameChannel = !string.IsNullOrWhiteSpace(previousChannel)
                && previousChannel.Equals(room.ChannelName, StringComparison.OrdinalIgnoreCase);

            if (_gameRoom != null)
            {
                bool onChannel = _connection?.IsLocalOnChannel(room.ChannelName) == true;
                if (!sameChannel || _gameRoom.IsLocalJoined || onChannel)
                    _gameRoom.Leave();
            }

            if (_connection != null)
            {
                if (!string.IsNullOrWhiteSpace(previousChannel))
                    _connection.ClearSendQueueForChannel(previousChannel);

                _connection.ClearSendQueueForChannel(room.ChannelName);
            }

            DisarmGameRoomJoinTimeout();
            _gameRoomJoinRetryCount = 0;
            _activeGameRoom = room;
            _gameRoomJoinPending = true;
            _gameRoom = new CnCNetGameRoomSession(room);
            if (_connection != null)
                AttachGameRoomSession();

            WarmActiveTunnelPing();

            ArmGameRoomJoinTimeout();
        }
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
        _gameRoom.AvailableTunnelsProvider = () => Tunnels;
        _gameRoom.HostAbandoned += OnGameRoomHostAbandoned;
        _gameRoom.LocalUserKicked += OnGameRoomLocalUserKicked;
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
        _gameRoom.LocalUserKicked -= OnGameRoomLocalUserKicked;
    }

    private void OnGameRoomLocalUserKicked()
    {
        LogActivity("You were kicked from the game room.");
        LeaveGameRoom();
    }

    private void OnGameRoomHostAbandoned()
    {
        LogActivity("Game host abandoned ??leaving game room.");
        LeaveGameRoom();
        GameRoomHostAbandoned?.Invoke();
    }

    private void OnGameRoomStateChanged() => StateChanged?.Invoke();

    private void OnGameRoomNotice(string msg) => LogActivity(msg);

    private void OnGameRoomStarting(CnCNetStartGameInfo info) => GameStarting?.Invoke(info);

    public void LeaveGameRoom(bool restoreBroadcastChannels = true)
    {
        lock (_gameRoomGate)
        {
            DisarmGameRoomJoinTimeout();
            _gameRoomJoinRetryCount = 0;

            if (_connection != null && !string.IsNullOrWhiteSpace(_activeGameRoom?.ChannelName))
                _connection.ClearSendQueueForChannel(_activeGameRoom.ChannelName);

            if (_gameRoom != null)
            {
                DetachGameRoomSessionHandlers();
                _gameRoom.Leave();
            }

            _gameRoom = null;
            _activeGameRoom = null;
            _gameRoomJoinPending = false;
        }

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
                message = "Still joining the CnCNet game room ??please wait.";
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
        if (_connection == null || !_connection.IsConnected)
            return;

        if (string.IsNullOrWhiteSpace(message))
            return;

        int colorIndex = LobbyState.SelectedChatColorIndex;
        if (colorIndex < 0)
            colorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);

        CnCNetChatColorEntry color = CnCNetChatColorCatalog.GetEntry(colorIndex);

        // Route priority mirrors DX: when the player is inside a CnCNet game room,
        // the in-room channel owns the chat send target (see DX CnCNetGameLobby.SendChatMessage
        // -> channel.SendChatMessage). Otherwise we fall back to the lobby channel.
        string trimmedMessage = message.Trim();
        if (_gameRoom is { IsLocalJoined: true })
        {
            CnCNetGameRoomSession.RoomChatSendResult result =
                _gameRoom.TrySendChat(trimmedMessage, color.IrcColorId);
            if (result == CnCNetGameRoomSession.RoomChatSendResult.Failed)
                return;

            if (result == CnCNetGameRoomSession.RoomChatSendResult.SentAsChat)
            {
                _gameRoom.AppendLocalChat(
                    FormatChatLine(LocalNick, trimmedMessage, DateTime.Now),
                    color.DisplayColor);
            }

            StateChanged?.Invoke();
            return;
        }

        if (_currentGame == null)
            return;

        _connection.SendChatMessage(_currentGame.ChatChannel, trimmedMessage, color.IrcColorId);

        LobbyState.AddChatLine(new CnCNetChatLine
        {
            Scope = CnCNetChatScope.LobbyChannel,
            Sender = LocalNick,
            DisplayText = FormatChatLine(LocalNick, trimmedMessage, DateTime.Now),
            TextColor = color.DisplayColor,
        });
        StateChanged?.Invoke();
    }

    /// <summary>Send a private IRC message (DX PRIVMSG nick :text, no color prefix).</summary>
    public void SendPrivateMessage(string recipient, string message)
    {
        if (_connection is not { IsConnected: true })
            return;

        string nick = StripIrcPrefixes(recipient).Trim();
        string text = message.Trim();
        if (string.IsNullOrWhiteSpace(nick) || string.IsNullOrWhiteSpace(text))
            return;

        if (text.Length > 200)
            text = text[..200];

        _connection.SendPrivateMessage(nick, text);

        var line = new CnCNetChatLine
        {
            Scope = CnCNetChatScope.PrivateMessage,
            Sender = LocalNick,
            DisplayText = FormatChatLine(LocalNick, text, DateTime.Now),
            TextColor = CnCNetIrcChatText.DefaultChatColor,
        };
        AppendPrivateMessage(nick, line, incrementUnread: false);
        LastPrivateMessagePartner = nick;
        StateChanged?.Invoke();
    }

    public IReadOnlyList<(string Nick, int Unread)> GetPrivateConversationSummaries()
        => _privateThreads.Values
            .OrderByDescending(t => t.LastActivityUtc)
            .Select(t => (t.PeerNick, t.UnreadCount))
            .ToList();

    public IReadOnlyList<CnCNetChatLine> GetPrivateMessages(string peerNick)
    {
        string nick = StripIrcPrefixes(peerNick);
        return _privateThreads.TryGetValue(nick, out CnCNetPrivateMessageThread? thread)
            ? thread.Messages
            : [];
    }

    public int UnreadPrivateMessageCount => _privateThreads.Values.Sum(t => t.UnreadCount);

    public string? LastPrivateMessagePartner { get; private set; }

    /// <summary>
    /// Peer currently focused in the PM overlay. Incoming messages from this nick do not
    /// increment unread and do not raise status-bar popups.
    /// </summary>
    public string? ViewingPrivateMessagePeer { get; private set; }

    /// <summary>
    /// Raised after a private message is stored (UI may show a brief status toast).
    /// Args: peer nick, preview text (already sanitized for display).
    /// </summary>
    public event Action<string, string>? PrivateMessageArrived;

    public void SetViewingPrivateMessagePeer(string? peerNick)
    {
        if (string.IsNullOrWhiteSpace(peerNick))
        {
            ViewingPrivateMessagePeer = null;
            return;
        }

        ViewingPrivateMessagePeer = StripIrcPrefixes(peerNick);
        if (_privateThreads.TryGetValue(ViewingPrivateMessagePeer, out CnCNetPrivateMessageThread? thread)
            && thread.MarkRead())
        {
            StateChanged?.Invoke();
        }
    }

    public void EnsurePrivateConversation(string peerNick)
    {
        string nick = StripIrcPrefixes(peerNick);
        if (string.IsNullOrWhiteSpace(nick))
            return;

        if (!_privateThreads.ContainsKey(nick))
            _privateThreads[nick] = new CnCNetPrivateMessageThread(nick);

        LastPrivateMessagePartner = nick;
        _privateThreads[nick].MarkRead();
        StateChanged?.Invoke();
    }

    public void MarkPrivateMessagesRead(string? peerNick = null)
    {
        bool changed = false;
        if (string.IsNullOrWhiteSpace(peerNick))
        {
            foreach (CnCNetPrivateMessageThread thread in _privateThreads.Values)
                changed |= thread.MarkRead();
        }
        else if (_privateThreads.TryGetValue(StripIrcPrefixes(peerNick), out CnCNetPrivateMessageThread? thread))
        {
            changed = thread.MarkRead();
        }

        if (changed)
            StateChanged?.Invoke();
    }

    private void AppendPrivateMessage(string peerNick, CnCNetChatLine line, bool incrementUnread)
    {
        string nick = StripIrcPrefixes(peerNick);
        if (!_privateThreads.TryGetValue(nick, out CnCNetPrivateMessageThread? thread))
        {
            thread = new CnCNetPrivateMessageThread(nick);
            _privateThreads[nick] = thread;
        }

        thread.Append(line, incrementUnread);
    }

    /// <summary>Test hook: clear PM threads between serial tests.</summary>
    internal void ResetPrivateMessagingForTests()
    {
        _privateThreads.Clear();
        LastPrivateMessagePartner = null;
        ViewingPrivateMessagePeer = null;
        PrivateMessagePolicyOverrideForTests = null;
    }

    /// <summary>Test hook: seed chat-channel membership used by PM accept policy.</summary>
    internal void SeedChannelUsersForTests(params string[] nicks)
    {
        _channelUsers.Clear();
        foreach (string nick in nicks)
        {
            string name = StripIrcPrefixes(nick);
            if (!string.IsNullOrWhiteSpace(name))
                _channelUsers.Add(name);
        }
    }

    /// <summary>Test hook: run the same path as <see cref="OnPrivateMessageReceived"/>.</summary>
    internal void ProcessPrivateMessageReceivedForTests(string sender, string message)
        => OnPrivateMessageReceived(null, new CnCNetPrivateMessageEventArgs(sender, message));

    public void SetChatColorIndex(int index)
    {
        LobbyState.SelectedChatColorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(index);
        UserINISettings.Instance.ChatColor.Value = LobbyState.SelectedChatColorIndex;
        UserINISettings.Instance.SaveSettings();
    }

    /// <summary>Game process started — keep IRC available (yuanming mod marks AWAY players as Error in GAME CTCP).</summary>
    public void NotifyGameProcessStarted()
    {
        if (_connection is not { IsConnected: true })
            return;

        LogActivity("In-game (IRC away disabled for mod compatibility).", notifyUi: false);
    }

    /// <summary>Game process exited.</summary>
    public void NotifyGameProcessExited()
    {
        EndLaunchPresenceKeepAlive();
        ProgramConstants.IsLaunchingGame = false;
    }

    /// <summary>During slow Syringe startup — optional synthetic heartbeats via RA2MG.ini.</summary>
    public void BeginLaunchPresenceKeepAlive()
    {
        if (_gameRoom is not { IsLocalJoined: true } || _activeGameRoom == null)
            return;

        bool synthetic = UserINISettings.Instance.LaunchPresenceKeepAlive.Value;
        _launchPresenceKeepAlive ??= new CnCNetLaunchPresenceKeepAlive(this);
        _launchPresenceKeepAlive.Start(synthetic);
    }

    public void EndLaunchPresenceKeepAlive() => _launchPresenceKeepAlive?.Stop();

    private bool IsLaunchPresenceKeepAliveActive => _launchPresenceKeepAlive?.IsActive == true;

    internal void RunAcceleratedLaunchPresenceHeartbeat(Action<int> notePing)
    {
        if (_gameRoom is not { IsLocalJoined: true } || _activeGameRoom == null)
            return;

        PingCurrentTunnelAsync(checkTunnelList: false);

        if (_activeGameRoom.Tunnel.PingInMs >= 0)
            notePing(_activeGameRoom.Tunnel.PingInMs);
    }

    internal void RunSyntheticLaunchPresenceHeartbeat(int lastSuccessfulPingMs)
    {
        if (_activeGameRoom == null || _gameRoom is not { IsLocalJoined: true })
            return;

        if (_connection is not { IsConnected: true })
        {
            ConnectIfNeeded();
            return;
        }

        if (_connection.IsLocalOnChannel(_activeGameRoom.ChannelName) != true)
        {
            LogActivity("Launch keepalive: re-JOIN game room channel.", notifyUi: false);
            TryRejoinActiveGameRoom();
            return;
        }

        int ping = lastSuccessfulPingMs >= 0 ? lastSuccessfulPingMs : _activeGameRoom.Tunnel.PingInMs;
        if (ping < 0)
            ping = 999;

        _gameRoom.BroadcastTunnelPingValue(ping);
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

    public void JoinGameChannel(string channelName, string password, out string? error, string? roomNameHint = null)
    {
        error = null;
        if (_connection is not { IsConnected: true })
        {
            error = "IRC not connected.";
            return;
        }

        string wire = CnCNetIrcChannelNames.Preserve(channelName);
        lock (_gameRoomGate)
        {
            _connection.PrepareChannelJoin(wire);
            // MG: JOIN channelName password — preserve exact channel name from GAME payload.
            _connection.SendInstant($"JOIN {wire} {password}");
        }

        if (!string.IsNullOrEmpty(password) && _gameRoomJoinPasswordCandidates is { Count: > 0 })
        {
            // Primary +k is DX SHA1(channelName); candidates may also include MG channel+room.
            string inputHint = _gameRoomJoinPasswordCandidateIndex == 0
                ? wire
                : string.IsNullOrEmpty(roomNameHint) ? wire : $"{wire}+{roomNameHint}";
            LogActivity(
                $"JOIN game channel {wire} (default +k candidate {_gameRoomJoinPasswordCandidateIndex + 1}/{_gameRoomJoinPasswordCandidates.Count}, SHA1 input: \"{inputHint}\")",
                notifyUi: false);
        }
        else
        {
            LogActivity($"? JOIN game channel {wire}", notifyUi: false);
        }
    }

    private void ArmGameRoomJoinTimeout()
    {
        DisarmGameRoomJoinTimeout();
        _gameRoomJoinTimeoutTimer = new Timer(_ =>
        {
            if (!_gameRoomJoinPending)
                return;

            FailGameRoomJoin("Timed out joining the game room. Try again.");
        }, null, TimeSpan.FromSeconds(GameRoomJoinTimeoutSeconds), Timeout.InfiniteTimeSpan);
    }

    private void DisarmGameRoomJoinTimeout()
    {
        _gameRoomJoinTimeoutTimer?.Dispose();
        _gameRoomJoinTimeoutTimer = null;
    }

    private void FailGameRoomJoin(string message)
    {
        DisarmGameRoomJoinTimeout();
        LogActivity($"Join failed: {message}");
        _gameRoomJoinPending = false;
        _gameRoomJoinRetryCount = 0;
        _gameRoomJoinPasswordCandidates = null;
        _gameRoomJoinPasswordCandidateIndex = 0;
        GameRoomJoinFailed?.Invoke(message);
        LeaveGameRoom();
    }

    private void CompleteLocalGameRoomJoin()
    {
        if (!_gameRoomJoinPending || _activeGameRoom == null || _gameRoom == null)
            return;

        DisarmGameRoomJoinTimeout();
        _gameRoomJoinPending = false;
        _gameRoomJoinRetryCount = 0;
        _gameRoomJoinPasswordCandidates = null;
        _gameRoomJoinPasswordCandidateIndex = 0;

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

        if (!_connection.IsLocalOnChannel(_activeGameRoom.ChannelName))
            return;

        bool localPresent = users.Any(u => _connection.IsLocalUser(StripIrcPrefixes(u)));
        if (localPresent)
            CompleteLocalGameRoomJoin();
    }

    private void OnChannelJoinFailed(int code, string channel, string detail)
    {
        string normalized = NormalizeIrcChannel(channel);
        _connection?.ClearSendQueueForChannel(channel);

        // Permanent denials: ban (+b), channel does not exist, too many channels.
        // Transient: 439 (target change too fast), 471 (limit) may recover — still avoid tight loops.
        bool permanent = code is 474 or 473 or 476 or 405;
        if (permanent)
            _joinPermanentlyDenied.Add(normalized);

        if (!string.IsNullOrWhiteSpace(channel) && IsKnownBroadcastChannel(channel))
        {
            DropBroadcastChannelMembership(channel);
            if (permanent)
            {
                LogActivity(
                    $"Game broadcast JOIN denied for {normalized} (IRC {code}) — not retrying. {detail}".Trim(),
                    notifyUi: true);
                return;
            }

            if (_autoReconnect && !IsJoinPermanentlyDenied(normalized))
            {
                LogActivity($"Game broadcast channel join failed (IRC {code}) — will retry.", notifyUi: false);
                EnsureGameBroadcastChannelsJoined();
            }

            return;
        }

        // Chat-channel ban while switching (e.g. #cncnet-mo +b): stop and surface clearly.
        if (_currentGame != null
            && NormalizeIrcChannel(_currentGame.ChatChannel).Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            if (code == 474)
            {
                LogActivity(
                    $"Cannot join chat channel {_currentGame.UiName} ({normalized}): you are banned.",
                    notifyUi: true);
            }
            else
            {
                LogActivity(
                    $"Cannot join chat channel {_currentGame.UiName} ({normalized}) (IRC {code}): {detail}".Trim(),
                    notifyUi: true);
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

        if (code == 475
            && !_activeGameRoom.Passworded
            && _gameRoomJoinPasswordCandidates is { Count: > 0 } candidates
            && _gameRoomJoinPasswordCandidateIndex + 1 < candidates.Count)
        {
            _gameRoomJoinPasswordCandidateIndex++;
            _activeGameRoom.Password = candidates[_gameRoomJoinPasswordCandidateIndex];
            LogActivity(
                $"IRC +k rejected — trying alternate default key ({_gameRoomJoinPasswordCandidateIndex + 1}/{candidates.Count}).",
                notifyUi: false);
            JoinGameChannel(_activeGameRoom.ChannelName, _activeGameRoom.Password, out _, _activeGameRoom.RoomName);
            return;
        }

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

    private bool IsJoinPermanentlyDenied(string normalizedChannel)
        => _joinPermanentlyDenied.Contains(NormalizeIrcChannel(normalizedChannel));

    public void Dispose()
    {
        _autoReconnect = false;
        _launchPresenceKeepAlive?.Dispose();
        _launchPresenceKeepAlive = null;
        _hostedGameRefreshTimer?.Dispose();
        _hostedGameRefreshTimer = null;
        _tunnelMaintenanceTimer?.Dispose();
        _tunnelMaintenanceTimer = null;
        DisarmGameRoomJoinTimeout();
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
        connection.PrivateCTCPReceived += OnPrivateCtcp;
        connection.PrivateMessageReceived += OnPrivateMessageReceived;
        connection.UserKicked += OnUserKicked;
        connection.UserNicknameChanged += OnUserNicknameChanged;
        connection.UserQuit += OnUserQuit;
        connection.ChatMessageReceived += OnChatMessageReceived;
        connection.ChannelNamesComplete += OnChannelNamesComplete;
        connection.ChannelJoinFailed += OnChannelJoinFailed;
        connection.NotOnChannel += OnNotOnChannel;
        connection.ChannelModeChanged += OnChannelModeChanged;
        connection.ActivityLogged += msg => LogActivity(msg, notifyUi: false);
    }

    private void OnChannelModeChanged(object? sender, Online.EventArguments.ChannelModeEventArgs e)
    {
        _gameRoom?.OnChannelModesChanged(e.ChannelName, e.ModeString);
        StateChanged?.Invoke();
    }

    private void OnNotOnChannel(string channel)
    {
        string normalized = NormalizeIrcChannel(channel);

        if (_gameRoomJoinPending
            && _activeGameRoom != null
            && normalized.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            _connection?.ClearSendQueueForChannel(normalized);

            if (_gameRoomJoinRetryCount < MaxGameRoomJoinRetries)
            {
                _gameRoomJoinRetryCount++;
                LogActivity(
                    $"Not on game room channel {normalized} yet ??retrying JOIN ({_gameRoomJoinRetryCount}/{MaxGameRoomJoinRetries}).",
                    notifyUi: false);
                JoinGameChannel(_activeGameRoom.ChannelName, _activeGameRoom.Password, out _, _activeGameRoom.RoomName);
                return;
            }

            FailGameRoomJoin("Could not join the game room (IRC 442). Try again.");
            return;
        }

        if (!_autoReconnect || !IsKnownBroadcastChannel(channel))
            return;

        DropBroadcastChannelMembership(channel);
        LogActivity($"Not on game broadcast channel {normalized} ??rejoining.", notifyUi: false);
        EnsureGameBroadcastChannelsJoined();
    }

    private void OnAnyCtcp(string channel, string sender, string ctcp)
    {
        if (ctcp.StartsWith("INVITE ", StringComparison.Ordinal))
        {
            WafDecision inviteDecision = EvaluateCtcpWaf(
                WafIngressKind.ChannelCtcp,
                WafSurface.Protocol,
                channel,
                sender,
                ctcpCommand: "INVITE",
                ctcpPayload: ctcp[7..],
                displayText: ctcp[7..]);
            if (inviteDecision.Severity == WafSeverity.Drop)
                return;

            HandleGameInvite(sender, ctcp[7..], skipWaf: true);
            return;
        }

        OnChannelCtcp(channel, sender, ctcp);
    }

    private void OnPrivateCtcp(object? sender, PrivateCTCPEventArgs e)
    {
        string payload = e.Message ?? string.Empty;
        string command = payload;
        string args = string.Empty;
        int sp = payload.IndexOf(' ');
        if (sp > 0)
        {
            command = payload[..sp];
            args = payload[(sp + 1)..];
        }

        WafDecision decision = EvaluateCtcpWaf(
            WafIngressKind.PrivateCtcp,
            WafSurface.PrivateMessage,
            channel: string.Empty,
            sender: e.Sender,
            ctcpCommand: command,
            ctcpPayload: args,
            displayText: args);

        if (decision.Severity == WafSeverity.Drop)
            return;

        if (payload.StartsWith("INVITE ", StringComparison.Ordinal))
            HandleGameInvite(e.Sender, payload[7..], skipWaf: true);
    }

    private void OnPrivateMessageReceived(object? sender, CnCNetPrivateMessageEventArgs e)
    {
        string peer = StripIrcPrefixes(e.Sender);
        if (string.IsNullOrWhiteSpace(peer))
            return;

        AllowPrivateMessagesFromEnum policy =
            PrivateMessagePolicyOverrideForTests ?? CnCNetPrivateMessagePolicy.FromUserSettings();
        bool inChannel = _channelUsers.Contains(peer);
        if (!CnCNetPrivateMessagePolicy.ShouldAccept(policy, inChannel))
        {
            LogActivity($"PM from {peer} ignored (AllowPrivateMessagesFrom={policy}).", notifyUi: false);
            return;
        }

        bool isAction = CnCNetIrcChatText.TryNormalizeActionCtcp(e.Message, out string actionBody);
        string textSource = isAction ? actionBody : e.Message;
        (string text, Color color) = CnCNetIrcChatText.Parse(
            textSource, CnCNetIrcChatText.DefaultChatColor);

        WafDecision decision = EvaluateChatWaf(
            isAction ? WafIngressKind.PrivateAction : WafIngressKind.PrivateChat,
            WafSurface.PrivateMessage,
            channel: string.Empty,
            sender: peer,
            displayText: text,
            rawBody: e.Message,
            senderIdent: e.Ident,
            senderHost: e.Host);

        if (decision.Severity == WafSeverity.Drop)
            return;

        // Private ACTION: "[time] nick: ====> body" (nick once — do not pre-embed nick in body).
        string display = FormatChatLine(peer, isAction ? $"====> {text}" : text, DateTime.Now);
        if (decision.Severity >= WafSeverity.Warn)
            display = "[风险] " + display;

        var line = new CnCNetChatLine
        {
            Scope = CnCNetChatScope.PrivateMessage,
            Sender = peer,
            DisplayText = display,
            TextColor = color,
            RiskLevel = decision.Severity,
            RiskSummary = decision.Summary,
        };

        bool viewingThisPeer = ViewingPrivateMessagePeer != null
            && ViewingPrivateMessagePeer.Equals(peer, StringComparison.OrdinalIgnoreCase);

        AppendPrivateMessage(peer, line, incrementUnread: !viewingThisPeer);
        LastPrivateMessagePartner = peer;
        LogActivity($"PM from {peer}: {text}", notifyUi: false);

        if (!viewingThisPeer)
            PrivateMessageArrived?.Invoke(peer, text);

        StateChanged?.Invoke();
    }

    private void OnUserKicked(object? sender, KickEventArgs e)
    {
        if (_activeGameRoom == null)
            return;

        string gameChannel = NormalizeIrcChannel(_activeGameRoom.ChannelName);
        if (!gameChannel.Equals(NormalizeIrcChannel(e.ChannelName), StringComparison.OrdinalIgnoreCase))
            return;

        _gameRoom?.OnUserKicked(e.ChannelName, e.UserName);
    }

    private void OnUserNicknameChanged(object? sender, UserNicknameEventArgs e)
    {
        string oldName = StripIrcPrefixes(e.OldNickname);
        string newName = StripIrcPrefixes(e.NewNickname);
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
            return;

        ReplaceChannelUserName(oldName, newName);
        _gameRoom?.OnUserNicknameChanged(oldName, newName);

        if (RemoveHostedGamesByHost(oldName))
            RefreshHostedGames();
    }

    private void OnUserQuit(object? sender, UserNicknameEventArgs e)
    {
        string name = StripIrcPrefixes(e.OldNickname);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_activeGameRoom != null)
            _gameRoom?.OnUserLeft(_activeGameRoom.ChannelName, name);

        if (RemoveHostedGamesByHost(name))
            return;

        if (_currentGame == null)
            return;

        _channelUsers.Remove(name);
        RefreshLobbyPlayers();
    }

    private void ReplaceChannelUserName(string oldName, string newName)
    {
        if (_channelUsers.Remove(oldName))
            _channelUsers.Add(newName);
    }

    private void HandleGameInvite(string sender, string arguments, bool skipWaf = false)
    {
        if (!skipWaf)
        {
            WafDecision decision = EvaluateCtcpWaf(
                WafIngressKind.PrivateCtcp,
                WafSurface.Protocol,
                channel: string.Empty,
                sender,
                ctcpCommand: "INVITE",
                ctcpPayload: arguments,
                displayText: arguments);
            if (decision.Severity == WafSeverity.Drop)
                return;
        }

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
                DropBroadcastChannelMembership(game.GameBroadcastChannel!);
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
            return;

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

        // DX CnCNetLobby.ConnectionManager_WelcomeMessageReceived → Channel.Join (Persistent):
        // random anti-flood delay + send queue. Instant JOINs here previously flooded GameSurge
        // (welcome plan + EnsureGameBroadcastChannelsJoined) and some hubs never echoed JOIN.
        foreach (CnCNetWelcomeChannelPlan.JoinStep step in CnCNetWelcomeChannelPlan.BuildForLocalGame(_currentGame))
        {
            if (step.Role == "broadcast")
                continue;

            _connection.JoinChannelPersistent(step.Channel, step.Key);
        }

        JoinGameBroadcastChannel(_currentGame);
        JoinFollowedBroadcastChannels();
        _namesRetryCount = 0;
        _connection.RequestChannelNames(_currentGame.ChatChannel);

        LobbyState.SetConnectionStatus("Connected");
        LobbyState.SelectedChatColorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);
        _reconnectAttempts = 0;
        string chatChannel = NormalizeIrcChannel(_currentGame.ChatChannel);
        LogActivity($"JOIN {chatChannel}, #cncnet + broadcast channels; NAMES requested.");
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
        bool inGameOrLaunching = ProgramConstants.IsInGame;
        ClearLobbyData(preserveGameRoom: _activeGameRoom != null);
        LobbyState.SetConnectionStatus("Offline");
        LogActivity(inGameOrLaunching
            ? $"Disconnected during game/launch: {message}"
            : $"Disconnected: {message}");
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
        // Allow reconnect during slow Syringe/Ares startup while launch keepalive is active.
        if (!_autoReconnect || _reconnectAttempts >= 3)
            return;

        if (ProgramConstants.IsInGame && !IsLaunchPresenceKeepAliveActive)
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
        _broadcastDialect.Clear();
        _followedGameIds.Clear();
        LobbyState.SetChannelPlayers([]);
        LobbyState.SetHostedGames([]);

        if (preserveGameRoom)
        {
            LogActivity("IRC disconnected ??preserving active game room for reconnect.");
            return;
        }

        _gameRoom = null;
        _activeGameRoom = null;
        _gameRoomJoinPending = false;
        _gameRoomJoinRetryCount = 0;
        _gameRoomJoinPasswordCandidates = null;
        _gameRoomJoinPasswordCandidateIndex = 0;
        DisarmGameRoomJoinTimeout();
    }

    private void TryRejoinActiveGameRoom()
    {
        if (_activeGameRoom == null || _connection is not { IsConnected: true })
            return;

        // Re-JOIN during launch keepalive; skip once in match without keepalive (DX).
        if (ProgramConstants.IsInGame && !IsLaunchPresenceKeepAliveActive)
            return;

        var gameSummary = new CnCNetHostedGameSummary
        {
            HostName = _activeGameRoom.HostName,
            RoomName = _activeGameRoom.RoomName,
            ChannelName = _activeGameRoom.ChannelName,
            RequiresPassword = _activeGameRoom.Passworded,
        };

        if (CnCNetLobbyOperations.TryResolveJoinPassword(
                gameSummary,
                _activeGameRoom.Passworded ? _activeGameRoom.Password : null,
                out string joinPassword,
                out IReadOnlyList<string>? defaultPasswordCandidates,
                out _))
        {
            _activeGameRoom.Password = joinPassword;
            BeginDefaultPasswordJoinCandidates(defaultPasswordCandidates);
        }

        _gameRoomJoinPending = true;
        _gameRoomJoinRetryCount = 0;
        JoinGameChannel(_activeGameRoom.ChannelName, _activeGameRoom.Password, out _, _activeGameRoom.RoomName);
        AttachGameRoomSession();
        ArmGameRoomJoinTimeout();
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
    {
        if (_gameRoom == null)
            return;

        try
        {
            _gameRoom.OnChannelCtcp(channel, sender, ctcp);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet CTCP handle failed ({channel}, {sender}): {ex.Message}");
            Logger.Log(ex.ToString());
        }
    }

    private void OnChatMessageReceived(string channel, string sender, string message)
    {
        bool isAction = CnCNetIrcChatText.TryNormalizeActionCtcp(message, out string actionBody);
        string parseSource = isAction ? actionBody : message;

        // Route to the in-room timeline when the message arrived on the active game-room channel.
        // This mirrors DX: the room Channel.MessageAdded feeds CnCNetGameLobby.Channel_MessageAdded
        // -> lbChatMessages, independent of the lobby channel.
        if (_gameRoom != null
            && _activeGameRoom != null
            && NormalizeIrcChannel(channel).Equals(
                NormalizeIrcChannel(_activeGameRoom.ChannelName),
                StringComparison.OrdinalIgnoreCase))
        {
            (string roomText, Color roomColor) = CnCNetIrcChatText.Parse(
                parseSource, CnCNetIrcChatText.DefaultChatColor);

            WafDecision roomDecision = EvaluateChatWaf(
                isAction ? WafIngressKind.ChannelAction : WafIngressKind.ChannelChat,
                WafSurface.GameRoomChat,
                channel,
                sender,
                roomText,
                message);
            if (roomDecision.Severity == WafSeverity.Drop)
                return;

            string roomDisplay = FormatChatLine(
                sender,
                isAction ? $"====> {roomText}" : roomText,
                DateTime.Now);
            if (roomDecision.Severity >= WafSeverity.Warn)
                roomDisplay = "[风险] " + roomDisplay;

            _gameRoom.AppendRemoteChat(
                sender,
                roomDisplay,
                isSystem: false,
                roomColor);
            StateChanged?.Invoke();
            return;
        }

        if (_currentGame == null || !IsChatChannel(channel))
            return;

        (string text, Color color) = CnCNetIrcChatText.Parse(
            parseSource, CnCNetIrcChatText.DefaultChatColor);

        WafDecision decision = EvaluateChatWaf(
            isAction ? WafIngressKind.ChannelAction : WafIngressKind.ChannelChat,
            WafSurface.LobbyChat,
            channel,
            sender,
            text,
            message);
        if (decision.Severity == WafSeverity.Drop)
            return;

        string display = FormatChatLine(sender, isAction ? $"====> {text}" : text, DateTime.Now);
        if (decision.Severity >= WafSeverity.Warn)
            display = "[风险] " + display;

        LobbyState.AddChatLine(new CnCNetChatLine
        {
            Scope = CnCNetChatScope.LobbyChannel,
            Sender = sender,
            DisplayText = display,
            TextColor = color,
            RiskLevel = decision.Severity,
            RiskSummary = decision.Summary,
        });
        StateChanged?.Invoke();
    }

    private static string FormatChatLine(string sender, string message, DateTime time)
        => string.IsNullOrEmpty(sender)
            ? $"[{time:HH:mm}] {message}"
            : $"[{time:HH:mm}] {sender}: {message}";

    private void OnGameBroadcast(string channel, string sender, string ctcp)
    {
        string normalizedBroadcast = NormalizeIrcChannel(channel);

        // First peer GAME on a joined listing channel locks R13 or R10 for the stay.
        // Skip our own echo so hosting unlocked-R13 does not pin an R10 channel wrongly.
        bool fromLocal = sender.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase);
        _broadcastDialect.ObserveInbound(normalizedBroadcast, ctcp, fromLocalSender: fromLocal);

        CnCNetGameEntry? sourceGame = _gameCollection?.FindByBroadcastChannel(normalizedBroadcast);
        if (sourceGame == null)
        {
            // C4: keep the strict known-channel contract (matches XNA GameBroadcastChannel_CTCPReceived:
            // `cncnetGame == null` → return). Improving the diagnostics here so admins can spot
            // either a misconfigured GameCollectionConfig.ini or a non-standard broadcast channel
            // without having to enable verbose logging.
            string known = _gameCollection == null
                ? "<no game collection loaded>"
                : string.Join(", ", _gameCollection.Games
                    .Where(g => g.HasGameBroadcast)
                    .Select(g => NormalizeIrcChannel(g.GameBroadcastChannel!)));
            Logger.Log(
                $"CnCNet: ignoring GAME from unknown broadcast channel {normalizedBroadcast} "
                + $"(sender={sender}). Known broadcast channels: [{known}]. "
                + "Verify GameCollectionConfig.ini / CnCNetGameBroadcastChannel in ClientDefinitions.ini.");
            return;
        }

        // Tunnel list starts empty and is filled asynchronously. Passing an empty list into the
        // parser makes every GAME reject with "no available tunnels" — which permanently empties
        // the lobby if the master-list HTTP is slow or unreachable. DX shows a warning in that
        // case; we skip validation until at least one tunnel is loaded, then
        // RevalidateHostedGamesAgainstTunnels prunes entries whose tunnel never appears.
        IReadOnlyList<CnCNetTunnel>? tunnelsForParse = Tunnels.Count > 0 ? Tunnels : null;

        CnCNetHostedGameSummary? game = CnCNetGameMessageParser.TryParse(
            sender,
            ctcp,
            tunnelsForParse,
            out string? rejectReason,
            sourceGameId: sourceGame.InternalName);
        if (game == null)
        {
            if (ctcp.StartsWith("GAME ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(rejectReason))
            {
                Logger.Log($"CnCNet: GAME from {sender} ignored: {rejectReason}");
                // Surface once-per-reason so an empty lobby is explainable (DX shows chat prompts).
                NotifyGameBroadcastRejectOnce(rejectReason!);
                EvaluateRejectedGameBroadcast(channel, sender, ctcp);
            }

            return;
        }

        WafDecision waf = EvaluateGameBroadcastWaf(channel, sender, ctcp, game);
        if (waf.Severity == WafSeverity.Drop)
            return;

        game.RiskLevel = waf.Severity;
        game.RiskSummary = waf.Summary;

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
            if (IsCurrentGameBroadcast(normalizedBroadcast) && game.RiskLevel != WafSeverity.Hide)
            {
                LogActivity(
                    $"Game listed: {game.RoomName} by {sender} ({game.PlayerCount}/{game.MaxPlayers})",
                    notifyUi: false);
            }
        }

        RefreshHostedGames();
    }

    /// <summary>
    /// DX CnCNetLobby shows a one-shot chat prompt when GAME CTCPs arrive but cannot be listed
    /// (no tunnels / invalid field count / …). Mirror that so an empty lobby is diagnosable.
    /// </summary>
    private void NotifyGameBroadcastRejectOnce(string rejectReason)
    {
        // Collapse noisy per-sender reasons into stable buckets.
        string bucket = rejectReason.StartsWith("unsupported protocol", StringComparison.OrdinalIgnoreCase)
            ? "unsupported protocol"
            : rejectReason.StartsWith("tunnel ", StringComparison.OrdinalIgnoreCase) && rejectReason.Contains("unavailable", StringComparison.Ordinal)
                ? "tunnel unavailable"
                : rejectReason.StartsWith("invalid field count", StringComparison.OrdinalIgnoreCase)
                    ? "invalid field count"
                    : rejectReason;

        if (!_gameBroadcastRejectHintsShown.Add(bucket))
            return;

        string hint = bucket switch
        {
            "unsupported protocol" =>
                "Received game broadcasts with an unsupported protocol revision. "
                + $"This client requires {ProgramConstants.CNCNET_PROTOCOL_REVISION}.",
            "no available tunnels" =>
                "Received game broadcasts but no NAT tunnels are loaded yet. Waiting for the tunnel list…",
            "tunnel unavailable" =>
                "Received game broadcasts whose tunnel servers are not on the current master list.",
            "invalid field count" =>
                "Received game broadcasts with an unexpected field layout "
                + "(expected stock DX 13 fields / R13, or legacy 11 fields / R10).",
            _ => $"Received game broadcasts that could not be listed ({bucket}).",
        };

        LogActivity(hint);
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

        // Keep WAF tunnel/template/rate caches aligned with hosted-game lifetime
        // (2× so shared-tunnel / template detection still sees a short overlap).
        try
        {
            IngressWaf?.PruneEphemeralState(TimeSpan.FromSeconds(HostedGameLifetimeSeconds * 2));
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet WAF prune failed: {ex.Message}");
        }

        if (changed)
            RefreshHostedGames();
    }

    private List<CnCNetHostedGameSummary> GetHostedGamesForCurrentChannel()
    {
        // C5/C6 contract (per user rule): the game list shows ONLY the rooms of
        // the currently-selected channel's broadcast frequency. Switching the
        // ddCurrentChannel dropdown → SwitchToGame → reassigns _currentGame →
        // this method returns the new channel's bucket. _gamesByBroadcast is
        // kept per-channel (not flattened) so users can switch back and forth
        // without losing cached entries (within HostedGameLifetimeSeconds = 35s).
        if (_currentGame?.HasGameBroadcast != true)
            return [];

        string broadcast = NormalizeIrcChannel(_currentGame.GameBroadcastChannel!);
        if (!_gamesByBroadcast.TryGetValue(broadcast, out Dictionary<string, CnCNetHostedGameSummary>? bucket))
            return [];

        return bucket.Values
            .Where(g => g.RiskLevel != WafSeverity.Hide)
            .Where(g => !UserINISettings.Instance.HideIncompatibleGames.Value || !g.Incompatible)
            .OrderBy(g => g.RoomName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private WafDecision EvaluateGameBroadcastWaf(
        string channel,
        string sender,
        string ctcp,
        CnCNetHostedGameSummary game)
    {
        ICnCNetIngressWaf? waf = IngressWaf;
        if (waf == null || !waf.IsEnabled)
            return WafDecision.Allow;

        ResolveActor(sender, out string ident, out string host);
        return waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            Channel = channel,
            SenderNick = sender,
            SenderIdent = ident,
            SenderHost = host,
            RawBody = ctcp,
            DisplayText = game.RoomName,
            CtcpCommand = "GAME",
            CtcpPayload = ctcp,
            Game = new WafGameBroadcastFields
            {
                Revision = game.Revision,
                FieldCount = game.FieldCount,
                RoomName = game.RoomName,
                MapName = game.MapName,
                GameMode = game.GameMode,
                TunnelHost = game.TunnelAddress,
                TunnelPort = game.TunnelPort,
                ChannelName = game.ChannelName,
                Players = game.Players,
            },
        });
    }

    private void EvaluateRejectedGameBroadcast(string channel, string sender, string ctcp)
    {
        ICnCNetIngressWaf? waf = IngressWaf;
        if (waf == null || !waf.IsEnabled)
            return;

        if (!WafGameBroadcastPeek.TryPeek(ctcp, out WafGameBroadcastFields fields))
            return;

        ResolveActor(sender, out string ident, out string host);
        // Alert-only: parser already rejected listing; still score host-bot fingerprints.
        waf.Evaluate(new WafIngressEvent
        {
            Kind = WafIngressKind.GameBroadcast,
            Surface = WafSurface.Protocol,
            Channel = channel,
            SenderNick = sender,
            SenderIdent = ident,
            SenderHost = host,
            RawBody = ctcp,
            DisplayText = fields.RoomName,
            CtcpCommand = "GAME",
            CtcpPayload = ctcp,
            Game = fields,
        });
    }

    private WafDecision EvaluateChatWaf(
        WafIngressKind kind,
        WafSurface surface,
        string channel,
        string sender,
        string displayText,
        string rawBody,
        string? senderIdent = null,
        string? senderHost = null)
    {
        ICnCNetIngressWaf? waf = IngressWaf;
        if (waf == null || !waf.IsEnabled)
            return WafDecision.Allow;

        if (string.IsNullOrWhiteSpace(senderIdent) || string.IsNullOrWhiteSpace(senderHost))
        {
            ResolveActor(sender, out string cachedIdent, out string cachedHost);
            if (string.IsNullOrWhiteSpace(senderIdent))
                senderIdent = cachedIdent;
            if (string.IsNullOrWhiteSpace(senderHost))
                senderHost = cachedHost;
        }

        return waf.Evaluate(new WafIngressEvent
        {
            Kind = kind,
            Surface = surface,
            Channel = channel,
            SenderNick = sender,
            SenderIdent = senderIdent ?? string.Empty,
            SenderHost = senderHost ?? string.Empty,
            DisplayText = displayText,
            RawBody = rawBody,
        });
    }

    private WafDecision EvaluateCtcpWaf(
        WafIngressKind kind,
        WafSurface surface,
        string channel,
        string sender,
        string ctcpCommand,
        string ctcpPayload,
        string displayText)
    {
        ICnCNetIngressWaf? waf = IngressWaf;
        if (waf == null || !waf.IsEnabled)
            return WafDecision.Allow;

        ResolveActor(sender, out string ident, out string host);
        return waf.Evaluate(new WafIngressEvent
        {
            Kind = kind,
            Surface = surface,
            Channel = channel,
            SenderNick = sender,
            SenderIdent = ident,
            SenderHost = host,
            DisplayText = displayText,
            RawBody = ctcpCommand + " " + ctcpPayload,
            CtcpCommand = ctcpCommand,
            CtcpPayload = ctcpPayload,
        });
    }

    private void ResolveActor(string sender, out string ident, out string host)
    {
        ident = string.Empty;
        host = string.Empty;
        _connection?.TryGetActor(sender, out ident, out host);
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
