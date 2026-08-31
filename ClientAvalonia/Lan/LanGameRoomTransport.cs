using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState;
using ClientAvalonia.Session;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Lan;

/// <summary>
/// TCP room control for LAN new-game lobby (DX <c>LANGameLobby</c> subset:
/// JOIN / POPTS / OPTS / READY / LAUNCH / QUIT).
/// </summary>
public sealed class LanGameRoomTransport : IDisposable
{
    private readonly LanGameRoomSession _session;
    private readonly LanLobbyBroadcastService _broadcast;
    private readonly string _mapName;
    private readonly string _gameMode;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.OrdinalIgnoreCase);
    private TcpListener? _listener;
    private TcpClient? _hostLoopback;
    private TcpClient? _client;
    private NetworkStream? _clientStream;
    private CancellationTokenSource? _cts;
    private DateTime _lastAdvertiseUtc = DateTime.MinValue;
    private int _disposed;

    public event Action? LaunchRequested;

    public LanGameRoomTransport(LanGameRoomSession session, LanLobbyBroadcastService broadcast, string mapName, string gameMode)
    {
        _session = session;
        _broadcast = broadcast;
        _mapName = mapName;
        _gameMode = gameMode;
    }

    public void StartHost()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, LanProtocol.GameLobbyTcpPort);
        _listener.Start();
        _ = AcceptLoop(_cts.Token);

        // DX: host also connects to 127.0.0.1 and JOINs as client #0.
        _hostLoopback = new TcpClient();
        _hostLoopback.Connect(IPAddress.Loopback, LanProtocol.GameLobbyTcpPort);
        NetworkStream stream = _hostLoopback.GetStream();
        SendFrame(stream, $"{LanProtocol.Join}{LanProtocol.DataSep}{_session.LocalPlayerName}");
        _ = ReadClientLoop(_hostLoopback, stream, isLocalHostMirror: true, _cts.Token);

        TickAdvertise(force: true);
    }

    public void StartClient(IPAddress hostAddress)
    {
        _cts = new CancellationTokenSource();
        _client = new TcpClient();
        _client.Connect(hostAddress, LanProtocol.GameLobbyTcpPort);
        _clientStream = _client.GetStream();
        SendFrame(_clientStream, $"{LanProtocol.Join}{LanProtocol.DataSep}{_session.LocalPlayerName}");
        _ = ReadClientLoop(_client, _clientStream, isLocalHostMirror: false, _cts.Token);
    }

    public void TickAdvertise(bool force = false)
    {
        if (!_session.IsHost)
            return;

        DateTime now = DateTime.UtcNow;
        if (!force && now - _lastAdvertiseUtc < LanProtocol.GameAdvertiseInterval)
            return;

        _lastAdvertiseUtc = now;
        string payload = LanGameBroadcastCodec.FormatPayload(
            ProgramConstants.GAME_VERSION,
            AppState.Configuration.LocalGame,
            _mapName,
            _gameMode,
            _session.OccupiedHumanNames(),
            _session.Locked,
            _session.IsLoadedGame,
            _session.LoadedGameId,
            mapSha1: string.Empty);
        _broadcast.BroadcastGame(payload);
    }

    public void BroadcastPlayerOptions()
    {
        if (!_session.IsHost)
            return;

        string body = LanPlayerOptionsCodec.Format(_session.SnapshotPlayerOptions());
        BroadcastToClients($"{LanProtocol.PlayerOptions} {body}");
    }

    public void HostLaunch()
    {
        if (!_session.IsHost)
            return;

        _session.Locked = true;
        BroadcastToClients($"{LanProtocol.Launch} {_session.UniqueGameId}");
        LaunchRequested?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (_session.IsHost)
                _broadcast.BroadcastQuit();
        }
        catch
        {
            // ignore
        }

        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _hostLoopback?.Close(); } catch { /* ignore */ }
        try { _client?.Close(); } catch { /* ignore */ }

        foreach (ClientConnection c in _clients.Values)
        {
            try { c.Client.Close(); } catch { /* ignore */ }
        }

        _clients.Clear();
        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (_session.Locked)
                {
                    client.Close();
                    continue;
                }

                _ = HandleAcceptedClient(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Log("LAN accept failed: " + ex.Message);
            }
        }
    }

    private async Task HandleAcceptedClient(TcpClient client, CancellationToken ct)
    {
        NetworkStream stream = client.GetStream();
        string? first = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
        if (first == null || !first.StartsWith(LanProtocol.Join, StringComparison.OrdinalIgnoreCase))
        {
            client.Close();
            return;
        }

        string[] parts = first.Split(LanProtocol.DataSep);
        string name = parts.Length >= 2 ? parts[1] : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            client.Close();
            return;
        }

        var connection = new ClientConnection(name, client, stream);
        _clients[name] = connection;

        if (!_session.OccupiedHumanNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            LobbyPlayerSlot? empty = _session.Slots.FirstOrDefault(s => !s.IsOccupied);
            if (empty != null)
            {
                empty.Name = name;
                empty.IsHumanLocal = name.Equals(_session.LocalPlayerName, StringComparison.OrdinalIgnoreCase);
                empty.Ready = false;
                _session.NotifyStateChanged();
            }
        }

        BroadcastPlayerOptions();
        SendFrame(stream, $"{LanProtocol.Options} {_mapName}{LanProtocol.DataSep}{_gameMode}{LanProtocol.DataSep}{_session.UniqueGameId}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? frame = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (frame == null)
                    break;
                HandleHostInbound(name, frame);
            }
        }
        finally
        {
            _clients.TryRemove(name, out _);
            client.Close();
        }
    }

    private void HandleHostInbound(string fromName, string frame)
    {
        if (frame.StartsWith(LanProtocol.Ready, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = frame.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            bool ready = parts.Length < 2 || parts[1].Trim() != "0";
            LobbyPlayerSlot? slot = _session.Slots.FirstOrDefault(s =>
                s.IsOccupied && s.Name.Equals(fromName, StringComparison.OrdinalIgnoreCase));
            if (slot != null)
            {
                slot.Ready = ready;
                _session.NotifyStateChanged();
                BroadcastPlayerOptions();
            }

            return;
        }

        if (!frame.StartsWith(LanProtocol.Quit, StringComparison.OrdinalIgnoreCase))
            return;

        LobbyPlayerSlot? leave = _session.Slots.FirstOrDefault(s =>
            s.IsOccupied && s.Name.Equals(fromName, StringComparison.OrdinalIgnoreCase));
        if (leave == null)
            return;

        leave.Name = string.Empty;
        leave.IsHumanLocal = false;
        leave.Ready = false;
        _session.NotifyStateChanged();
        BroadcastPlayerOptions();
    }

    private async Task ReadClientLoop(TcpClient client, NetworkStream stream, bool isLocalHostMirror, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                string? frame = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (frame == null)
                    break;

                if (isLocalHostMirror)
                {
                    if (frame.StartsWith(LanProtocol.Launch, StringComparison.OrdinalIgnoreCase))
                        LaunchRequested?.Invoke();
                    continue;
                }

                HandleClientInbound(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log("LAN client read failed: " + ex.Message);
        }
    }

    private void HandleClientInbound(string frame)
    {
        if (frame.StartsWith(LanProtocol.PlayerOptions, StringComparison.OrdinalIgnoreCase))
        {
            string payload = frame.Length > LanProtocol.PlayerOptions.Length
                ? frame[(LanProtocol.PlayerOptions.Length)..].TrimStart()
                : string.Empty;
            _session.ApplyRemotePlayerOptions(LanPlayerOptionsCodec.Parse(payload));
            return;
        }

        if (frame.StartsWith(LanProtocol.Options, StringComparison.OrdinalIgnoreCase))
        {
            string payload = frame.Length > LanProtocol.Options.Length
                ? frame[(LanProtocol.Options.Length)..].TrimStart()
                : string.Empty;
            string[] parts = payload.Split(LanProtocol.DataSep);
            if (parts.Length >= 3 && int.TryParse(parts[2], out int gameId))
                _session.UniqueGameId = gameId;
            return;
        }

        if (!frame.StartsWith(LanProtocol.Launch, StringComparison.OrdinalIgnoreCase))
            return;

        string[] launchParts = frame.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (launchParts.Length >= 2 && int.TryParse(launchParts[1], out int id))
            _session.UniqueGameId = id;
        _session.State = GameSessionState.Launching;
        LaunchRequested?.Invoke();
    }

    private void BroadcastToClients(string command)
    {
        foreach (ClientConnection c in _clients.Values)
        {
            try
            {
                SendFrame(c.Stream, command);
            }
            catch (Exception ex)
            {
                Logger.Log($"LAN send to {c.Name} failed: {ex.Message}");
            }
        }
    }

    private static void SendFrame(NetworkStream stream, string command)
    {
        byte[] bytes = LanProtocol.Encoding.GetBytes(command + LanProtocol.MessageSep);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static async Task<string?> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            var acc = new MemoryStream();
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read <= 0)
                    return null;

                acc.Write(buffer, 0, read);
                byte[] data = acc.ToArray();
                int sep = Array.IndexOf(data, (byte)LanProtocol.MessageSep);
                if (sep < 0)
                    continue;

                return LanProtocol.Encoding.GetString(data, 0, sep);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class ClientConnection(string name, TcpClient client, NetworkStream stream)
    {
        public string Name { get; } = name;
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = stream;
    }
}
