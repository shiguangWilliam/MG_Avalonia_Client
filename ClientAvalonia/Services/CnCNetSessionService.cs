using Avalonia.Threading;
using ClientAvalonia.Network;
using ClientCore;
using ClientCore.Network;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>CnCNet session: IRC connect, channel join, tunnel list, player/game state for Avalonia UI.</summary>
public sealed class CnCNetSessionService : IDisposable
{
    public static CnCNetSessionService Instance { get; } = new();

    private readonly object _sync = new();
    private readonly Dictionary<string, CnCNetHostedGameSummary> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _channelUsers = new(StringComparer.OrdinalIgnoreCase);

    private CnCNetIrcConnection? _connection;
    private CnCNetGameChannels? _channels;
    private CnCNetPlayerCountService? _playerCountService;
    private CnCNetActiveGameRoom? _activeGameRoom;
    private readonly CnCNetGameBroadcastService _gameBroadcast = new();
    private string _systemId = string.Empty;
    private bool _autoReconnect;
    private int _reconnectAttempts;
    private int _namesRetryCount;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public MultiplayerLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<CnCNetTunnelEntry> Tunnels { get; private set; } = [];

    public CnCNetIrcConnection? Connection => _connection;

    public CnCNetGameChannels? Channels => _channels;

    public CnCNetActiveGameRoom? ActiveGameRoom => _activeGameRoom;

    public void EnsureStarted()
    {
        ClientLogService.EnsureInitialized();

        if (_playerCountService == null)
        {
            _playerCountService = new CnCNetPlayerCountService();
            _playerCountService.PlayerCountUpdated += count =>
            {
                OnlinePlayerCount = count;
                LogActivity($"Online players (HTTP): {count}");
                Dispatcher.UIThread.Post(() =>
                {
                    LobbyState.SetOnlinePlayerCount(count);
                    StateChanged?.Invoke();
                });
            };
            _playerCountService.Start();
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Tunnels = CnCNetTunnelListLoader.Load();
                LogActivity($"Loaded {Tunnels.Count} NAT tunnels.");
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshHostedGames();
                    StateChanged?.Invoke();
                });
            }
            catch (Exception ex)
            {
                LogActivity($"Tunnel list failed: {ex.Message}");
            }
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

            _channels = CnCNetGameChannels.LoadForLocalGame();
            if (_channels == null)
            {
                LogActivity("No GameCollectionConfig channels for LocalGame — IRC connect skipped.");
                LobbyState.SetConnectionStatus("No chat channels configured.");
                StateChanged?.Invoke();
                return;
            }

            LobbyState.ClearConnectionLog();
            LogActivity($"Starting session as {ProgramConstants.PLAYERNAME} ({ClientConfiguration.Instance.LocalGame})");
            LogActivity($"Channels: chat={_channels.ChatChannel}, games={_channels.GameBroadcastChannel} ({_channels.UiName})");

            _systemId = CnCNetIdentity.CreateSystemId();
            _connection = new CnCNetIrcConnection(_systemId);
            WireConnection(_connection);
            LobbyState.SetConnectionStatus("Connecting to CnCNet...");
            LobbyState.SetChannelName(_channels.UiName, _channels.ChatChannel);
            StateChanged?.Invoke();
            LogActivity("Connecting to CnCNet IRC...");
            _connection.ConnectAsync();
        }
    }

    public void Disconnect()
    {
        _autoReconnect = false;
        _gameBroadcast.Stop();
        lock (_sync)
        {
            if (_connection == null)
                return;

            if (_channels != null)
            {
                _connection.PartChannel(_channels.ChatChannel);
                _connection.PartChannel(_channels.GameBroadcastChannel);
            }

            _connection.Disconnect();
        }
    }

    public void SetActiveGameRoom(CnCNetActiveGameRoom room) => _activeGameRoom = room;

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

        string normalized = channelName.StartsWith('#') ? channelName : "#" + channelName;
        _connection.SendInstant($"JOIN {normalized.ToLowerInvariant()} {password}");
        LogActivity($"→ JOIN game channel {normalized}");
    }

    public void Dispose()
    {
        _autoReconnect = false;
        _gameBroadcast.Dispose();
        Disconnect();
        _connection?.Dispose();
        _playerCountService?.Dispose();
    }

    private void WireConnection(CnCNetIrcConnection connection)
    {
        connection.Connected += () => Dispatcher.UIThread.Post(OnTcpConnected);
        connection.WelcomeReceived += msg => Dispatcher.UIThread.Post(() => OnWelcomeReceived(msg));
        connection.ConnectionFailed += msg => Dispatcher.UIThread.Post(() => OnConnectionFailed(msg));
        connection.Disconnected += msg => Dispatcher.UIThread.Post(() => OnDisconnected(msg));
        connection.ServerMessage += msg =>
        {
            if (msg.Length > 120)
                msg = msg[..117] + "...";
            LogActivity($"← {msg}");
        };
        connection.ChannelUserListReceived += (channel, users) =>
            Dispatcher.UIThread.Post(() => OnUserList(channel, users));
        connection.UserJoined += (channel, user) =>
            Dispatcher.UIThread.Post(() => OnUserJoined(channel, user));
        connection.UserLeft += (channel, user) =>
            Dispatcher.UIThread.Post(() => OnUserLeft(channel, user));
        connection.GameBroadcastReceived += (channel, sender, ctcp) =>
            Dispatcher.UIThread.Post(() => OnGameBroadcast(channel, sender, ctcp));
        connection.ChannelNamesComplete += channel =>
            Dispatcher.UIThread.Post(() => OnChannelNamesComplete(channel));
        connection.ActivityLogged += LogActivity;
    }

    private void OnChannelNamesComplete(string channel)
    {
        if (_channels == null || !IsChatChannel(channel))
            return;

        if (_channelUsers.Count == 0 && _connection is { IsConnected: true } && _namesRetryCount < 2)
        {
            _namesRetryCount++;
            LogActivity($"NAMES empty for {channel}, retrying ({_namesRetryCount}/2)...");
            _connection.RequestChannelNames(_channels.ChatChannel);
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
        PlayerNameSettings.ApplyFromUserSettings();
        LobbyState.SetConnectionStatus("Connected — joining channels...");
        LogActivity($"IRC welcome: {welcomeLine}");
        StateChanged?.Invoke();

        if (_connection == null || _channels == null)
            return;

        _connection.JoinChannel(_channels.ChatChannel);
        _connection.JoinChannel(_channels.GameBroadcastChannel);
        _namesRetryCount = 0;
        _connection.RequestChannelNames(_channels.ChatChannel);

        LobbyState.SetConnectionStatus("Connected");
        _reconnectAttempts = 0;
        LogActivity($"JOIN {_channels.ChatChannel}, {_channels.GameBroadcastChannel}; NAMES requested.");
        StateChanged?.Invoke();
    }

    private void OnConnectionFailed(string message)
    {
        ClearLobbyData();
        LobbyState.SetConnectionStatus(message);
        LogActivity($"Connection failed: {message}");
        StateChanged?.Invoke();
        ScheduleReconnect();
    }

    private void OnDisconnected(string message)
    {
        ClearLobbyData();
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
        if (!_autoReconnect || _reconnectAttempts >= 3)
            return;

        _reconnectAttempts++;
        int attempt = _reconnectAttempts;
        LogActivity($"Reconnecting in 5s (attempt {attempt}/3)...");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(5000);
            if (!_autoReconnect)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                lock (_sync)
                {
                    if (_connection is { IsConnected: true } or { IsConnecting: true })
                        return;
                }

                ConnectIfNeeded();
            });
        });
    }

    private void ClearLobbyData()
    {
        _gameBroadcast.Stop();
        _channelUsers.Clear();
        _games.Clear();
        _activeGameRoom = null;
        LobbyState.SetChannelPlayers([]);
        LobbyState.SetHostedGames([]);
    }

    private void OnUserList(string channel, IReadOnlyList<string> users)
    {
        if (_channels == null || !IsChatChannel(channel))
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
            && channel.Equals(_activeGameRoom.ChannelName, StringComparison.OrdinalIgnoreCase)
            && name.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase))
        {
            if (_activeGameRoom.IsHost && _connection != null && _channels != null)
                _gameBroadcast.StartHost(_connection, _channels, _activeGameRoom);

            LogActivity($"Joined game room {_activeGameRoom.RoomName}.");
            GameRoomJoined?.Invoke(_activeGameRoom);
        }

        if (_channels == null || !IsChatChannel(channel))
            return;

        _channelUsers.Add(name);
        if (name.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase)
            && _channelUsers.Count == 1)
        {
            LogActivity($"Joined chat channel {channel} as {name}.");
        }

        RefreshLobbyPlayers();
    }

    private void OnUserLeft(string channel, string user)
    {
        if (_channels == null)
            return;

        if (!IsChatChannel(channel) && !channel.Equals("*", StringComparison.Ordinal))
            return;

        _channelUsers.Remove(StripIrcPrefixes(user));
        RefreshLobbyPlayers();
    }

    private void OnGameBroadcast(string channel, string sender, string ctcp)
    {
        if (_channels != null
            && !channel.Equals(_channels.GameBroadcastChannel, StringComparison.OrdinalIgnoreCase))
            return;

        CnCNetHostedGameSummary? game = CnCNetGameMessageParser.TryParse(sender, ctcp, Tunnels, out string? rejectReason);
        if (game == null)
        {
            if (!string.IsNullOrWhiteSpace(rejectReason))
                LogActivity($"GAME from {sender} ignored: {rejectReason}");
            return;
        }

        if (game.IsClosed)
        {
            _games.Remove(game.ChannelName);
            LogActivity($"Game closed: {game.RoomName} ({sender})");
        }
        else
        {
            _games[game.ChannelName] = game;
            LogActivity($"Game listed: {game.RoomName} by {sender} ({game.PlayerCount}/{game.MaxPlayers})");
        }

        RefreshHostedGames();
    }

    private void RefreshHostedGames()
    {
        var list = _games.Values
            .OrderBy(g => g.RoomName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LobbyState.SetHostedGames(list);
        StateChanged?.Invoke();
    }

    private void RefreshLobbyPlayers()
    {
        var players = _channelUsers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        LobbyState.SetChannelPlayers(players);
        StateChanged?.Invoke();
    }

    private bool IsChatChannel(string channel)
        => _channels != null && channel.Equals(_channels.ChatChannel, StringComparison.OrdinalIgnoreCase);

    private void LogActivity(string message)
    {
        Logger.Log(message.StartsWith("CnCNet", StringComparison.Ordinal) ? message : $"CnCNet: {message}");
        Dispatcher.UIThread.Post(() =>
        {
            LobbyState.AppendConnectionLog(message);
            StateChanged?.Invoke();
        });
    }

    private static string StripIrcPrefixes(string user)
    {
        int index = 0;
        while (index < user.Length && (user[index] == '@' || user[index] == '+' || user[index] == '%' || user[index] == '~' || user[index] == '&'))
            index++;

        return user[index..];
    }
}
