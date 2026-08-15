using System.Net;
using System.Net.Sockets;
using ClientAvalonia.GlobalState;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.Session;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Lan;

/// <summary>
/// LAN lobby + active room facade (parallel to <see cref="ClientAvalonia.CnCNet.ICnCNetSession"/>).
/// Owns UDP discovery and the optional active <see cref="ILANGameSession"/>.
/// </summary>
public interface ILanSession : IDisposable
{
    bool IsListening { get; }

    ILANGameSession? ActiveGameRoom { get; }

    IReadOnlyList<LanHostedGame> HostedGames { get; }

    IReadOnlyList<string> LobbyPlayers { get; }

    event Action? LobbyChanged;

    event Action? ActiveRoomChanged;

    void StartLobby();

    void StopLobby();

    bool TryHostNewGame(string? mapName, string? gameMode, out string message);

    bool TryJoinGame(LanHostedGame game, out string message);

    void LeaveActiveRoom();

    bool TryLaunchActiveRoom(out string message);

    LanHostedGame? GetSelectedOrFirstUnlocked();
}

/// <summary>Production LAN session: UDP lobby + TCP room transport.</summary>
public sealed class LanSession : ILanSession
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LanHostedGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Name, DateTime SeenUtc)> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly LanLobbyBroadcastService _broadcast = new();
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private LanGameRoomTransport? _transport;
    private bool _disposed;

    public bool IsListening { get; private set; }

    public ILANGameSession? ActiveGameRoom { get; private set; }

    public LanGameRoomSession? ActiveRoomCore => ActiveGameRoom as LanGameRoomSession;

    public IReadOnlyList<LanHostedGame> HostedGames
    {
        get
        {
            lock (_sync)
                return _games.Values.OrderByDescending(g => g.LastRefreshUtc).ToArray();
        }
    }

    public IReadOnlyList<string> LobbyPlayers
    {
        get
        {
            lock (_sync)
                return _players.Values.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
        }
    }

    public event Action? LobbyChanged;
    public event Action? ActiveRoomChanged;

    public void StartLobby()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening)
            return;

        _broadcast.MessageReceived += OnBroadcastMessage;
        _broadcast.Start();
        IsListening = true;

        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTask = Task.Run(() => HeartbeatLoop(_heartbeatCts.Token));
        LobbyChanged?.Invoke();
    }

    public void StopLobby()
    {
        if (!IsListening)
            return;

        try { _broadcast.BroadcastQuit(); } catch { /* ignore */ }
        _heartbeatCts?.Cancel();
        try { _heartbeatTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
        _heartbeatTask = null;

        _broadcast.MessageReceived -= OnBroadcastMessage;
        _broadcast.Stop();
        IsListening = false;

        lock (_sync)
        {
            _games.Clear();
            _players.Clear();
        }

        LobbyChanged?.Invoke();
    }

    public bool TryHostNewGame(string? mapName, string? gameMode, out string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveGameRoom != null)
        {
            message = "Already in a LAN game room.";
            return false;
        }

        if (!IsListening)
            StartLobby();

        string playerName = ResolvePlayerName();
        var session = new LanGameRoomSession(playerName, isHost: true, playerName);
        try
        {
            var transport = new LanGameRoomTransport(session, _broadcast, mapName ?? "Unknown Map", gameMode ?? "Standard");
            transport.StartHost();
            _transport = transport;
            ActiveGameRoom = session;
            ActiveRoomChanged?.Invoke();
            message = "Hosting LAN game.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Failed to host LAN game: " + ex.Message;
            Logger.Log(message);
            return false;
        }
    }

    public bool TryJoinGame(LanHostedGame game, out string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveGameRoom != null)
        {
            message = "Already in a LAN game room.";
            return false;
        }

        if (game.EndPoint == null)
        {
            message = "LAN game has no host endpoint.";
            return false;
        }

        if (game.Locked)
        {
            message = "That LAN game is locked.";
            return false;
        }

        if (game.IsLoadedGame)
        {
            message = "LAN loaded-game rooms use the loading lobby path.";
            // Still allow join for now via normal room if names match — DX uses LANGameLoadingLobby.
        }

        string playerName = ResolvePlayerName();
        var session = new LanGameRoomSession(game.HostName, isHost: false, playerName);
        session.IsLoadedGame = game.IsLoadedGame;
        session.LoadedGameId = game.LoadedGameId;

        try
        {
            var transport = new LanGameRoomTransport(session, _broadcast, game.MapName, game.GameMode);
            transport.StartClient(game.EndPoint.Address);
            _transport = transport;
            ActiveGameRoom = session;
            ActiveRoomChanged?.Invoke();
            message = $"Joined {game.DisplayName}.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Failed to join LAN game: " + ex.Message;
            Logger.Log(message);
            return false;
        }
    }

    public void LeaveActiveRoom()
    {
        _transport?.Dispose();
        _transport = null;
        if (ActiveGameRoom != null)
        {
            ActiveGameRoom = null;
            ActiveRoomChanged?.Invoke();
        }
    }

    public bool TryLaunchActiveRoom(out string message)
    {
        if (ActiveRoomCore is not { IsHost: true } room)
        {
            message = "Only the LAN host can launch.";
            return false;
        }

        if (_transport == null)
        {
            message = "LAN transport is not active.";
            return false;
        }

        _transport.HostLaunch();
        room.State = GameSessionState.Launching;
        message = "LAN launch signaled.";
        return true;
    }

    public LanHostedGame? GetSelectedOrFirstUnlocked()
    {
        lock (_sync)
            return _games.Values.FirstOrDefault(g => !g.Locked) ?? _games.Values.FirstOrDefault();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        LeaveActiveRoom();
        StopLobby();
        _broadcast.Dispose();
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _broadcast.BroadcastAlive(0, ResolvePlayerName());
                _transport?.TickAdvertise();
                PruneStale();
                await Task.Delay(LanProtocol.AliveInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Log("LAN heartbeat: " + ex.Message);
            }
        }
    }

    private void OnBroadcastMessage(LanLobbyMessage msg)
    {
        if (msg.Command.Equals(LanProtocol.Alive, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = msg.Payload.Split(LanProtocol.DataSep);
            string name = parts.Length >= 2 ? parts[1] : parts.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return;

            string key = msg.RemoteEndPoint.Address + "|" + name;
            lock (_sync)
                _players[key] = (name, DateTime.UtcNow);
            LobbyChanged?.Invoke();
            return;
        }

        if (msg.Command.Equals(LanProtocol.Quit, StringComparison.OrdinalIgnoreCase))
        {
            lock (_sync)
            {
                foreach (string key in _players.Keys.Where(k => k.StartsWith(msg.RemoteEndPoint.Address + "|", StringComparison.Ordinal)).ToArray())
                    _players.Remove(key);
            }

            LobbyChanged?.Invoke();
            return;
        }

        if (msg.Command.Equals(LanProtocol.Game, StringComparison.OrdinalIgnoreCase))
        {
            if (!LanGameBroadcastCodec.TryParse(msg.Payload, out LanHostedGame game))
                return;

            game.EndPoint = new IPEndPoint(msg.RemoteEndPoint.Address, LanProtocol.GameLobbyTcpPort);
            game.LastRefreshUtc = DateTime.UtcNow;
            string key = msg.RemoteEndPoint.Address.ToString();
            lock (_sync)
                _games[key] = game;
            LobbyChanged?.Invoke();
        }
    }

    private void PruneStale()
    {
        DateTime now = DateTime.UtcNow;
        bool changed = false;
        lock (_sync)
        {
            foreach (string key in _players.Where(p => now - p.Value.SeenUtc > LanProtocol.PlayerInactivity).Select(p => p.Key).ToArray())
            {
                _players.Remove(key);
                changed = true;
            }

            foreach (string key in _games.Where(g => now - g.Value.LastRefreshUtc > LanProtocol.GameListStale).Select(g => g.Key).ToArray())
            {
                _games.Remove(key);
                changed = true;
            }
        }

        if (changed)
            LobbyChanged?.Invoke();
    }

    private static string ResolvePlayerName()
    {
        try
        {
            return EnvironmentServices.Resolve<IGameEnvironment>().PlayerName;
        }
        catch
        {
            return AppState.Environment.PlayerName;
        }
    }
}

/// <summary>
/// AppState accessor for LAN (same pattern as <see cref="AppState.CnCNet"/>).
/// </summary>
public static class LanSessionAccessor
{
    private static readonly object Gate = new();
    private static LanSession? _fallback;

    public static ILanSession Current
    {
        get
        {
            ILanSession? resolved = EnvironmentServices.TryResolve<ILanSession>();
            if (resolved != null)
                return resolved;

            lock (Gate)
                return _fallback ??= new LanSession();
        }
    }

    public static void ResetFallback()
    {
        lock (Gate)
        {
            _fallback?.Dispose();
            _fallback = null;
        }
    }
}
