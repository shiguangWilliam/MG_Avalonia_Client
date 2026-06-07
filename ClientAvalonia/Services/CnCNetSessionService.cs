using Avalonia.Threading;
using ClientAvalonia.CnCNet;
using System.Linq;

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

    public event Action? GameRoomHostAbandoned;

    public Func<CnCNetGameOptionsState>? GameOptionsProvider
    {
        get => _session.GameOptionsProvider;
        set => _session.GameOptionsProvider = value;
    }

    public Action<CnCNetGameOptionsState>? GameOptionsReceiver
    {
        get => _session.GameOptionsReceiver;
        set => _session.GameOptionsReceiver = value;
    }

    public Func<(int CheckBoxCount, int DropDownCount)>? GameOptionsControlCounts
    {
        get => _session.GameOptionsControlCounts;
        set => _session.GameOptionsControlCounts = value;
    }

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
        _session.GameRoomHostAbandoned += () => Dispatcher.UIThread.Post(() => GameRoomHostAbandoned?.Invoke());
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

    public void LeaveGameRoom(bool restoreBroadcastChannels)
        => _session.LeaveGameRoom(restoreBroadcastChannels);

    public void EnsureGameBroadcastChannelsJoined() => _session.EnsureGameBroadcastChannelsJoined();

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
        CnCNetHostedGameSummary? game = ResolveSelectedGameForJoin();
        if (game == null)
        {
            message = "Select a game from the list first.";
            return false;
        }

        return TryJoinGame(game, password: null, out message);
    }

    /// <summary>Re-read the selected entry from core lobby state (avoids stale CustomPassword flags).</summary>
    public CnCNetHostedGameSummary? ResolveSelectedGameForJoin()
    {
        CnCNetHostedGameSummary? selected = LobbyState.GetSelectedGame();
        if (selected == null)
            return null;

        SyncLobbyStateFromCore();
        CnCNetHostedGameSummary? fresh = _session.LobbyState.HostedGameDetails
            .FirstOrDefault(g => g.ChannelName.Equals(selected.ChannelName, StringComparison.OrdinalIgnoreCase));

        return fresh ?? selected;
    }

    public bool SelectedGameRequiresPassword()
    {
        CnCNetHostedGameSummary? game = ResolveSelectedGameForJoin();
        return game is { CustomPassword: true };
    }

    public void SwitchToChannel(int channelIndex) => _session.SwitchToGame(channelIndex);

    public void SyncGameRoomFromLobby(LobbyPlayerState state)
    {
        CnCNetGameRoomSession? gameRoom = _session.GameRoom;
        if (gameRoom == null || !gameRoom.IsHost)
            return;

        string hostName = string.IsNullOrWhiteSpace(state.HostPlayerName)
            ? LocalNick
            : state.HostPlayerName;

        gameRoom.SyncPlayersFromLobby(state, hostName);
    }

    public bool TryLaunchHostedGame(out string message) => _session.TryLaunchHostedGame(out message);

    public void SetGameRoomReady(bool ready, bool autoReady = false)
        => _session.SetGameRoomReady(ready, autoReady);

    public void SetGameRoomLocked(bool locked) => _session.SetGameRoomLocked(locked);

    public void SendChatMessage(string message) => _session.SendChatMessage(message);

    public void SetChatColorIndex(int index) => _session.SetChatColorIndex(index);

    public void Dispose() => _session.Dispose();

    private void OnCoreStateChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            LobbyState.SyncFrom(_session.LobbyState);
            StateChanged?.Invoke();
        });
}
