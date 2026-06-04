using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Rampastring.Tools;

namespace ClientCore.Network;

/// <summary>Minimal IRC client for CnCNet lobby (Core config + Connection register/join conventions).</summary>
public sealed class CnCNetIrcConnection : IDisposable
{
    private const int ReadBufferSize = 1024;
    private const int ConnectTimeoutMs = 3000;
    private const int MaxReadIdleErrors = 30;

    private readonly object _sendLock = new();
    private readonly Queue<string> _sendQueue = new();
    private readonly StringBuilder _readBuffer = new();
    private readonly Encoding _encoding = Encoding.UTF8;
    private readonly string _systemId;

    private readonly HashSet<string> _pendingNamesUsers = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingNamesChannel;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Thread? _readerThread;
    private Thread? _senderThread;
    private Thread? _connectThread;
    private Timer? _keepAliveTimer;
    private volatile bool _welcomeMessageReceived;

    public CnCNetIrcConnection(string systemId)
    {
        _systemId = systemId;
    }

    public bool IsConnected { get; private set; }

    public bool IsConnecting { get; private set; }

    public string? ConnectedServer { get; private set; }

    public event Action? Connected;

    /// <summary>Fired after IRC numeric 001 — client may JOIN channels.</summary>
    public event Action<string>? WelcomeReceived;

    public event Action<string>? ConnectionFailed;

    public event Action<string>? Disconnected;

    public event Action<string>? ServerMessage;

    public event Action<string, IReadOnlyList<string>>? ChannelUserListReceived;

    /// <summary>Fired after numeric 366 (end of NAMES list).</summary>
    public event Action<string>? ChannelNamesComplete;

    public event Action<string, string>? UserJoined;

    public event Action<string, string>? UserLeft;

    public event Action<string, string, string>? GameBroadcastReceived;

    /// <summary>User-facing connection progress (not raw RMP/SRM traffic).</summary>
    public event Action<string>? ActivityLogged;

    public void ConnectAsync()
    {
        if (IsConnected || IsConnecting)
            return;

        IsConnecting = true;
        _welcomeMessageReceived = false;
        _cts = new CancellationTokenSource();

        _connectThread = new Thread(() => ConnectLoop(_cts.Token)) { IsBackground = true };
        _connectThread.Start();
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        TearDown("User disconnect.");
    }

    public void JoinChannel(string channel, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        normalized = normalized.ToLowerInvariant();
        string command = string.IsNullOrWhiteSpace(key)
            ? $"JOIN {normalized}"
            : $"JOIN {normalized} {key}";
        EnqueueSend(command);
        EmitActivity(string.IsNullOrWhiteSpace(key) ? $"→ JOIN {normalized}" : $"→ JOIN {normalized} (key)");
    }

    /// <summary>Immediate send for JOIN during create/join (XNA QueuedMessageType.INSTANT_MESSAGE).</summary>
    public void SendInstant(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        SendImmediate(message);
        EmitActivity($"→ {message}");
    }

    /// <summary>Channel CTCP NOTICE (XNA Channel.SendCTCPMessage).</summary>
    public void SendCtcpNotice(string channel, string ctcpMessage)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(ctcpMessage))
            return;

        string normalized = channel.StartsWith('#') ? channel.ToLowerInvariant() : "#" + channel.ToLowerInvariant();
        SendImmediate($"NOTICE {normalized} :\u0001{ctcpMessage}\u0001");
    }

    public void RequestChannelNames(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        EnqueueSend($"NAMES {normalized.ToLowerInvariant()}");
    }

    public void RequestChannelUserInfo(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        EnqueueSend($"WHO {normalized.ToLowerInvariant()}");
    }

    public void PartChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        EnqueueSend($"PART {normalized.ToLowerInvariant()}");
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }

    private void ConnectLoop(CancellationToken token)
    {
        IReadOnlyList<CnCNetIrcServer> servers = CnCNetIrcServerList.Load();
        foreach (CnCNetIrcServer server in servers)
        {
            if (token.IsCancellationRequested)
                break;

            foreach (int port in server.Ports)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    EmitActivity($"Trying {server.Host}:{port} ({server.Name})...");
                    var client = new TcpClient();
                    var connectTask = client.ConnectAsync(server.Host, port);
                    if (!connectTask.Wait(ConnectTimeoutMs, token))
                    {
                        EmitActivity($"Timeout on {server.Host}:{port}");
                        client.Dispose();
                        continue;
                    }

                    _client = client;
                    _stream = _client.GetStream();
                    _stream.ReadTimeout = 1000;
                    ConnectedServer = $"{server.Host}:{port}";
                    IsConnected = true;
                    IsConnecting = false;

                    _readerThread = new Thread(ReadLoop) { IsBackground = true };
                    _senderThread = new Thread(SendLoop) { IsBackground = true };
                    _readerThread.Start(token);
                    _senderThread.Start(token);

                    Register();
                    StartKeepAlive();
                    Connected?.Invoke();
                    return;
                }
                catch (Exception ex)
                {
                    EmitActivity($"Connect failed ({server.Host}:{port}): {ex.Message}");
                }
            }
        }

        IsConnecting = false;
        string message = "Connecting to CnCNet failed.";
        Logger.Log(message);
        ConnectionFailed?.Invoke(message);
    }

    private void Register()
    {
        if (_welcomeMessageReceived)
            return;

        string localGame = ClientConfiguration.Instance.LocalGame;
        string realname = ProgramConstants.GAME_VERSION + " " + localGame + " CnCNet";
        EnqueueSend($"USER {localGame}.{_systemId} 0 * :{realname}");
        EnqueueSend("NICK " + ProgramConstants.PLAYERNAME);
        EmitActivity("Registering USER/NICK...");
        EmitActivity("→ NICK " + ProgramConstants.PLAYERNAME);
    }

    private void ChangeNickname()
    {
        EnqueueSend("NICK " + ProgramConstants.PLAYERNAME);
        EmitActivity("→ NICK " + ProgramConstants.PLAYERNAME);
    }

    private void OnNameAlreadyInUse()
    {
        var charList = ProgramConstants.PLAYERNAME.ToList();
        int maxNameLength = ClientConfiguration.Instance.MaxNameLength;

        if (charList.Count < maxNameLength)
            charList.Add('_');
        else
        {
            int lastNonUnderscoreIndex = charList.FindLastIndex(c => c != '_');
            if (lastNonUnderscoreIndex == -1)
            {
                Logger.Log("CnCNetIrcConnection: nickname invalid or exhausted retries.");
                TearDown("Nickname already in use.");
                return;
            }

            charList[lastNonUnderscoreIndex] = '_';
        }

        string newName = string.Concat(charList);
        EmitActivity($"Nickname in use, retrying as {newName}.");
        ProgramConstants.PLAYERNAME = newName;
        ChangeNickname();
    }

    private void ReadLoop(object? state)
    {
        if (state is not CancellationToken token || _stream == null)
            return;

        byte[] buffer = new byte[ReadBufferSize];
        int idleReadErrors = 0;

        while (!token.IsCancellationRequested && IsConnected)
        {
            try
            {
                int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    idleReadErrors++;
                    if (idleReadErrors > MaxReadIdleErrors)
                        break;

                    Thread.Sleep(10);
                    continue;
                }

                idleReadErrors = 0;
                string chunk = _encoding.GetString(buffer, 0, bytesRead);
                _readBuffer.Append(chunk);

                while (TryDequeueLine(out string? line))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    Logger.Log("CnCNet IRC RMP: " + line);
                    HandleLine(line);
                }
            }
            catch (IOException)
            {
                // ReadTimeout (1s) when idle — not a disconnect.
                continue;
            }
            catch (Exception ex)
            {
                Logger.Log($"CnCNetIrcConnection read error: {ex.Message}");
                break;
            }
        }

        TearDown("Connection lost.");
    }

    private bool TryDequeueLine(out string? line)
    {
        line = null;
        string text = _readBuffer.ToString();
        int index = text.IndexOf('\n');
        if (index < 0)
            return false;

        line = text[..index].TrimEnd('\r');
        _readBuffer.Remove(0, index + 1);
        return true;
    }

    private void SendLoop(object? state)
    {
        if (state is not CancellationToken token)
            return;

        int sendDelay = Math.Max(1, ClientConfiguration.Instance.SendSleep);

        while (!token.IsCancellationRequested && IsConnected)
        {
            string? message = null;
            lock (_sendLock)
            {
                if (_sendQueue.Count > 0)
                    message = _sendQueue.Dequeue();
            }

            if (message == null)
            {
                Thread.Sleep(25);
                continue;
            }

            SendImmediate(message);
            Thread.Sleep(sendDelay);
        }
    }

    private void EnqueueSend(string message)
    {
        lock (_sendLock)
            _sendQueue.Enqueue(message);
    }

    private void SendImmediate(string message)
    {
        if (_stream == null || !IsConnected)
            return;

        try
        {
            Logger.Log("CnCNet IRC SRM: " + message);
            byte[] buffer = _encoding.GetBytes(message + "\r\n");
            _stream.Write(buffer, 0, buffer.Length);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetIrcConnection send failed: {ex.Message}");
        }
    }

    private void TearDown(string reason)
    {
        if (!IsConnected && !IsConnecting)
            return;

        IsConnected = false;
        IsConnecting = false;
        _welcomeMessageReceived = false;

        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch
        {
        }

        _stream = null;
        _client = null;
        ConnectedServer = null;

        EmitActivity(reason);
        Disconnected?.Invoke(reason);
    }

    private void EmitActivity(string message)
    {
        Logger.Log($"CnCNetIrcConnection: {message}");
        ActivityLogged?.Invoke(message);
    }

    private void StartKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = new Timer(_ =>
        {
            if (!IsConnected)
                return;

            int tag = Random.Shared.Next(100000, 999999);
            EnqueueSend($"PING LAG{tag}");
        }, null, 30_000, 120_000);
    }

    private void HandleLine(string line)
    {
        ParseIrcMessage(line, out string prefix, out string command, out List<string> parameters);

        if (int.TryParse(command, out int numeric))
        {
            HandleNumeric(numeric, prefix, parameters);
            return;
        }

        switch (command)
        {
            case "PING":
            {
                string pong = parameters.Count > 0 ? "PONG " + parameters[0] : "PONG";
                SendImmediate(pong);
                break;
            }
            case "CAP":
                if (parameters.Count > 1 && parameters[1].Equals("LS", StringComparison.OrdinalIgnoreCase))
                    EnqueueSend("CAP END");
                break;
            case "NOTICE":
                HandleNotice(prefix, parameters);
                break;
            case "PRIVMSG":
                HandlePrivMsg(prefix, parameters);
                break;
            case "JOIN":
                HandleJoin(prefix, parameters);
                break;
            case "PART":
                HandlePart(prefix, parameters);
                break;
            case "QUIT":
                HandleQuit(prefix);
                break;
        }
    }

    private void HandleNumeric(int code, string prefix, List<string> parameters)
    {
        switch (code)
        {
            case 001:
            {
                string welcome = parameters.Count > 1 ? parameters[1] : "Welcome.";
                _welcomeMessageReceived = true;
                ServerMessage?.Invoke(welcome);
                WelcomeReceived?.Invoke(welcome);
                break;
            }
            case 353:
                Handle353(parameters);
                break;
            case 366:
                if (parameters.Count >= 2)
                    FinishNamesBatch(parameters[1].ToLowerInvariant());
                break;
            case 433:
                OnNameAlreadyInUse();
                break;
            case 471:
            case 473:
            case 474:
            case 475:
                if (parameters.Count > 1)
                    ServerMessage?.Invoke($"Cannot join {parameters[1]} ({code}).");
                break;
            case 451:
                Register();
                if (parameters.Count > 1)
                    ServerMessage?.Invoke(string.Join(' ', parameters.Skip(1)));
                break;
            default:
                if (parameters.Count > 1)
                    ServerMessage?.Invoke(string.Join(' ', parameters.Skip(1)));
                break;
        }
    }

    private void HandleNotice(string prefix, List<string> parameters)
    {
        if (parameters.Count < 2 || parameters[1].Length == 0 || parameters[1][0] != '\u0001')
            return;

        HandleCtcp(prefix, parameters[0], parameters[1]);
    }

    private void HandlePrivMsg(string prefix, List<string> parameters)
    {
        if (parameters.Count < 2 || parameters[1].Length == 0)
            return;

        if (parameters[1][0] == '\u0001' && !parameters[1].Contains("ACTION", StringComparison.Ordinal))
            HandleCtcp(prefix, parameters[0], parameters[1]);
    }

    private void HandleCtcp(string prefix, string channel, string ctcpPayload)
    {
        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string sender = prefix[..exclam];
        string ctcp = ctcpPayload.Trim('\u0001');

        if (ctcp.StartsWith("GAME ", StringComparison.Ordinal))
            GameBroadcastReceived?.Invoke(channel, sender, ctcp);
    }

    private void HandleJoin(string prefix, List<string> parameters)
    {
        if (parameters.Count == 0)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        UserJoined?.Invoke(parameters[0].ToLowerInvariant(), user);
    }

    private void HandlePart(string prefix, List<string> parameters)
    {
        if (parameters.Count == 0)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        UserLeft?.Invoke(parameters[0].ToLowerInvariant(), user);
    }

    private void HandleQuit(string prefix)
    {
        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        UserLeft?.Invoke("*", user);
    }

    private void Handle353(List<string> parameters)
    {
        if (parameters.Count < 3)
            return;

        if (parameters[0].Length > 0
            && !parameters[0].Equals(ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase)
            && !parameters[0].Equals("*", StringComparison.Ordinal))
            return;

        if (!TryParseNames353(parameters, out string channel, out string userList))
            return;

        channel = channel.ToLowerInvariant();
        if (_pendingNamesChannel != channel)
        {
            _pendingNamesUsers.Clear();
            _pendingNamesChannel = channel;
        }

        foreach (string user in userList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            _pendingNamesUsers.Add(user);
    }

    private static bool TryParseNames353(List<string> parameters, out string channel, out string userList)
    {
        channel = string.Empty;
        userList = string.Empty;

        if (parameters.Count >= 4
            && (parameters[1] == "=" || parameters[1] == "@" || parameters[1] == "*"))
        {
            channel = parameters[2];
            userList = parameters[3];
            return true;
        }

        if (parameters.Count >= 3)
        {
            channel = parameters[1];
            userList = parameters[2];
            return channel.StartsWith('#') || channel.StartsWith('&');
        }

        return false;
    }

    private void FinishNamesBatch(string channel)
    {
        channel = channel.ToLowerInvariant();
        if (_pendingNamesChannel != null
            && !_pendingNamesChannel.Equals(channel, StringComparison.OrdinalIgnoreCase))
            return;

        var users = _pendingNamesUsers.ToList();
        _pendingNamesUsers.Clear();
        _pendingNamesChannel = null;

        if (users.Count > 0)
            ChannelUserListReceived?.Invoke(channel, users);

        ChannelNamesComplete?.Invoke(channel);
    }

    private static void ParseIrcMessage(string message, out string prefix, out string command, out List<string> parameters)
    {
        prefix = string.Empty;
        command = string.Empty;
        parameters = [];

        int prefixEnd = -1;
        if (message.StartsWith(':'))
        {
            prefixEnd = message.IndexOf(' ');
            if (prefixEnd > 0)
                prefix = message[1..prefixEnd];
        }

        int trailingStart = message.IndexOf(" :", StringComparison.Ordinal);
        string? trailing = trailingStart >= 0 ? message[(trailingStart + 2)..] : null;
        if (trailingStart < 0)
            trailingStart = message.Length;

        int start = prefixEnd + 1;
        if (start < 0 || start >= message.Length)
            return;

        string[] commandAndParameters = message[start..trailingStart]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (commandAndParameters.Length == 0)
            return;

        command = commandAndParameters[0];
        for (int i = 1; i < commandAndParameters.Length; i++)
            parameters.Add(commandAndParameters[i]);

        if (!string.IsNullOrEmpty(trailing))
            parameters.Add(trailing);
    }
}
