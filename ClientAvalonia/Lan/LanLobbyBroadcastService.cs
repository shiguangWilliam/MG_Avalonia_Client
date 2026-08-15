using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Rampastring.Tools;
using NetworkInterface = System.Net.NetworkInformation.NetworkInterface;

namespace ClientAvalonia.Lan;

/// <summary>
/// UDP discovery / chat / GAME advertise for LAN lobby (DX <c>LANLobbyBroadcastManager</c> semantics).
/// Broadcasts to per-interface directed broadcast addresses; MID_ dedup for multi-NIC echoes.
/// </summary>
public sealed class LanLobbyBroadcastService : IDisposable
{
    private readonly object _socketLock = new();
    private readonly ConcurrentDictionary<string, PlayerNetworkInterface> _interfaces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _seenMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _refreshTask;
    private int _disposed;

    public event Action<LanLobbyMessage>? MessageReceived;

    public bool IsInitialized
    {
        get
        {
            lock (_socketLock)
                return _socket is { IsBound: true };
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        lock (_socketLock)
        {
            if (_socket != null)
                return;

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, LanProtocol.LobbyUdpPort));
            _socket = socket;
        }

        RefreshInterfaces();
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        _refreshTask = Task.Run(() => RefreshLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_socketLock)
        {
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket = null;
        }

        try { _listenTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        try { _refreshTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _listenTask = null;
        _refreshTask = null;
    }

    public void Broadcast(string commandAndPayload)
    {
        if (string.IsNullOrWhiteSpace(commandAndPayload))
            return;

        string mid = "MID_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        byte[] bytes = LanProtocol.Encoding.GetBytes(mid + commandAndPayload);

        lock (_socketLock)
        {
            Socket? socket = _socket;
            if (socket == null)
                return;

            foreach (PlayerNetworkInterface nic in _interfaces.Values)
            {
                try
                {
                    socket.SendTo(bytes, nic.Broadcast);
                }
                catch (Exception ex)
                {
                    Logger.Log($"LAN broadcast send failed ({nic.Broadcast}): {ex.Message}");
                }
            }
        }
    }

    public void BroadcastAlive(int gameIndex, string playerName)
        => Broadcast($"{LanProtocol.Alive} {gameIndex}{LanProtocol.DataSep}{playerName}");

    public void BroadcastChat(int colorIndex, string text)
        => Broadcast($"{LanProtocol.Chat} {colorIndex}{LanProtocol.DataSep}{text}");

    public void BroadcastQuit()
        => Broadcast(LanProtocol.Quit);

    public void BroadcastGame(string gamePayloadWithoutCommand)
        => Broadcast($"{LanProtocol.Game} {gamePayloadWithoutCommand}");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Stop();
    }

    private Task ListenLoop(CancellationToken ct)
        => Task.Run(() =>
        {
            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int read;
                    lock (_socketLock)
                    {
                        if (_socket == null)
                            break;
                        read = _socket.ReceiveFrom(buffer, ref remote);
                    }

                    if (read <= 0)
                        continue;

                    HandleDatagram(buffer.AsSpan(0, read), (IPEndPoint)remote);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (ct.IsCancellationRequested)
                        break;
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Logger.Log("LAN listen error: " + ex.Message);
                    Thread.Sleep(100);
                }
            }
        }, ct);

    private async Task RefreshLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                RefreshInterfaces();
                PruneMessageIds();
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void HandleDatagram(ReadOnlySpan<byte> data, IPEndPoint remote)
    {
        string text;
        try
        {
            text = LanProtocol.Encoding.GetString(data);
        }
        catch
        {
            return;
        }

        if (text.Length < 5 || !text.StartsWith("MID_", StringComparison.Ordinal))
            return;

        int payloadStart = 4 + 8; // MID_ + 8 hex
        if (text.Length <= payloadStart)
            return;

        string mid = text[..payloadStart];
        if (!_seenMessageIds.TryAdd(mid, DateTime.UtcNow))
            return;

        string body = text[payloadStart..];
        if (string.IsNullOrWhiteSpace(body))
            return;

        int space = body.IndexOf(' ');
        string command = space < 0 ? body : body[..space];
        string payload = space < 0 ? string.Empty : body[(space + 1)..];

        MessageReceived?.Invoke(new LanLobbyMessage(command, payload, remote, DateTime.UtcNow));
    }

    private void RefreshInterfaces()
    {
        var next = new Dictionary<string, PlayerNetworkInterface>(StringComparer.Ordinal);
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    IPAddress? broadcast = TryDirectedBroadcast(uni.Address, uni.IPv4Mask);
                    if (broadcast == null)
                        continue;

                    string key = uni.Address.ToString();
                    next[key] = new PlayerNetworkInterface(uni.Address, new IPEndPoint(broadcast, LanProtocol.LobbyUdpPort));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("LAN interface refresh failed: " + ex.Message);
        }

        // Always include limited broadcast as fallback.
        next["255.255.255.255"] = new PlayerNetworkInterface(
            IPAddress.Any,
            new IPEndPoint(IPAddress.Broadcast, LanProtocol.LobbyUdpPort));

        foreach (string key in _interfaces.Keys.ToArray())
        {
            if (!next.ContainsKey(key))
                _interfaces.TryRemove(key, out _);
        }

        foreach ((string key, PlayerNetworkInterface value) in next)
            _interfaces[key] = value;
    }

    private void PruneMessageIds()
    {
        DateTime cutoff = DateTime.UtcNow - LanProtocol.DedupTtl;
        foreach ((string key, DateTime seen) in _seenMessageIds.ToArray())
        {
            if (seen < cutoff)
                _seenMessageIds.TryRemove(key, out _);
        }
    }

    private static IPAddress? TryDirectedBroadcast(IPAddress address, IPAddress? mask)
    {
        if (mask == null)
            return null;

        byte[] ip = address.GetAddressBytes();
        byte[] m = mask.GetAddressBytes();
        if (ip.Length != 4 || m.Length != 4)
            return null;

        var b = new byte[4];
        for (int i = 0; i < 4; i++)
            b[i] = (byte)(ip[i] | ~m[i]);
        return new IPAddress(b);
    }

    private readonly record struct PlayerNetworkInterface(IPAddress LocalIP, IPEndPoint Broadcast);
}

public sealed record LanLobbyMessage(
    string Command,
    string Payload,
    IPEndPoint RemoteEndPoint,
    DateTime UtcTimestamp);
