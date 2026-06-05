using Avalonia.Threading;
using ClientAvalonia.CnCNet;

namespace ClientAvalonia.Services;

/// <summary>Avalonia controller: marshals <see cref="CnCNetSession"/> (ClientAvalonia.CnCNet) to UI thread.</summary>
public sealed class CnCNetSessionService : IDisposable
{
    public static CnCNetSessionService Instance { get; } = new();

    private readonly CnCNetSession _session = CnCNetSession.Instance;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public event Action<string>? GameRoomJoinFailed;

    public event Action<CnCNetStartGameInfo>? GameStarting;

    public MultiplayerLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount => _session.OnlinePlayerCount;

    public IReadOnlyList<CnCNetTunnelEntry> Tunnels => _session.Tunnels;

    public CnCNetIrcConnection? Connection => _session.Connection;

    public CnCNetGameCollection? GameCollection => _session.GameCollection;

    public int SelectedChannelIndex => _session.SelectedChannelIndex;

    public CnCNetActiveGameRoom? ActiveGameRoom => _session.ActiveGameRoom;

    public CnCNetGameRoomSession? GameRoom => _session.GameRoom;

    public bool IsGameRoomJoinPending => _session.IsGameRoomJoinPending;

    public string LocalNick => _session.LocalNick;

    private CnCNetSessionService()
    {
        _session.StateChanged += OnCoreStateChanged;
        _session.GameRoomJoined += room => Dispatcher.UIThread.Post(() => GameRoomJoined?.Invoke(room));
        _session.GameStarting += info => Dispatcher.UIThread.Post(() => GameStarting?.Invoke(info));
        _session.GameRoomJoinFailed += msg => Dispatcher.UIThread.Post(() => GameRoomJoinFailed?.Invoke(msg));
    }

    /// <summary>Pull latest session lobby state (call when opening CnCNetLobby).</summary>
    public void SyncLobbyStateFromCore()
    {
        LobbyState.SyncFrom(_session.LobbyState);
    }

    public void EnsureStarted()
    {
        ClientLogService.EnsureInitialized();
        _session.EnsureStarted();
    }

    public void ConnectIfNeeded() => _session.ConnectIfNeeded();

    public void Disconnect() => _session.Disconnect();

    public void LeaveGameRoom() => _session.LeaveGameRoom();

    public void UpdateHostedGameListing(
        string mapName,
        string gameModeName,
        string mapSha1,
        IReadOnlyList<string> playerNames,
        bool locked = false,
        bool closed = false)
        => _session.UpdateHostedGameListing(mapName, gameModeName, mapSha1, playerNames, locked, closed);

    public void UpdateGameRoomListing(string mapName, string gameModeName, string mapSha1)
        => _session.GameRoom?.UpdateHostListing(mapName, gameModeName, mapSha1);

    public bool TryCreateGame(CnCNetGameCreationRequest request, out string message)
    {
        if (_session.IsGameRoomJoinPending)
        {
            message = "Already joining a game room — please wait.";
            return false;
        }

        return CnCNetLobbyOperations.TryCreateGame(_session, request, out message);
    }

    public bool TryCreateGame(out string message)
    {
        if (_session.IsGameRoomJoinPending)
        {
            message = "Already joining a game room — please wait.";
            return false;
        }

        return CnCNetLobbyOperations.TryCreateGame(_session, out message);
    }

    public bool TryJoinGame(CnCNetHostedGameSummary game, string? password, out string message)
    {
        if (_session.IsGameRoomJoinPending)
        {
            message = "Already joining a game room — please wait.";
            return false;
        }

        return CnCNetLobbyOperations.TryJoinGame(_session, game, password, out message);
    }

    public bool TryJoinSelectedGame(out string message)
    {
        CnCNetHostedGameSummary? game = LobbyState.GetSelectedGame();
        if (game == null)
        {
            message = "Select a game from the list first.";
            return false;
        }

        return TryJoinGame(game, password: null, out message);
    }

    public bool SelectedGameRequiresPassword()
    {
        CnCNetHostedGameSummary? game = LobbyState.GetSelectedGame();
        return game is { CustomPassword: true };
    }

    public void SwitchToChannel(int channelIndex) => _session.SwitchToGame(channelIndex);

    public bool TryLaunchHostedGame(out string message) => _session.TryLaunchHostedGame(out message);

    public void SetGameRoomReady(bool ready) => _session.SetGameRoomReady(ready);

    public void SetGameRoomLocked(bool locked) => _session.SetGameRoomLocked(locked);

    public void Dispose() => _session.Dispose();

    private void OnCoreStateChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            LobbyState.SyncFrom(_session.LobbyState);
            StateChanged?.Invoke();
        });
}
