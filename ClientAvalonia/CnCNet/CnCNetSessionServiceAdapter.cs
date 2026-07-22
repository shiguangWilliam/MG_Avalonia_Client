using ClientAvalonia.CnCNet.Tunnels;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// 将 <see cref="CnCNetSessionService.Instance"/> 适配为 <see cref="ICnCNetSession"/>。
/// ActiveGameRoom 返回已实现 <see cref="ICnCNetGameSession"/> 的 <see cref="CnCNetGameRoomSession"/>。
/// </summary>
public sealed class CnCNetSessionServiceAdapter : ICnCNetSession
{
    private readonly CnCNetSessionService _service;

    /// <summary>
    /// 用于迁移期的 escape hatch：当某些遗留回调（GameOptionsProvider 等）
    /// 暂未抽象到接口时，调用方可通过此属性访问具体 service。
    /// </summary>
    public CnCNetSessionService Service => _service;

    public CnCNetSessionServiceAdapter()
        : this(CnCNetSessionService.Instance)
    {
    }

    public CnCNetSessionServiceAdapter(CnCNetSessionService service)
    {
        _service = service;
        _service.GameRoomJoined += _ =>
        {
            ICnCNetGameSession? session = _service.GameRoom;
            if (session != null)
                GameRoomJoined?.Invoke(session);
        };
        _service.GameRoomJoinFailed += message => GameRoomJoinFailed?.Invoke(message);
        _service.GameStarting += info => GameStarting?.Invoke(info);
        _service.GameRoomHostAbandoned += () => GameRoomHostAbandoned?.Invoke();
        _service.StateChanged += () => StateChanged?.Invoke();
    }

    /// <inheritdoc />
    public CnCNetConnectionState ConnectionState
    {
        get
        {
            CnCNetIrcConnection? connection = _service.Connection;
            if (connection == null)
                return CnCNetConnectionState.Disconnected;

            if (connection.IsConnecting)
                return CnCNetConnectionState.Connecting;

            return connection.IsConnected
                ? CnCNetConnectionState.Connected
                : CnCNetConnectionState.Disconnected;
        }
    }

    /// <inheritdoc />
    public string LocalNick => _service.LocalNick;

    /// <inheritdoc />
    public ICnCNetGameSession? ActiveGameRoom => _service.GameRoom;

    /// <summary>
    /// 返回具体的 <see cref="CnCNetActiveGameRoom"/>（来自底层 service）。
    /// 仅供迁移期使用——某些遗留 helper 仍以具体类型作为参数签名。
    /// </summary>
    public CnCNetActiveGameRoom? ActiveGameRoomCore => _service.ActiveGameRoom;

    /// <inheritdoc />
    public CnCNetGameRoomSession? GameRoom => _service.GameRoom;

    /// <inheritdoc />
    public IReadOnlyList<CnCNetTunnel> Tunnels => _service.Tunnels;

    /// <inheritdoc />
    public TunnelSorter TunnelSorter => CnCNetSession.Instance.TunnelSorter;

    /// <inheritdoc />
    public int OnlinePlayerCount => _service.OnlinePlayerCount;

    /// <inheritdoc />
    public bool IsGameRoomJoinPending => _service.IsGameRoomJoinPending;

    /// <inheritdoc />
    public CnCNetIrcConnection? Connection => _service.Connection;

    /// <inheritdoc />
    public MultiplayerLobbyState LobbyState => _service.LobbyState;

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <inheritdoc />
    public event Action<ICnCNetGameSession>? GameRoomJoined;

    /// <inheritdoc />
    public event Action<string>? GameRoomJoinFailed;

    /// <inheritdoc />
    public event Action<CnCNetStartGameInfo>? GameStarting;

    /// <inheritdoc />
    public event Action? GameRoomHostAbandoned;

    /// <inheritdoc />
    public void ConnectIfNeeded() => _service.ConnectIfNeeded();

    /// <inheritdoc />
    public void Disconnect() => _service.Disconnect();

    /// <inheritdoc />
    public bool TryJoinGame(CnCNetHostedGameSummary game, string? password, out string message)
        => _service.TryJoinGame(game, password, out message);

    /// <inheritdoc />
    public bool TryLaunchHostedGame(out string message)
        => _service.TryLaunchHostedGame(out message);

    /// <inheritdoc />
    public void SendChatMessage(string message) => _service.SendChatMessage(message);

    /// <inheritdoc />
    public void SetGameRoomReady(bool ready, bool autoReady = false)
        => _service.SetGameRoomReady(ready, autoReady);

    /// <inheritdoc />
    public void SetGameRoomLocked(bool locked) => _service.SetGameRoomLocked(locked);

    /// <inheritdoc />
    public void LeaveGameRoom() => _service.LeaveGameRoom();

    /// <inheritdoc />
    public void UpdateGameRoomListing(string mapName, string gameModeName, string mapSha1)
        => _service.UpdateGameRoomListing(mapName, gameModeName, mapSha1);

    /// <inheritdoc />
    public void UpdateGameLobbySettings(string roomName, int maxPlayers, int skillLevel, string? password)
        => _service.UpdateGameLobbySettings(roomName, maxPlayers, skillLevel, password);

    /// <inheritdoc />
    public bool TryHostChangeTunnel(CnCNetTunnel tunnel) => _service.TryHostChangeTunnel(tunnel);

    [Obsolete("Phase 3 P3-4: 改用 ICnCNetGameSession.BroadcastPlayerOptionsFromSlots。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public void SyncGameRoomFromLobby(LobbyPlayerState state) => _service.SyncGameRoomFromLobby(state);

    /// <inheritdoc />
    public void EnsureGameBroadcastChannelsJoined() => _service.EnsureGameBroadcastChannelsJoined();

    /// <inheritdoc />
    public void EnsureStarted() => _service.EnsureStarted();

    /// <inheritdoc />
    public bool TryCreateGame(CnCNetGameCreationRequest request, out string message)
        => _service.TryCreateGame(request, out message);

    /// <inheritdoc />
    public void SwitchToChannel(int channelIndex) => _service.SwitchToChannel(channelIndex);

    /// <inheritdoc />
    public int SelectedChannelIndex => _service.SelectedChannelIndex;

    /// <inheritdoc />
    public void SetChatColorIndex(int index) => _service.SetChatColorIndex(index);

    /// <inheritdoc />
    public void NotifyGameProcessStarted() => _service.NotifyGameProcessStarted();

    /// <inheritdoc />
    public void NotifyGameProcessExited() => _service.NotifyGameProcessExited();

    /// <inheritdoc />
    public void BeginLaunchPresenceKeepAlive() => _service.BeginLaunchPresenceKeepAlive();

    /// <inheritdoc />
    public void EndLaunchPresenceKeepAlive() => _service.EndLaunchPresenceKeepAlive();
}
