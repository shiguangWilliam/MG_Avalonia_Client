using Avalonia.Threading;
using ClientCore.Network;

namespace ClientAvalonia.Services;

/// <summary>Avalonia controller: marshals <see cref="CnCNetSession"/> state to UI thread and <see cref="MultiplayerLobbyState"/>.</summary>
public sealed class CnCNetSessionService : IDisposable
{
    public static CnCNetSessionService Instance { get; } = new();

    private readonly CnCNetSession _session = CnCNetSession.Instance;

    public event Action? StateChanged;

    public event Action<CnCNetActiveGameRoom>? GameRoomJoined;

    public MultiplayerLobbyState LobbyState { get; } = new();

    public int OnlinePlayerCount => _session.OnlinePlayerCount;

    public IReadOnlyList<CnCNetTunnelEntry> Tunnels => _session.Tunnels;

    public CnCNetIrcConnection? Connection => _session.Connection;

    public CnCNetGameChannels? Channels => _session.Channels;

    public CnCNetActiveGameRoom? ActiveGameRoom => _session.ActiveGameRoom;

    private CnCNetSessionService()
    {
        _session.StateChanged += OnCoreStateChanged;
        _session.GameRoomJoined += room => Dispatcher.UIThread.Post(() => GameRoomJoined?.Invoke(room));
    }

    public void EnsureStarted()
    {
        ClientLogService.EnsureInitialized();
        _session.EnsureStarted();
    }

    public void ConnectIfNeeded() => _session.ConnectIfNeeded();

    public void Disconnect() => _session.Disconnect();

    public void UpdateHostedGameListing(
        string mapName,
        string gameModeName,
        string mapSha1,
        IReadOnlyList<string> playerNames,
        bool locked = false,
        bool closed = false)
        => _session.UpdateHostedGameListing(mapName, gameModeName, mapSha1, playerNames, locked, closed);

    public bool TryCreateGame(out string message)
        => CnCNetLobbyOperations.TryCreateGame(_session, out message);

    public bool TryJoinSelectedGame(out string message)
    {
        CnCNetHostedGameSummary? game = LobbyState.GetSelectedGame();
        if (game == null)
        {
            message = "Select a game from the list first.";
            return false;
        }

        return CnCNetLobbyOperations.TryJoinGame(_session, game, password: null, out message);
    }

    public void Dispose() => _session.Dispose();

    private void OnCoreStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LobbyState.SyncFrom(_session.LobbyState);
            StateChanged?.Invoke();
        });
    }
}
