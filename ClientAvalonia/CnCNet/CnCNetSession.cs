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
    private readonly HashSet<string> _channelUsers = new(StringComparer.OrdinalIgnoreCase);

    private CnCNetIrcConnection? _connection;
    private CnCNetGameChannels? _channels;
    private CnCNetPlayerCountService? _playerCountService;
    private CnCNetActiveGameRoom? _activeGameRoom;
    private CnCNetGameRoomSession? _gameRoom;
    private readonly CnCNetGameBroadcastService _gameBroadcast = new();
    private string _systemId = string.Empty;
    private bool _autoReconnect;
    private int _reconnectAttempts;
    private int _namesRetryCount;
    private bool _gameRoomJoinPending;

    public bool IsGameRoomJoinPending => _gameRoomJoinPending;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public event Action<string>? GameRoomJoinFailed;

    public CnCNetLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount { get; private set; } = -1;

    public IReadOnlyList<CnCNetTunnelEntry> Tunnels { get; private set; } = [];

    public CnCNetIrcConnection? Connection => _connection;

    public CnCNetGameChannels? Channels => _channels;

    public CnCNetActiveGameRoom? ActiveGameRoom => _activeGameRoom;

    public CnCNetGameRoomSession? GameRoom => _gameRoom;

    public event Action<CnCNetStartGameInfo>? GameStarting;

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

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Tunnels = CnCNetTunnelListLoader.Load();
                LogActivity($"Loaded {Tunnels.Count} NAT tunnels.");
                RefreshHostedGames();
                StateChanged?.Invoke();
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
                LogActivity("No GameCollectionConfig channels for LocalGame ?IRC connect skipped.");
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
        LeaveGameRoom();
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

        _gameRoom.Attach(_connection, _gameBroadcast, _channels);
        _gameRoom.StateChanged += OnGameRoomStateChanged;
        _gameRoom.NoticeLogged += OnGameRoomNotice;
        _gameRoom.GameStarting += OnGameRoomStarting;
    }

    private void OnGameRoomStateChanged() => StateChanged?.Invoke();

    private void OnGameRoomNotice(string msg) => LogActivity(msg);

    private void OnGameRoomStarting(CnCNetStartGameInfo info) => GameStarting?.Invoke(info);

    public void LeaveGameRoom()
    {
        if (_gameRoom != null)
        {
            _gameRoom.StateChanged -= OnGameRoomStateChanged;
            _gameRoom.NoticeLogged -= OnGameRoomNotice;
            _gameRoom.GameStarting -= OnGameRoomStarting;
            _gameRoom.Leave();
        }

        _gameRoom = null;
        _activeGameRoom = null;
        _gameRoomJoinPending = false;
        StateChanged?.Invoke();
    }

    public bool TryLaunchHostedGame(out string message)
    {
        if (_gameRoom == null)
        {
            message = "Not in a game room.";
            return false;
        }

        return _gameRoom.TryHostLaunch(out message);
    }

    public void SetGameRoomReady(bool ready) => _gameRoom?.SetLocalReady(ready);

    public void SetGameRoomLocked(bool locked) => _gameRoom?.SetLocked(locked);

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
        LogActivity($"? JOIN game channel {normalized}", notifyUi: false);
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
        connection.ChannelCtcpReceived += OnChannelCtcp;
        connection.ChannelNamesComplete += OnChannelNamesComplete;
        connection.ActivityLogged += msg => LogActivity(msg, notifyUi: false);
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
        ApplyPlayerNameFromUserSettings();
        LobbyState.SetConnectionStatus("Connected ?joining channels...");
        LogActivity($"IRC welcome: {welcomeLine}");
        StateChanged?.Invoke();

        if (_connection == null || _channels == null)
            return;

        string chatChannel = NormalizeIrcChannel(_channels.ChatChannel);
        string gameBroadcastChannel = NormalizeIrcChannel(_channels.GameBroadcastChannel);
        _connection.JoinChannelInstant(chatChannel);
        _connection.JoinChannelInstant(gameBroadcastChannel);
        _namesRetryCount = 0;
        _connection.RequestChannelNames(_channels.ChatChannel);

        LobbyState.SetConnectionStatus("Connected");
        _reconnectAttempts = 0;
        LogActivity($"JOIN {chatChannel}, {gameBroadcastChannel}; NAMES requested.");
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

            lock (_sync)
            {
                if (_connection is { IsConnected: true } or { IsConnecting: true })
                    return;
            }

            ConnectIfNeeded();
        });
    }

    private void ClearLobbyData()
    {
        _gameBroadcast.Stop();
        _gameRoom = null;
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
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            _gameRoom?.OnUserJoined(channel, name);

            if (name.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase))
            {
                _gameRoomJoinPending = false;
                _gameRoom?.OnLocalJoined();
                LogActivity($"Joined game room {_activeGameRoom.RoomName}.");
                GameRoomJoined?.Invoke(_activeGameRoom);
            }

            return;
        }

        if (_channels != null
            && channel.Equals(NormalizeIrcChannel(_channels.GameBroadcastChannel), StringComparison.OrdinalIgnoreCase)
            && name.Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase))
        {
            LogActivity($"Joined game broadcast channel {channel}.");
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
        string name = StripIrcPrefixes(user);

        if (_activeGameRoom != null
            && channel.Equals(NormalizeIrcChannel(_activeGameRoom.ChannelName), StringComparison.OrdinalIgnoreCase))
        {
            _gameRoom?.OnUserLeft(channel, name);
            return;
        }

        if (_channels == null)
            return;

        if (!IsChatChannel(channel) && !channel.Equals("*", StringComparison.Ordinal))
            return;

        _channelUsers.Remove(name);
        RefreshLobbyPlayers();
    }

    private void OnChannelCtcp(string channel, string sender, string ctcp)
        => _gameRoom?.OnChannelCtcp(channel, sender, ctcp);

    private void OnGameBroadcast(string channel, string sender, string ctcp)
    {
        if (_channels != null
            && !NormalizeIrcChannel(channel).Equals(
                NormalizeIrcChannel(_channels.GameBroadcastChannel),
                StringComparison.OrdinalIgnoreCase))
            return;

        CnCNetHostedGameSummary? game = CnCNetGameMessageParser.TryParse(sender, ctcp, Tunnels, out string? rejectReason);
        if (game == null)
        {
            if (ctcp.StartsWith("GAME ", StringComparison.Ordinal))
            {
                int fieldCount = ctcp[5..].Split(';').Length;
                LogActivity(
                    $"GAME CTCP from {sender} ({fieldCount} fields, rev={ctcp[5..].Split(';')[0]})",
                    notifyUi: false);
                if (!string.IsNullOrWhiteSpace(rejectReason))
                    LogActivity($"GAME from {sender} ignored: {rejectReason}");
            }

            return;
        }

        if (game.IsClosed)
        {
            _games.Remove(game.ChannelName);
            LogActivity($"Game closed: {game.RoomName} ({sender})", notifyUi: false);
        }
        else
        {
            _games[game.ChannelName] = game;
            LogActivity(
                $"Game listed: {game.RoomName} by {sender} ({game.PlayerCount}/{game.MaxPlayers})",
                notifyUi: false);
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
        => _channels != null
           && NormalizeIrcChannel(channel).Equals(
               NormalizeIrcChannel(_channels.ChatChannel),
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

    private void LogActivity(string message, bool notifyUi = true)
    {
        Logger.Log(message.StartsWith("CnCNet", StringComparison.Ordinal) ? message : $"CnCNet: {message}");
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
        int max = ClientConfiguration.Instance.MaxNameLength;
        if (max > 0 && trimmed.Length > max)
            trimmed = trimmed[..max];

        if (!string.IsNullOrEmpty(trimmed))
            ProgramConstants.PLAYERNAME = trimmed;
    }

    private static string StripIrcPrefixes(string user)
    {
        int index = 0;
        while (index < user.Length && (user[index] == '@' || user[index] == '+' || user[index] == '%' || user[index] == '~' || user[index] == '&'))
            index++;

        return user[index..];
    }
}
