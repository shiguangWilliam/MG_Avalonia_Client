using ClientAvalonia.Online.EventArguments;
using ClientCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Minimal IRC client for CnCNet lobby (Core config + Connection register/join conventions).</summary>
public sealed class CnCNetIrcConnection : IDisposable
{
    private const int ReadBufferSize = 1024;
    private const int ConnectTimeoutMs = 3000;
    private const int MaxReadIdleErrors = 30;
    private const int KeepAliveInitialMs = 30_000;
    private const int KeepAliveIdlePeriodMs = 120_000;
    private const int KeepAliveActivePeriodMs = 30_000;

    private readonly object _sendLock = new();
    private readonly List<QueuedOutboundMessage> _sendQueue = [];
    private readonly StringBuilder _readBuffer = new();
    private readonly Encoding _encoding = Encoding.UTF8;
    private readonly string _systemId;

    private readonly HashSet<string> _pendingNamesUsers = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingNamesChannel;
    private readonly Dictionary<string, int> _channelUserCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _localJoinedChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _localJoinedChannelWireNames = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Confirmed IRC nick (001 / local JOIN / NICK).</summary>
    public string CurrentNick { get; private set; } = ProgramConstants.PLAYERNAME;

    public bool IsLocalUser(string nick)
        => !string.IsNullOrWhiteSpace(nick)
           && nick.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase);

    public int GetChannelUserCount(string channel)
    {
        string normalized = NormalizeChannelParameter(channel);
        return _channelUserCounts.GetValueOrDefault(normalized);
    }

    public bool IsLocalOnChannel(string channel)
    {
        string normalized = NormalizeChannelParameter(channel);
        return _localJoinedChannels.Contains(normalized);
    }

    /// <summary>Drop queued outbound traffic targeting a channel (stale CTCP after PART / re-join).</summary>
    public void ClearSendQueueForChannel(string channel)
    {
        string normalized = NormalizeChannelParameter(channel);
        lock (_sendLock)
        {
            _sendQueue.RemoveAll(m => MessageTargetsChannel(m.Message, normalized));
        }
    }

    /// <summary>Before JOIN: drop stale traffic and remember exact wire name (DX Channel.ChannelName).</summary>
    public void PrepareChannelJoin(string channelWire)
    {
        if (string.IsNullOrWhiteSpace(channelWire))
            return;

        string wire = CnCNetIrcChannelNames.Preserve(channelWire);
        string normalized = NormalizeChannelParameter(wire);
        lock (_sendLock)
        {
            _sendQueue.RemoveAll(m => MessageTargetsChannel(m.Message, normalized));
            _localJoinedChannelWireNames[normalized] = wire;
        }
    }

    private string GetOutboundChannelWire(string channel)
    {
        string normalized = NormalizeChannelParameter(channel);
        if (_localJoinedChannelWireNames.TryGetValue(normalized, out string? wire))
            return wire;

        return CnCNetIrcChannelNames.Preserve(channel);
    }

    private void DropLocalChannelMembership(string channel)
    {
        string normalized = NormalizeChannelParameter(channel);
        _localJoinedChannels.Remove(normalized);
        _localJoinedChannelWireNames.Remove(normalized);
    }

    /// <summary>Fired after TCP connect and USER/NICK registration sent.</summary>
    public event Action? Connected;

    public event Action<int, string, string>? ChannelJoinFailed;

    /// <summary>Fired after IRC numeric 001 ??client may JOIN channels.</summary>
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

    /// <summary>Any channel CTCP (game room PO/GO/START/etc.).</summary>
    public event Action<string, string, string>? ChannelCtcpReceived;

    /// <summary>CTCP delivered to local nick (PRIVMSG/NOTICE target is not a channel).</summary>
    public event EventHandler<PrivateCTCPEventArgs>? PrivateCTCPReceived;

    /// <summary>Regular channel PRIVMSG (chat).</summary>
    public event Action<string, string, string>? ChatMessageReceived;

    /// <summary>Private PRIVMSG to local nick.</summary>
    public event EventHandler<CnCNetPrivateMessageEventArgs>? PrivateMessageReceived;

    /// <summary>User was kicked from a channel.</summary>
    public event EventHandler<KickEventArgs>? UserKicked;

    /// <summary>Remote user changed nick (local nick updates CurrentNick separately).</summary>
    public event EventHandler<UserNicknameEventArgs>? UserNicknameChanged;

    /// <summary>User quit IRC (all channels).</summary>
    public event EventHandler<UserNicknameEventArgs>? UserQuit;

    /// <summary>Channel topic changed.</summary>
    public event EventHandler<ChannelTopicEventArgs>? ChannelTopicChanged;

    /// <summary>Channel mode changed.</summary>
    public event EventHandler<ChannelModeEventArgs>? ChannelModeChanged;

    /// <summary>IRC server connection attempt (DX ConnectionManager.OnAttemptedServerChanged).</summary>
    public event EventHandler<AttemptedServerEventArgs>? AttemptedServerChanged;

    /// <summary>IRC 442/404 — attempted to send to a channel we are not on.</summary>
    public event Action<string>? NotOnChannel;

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
        EmitActivity(string.IsNullOrWhiteSpace(key) ? $"??JOIN {normalized}" : $"??JOIN {normalized} (key)");
    }

    /// <summary>Immediate JOIN (welcome / create game). Bypasses SendSleep queue delay.</summary>
    public void JoinChannelInstant(string channel, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        normalized = normalized.ToLowerInvariant();
        string command = string.IsNullOrWhiteSpace(key)
            ? $"JOIN {normalized}"
            : $"JOIN {normalized} {key}";
        SendInstant(command);
    }

    /// <summary>Immediate send for JOIN during create/join (XNA QueuedMessageType.INSTANT_MESSAGE).</summary>
    public void SendInstant(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        SendImmediate(message);
        EmitActivity($"??{message}");
    }

    /// <summary>Send only when local JOIN for the channel has been confirmed (avoids IRC 442).</summary>
    public bool TrySendInstantOnChannel(string channel, string message)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(message))
            return false;

        string normalized = NormalizeChannelParameter(channel);
        if (!IsLocalOnChannel(normalized))
        {
            EmitActivity($"Skipping send on {normalized} (not on channel yet).");
            return false;
        }

        SendInstant(message);
        return true;
    }

    /// <summary>Channel CTCP NOTICE (XNA Channel.SendCTCPMessage — queued with priority, not instant flood).</summary>
    public void SendCtcpNotice(string channel, string ctcpMessage)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(ctcpMessage))
            return;

        string normalized = NormalizeChannelParameter(channel);
        if (!IsLocalOnChannel(normalized))
        {
            EmitActivity($"Skipping CTCP on {normalized} (not on channel yet).");
            return;
        }

        string wireChannel = GetOutboundChannelWire(normalized);
        string wire = $"NOTICE {wireChannel} :\u0001{ctcpMessage}\u0001";
        int priority = GetCtcpPriority(ctcpMessage);
        string? dedupeKey = GetCtcpDedupeKey(ctcpMessage);
        EnqueueSend(wire, priority, dedupeKey);
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

    public void PartChannelInstant(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return;

        string normalized = NormalizeChannelParameter(channel);
        lock (_sendLock)
        {
            _sendQueue.RemoveAll(m => MessageTargetsChannel(m.Message, normalized));
        }

        if (!_localJoinedChannels.Contains(normalized))
            return;

        string wire = GetOutboundChannelWire(normalized);
        DropLocalChannelMembership(normalized);
        SendInstant($"PART {wire}");
    }

    public void KickFromChannel(string channel, string userName)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(userName))
            return;

        string normalized = channel.StartsWith('#') ? channel : "#" + channel;
        SendInstant($"KICK {normalized.ToLowerInvariant()} {userName}");
    }

    public void SendChatMessage(string channel, string message, int ircColorId)
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(message))
            return;

        string normalized = channel.StartsWith('#') ? channel.ToLowerInvariant() : "#" + channel.ToLowerInvariant();
        string colorPrefix = $"{(char)3}{ircColorId:D2}";
        SendImmediate($"PRIVMSG {normalized} :{colorPrefix}{message}");
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
                    AttemptedServerChanged?.Invoke(this, new AttemptedServerEventArgs(server.Name));
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
        SendImmediate($"USER {localGame}.{_systemId} 0 * :{realname}");
        SendImmediate("NICK " + ProgramConstants.PLAYERNAME);
        EmitActivity("Registering USER/NICK...");
        EmitActivity("??NICK " + ProgramConstants.PLAYERNAME);
    }

    private void ChangeNickname()
    {
        SendImmediate("NICK " + ProgramConstants.PLAYERNAME);
        EmitActivity("??NICK " + ProgramConstants.PLAYERNAME);
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
                    try
                    {
                        HandleLine(line);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"CnCNetIrcConnection handler error: {ex.Message}");
                    }
                }
            }
            catch (IOException)
            {
                // ReadTimeout (1s) when idle ??not a disconnect.
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
            QueuedOutboundMessage? outbound = null;
            lock (_sendLock)
            {
                if (_sendQueue.Count > 0)
                {
                    outbound = _sendQueue[0];
                    _sendQueue.RemoveAt(0);
                }
            }

            if (outbound == null)
            {
                Thread.Sleep(25);
                continue;
            }

            if (TryGetMessageChannelTarget(outbound.Value.Message, out string targetChannel)
                && !IsLocalOnChannel(targetChannel))
            {
                continue;
            }

            SendImmediate(outbound.Value.Message);
            Thread.Sleep(sendDelay);
        }
    }

    private static bool TryGetMessageChannelTarget(string message, out string normalizedChannel)
    {
        normalizedChannel = string.Empty;
        foreach (string prefix in new[] { "NOTICE ", "PRIVMSG ", "MODE ", "TOPIC ", "PART " })
        {
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = message[prefix.Length..];
            int space = rest.IndexOf(' ');
            string target = space >= 0 ? rest[..space] : rest;
            if (!target.StartsWith('#'))
                return false;

            normalizedChannel = NormalizeChannelParameter(target);
            return true;
        }

        return false;
    }

    private void EnqueueSend(string message, int priority = 0, string? dedupeKey = null)
    {
        lock (_sendLock)
        {
            if (dedupeKey != null)
            {
                int existing = _sendQueue.FindIndex(m => dedupeKey.Equals(m.DedupeKey, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    _sendQueue[existing] = new QueuedOutboundMessage(message, priority, dedupeKey);
                    return;
                }
            }

            var entry = new QueuedOutboundMessage(message, priority, dedupeKey);
            int insertAt = _sendQueue.FindIndex(m => m.Priority < priority);
            if (insertAt < 0)
                _sendQueue.Add(entry);
            else
                _sendQueue.Insert(insertAt, entry);
        }
    }

    private void SendImmediate(string message)
    {
        if (_stream == null || !IsConnected)
            return;

        lock (_sendLock)
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

        lock (_sendLock)
            _sendQueue.Clear();

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
        _channelUserCounts.Clear();
        _localJoinedChannels.Clear();
        _localJoinedChannelWireNames.Clear();

        EmitActivity(reason);
        Disconnected?.Invoke(reason);
    }

    private static bool MessageTargetsChannel(string message, string normalizedChannel)
    {
        foreach (string prefix in new[] { "NOTICE ", "PRIVMSG ", "MODE ", "TOPIC ", "PART " })
        {
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = message[prefix.Length..];
            int space = rest.IndexOf(' ');
            string target = space >= 0 ? rest[..space] : rest;
            target = NormalizeChannelParameter(target);
            if (target.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
            EnqueueSend($"PING LAG{tag}", priority: 100);
        }, null, KeepAliveInitialMs, KeepAliveIdlePeriodMs);
    }

    private void ResetKeepAliveTimer()
    {
        _keepAliveTimer?.Change(KeepAliveInitialMs, KeepAliveActivePeriodMs);
    }

    private void HandleLine(string line)
    {
        ResetKeepAliveTimer();

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
                EnqueueSend(pong, priority: 100);
                Logger.Log("CnCNet IRC PONG: " + pong);
                break;
            }
            case "ERROR":
            {
                string errorText = parameters.Count > 0 ? string.Join(' ', parameters) : "Server error.";
                Logger.Log("CnCNet IRC ERROR: " + errorText);
                ServerMessage?.Invoke(errorText);
                TearDown(errorText);
                break;
            }
            case "CAP":
                if (parameters.Count > 1 && parameters[1].Equals("LS", StringComparison.OrdinalIgnoreCase))
                    EnqueueSend("CAP END", priority: 100);
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
            case "NICK":
                HandleNick(prefix, parameters);
                break;
            case "PART":
                HandlePart(prefix, parameters);
                break;
            case "QUIT":
                HandleQuit(prefix);
                break;
            case "MODE":
                HandleMode(prefix, parameters);
                break;
            case "KICK":
                HandleKick(parameters);
                break;
            case "TOPIC":
                HandleTopic(prefix, parameters);
                break;
        }
    }

    private void HandleNumeric(int code, string prefix, List<string> parameters)
    {
        switch (code)
        {
            case 001:
            {
                if (parameters.Count > 0 && !string.IsNullOrWhiteSpace(parameters[0]))
                    SetCurrentNick(parameters[0]);

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
            case 404:
            case 442:
                if (parameters.Count > 1 && parameters[1].StartsWith('#'))
                {
                    string notOnChannel = NormalizeChannelParameter(parameters[1]);
                    DropLocalChannelMembership(notOnChannel);
                    NotOnChannel?.Invoke(notOnChannel);
                }
                if (parameters.Count > 2)
                    ServerMessage?.Invoke(string.Join(' ', parameters.Skip(2)));
                break;
            case 471:
            case 473:
            case 474:
            case 475:
            case 476:
            case 477:
            case 405:
            case 439:
                if (parameters.Count > 1)
                {
                    string detail = parameters.Count > 2 ? string.Join(' ', parameters.Skip(2)) : string.Empty;
                    string joinError = $"Cannot join {parameters[1]} (IRC {code}){(string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail)}";
                    ServerMessage?.Invoke(joinError);
                    ActivityLogged?.Invoke(joinError);
                    ChannelJoinFailed?.Invoke(code, parameters[1], detail);
                }
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

        // DX Connection.cs: CTCP may arrive via PRIVMSG with leading SOH (same as NOTICE).
        if (parameters[1][0] == '\u0001' && !parameters[1].Contains("ACTION", StringComparison.Ordinal))
        {
            HandleCtcp(prefix, parameters[0], parameters[1]);
            return;
        }

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string sender = prefix[..exclam];
        string target = parameters[0];
        string message = parameters[1];

        if (message.StartsWith('\u0001'.ToString() + "ACTION", StringComparison.Ordinal) && message.Length > 2)
            message = message[1..^1];

        if (IsChannelTarget(target))
        {
            ChatMessageReceived?.Invoke(target, sender, message);
            return;
        }

        if (IsLocalUser(target))
            PrivateMessageReceived?.Invoke(this, new CnCNetPrivateMessageEventArgs(sender, message));
    }

    private void HandleCtcp(string prefix, string target, string ctcpPayload)
    {
        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string sender = prefix[..exclam];
        string ctcp = ctcpPayload.Trim('\u0001');

        if (IsChannelTarget(target))
        {
            ChannelCtcpReceived?.Invoke(target, sender, ctcp);

            if (ctcp.StartsWith("GAME ", StringComparison.Ordinal))
                GameBroadcastReceived?.Invoke(target, sender, ctcp);

            return;
        }

        // DX CnCNetManager.DoCTCPParsed: target == local nick → private CTCP (INVITE, etc.).
        if (IsLocalUser(target))
            PrivateCTCPReceived?.Invoke(this, new PrivateCTCPEventArgs(sender, ctcp));
    }

    private static bool IsChannelTarget(string target)
        => target.StartsWith('#') || target.StartsWith('&');

    private void HandleJoin(string prefix, List<string> parameters)
    {
        if (parameters.Count == 0)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        string channel = NormalizeChannelParameter(parameters[0]);
        if (IsLocalUser(user))
        {
            SetCurrentNick(user);
            _localJoinedChannels.Add(channel);
            _localJoinedChannelWireNames[channel] = CnCNetIrcChannelNames.Preserve(parameters[0]);
        }

        IncrementChannelUserCount(channel);
        UserJoined?.Invoke(channel, user);
    }

    private void HandleNick(string prefix, List<string> parameters)
    {
        if (parameters.Count == 0)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string oldNick = prefix[..exclam];
        string newNick = parameters[0].TrimStart(':');
        if (IsLocalUser(oldNick))
        {
            SetCurrentNick(newNick);
            return;
        }

        UserNicknameChanged?.Invoke(this, new UserNicknameEventArgs(oldNick, newNick));
    }

    private void SetCurrentNick(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            return;

        CurrentNick = nick;
        ProgramConstants.PLAYERNAME = nick;
    }

    private void HandlePart(string prefix, List<string> parameters)
    {
        if (parameters.Count == 0)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        string channel = NormalizeChannelParameter(parameters[0]);
        if (IsLocalUser(user))
            DropLocalChannelMembership(channel);

        DecrementChannelUserCount(channel);
        UserLeft?.Invoke(channel, user);
    }

    private void HandleQuit(string prefix)
    {
        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        UserQuit?.Invoke(this, new UserNicknameEventArgs(user, string.Empty));
        UserLeft?.Invoke("*", user);
    }

    private void HandleKick(List<string> parameters)
    {
        if (parameters.Count < 2)
            return;

        string channel = NormalizeChannelParameter(parameters[0]);
        string user = StripIrcPrefixes(parameters[1]);
        if (string.IsNullOrWhiteSpace(user))
            return;

        if (IsLocalUser(user))
            DropLocalChannelMembership(channel);
        else
            DecrementChannelUserCount(channel);

        UserKicked?.Invoke(this, new KickEventArgs(channel, user));
        UserLeft?.Invoke(channel, user);
    }

    private void HandleMode(string prefix, List<string> parameters)
    {
        if (parameters.Count < 2)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string user = prefix[..exclam];
        string channel = NormalizeChannelParameter(parameters[0]);
        string modeString = parameters[1];
        ChannelModeChanged?.Invoke(this, new ChannelModeEventArgs(user, channel, modeString));
    }

    private void HandleTopic(string prefix, List<string> parameters)
    {
        if (parameters.Count < 2)
            return;

        int exclam = prefix.IndexOf('!');
        if (exclam <= 0)
            return;

        string channel = NormalizeChannelParameter(parameters[0]);
        string topic = parameters[1].TrimStart(':');
        ChannelTopicChanged?.Invoke(this, new ChannelTopicEventArgs(channel, topic));
    }

    private static string StripIrcPrefixes(string user)
    {
        string name = user.Trim();
        while (name.Length > 0 && (name[0] == '@' || name[0] == '+' || name[0] == '%'))
            name = name[1..];

        return name;
    }

    private void Handle353(List<string> parameters)
    {
        if (parameters.Count < 3)
            return;

        if (parameters[0].Length > 0
            && !IsLocalUser(parameters[0])
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

        _channelUserCounts[channel] = users.Count;
        ChannelNamesComplete?.Invoke(channel);
    }

    private void IncrementChannelUserCount(string channel)
    {
        _channelUserCounts.TryGetValue(channel, out int count);
        _channelUserCounts[channel] = count + 1;
    }

    private void DecrementChannelUserCount(string channel)
    {
        if (!_channelUserCounts.TryGetValue(channel, out int count))
            return;

        if (count <= 1)
            _channelUserCounts.Remove(channel);
        else
            _channelUserCounts[channel] = count - 1;
    }

    private static string NormalizeChannelParameter(string channel)
    {
        string normalized = channel.Trim().TrimStart(':');
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        return normalized.ToLowerInvariant();
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

    private static int GetCtcpPriority(string ctcpMessage)
    {
        if (ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal))
            return 20;

        if (ctcpMessage.StartsWith("PO ", StringComparison.Ordinal)
            || ctcpMessage.StartsWith("GO ", StringComparison.Ordinal)
            || ctcpMessage.StartsWith("GSETTINGS ", StringComparison.Ordinal))
            return 11;

        return 10;
    }

    private static string? GetCtcpDedupeKey(string ctcpMessage)
    {
        int space = ctcpMessage.IndexOf(' ');
        string command = space > 0 ? ctcpMessage[..space] : ctcpMessage;
        return command is "PO" or "GO" or "GAME" ? "CTCP:" + command : null;
    }

    private readonly record struct QueuedOutboundMessage(string Message, int Priority, string? DedupeKey);
}
