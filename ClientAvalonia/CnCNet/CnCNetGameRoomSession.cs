using ClientCore;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.Configuration;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>In-room CnCNet game lobby logic (XNA CnCNetGameLobby CTCP subset). Implements ICnCNetGameSession.</summary>
public sealed class CnCNetGameRoomSession : ICnCNetGameSession
{
    private readonly object _sync = new();
    private readonly List<CnCNetGameRoomPlayer> _players = [];
    private readonly HashSet<string> _channelUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICnCNetPlayerSlot[] _playerSlots =
        Enumerable.Range(0, LobbyPlayerSlot.MaxSlots).Select(_ => new LobbyPlayerSlot()).ToArray();
    private readonly GameOptionsState _sessionOptions = new();
    private IMapResource? _sessionMap;
    private GameSessionState _sessionState = GameSessionState.Lobby;

    private CnCNetIrcConnection? _connection;
    private CnCNetGameBroadcastService? _gameBroadcast;
    private CnCNetGameChannels? _channels;
    private bool _locked;
    private int _uniqueGameId;
    private string _localNick = AppState.Environment.PlayerName;
    private int _randomSeed;
    private bool _removeStartingLocations;
    private int _frameSendRate = ResolveDefaultFrameSendRate();
    private int _maxAhead = ResolveDefaultMaxAhead();
    private int _protocolVersion = ResolveDefaultProtocolVersion();
    private bool _tunnelErrorMode;
    private bool _localJoined;
    private string? _pendingGameOptionsBody;
    private bool _hostLaunchInFlight;
    private CancellationTokenSource? _lobbyPrewarmCts;

    /// <summary>
    /// In-room chat timeline (PRIVMSG on the room channel + system notices).
    /// Kept independent of <see cref="CnCNetLobbyState"/>'s lobby timeline to mirror
    /// DX's two-channel split (lobby Channel vs in-room Channel).
    /// </summary>
    private readonly List<CnCNetChatLine> _chatLines = [];
    private const int MaxRoomChatLines = 200;
    private GameRoomChatCommandDispatcher? _chatCommands;

    public bool TunnelErrorMode => _tunnelErrorMode;

    public bool IsLocalJoined => _localJoined;

    public Func<CnCNetGameOptionsState>? GameOptionsProvider { get; set; }

    public Action<CnCNetGameOptionsState>? GameOptionsReceiver { get; set; }

    public Func<(int CheckBoxCount, int DropDownCount)>? GameOptionsControlCounts { get; set; }

    public Func<IReadOnlyList<CnCNetTunnel>>? AvailableTunnelsProvider { get; set; }

    public int PlayerOptionsMaxSideIndex { get; set; } = 10;

    public int PlayerOptionsMaxColorIndex { get; set; } = 16;

    public int RandomSeed => _randomSeed;

    public bool RemoveStartingLocations => _removeStartingLocations;

    public int FrameSendRate => _frameSendRate;

    public int MaxAhead => _maxAhead;

    public int ProtocolVersion => _protocolVersion;

    /// <summary>
    /// Host broadcasts current game options (GO) to everyone already in the room.
    /// Call after lobby option controls change (DX <c>OnGameOptionChanged</c>).
    /// </summary>
    public void BroadcastGameOptions()
    {
        if (!IsHost || !_localJoined)
            return;

        BroadcastGameOptionsLocked();
    }

    /// <summary>
    /// Replay a GO that arrived before lobby option controls were wired.
    /// </summary>
    public void TryFlushPendingGameOptions()
    {
        string? pending;
        lock (_sync)
        {
            pending = _pendingGameOptionsBody;
            _pendingGameOptionsBody = null;
        }

        if (pending == null)
            return;

        ApplyGameOptions(HostName, pending);
    }

    public event Action? HostAbandoned;

    public event Action? LocalUserKicked;

    private string _gameFilesHash = string.Empty;
    private long _revision;

    public CnCNetGameRoomSession(CnCNetActiveGameRoom room)
    {
        Room = room;
        HostName = room.IsHost ? AppState.Environment.PlayerName : room.HostName;
        SlotSink = new LobbyPlayerSlotSink(
            () => _playerSlots,
            () => BumpRevision());
    }

    /// <summary>原子脏读 tag 自增并触发 StateChanged。</summary>
    private void BumpRevision()
    {
        System.Threading.Interlocked.Increment(ref _revision);
        StateChanged?.Invoke();
    }

    public CnCNetActiveGameRoom Room { get; }

    /// <inheritdoc />
    public LobbyPlayerMode Mode => LobbyPlayerMode.Multiplayer;

    /// <inheritdoc />
    public long Revision => _revision;

    public bool IsHost => Room.IsHost;

    public string HostName { get; private set; }

    /// <summary>ICnCNetGameSession.RoomName — 转发到底层 Room。</summary>
    public string RoomName => Room.RoomName;

    public bool Locked => _locked;

    public int UniqueGameId => _uniqueGameId;

    /// <inheritdoc />
    public IMapResource? Map
    {
        get => _sessionMap;
        set
        {
            _sessionMap = value;
            StateChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IPlayerSlot> PlayerSlots => _playerSlots;

    /// <summary>
    /// CnCNet 视角的槽位（含 Ready / Ping / Port 等网络字段）。
    /// 与 <see cref="PlayerSlots"/> 同源；用此属性避免在调用点做类型转换。
    /// </summary>
    public IReadOnlyList<ICnCNetPlayerSlot> CnCNetPlayerSlots => _playerSlots;

    /// <inheritdoc />
    /// <remarks>
    /// Sink 直接操作 <c>_playerSlots</c>。非静默写入后触发 <see cref="StateChanged"/>。
    /// 注：sink 写入不会自动广播 PO CTCP——广播责任在 Service 层
    /// （见 <see cref="BroadcastPlayerOptions"/>）。
    /// </remarks>
    public IPlayerSlotSink SlotSink { get; }

    /// <inheritdoc />
    /// <remarks>
    /// CnCNet 房间里改地图：房主会清 AI 槽并按新 maxPlayers 调整，但保留人类玩家。
    /// Joiner 不应调用此方法（地图由房主决定）。
    /// </remarks>
    public void ResetSlotsForMap(int maxPlayers)
    {
        if (!IsHost)
            return;

        lock (_sync)
        {
            // 仅清 AI 槽（人类玩家保留）；与 SyncPlayersFromLobby 的语义一致。
            for (int i = 0; i < _playerSlots.Length; i++)
            {
                if (_playerSlots[i].IsAi)
                    ClearSlotLocked(_playerSlots[i]);
            }
        }
        BumpRevision();
    }

    /// <inheritdoc />
    public void InitHostSlots(string localPlayerName)
    {
        // 房间初次创建：清空所有槽位，slot[0] 写本地人。空槽保持 Open（UI override），
        // 不走 DefaultAiSlotPolicy 自动填充；AI / 玩家由房主操作或 PO 同步。
        lock (_sync)
        {
            for (int i = 0; i < _playerSlots.Length; i++)
                ClearSlotLocked(_playerSlots[i]);

            var host = _playerSlots[0];
            host.Name = localPlayerName;
            host.IsAi = false;
            host.IsHumanLocal = true;
        }
        BumpRevision();
    }

    /// <inheritdoc />
    public void ReorderHostFirst(string hostName, string localNick)
    {
        // 保留现有 humans/ais，把 host 强制移到 slot[0]。
        // 算法对应 LobbyPlayerState.EnsureHostAsFirstHuman（语义保持一致）。
        hostName = NormalizeNick(hostName, localNick);

        List<ICnCNetPlayerSlot> humans = new();
        List<ICnCNetPlayerSlot> ais = new();

        lock (_sync)
        {
            foreach (var slot in _playerSlots)
            {
                if (!slot.IsOccupied)
                    continue;
                if (slot.IsAi)
                    ais.Add(CloneSlot(slot));
                else
                    humans.Add(CloneSlot(slot));
            }

            var host = humans.FirstOrDefault(h => h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase));
            if (host == null)
            {
                host = CloneSlot(_playerSlots[0]); // 模板
                host.Name = hostName;
                host.IsAi = false;
                host.SideIndex = 0;
                host.ColorIndex = 0;
                host.TeamIndex = 0;
                host.StartIndex = 0;
            }
            host.IsAi = false;
            host.IsHumanLocal = host.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);

            humans.RemoveAll(h => h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase));
            humans.Insert(0, host);

            // 清空再写回
            for (int i = 0; i < _playerSlots.Length; i++)
                ClearSlotLocked(_playerSlots[i]);

            int row = 0;
            foreach (var h in humans)
            {
                if (row >= _playerSlots.Length) break;
                CopySlotTo(h, _playerSlots[row]);
                row++;
            }
            foreach (var a in ais)
            {
                if (row >= _playerSlots.Length) break;
                CopySlotTo(a, _playerSlots[row]);
                row++;
            }
        }
        BumpRevision();
    }

    private static ICnCNetPlayerSlot CloneSlot(ICnCNetPlayerSlot src)
    {
        // LobbyPlayerSlot 是 ICnCNetPlayerSlot 的默认实现，Clone 直接 new 一个。
        if (src is LobbyPlayerSlot concrete)
            return concrete.Clone();

        // 兜底：手动复制（不应该走到，但保证正确性）
        var dst = new LobbyPlayerSlot
        {
            Name = src.Name,
            IsAi = src.IsAi,
            IsHumanLocal = src.IsHumanLocal,
            SideIndex = src.SideIndex,
            ColorIndex = src.ColorIndex,
            StartIndex = src.StartIndex,
            TeamIndex = src.TeamIndex,
            AiLevel = src.AiLevel,
            IsHost = src.IsHost,
            Ready = src.Ready,
            AutoReady = src.AutoReady,
            Ping = src.Ping,
            Port = src.Port,
        };
        return dst;
    }

    private static void CopySlotTo(ICnCNetPlayerSlot src, ICnCNetPlayerSlot dst)
    {
        dst.Name = src.Name;
        dst.IsAi = src.IsAi;
        dst.IsHumanLocal = src.IsHumanLocal;
        dst.SideIndex = src.SideIndex;
        dst.ColorIndex = src.ColorIndex;
        dst.StartIndex = src.StartIndex;
        dst.TeamIndex = src.TeamIndex;
        dst.AiLevel = src.AiLevel;
        dst.IsHost = src.IsHost;
        dst.Ready = src.Ready;
        dst.AutoReady = src.AutoReady;
        dst.Ping = src.Ping;
        dst.Port = src.Port;
    }

    private static string NormalizeNick(string primary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();

        return AppState.Environment.PlayerName;
    }

    /// <inheritdoc />
    public void MarkLocalHuman(string playerName)
    {
        // 在所有槽位中找到名字匹配的，标 IsHumanLocal=true，其余清掉此标志。
        lock (_sync)
        {
            foreach (var slot in _playerSlots)
                slot.IsHumanLocal = string.Equals(slot.Name, playerName, StringComparison.Ordinal);
        }
        BumpRevision();
    }

    /// <inheritdoc />
    public void ApplyPlayersFromNetwork(IReadOnlyList<CnCNetGameRoomPlayer> entries, string hostName, string localNick)
    {
        // Phase 2 缺口 2.3：把 CTCP 收到的 PO DTO 应用到 _playerSlots，并完成 host 重排 + 本地标记。
        // 替代 MainWindow 旧的 ApplyToState + EnsureHostAsFirstHuman + MarkLocalHuman 三步胶水。
        ArgumentNullException.ThrowIfNull(entries);

        lock (_sync)
        {
            // 清空 → 写入
            for (int i = 0; i < _playerSlots.Length; i++)
                ClearSlotLocked(_playerSlots[i]);

            MultiplayerSlotLayout.ApplyToSlots(_playerSlots, entries, localNick);
        }

        // Host 重排（保留 + 移到 [0]）+ 本地标记
        if (IsHost)
            ReorderHostFirst(hostName, localNick);
        else
            MarkLocalHuman(localNick);
    }

    /// <inheritdoc />
    public void BroadcastPlayerOptionsFromSlots(string hostName, IReadOnlyList<string> aiNames)
    {
        // Phase 2 缺口 2.4：从 _playerSlots 重建 PO DTO + 广播。
        // 替代旧 SyncPlayersFromLobby(LobbyPlayerState, string)，不再依赖 LobbyPlayerState。
        if (!IsHost)
            return;

        var dto = MultiplayerSlotLayout.BuildPoList(_playerSlots, hostName, aiNames);
        SyncPlayersFromDtoLocked(dto, hostName, aiNames);
        BumpRevision();
    }

    /// <summary>
    /// Phase 2 内部：从给定 DTO 重建 _players + 广播 + StateChanged。
    /// 替代 SyncPlayersFromLobby 的 LobbyPlayerState 入口。
    /// </summary>
    private void SyncPlayersFromDtoLocked(List<CnCNetGameRoomPlayer> entries, string hostName, IReadOnlyList<string> aiNames)
    {
        if (!IsHost)
            return;

        AppendChannelJoinersLocked(entries, hostName);

        var readyByName = _players.Where(p => !p.IsAi)
            .ToDictionary(p => p.Name, p => (p.Ready, p.AutoReady), StringComparer.OrdinalIgnoreCase);

        foreach (CnCNetGameRoomPlayer entry in entries)
        {
            if (entry.IsAi)
            {
                entry.Ready = true;
            }
            else if (readyByName.TryGetValue(entry.Name, out (bool Ready, bool AutoReady) existing))
            {
                entry.Ready = existing.Ready;
                entry.AutoReady = existing.AutoReady;
            }

            if (entry.IsHost || (!entry.IsAi && entry.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase) && IsHost))
                entry.Ready = true;
        }

        if (PlayerListsEquivalent(_players, entries))
            return;

        PlayerOptionsCodec.ApplyDto(entries, _playerSlots, _localNick);
        SyncPlayersFromSlotsLocked(hostName, aiNames);
        BroadcastPlayerOptionsLocked();
    }

    /// <inheritdoc />
    public void UpdateHuman(string playerName, in SlotFieldUpdate update)
    {
        // Phase 2 缺口 2.4：根据玩家名找到槽位，按 SlotFieldUpdate 部分更新。
        // 替代旧 UpdateHumanFromSlot(LobbyPlayerSlot)，调用方无需先持有 LobbyPlayerSlot 引用。
        if (!IsHost || update.IsEmpty)
            return;

        lock (_sync)
        {
            // 优先按 _players 中的真实玩家记录更新（保留旧 UpdateHumanFromSlot 语义）
            CnCNetGameRoomPlayer? player = FindPlayerLocked(playerName);
            if (player == null)
                return;

            if (update.SideIndex.HasValue) player.SideId = update.SideIndex.Value;
            if (update.ColorIndex.HasValue) player.ColorId = update.ColorIndex.Value;
            if (update.TeamIndex.HasValue) player.TeamId = update.TeamIndex.Value;
            if (update.StartIndex.HasValue) player.StartingLocation = update.StartIndex.Value;
        }
        BumpRevision();
    }

    private static void ClearSlotLocked(ICnCNetPlayerSlot slot)
    {
        slot.Name = string.Empty;
        slot.IsAi = false;
        slot.IsHumanLocal = false;
        slot.SideIndex = 0;
        slot.ColorIndex = 0;
        slot.StartIndex = 0;
        slot.TeamIndex = 0;
        slot.AiLevel = 0;
        slot.IsHost = false;
        slot.Ready = false;
        slot.AutoReady = false;
        slot.Ping = -1;
        slot.Port = 0;
    }

    /// <summary>可变游戏选项（GO 同步可用）。</summary>
    public GameOptionsState SessionOptions => _sessionOptions;

    /// <inheritdoc />
    IGameOptionsState IGameSession.Options => _sessionOptions;

    /// <inheritdoc />
    public GameSessionState State
    {
        get => _sessionState;
        set
        {
            if (_sessionState == value)
                return;
            _sessionState = value;
            StateChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public CnCNetTunnel Tunnel
    {
        get => Room.Tunnel;
        set => Room.Tunnel = value;
    }

    /// <inheritdoc />
    public string ChannelName => Room.ChannelName;

    /// <inheritdoc />
    public string? Password
    {
        get => Room.Password;
        set => Room.Password = value ?? string.Empty;
    }

    /// <inheritdoc />
    public int MaxPlayers
    {
        get => Room.MaxPlayers;
        set => Room.MaxPlayers = value;
    }

    /// <inheritdoc />
    public int SkillLevel
    {
        get => Room.SkillLevel;
        set => Room.SkillLevel = value;
    }

    /// <inheritdoc />
    public bool Passworded
    {
        get => Room.Passworded;
        set => Room.Passworded = value;
    }

    public IReadOnlyList<CnCNetGameRoomPlayer> Players
    {
        get
        {
            lock (_sync)
                return _players.ToList();
        }
    }

    public event Action? StateChanged;

    public event Action<string>? NoticeLogged;

    /// <summary>
    /// Raised whenever the in-room chat timeline changes (new user message, system notice,
    /// or clear on leave). Mirrors DX's <c>Channel.MessageAdded</c> for the game-room channel.
    /// </summary>
    public event Action? ChatChanged;

    public event Action<CnCNetStartGameInfo>? GameStarting;

    public void Attach(
        CnCNetIrcConnection connection,
        CnCNetGameBroadcastService gameBroadcast,
        CnCNetGameChannels? channels)
    {
        _connection = connection;
        _gameBroadcast = gameBroadcast;
        _channels = channels;
        _localJoined = false;
        _localNick = connection.CurrentNick;
        if (IsHost)
        {
            HostName = _localNick;
            lock (_sync)
                EnsureHostPlayerLocked();
        }
    }

    public void OnLocalJoined()
    {
        _localJoined = true;

        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();
        _gameFilesHash = fhc.GetCompleteHash();

        lock (_sync)
        {
            _uniqueGameId = Random.Shared.Next(1_000_000, int.MaxValue);
            _randomSeed = Random.Shared.Next();
            _channelUsers.Add(_localNick);

            if (IsHost)
            {
                HostName = _localNick;
                EnsureHostPlayerLocked();
                _gameBroadcast?.StartHost(_connection!, _channels!, Room);
                BroadcastPlayerOptionsLocked();
            }
            else
            {
                SendCtcp($"FHSH {_gameFilesHash}");
            }
        }

        LogNotice(IsHost ? $"Hosting \"{Room.RoomName}\"." : $"Joined \"{Room.RoomName}\".");

        if (_connection?.IsLocalOnChannel(Room.ChannelName) == true)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Room.Tunnel.UpdatePing();
                BroadcastLocalTunnelPingLocked();
                StateChanged?.Invoke();
            });
        }

        StateChanged?.Invoke();
    }

    public void OnChannelUserList(IReadOnlyList<string> users)
    {
        if (!IsGameChannel(Room.ChannelName))
            return;

        lock (_sync)
        {
            foreach (string user in users)
            {
                string name = StripPrefixes(user);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                _channelUsers.Add(name);

                if (IsHost)
                    AddOrRefreshHumanPlayerLocked(name, name.Equals(_localNick, StringComparison.OrdinalIgnoreCase));
            }

            if (IsHost)
                BroadcastPlayerOptionsLocked();
        }

        if (!IsHost && !string.IsNullOrWhiteSpace(HostName))
        {
            bool hostPresent = users.Any(u =>
                StripPrefixes(u).Equals(HostName, StringComparison.OrdinalIgnoreCase));

            if (!hostPresent)
            {
                LogNotice("The game host has abandoned the game.");
                HostAbandoned?.Invoke();
                return;
            }
        }

        StateChanged?.Invoke();
    }

    public void OnUserJoined(string channel, string user)
    {
        if (!IsGameChannel(channel))
            return;

        string name = StripPrefixes(user);
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_sync)
        {
            _channelUsers.Add(name);

            if (!IsHost && string.IsNullOrEmpty(HostName))
            {
                // First non-local joiner on channel is treated as host nick until PO arrives.
            }

            if (IsHost)
            {
                AddOrRefreshHumanPlayerLocked(name, name.Equals(_localNick, StringComparison.OrdinalIgnoreCase));
                BroadcastPlayerOptionsLocked();
                // DX ChangeMap → OnGameOptionChanged: push current GO so the joiner syncs options.
                BroadcastGameOptionsLocked();

                int humanCount = _players.Count(p => !p.IsAi);
                if (!name.Equals(_localNick, StringComparison.OrdinalIgnoreCase)
                    && humanCount >= Room.MaxPlayers
                    && !_locked)
                {
                    SetLocked(true, autoPlayerLimit: true);
                }
            }
        }

        StateChanged?.Invoke();
    }

    public void OnUserLeft(string channel, string user)
    {
        if (!IsGameChannel(channel))
            return;

        string name = StripPrefixes(user);
        lock (_sync)
        {
            _channelUsers.Remove(name);
            _players.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            SyncSlotsFromPlayersLocked();

            if (IsHost)
            {
                BroadcastPlayerOptionsLocked();

                if (_locked && !ProgramConstants.IsInGame && !ProgramConstants.IsLaunchingGame)
                    SetLocked(false);
            }
        }

        StateChanged?.Invoke();
    }

    public void OnUserKicked(string channel, string user)
    {
        if (!IsGameChannel(channel))
            return;

        string name = StripPrefixes(user);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
        {
            LogNotice("You were kicked from the game!");
            LocalUserKicked?.Invoke();
            return;
        }

        OnUserLeft(channel, name);
    }

    public void OnUserNicknameChanged(string oldNickname, string newNickname)
    {
        if (string.IsNullOrWhiteSpace(oldNickname) || string.IsNullOrWhiteSpace(newNickname))
            return;

        lock (_sync)
        {
            if (_channelUsers.Remove(oldNickname))
                _channelUsers.Add(newNickname);

            foreach (CnCNetGameRoomPlayer player in _players)
            {
                if (player.Name.Equals(oldNickname, StringComparison.OrdinalIgnoreCase))
                    player.Name = newNickname;
            }

            if (HostName.Equals(oldNickname, StringComparison.OrdinalIgnoreCase))
                HostName = newNickname;
        }

        StateChanged?.Invoke();
    }

    public void OnChannelCtcp(string channel, string sender, string ctcp)
    {
        if (!IsGameChannel(channel))
            return;

        try
        {
            HandleChannelCtcpCore(sender, ctcp);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet CTCP handle failed ({DescribeCtcp(ctcp)} from {sender}): {ex.Message}");
            Logger.Log(ex.ToString());
        }
    }

    private void HandleChannelCtcpCore(string sender, string ctcp)
    {
        if (ctcp.StartsWith("PO ", StringComparison.Ordinal))
        {
            ApplyPlayerOptions(sender, ctcp[3..]);
            return;
        }

        if (ctcp.StartsWith("GO ", StringComparison.Ordinal))
        {
            ApplyGameOptions(sender, ctcp[3..]);
            return;
        }

        if (ctcp.StartsWith("START ", StringComparison.Ordinal))
        {
            HandleStart(sender, ctcp[6..]);
            return;
        }

        if (ctcp.StartsWith("R ", StringComparison.Ordinal) && IsHost)
        {
            HandleReadyRequest(sender, ctcp[2..].Trim());
            return;
        }

        if (ctcp.StartsWith("OR ", StringComparison.Ordinal) && IsHost)
        {
            ApplyOptionsRequest(sender, ctcp[3..].Trim());
            return;
        }

        if (ctcp.StartsWith("TNLPNG ", StringComparison.Ordinal))
        {
            HandleTunnelPing(sender, ctcp[7..].Trim());
            return;
        }

        if (ctcp.StartsWith("GSETTINGS ", StringComparison.Ordinal))
        {
            ApplyGameLobbySettings(sender, ctcp[10..]);
            return;
        }

        if (ctcp.StartsWith("FHSH ", StringComparison.Ordinal) && IsHost)
        {
            HandleFileHashNotification(sender, ctcp[5..].Trim());
            return;
        }

        if (ctcp.StartsWith("MM ", StringComparison.Ordinal))
        {
            HandleCheaterNotification(sender, ctcp[3..].Trim());
            return;
        }

        if (ctcp.Equals("CD", StringComparison.Ordinal))
        {
            LogNotice($"{sender} has modified game files during the client session. They are likely attempting to cheat!");
            return;
        }

        if (ctcp.StartsWith("CHTNL ", StringComparison.Ordinal))
        {
            HandleTunnelChange(sender, ctcp[6..].Trim());
            return;
        }

        if (ctcp.Equals("STRTD", StringComparison.Ordinal))
            LogNotice($"{sender} started the game.");
    }

    private static string DescribeCtcp(string ctcp)
    {
        int space = ctcp.IndexOf(' ');
        return space > 0 ? ctcp[..space] : ctcp;
    }

    /// <param name="autoPlayerLimit">
    /// When true, room notice matches DX "Player limit reached…" wording (vs host manual lock).
    /// </param>
    public void SetLocked(bool locked, bool autoPlayerLimit = false)
    {
        bool changed = false;
        lock (_sync)
        {
            if (_locked == locked)
                return;

            _locked = locked;
            changed = true;

            if (IsHost && _connection != null)
            {
                string wire = CnCNetIrcChannelNames.Preserve(Room.ChannelName);
                _connection.TrySendInstantOnChannel(wire, $"MODE {wire} {(locked ? "+i" : "-i")}");
                BroadcastPlayerOptionsLocked();
            }
        }

        if (!changed)
            return;

        if (IsHost)
        {
            RefreshGameListingLockedFlag();
            if (locked && autoPlayerLimit)
                AddRoomNotice("Player limit reached. The game room has been locked.");
            else if (locked)
                AddRoomNotice("You've locked the game room.");
            else
                AddRoomNotice("The game room has been unlocked.");
        }

        if (locked)
            RequestLobbyPrewarm();
        else
            CancelLobbyPrewarm();

        StateChanged?.Invoke();
    }

    private void RefreshGameListingLockedFlag()
    {
        if (_gameBroadcast == null || !IsHost)
            return;

        List<string> names;
        lock (_sync)
            names = GetHumanPlayerNamesLocked();

        _gameBroadcast.UpdateListing(
            _gameBroadcastListingMapName,
            _gameBroadcastListingGameMode,
            _gameBroadcastListingMapSha1,
            names,
            _locked,
            closed: false);
    }

    /// <summary>
    /// Joiner/host sync when IRC <c>MODE +i/-i</c> arrives (DX <c>Channel_ChannelModesChanged</c>).
    /// </summary>
    public void OnChannelModesChanged(string channel, string modeString)
    {
        if (!IsGameChannel(channel) || string.IsNullOrWhiteSpace(modeString))
            return;

        bool lockRoom = modeString.Contains("+i", StringComparison.Ordinal);
        bool unlockRoom = modeString.Contains("-i", StringComparison.Ordinal);
        if (!lockRoom && !unlockRoom)
            return;

        bool next = lockRoom;
        lock (_sync)
        {
            if (_locked == next)
                return;
            _locked = next;
        }

        if (IsHost)
        {
            // Host already issued MODE and notices from SetLocked; avoid duplicate chat lines.
            StateChanged?.Invoke();
            return;
        }

        if (next)
        {
            int humans = Players.Count(p => !p.IsAi);
            if (humans >= Room.MaxPlayers)
                AddRoomNotice("Player limit reached. The game room has been locked.");
            else
                AddRoomNotice("The game host has locked the game room.");
        }
        else
        {
            AddRoomNotice("The game room has been unlocked.");
        }

        StateChanged?.Invoke();
    }

    public void SetLocalReady(bool ready, bool autoReady = false)
    {
        if (IsHost)
            return;

        int readyState = autoReady ? 2 : ready ? 1 : 0;
        SendCtcp($"R {readyState}");
        lock (_sync)
        {
            CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
            if (local != null)
            {
                local.Ready = readyState > 0;
                local.AutoReady = autoReady;
            }
        }

        if (readyState > 0)
            RequestLobbyPrewarm();

        StateChanged?.Invoke();
    }

    /// <summary>Joiner sends side/color/start/team (XNA CnCNetGameLobby.RequestPlayerOptions / OR CTCP).</summary>
    public void RequestLocalPlayerOptions(LobbyPlayerSlot slot)
    {
        if (IsHost || _connection == null)
            return;

        int packed = PackOptionsRequest(slot.SideIndex, slot.ColorIndex, slot.StartIndex, slot.TeamIndex);
        SendCtcp($"OR {packed}");
    }

    public void UpdateHostListing(string mapName, string gameModeName, string mapSha1)
    {
        if (!IsHost || !_localJoined)
            return;

        _gameBroadcastListingMapName = mapName;
        _gameBroadcastListingGameMode = gameModeName;
        _gameBroadcastListingMapSha1 = mapSha1;

        var names = GetHumanPlayerNamesLocked();
        _gameBroadcast?.UpdateListing(mapName, gameModeName, mapSha1, names, _locked, closed: false);
        BroadcastGameOptionsLocked();
    }

    public void BroadcastLocalTunnelPing()
    {
        if (!_localJoined)
            return;

        lock (_sync)
            BroadcastLocalTunnelPingLocked();

        StateChanged?.Invoke();
    }

    /// <summary>Send TNLPNG without ICMP (launch keepalive synthetic mode).</summary>
    public void BroadcastTunnelPingValue(int pingMs)
    {
        if (!_localJoined)
            return;

        lock (_sync)
        {
            if (_connection == null)
                return;

            SendCtcp($"TNLPNG {pingMs}");

            CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
            if (local != null)
                local.Ping = pingMs;
        }

        StateChanged?.Invoke();
    }

    public void UpdateGameLobbySettings(string roomName, int maxPlayers, int skillLevel, string? password)
    {
        if (!IsHost || _connection == null)
            return;

        int occupiedCount = _players.Count;
        if (!GameLobbySettingsRules.CanSetMaxPlayers(maxPlayers, occupiedCount, out string? reject))
        {
            LogNotice(reject!);
            return;
        }

        string oldRoomName = Room.RoomName;
        int oldMaxPlayers = Room.MaxPlayers;
        bool oldPassworded = Room.Passworded;
        Room.RoomName = roomName;
        Room.MaxPlayers = maxPlayers;
        Room.SkillLevel = skillLevel;

        bool passwordSettingsChanged = false;
        if (password != null)
        {
            string currentUserPassword = Room.Passworded ? Room.Password : string.Empty;
            passwordSettingsChanged = !string.Equals(currentUserPassword, password, StringComparison.Ordinal);

            if (passwordSettingsChanged)
            {
                bool newCustomPassword = !string.IsNullOrEmpty(password);
                string actualPassword = newCustomPassword
                    ? password
                    : CnCNetLobbyOperations.GetDefaultChannelPassword(Room.ChannelName, Room.RoomName);
                string oldPassword = Room.Password;
                Room.Passworded = newCustomPassword;
                Room.Password = actualPassword;

                string wire = CnCNetIrcChannelNames.Preserve(Room.ChannelName);
                string mode = CnCNetLobbyOperations.BuildChannelPasswordModeCommand(wire, oldPassword, actualPassword);
                _connection.TrySendInstantOnChannel(wire, mode);
            }
        }

        BroadcastGameLobbySettings();

        if (!oldRoomName.Equals(roomName, StringComparison.Ordinal))
            LogNotice($"Game room name changed from \"{oldRoomName}\" to \"{roomName}\".");

        if (oldMaxPlayers != maxPlayers)
            LogNotice($"Maximum players changed to {maxPlayers}.");

        if (passwordSettingsChanged)
        {
            if (string.IsNullOrEmpty(password))
                LogNotice("Password removed from the game.");
            else if (!oldPassworded)
                LogNotice("Password added to the game.");
            else
                LogNotice("Password changed.");
        }

        BroadcastPlayerOptionsLocked();
        _gameBroadcast?.UpdateListing(
            _gameBroadcastListingMapName,
            _gameBroadcastListingGameMode,
            _gameBroadcastListingMapSha1,
            GetHumanPlayerNamesLocked(),
            _locked,
            closed: false);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Host changes the room tunnel and broadcasts <c>CHTNL address:port</c>
    /// (DX <c>TunnelSelectionWindow_TunnelSelected</c>).
    /// </summary>
    public bool TryHostChangeTunnel(CnCNetTunnel tunnel)
    {
        if (!IsHost || tunnel == null || _connection == null || !_localJoined)
            return false;

        if (string.IsNullOrWhiteSpace(tunnel.Address) || tunnel.Port == 0)
            return false;

        Room.Tunnel = tunnel;
        _tunnelErrorMode = false;

        SendCtcp(CnCNetTunnelChangeProtocol.FormatChtnl(tunnel.Address, tunnel.Port));
        LogNotice($"The game host has changed the tunnel server to: {tunnel.Name}");

        lock (_sync)
        {
            foreach (CnCNetGameRoomPlayer player in _players)
            {
                if (!player.IsAi)
                    player.Ping = -1;
            }
        }

        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Current tunnel summary for <c>/tunnelinfo</c>.</summary>
    public string FormatTunnelInfoNotice()
    {
        CnCNetTunnel tunnel = Room.Tunnel;
        string ping = tunnel.PingInMs >= 0 ? $"{tunnel.PingInMs} ms" : "n/a";
        return $"Tunnel: {tunnel.Name} ({tunnel.Address}:{tunnel.Port}), ping {ping}";
    }

    private string _gameBroadcastListingMapName = string.Empty;
    private string _gameBroadcastListingGameMode = string.Empty;
    private string _gameBroadcastListingMapSha1 = string.Empty;

    public void KickPlayer(string playerName)
    {
        if (!IsHost || _connection == null || string.IsNullOrWhiteSpace(playerName))
            return;

        if (playerName.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
            return;

        LogNotice($"Kicking {playerName} from the game...");
        _connection.KickFromChannel(Room.ChannelName, playerName);
    }

    /// <summary>
    /// 把 <see cref="_playerSlots"/> 重新编码为 PO DTO 列表，覆盖 <see cref="_players"/>。
    /// 保留 NAT 分配的端口（端口不在槽位状态里，只存在于 _players / START 协议中）。
    /// </summary>
    /// <param name="hostName">房主名（PO DTO 中 IsHost 判定）。</param>
    /// <param name="aiNames">AI 名字目录（按 AiLevel 索引）。</param>
    private void SyncPlayersFromSlotsLocked(string hostName, IReadOnlyList<string> aiNames)
    {
        var dto = PlayerOptionsCodec.ToDto(_playerSlots, hostName, aiNames);

        var preservedPorts = _players.Where(p => !p.IsAi)
            .ToDictionary(p => p.Name, p => p.Port, StringComparer.OrdinalIgnoreCase);

        _players.Clear();
        foreach (CnCNetGameRoomPlayer p in dto)
        {
            if (preservedPorts.TryGetValue(p.Name, out ushort port) && port != CnCNetPortValidator.UnsetPort)
                p.Port = port;

            _players.Add(p);
        }
    }

    /// <summary>
    /// 把 <see cref="_players"/> 的当前内容同步到 <see cref="_playerSlots"/>。
    /// 在每个修改 <see cref="_players"/> 的方法末尾、释放锁或触发 StateChanged 之前调用。
    ///
    /// 这是 PRAGMATIC MINIMAL 版本：_players 仍是 CTCP/START 路径写入的状态，
    /// 但 _playerSlots 始终与之保持一致，作为对外的「单一真相源」投影。
    /// </summary>
    private void SyncSlotsFromPlayersLocked()
    {
        PlayerOptionsCodec.ApplyDto(_players, _playerSlots, _localNick);
    }

    public bool TryHostLaunch(out string message)
    {
        message = string.Empty;
        if (!IsHost)
        {
            message = "Only the host can launch the game.";
            return false;
        }

        if (_connection == null || !_connection.IsConnected)
        {
            message = "IRC not connected.";
            return false;
        }

        if (!_locked)
        {
            message = "The host needs to lock the game room before launching the game.";
            return false;
        }

        List<CnCNetGameRoomPlayer> humans;
        lock (_sync)
        {
            if (_hostLaunchInFlight)
            {
                message = "Already contacting the tunnel server…";
                return false;
            }

            if (_players.Count == 0)
                EnsureHostPlayerLocked();

            humans = _players.Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (humans.Count == 0)
            {
                message = "No players in the room.";
                return false;
            }

            foreach (CnCNetGameRoomPlayer human in humans)
            {
                if (human.IsHost || human.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                {
                    human.Ready = true;
                    continue;
                }

                if (!human.Ready)
                {
                    message = "Not all players are ready.";
                    return false;
                }
            }

            if (humans.Count == 1)
            {
                Logger.Log("One player MP -- starting!");
                EmitHostStartingCheatCheck();
                _gameBroadcast?.MarkGameStarting();
                SendCtcp("STRTD");
                GameStarting?.Invoke(new CnCNetStartGameInfo
                {
                    UniqueGameId = _uniqueGameId,
                    Tunnel = Room.Tunnel,
                    LocalPlayerPort = CnCNetPortValidator.UnsetPort,
                    IsHost = true,
                });
                message = "Starting game...";
                return true;
            }

            if (string.IsNullOrWhiteSpace(Room.Tunnel.Address)
                || Room.Tunnel.Address.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                message = "The selected tunnel server is invalid. Choose another tunnel and try again.";
                return false;
            }

            _hostLaunchInFlight = true;
        }

        int playerCount = humans.Count;
        string[] playerNames = humans.Select(h => h.Name).ToArray();
        CnCNetTunnel tunnel = Room.Tunnel;

        // Tunnel HTTP can take several seconds — never block the UI/IRC pump.
        Task.Run(() =>
        {
            IReadOnlyList<ushort> ports;
            try
            {
                ports = tunnel.GetPlayerPortInfo(playerCount);
            }
            catch (Exception ex)
            {
                Logger.Log($"CnCNet TryHostLaunch: GetPlayerPortInfo failed: {ex.Message}");
                ports = [];
            }

            Dispatcher.UIThread.Post(() => CompleteHostLaunchWithPorts(playerNames, ports));
        });

        message = "Contacting tunnel server...";
        return true;
    }

    private void CompleteHostLaunchWithPorts(string[] expectedPlayerNames, IReadOnlyList<ushort> ports)
    {
        try
        {
            if (!IsHost || _connection == null || !_connection.IsConnected || !_locked)
            {
                AddRoomNotice("Game launch cancelled.");
                return;
            }

            List<CnCNetGameRoomPlayer> humans;
            lock (_sync)
            {
                humans = _players.Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name)).ToList();
                if (humans.Count != expectedPlayerNames.Length
                    || !humans.Select(h => h.Name).SequenceEqual(expectedPlayerNames, StringComparer.OrdinalIgnoreCase))
                {
                    AddRoomNotice("Player list changed while contacting the tunnel. Launch cancelled.");
                    return;
                }

                foreach (CnCNetGameRoomPlayer human in humans)
                {
                    if (human.IsHost || human.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                    {
                        human.Ready = true;
                        continue;
                    }

                    if (!human.Ready)
                    {
                        AddRoomNotice("Not all players are ready. Launch cancelled.");
                        return;
                    }
                }
            }

            if (!CnCNetPortValidator.TryValidatePlayerPorts(ports, humans.Count, out string? portError))
            {
                AddRoomNotice(portError ?? "Could not contact the CnCNet tunnel server. Try another tunnel.");
                return;
            }

            var playerPorts = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder("START ");
            sb.Append(_uniqueGameId);
            for (int i = 0; i < humans.Count; i++)
            {
                humans[i].Port = ports[i];
                playerPorts[humans[i].Name] = ports[i];
                sb.Append(';');
                sb.Append(humans[i].Name);
                sb.Append(';');
                // DX CnCNetGameLoadingLobby / NonHostLaunchGame: only NAT port matters in START;
                // tunnel IP is taken from the game room (GAME/CHTNL).
                sb.Append("0.0.0.0:");
                sb.Append(ports[i]);
            }

            string startCtcp = sb.ToString();
            Logger.Log($"CnCNet START: broadcasting via tunnel {Room.Tunnel.Address}:{Room.Tunnel.Port} — {startCtcp}");
            SendCtcp(startCtcp);

            if (!TryBuildStartGameInfo(playerPorts, out CnCNetStartGameInfo? startInfo, out string buildError)
                || startInfo == null)
            {
                AddRoomNotice(string.IsNullOrWhiteSpace(buildError) ? "Failed to build start info." : buildError);
                return;
            }

            EmitHostStartingCheatCheck();
            _gameBroadcast?.MarkGameStarting();
            SendCtcp("STRTD");
            GameStarting?.Invoke(startInfo);
        }
        finally
        {
            lock (_sync)
                _hostLaunchInFlight = false;
        }
    }

    private void EmitHostStartingCheatCheck()
    {
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();
        if (_gameFilesHash != fhc.GetCompleteHash())
        {
            Logger.Log("Game files modified during client session!");
            SendCtcp("CD");
            LogNotice($"{_localNick} has modified game files during the client session. They are likely attempting to cheat!");
        }
    }

    private void RequestLobbyPrewarm()
    {
        CancelLobbyPrewarm();
        _lobbyPrewarmCts = new CancellationTokenSource();
        GameLaunchPreparation.BeginLobbyPrewarm(_lobbyPrewarmCts.Token);
    }

    private void CancelLobbyPrewarm()
    {
        try
        {
            _lobbyPrewarmCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _lobbyPrewarmCts?.Dispose();
        _lobbyPrewarmCts = null;
    }

    private bool AreAllHumansReadyLocked()
    {
        foreach (CnCNetGameRoomPlayer human in _players.Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name)))
        {
            if (human.IsHost || human.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!human.Ready)
                return false;
        }

        return _players.Any(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name));
    }

    private bool TryBuildStartGameInfo(
        IReadOnlyDictionary<string, ushort> playerPorts,
        out CnCNetStartGameInfo? startInfo,
        out string message)
    {
        startInfo = null;
        message = string.Empty;

        if (!playerPorts.TryGetValue(_localNick, out ushort localPort)
            && !playerPorts.TryGetValue(AppState.Environment.PlayerName, out localPort))
        {
            message = "Local player port was not assigned by the tunnel server.";
            return false;
        }

        if (!CnCNetPortValidator.IsValid(localPort))
        {
            message = $"Tunnel assigned invalid local port {localPort}. Try another tunnel server.";
            return false;
        }

        startInfo = new CnCNetStartGameInfo
        {
            UniqueGameId = _uniqueGameId,
            Tunnel = Room.Tunnel,
            LocalPlayerPort = localPort,
            IsHost = true,
            PlayerPorts = playerPorts,
        };
        return true;
    }

    private List<string> GetHumanPlayerNamesLocked()
    {
        lock (_sync)
        {
            return _players
                .Where(p => !p.IsAi && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name)
                .ToList();
        }
    }

    public IReadOnlyList<string> GetHumanPlayerNames()
    {
        lock (_sync)
            return GetHumanPlayerNamesLocked();
    }

    public void Leave()
    {
        if (_connection == null)
            return;

        CancelLobbyPrewarm();
        lock (_sync)
            _hostLaunchInFlight = false;

        string wire = CnCNetIrcChannelNames.Preserve(Room.ChannelName);
        _gameBroadcast?.Stop();

        if (_localJoined || _connection.IsLocalOnChannel(wire))
            _connection.PartChannelInstant(wire);
        else
            _connection.ClearSendQueueForChannel(wire);

        _localJoined = false;
        _connection = null;

        // Drop the in-room timeline on leave — rejoining later starts a fresh history,
        // matching DX behavior where a new Channel instance is created per room session.
        ClearChat();
    }

    private void HandleStart(string sender, string payload)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        IReadOnlyList<CnCNetTunnel> tunnels = AvailableTunnelsProvider?.Invoke() ?? [];
        IReadOnlyList<CnCNetGameRoomPlayer> knownPlayers;
        lock (_sync)
            knownPlayers = _players.ToList();

        if (!CnCNetMultiplayerProtocol.TryParseStartCommand(
                payload,
                _localNick,
                knownPlayers,
                tunnels,
                Room.Tunnel,
                out CnCNetStartParseResult result,
                out string? startError))
        {
            LogNotice(startError ?? "Invalid START message from host.");
            return;
        }

        lock (_sync)
        {
            _uniqueGameId = result.UniqueGameId;

            foreach (KeyValuePair<string, ushort> pair in result.PlayerPorts)
            {
                CnCNetGameRoomPlayer? player = FindPlayerLocked(pair.Key);
                if (player != null)
                    player.Port = pair.Value;
            }
        }

        if (result.MatchedTunnel != null)
            Room.Tunnel = result.MatchedTunnel;

        SendCtcp("STRTD");

        GameStarting?.Invoke(new CnCNetStartGameInfo
        {
            UniqueGameId = result.UniqueGameId,
            Tunnel = Room.Tunnel,
            LocalPlayerPort = result.LocalPlayerPort,
            IsHost = false,
            PlayerPorts = result.PlayerPorts,
        });
    }

    private void HandleTunnelPing(string sender, string payload)
    {
        if (!int.TryParse(payload, out int ping))
            return;

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(sender);
            if (player == null || player.IsAi)
                return;

            player.Ping = ping;
        }

        StateChanged?.Invoke();
    }

    private void BroadcastLocalTunnelPingLocked()
    {
        if (_connection == null)
            return;

        int ping = Room.Tunnel.PingInMs;
        SendCtcp($"TNLPNG {ping}");

        CnCNetGameRoomPlayer? local = FindPlayerLocked(_localNick);
        if (local != null)
            local.Ping = ping;
    }

    private void HandleReadyRequest(string sender, string payload)
    {
        if (!int.TryParse(payload, out int readyState))
            return;

        bool allReady = false;
        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(sender);
            if (player == null)
                return;

            player.Ready = readyState > 0;
            player.AutoReady = readyState > 1;
            BroadcastPlayerOptionsLocked();
            allReady = AreAllHumansReadyLocked();
        }

        if (allReady)
            RequestLobbyPrewarm();

        StateChanged?.Invoke();
    }

    private void ApplyOptionsRequest(string playerName, string payload)
    {
        if (!IsHost || !int.TryParse(payload, out int packed))
            return;

        UnpackOptionsRequest(packed, out int side, out int color, out int start, out int team);

        lock (_sync)
        {
            CnCNetGameRoomPlayer? player = FindPlayerLocked(playerName);
            if (player == null || player.IsAi)
                return;

            if (side < 0 || color < 0 || start < 0 || team < 0)
                return;

            if (side != player.SideId || start != player.StartingLocation || team != player.TeamId)
                ClearHumanReadyStatesLocked(exceptName: playerName);

            player.SideId = side;
            player.ColorId = color;
            player.StartingLocation = start;
            player.TeamId = team;
            BroadcastPlayerOptionsLocked();
        }

        StateChanged?.Invoke();
    }

    private void ClearHumanReadyStatesLocked(string? exceptName = null)
    {
        foreach (CnCNetGameRoomPlayer player in _players)
        {
            if (player.IsAi)
                continue;

            if (exceptName != null && player.Name.Equals(exceptName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (player.IsHost)
                continue;

            player.Ready = false;
            player.AutoReady = false;
        }
    }

    private void ApplyPlayerOptions(string sender, string message)
    {
        if (IsHost && !sender.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsHost && !string.IsNullOrEmpty(HostName) && !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsHost && string.IsNullOrEmpty(HostName))
            HostName = sender;

        HashSet<string> channelUsers;
        lock (_sync)
            channelUsers = new HashSet<string>(_channelUsers, StringComparer.OrdinalIgnoreCase);

        if (!IsHost)
            channelUsers.Add(_localNick);

        if (!CnCNetMultiplayerProtocol.TryParsePlayerOptions(
                message,
                channelUsers,
                PlayerOptionsMaxSideIndex,
                PlayerOptionsMaxColorIndex,
                out List<CnCNetGameRoomPlayer> parsed,
                out string? error))
        {
            Logger.Log($"CnCNet PO parse failed from {sender}: {error}");
            return;
        }

        foreach (CnCNetGameRoomPlayer player in parsed)
        {
            if (!player.IsAi)
                player.IsHost = player.Name.Equals(HostName, StringComparison.OrdinalIgnoreCase);
        }

        lock (_sync)
        {
            if (PlayerListsEquivalent(_players, parsed))
                return;

            var preservedPorts = _players
                .Where(p => !p.IsAi)
                .ToDictionary(p => p.Name, p => p.Port, StringComparer.OrdinalIgnoreCase);

            _players.Clear();
            foreach (CnCNetGameRoomPlayer player in parsed)
            {
                if (preservedPorts.TryGetValue(player.Name, out ushort port) && port != CnCNetPortValidator.UnsetPort)
                    player.Port = port;

                _players.Add(player);
            }

            SyncSlotsFromPlayersLocked();
        }

        StateChanged?.Invoke();
    }

    private void ApplyGameOptions(string sender, string message)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        (int checkBoxCount, int dropDownCount) = GameOptionsControlCounts?.Invoke() ?? (0, 0);
        if (checkBoxCount == 0 && dropDownCount == 0)
        {
            lock (_sync)
                _pendingGameOptionsBody = message;
            Logger.Log("CnCNet GO: deferred until lobby game-option controls are initialized.");
            return;
        }

        if (!CnCNetGameOptionsCodec.TryParseBody(message, checkBoxCount, dropDownCount, out CnCNetGameOptionsState? parsed, out string? error)
            || parsed == null)
        {
            AddRoomNotice("The game host has sent an invalid game options message! The game host's game version might be different from yours.");
            Logger.Log($"CnCNet GO parse failed: {error}");
            return;
        }

        _randomSeed = parsed.RandomSeed;
        _removeStartingLocations = parsed.RemoveStartingLocations;
        _frameSendRate = parsed.FrameSendRate;
        _maxAhead = parsed.MaxAhead;
        _protocolVersion = parsed.ProtocolVersion;
        try
        {
            GameOptionsReceiver?.Invoke(parsed);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet GO UI apply failed: {ex.Message}");
        }

        AddRoomNotice("Game options updated by host.");
        StateChanged?.Invoke();
    }

    private void ApplyGameLobbySettings(string sender, string message)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        string[] parts = message.Split(';');
        if (parts.Length < 4)
            return;

        string newRoomName = parts[0];
        int newMaxPlayers = Conversions.IntFromString(parts[1], Room.MaxPlayers);
        int newSkillLevel = Conversions.IntFromString(parts[2], Room.SkillLevel);
        bool newPassworded = CnCNetGameFlags.ParseSettingsPassworded(parts[3]);

        bool nameChanged = !Room.RoomName.Equals(newRoomName, StringComparison.Ordinal);
        bool maxChanged = Room.MaxPlayers != newMaxPlayers;
        bool skillChanged = Room.SkillLevel != newSkillLevel;

        Room.RoomName = newRoomName;
        Room.MaxPlayers = newMaxPlayers;
        Room.SkillLevel = newSkillLevel;
        Room.Passworded = newPassworded;
        if (!newPassworded)
            Room.Password = CnCNetLobbyOperations.GetDefaultChannelPassword(Room.ChannelName, Room.RoomName);

        if (nameChanged)
            LogNotice($"{sender} changed game room name to \"{newRoomName}\".");

        if (maxChanged)
            LogNotice($"{sender} changed maximum players to {newMaxPlayers}.");

        if (skillChanged)
        {
            string[] options = EnvironmentServices.Resolve<IGameConfiguration>().SkillLevelOptions.Split(',');
            string skillName = newSkillLevel >= 0 && newSkillLevel < options.Length
                ? options[newSkillLevel]
                : newSkillLevel.ToString();
            LogNotice($"{sender} changed skill level to {skillName}.");
        }

        StateChanged?.Invoke();
    }

    private void HandleFileHashNotification(string sender, string filesHash)
    {
        if (!IsHost)
            return;

        if (filesHash.Equals(_gameFilesHash, StringComparison.OrdinalIgnoreCase))
            return;

        Logger.Log($"CnCNet FHSH mismatch from {sender}: joiner={filesHash} host={_gameFilesHash}");
        SendCtcp($"MM {sender}");
        HandleCheaterNotification(_localNick, sender);
    }

    private void HandleCheaterNotification(string sender, string cheaterName)
    {
        if (!sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        LogNotice($"Player {cheaterName} has different files compared to the game host. Either {cheaterName} or the game host could be cheating.");
    }

    private void HandleTunnelChange(string sender, string tunnelAddressAndPort)
    {
        if (IsHost || !sender.Equals(HostName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!CnCNetPortValidator.TryParseEndpoint(tunnelAddressAndPort, out string address, out ushort tunnelPort))
            return;

        CnCNetTunnel? tunnel = AvailableTunnelsProvider?.Invoke()
            .FirstOrDefault(t => t.Address.Equals(address, StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);

        if (tunnel == null)
            tunnel = CnCNetTunnelListLoader.Load()
                .FirstOrDefault(t => t.Address.Equals(address, StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);

        if (tunnel == null)
        {
            _tunnelErrorMode = true;
            LogNotice("The game host has selected an invalid tunnel server! The game host needs to change the server or you will be unable to participate in the match.");
            StateChanged?.Invoke();
            return;
        }

        _tunnelErrorMode = false;
        Room.Tunnel = tunnel;
        LogNotice($"The game host has changed the tunnel server to: {tunnel.Name}");

        lock (_sync)
        {
            foreach (CnCNetGameRoomPlayer player in _players)
            {
                if (!player.IsAi)
                    player.Ping = -1;
            }
        }

        StateChanged?.Invoke();
    }

    private void BroadcastGameLobbySettings()
    {
        if (!IsHost || _connection == null)
            return;

        var sb = new StringBuilder("GSETTINGS ");
        sb.Append(Room.RoomName);
        sb.Append(';');
        sb.Append(Room.MaxPlayers);
        sb.Append(';');
        sb.Append(Room.SkillLevel);
        sb.Append(';');
        sb.Append(Convert.ToInt32(Room.Passworded));
        SendCtcp(sb.ToString());
    }

    private void BroadcastPlayerOptionsLocked()
    {
        if (!IsHost || !_localJoined || _connection == null)
            return;

        if (_players.Count == 0)
            EnsureHostPlayerLocked();

        var sb = new StringBuilder("PO ");
        foreach (CnCNetGameRoomPlayer player in _players)
        {
            if (player.IsAi)
                sb.Append(player.AiLevel);
            else
                sb.Append(player.Name);

            sb.Append(';');
            sb.Append(PackOptions(player.TeamId, player.StartingLocation, player.ColorId, player.SideId));
            sb.Append(';');

            if (!player.IsAi)
            {
                int readyState = player.AutoReady ? 2 : player.Ready ? 1 : 0;
                sb.Append(readyState);
                sb.Append(';');
            }
        }

        SendCtcp(sb.ToString());
    }

    private void BroadcastGameOptionsLocked()
    {
        if (!IsHost || !_localJoined || _connection == null)
            return;

        (int checkBoxCount, int dropDownCount) = GameOptionsControlCounts?.Invoke() ?? (0, 0);
        if (checkBoxCount == 0 && dropDownCount == 0)
        {
            Logger.Log("CnCNet GO: skipping broadcast until lobby game-option controls are initialized.");
            return;
        }

        CnCNetGameOptionsState? state = GameOptionsProvider?.Invoke();
        if (state == null)
        {
            state = new CnCNetGameOptionsState
            {
                CheckBoxValues = [],
                DropDownIndices = [],
                MapOfficial = false,
                MapSha1 = _gameBroadcastListingMapSha1,
                GameModeName = _gameBroadcastListingGameMode,
                MapUntranslatedName = _gameBroadcastListingMapName,
                FrameSendRate = _frameSendRate,
                MaxAhead = _maxAhead,
                ProtocolVersion = _protocolVersion,
                RandomSeed = _randomSeed,
                RemoveStartingLocations = _removeStartingLocations,
            };
        }
        else
        {
            _randomSeed = state.RandomSeed;
            _removeStartingLocations = state.RemoveStartingLocations;
            _frameSendRate = state.FrameSendRate;
            _maxAhead = state.MaxAhead;
            _protocolVersion = state.ProtocolVersion;
        }

        SendCtcp("GO " + CnCNetGameOptionsCodec.BuildBody(state, checkBoxCount, dropDownCount));
    }

    private void AppendChannelJoinersLocked(List<CnCNetGameRoomPlayer> entries, string hostName)
    {
        var namesInEntries = new HashSet<string>(
            entries.Where(e => !e.IsAi).Select(e => e.Name),
            StringComparer.OrdinalIgnoreCase);

        int insertAt = entries.FindIndex(e => e.IsAi);
        if (insertAt < 0)
            insertAt = entries.Count;

        foreach (string channelUser in _channelUsers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (channelUser.Equals(_localNick, StringComparison.OrdinalIgnoreCase))
                continue;

            if (namesInEntries.Contains(channelUser))
                continue;

            CnCNetGameRoomPlayer? existing = FindPlayerLocked(channelUser);
            entries.Insert(insertAt++, existing ?? new CnCNetGameRoomPlayer
            {
                Name = channelUser,
                IsHost = channelUser.Equals(hostName, StringComparison.OrdinalIgnoreCase),
            });
            namesInEntries.Add(channelUser);
        }
    }

    private void EnsureHostPlayerLocked()
    {
        if (_players.Any(p => p.Name.Equals(_localNick, StringComparison.OrdinalIgnoreCase)))
            return;

        _players.Insert(0, new CnCNetGameRoomPlayer
        {
            Name = _localNick,
            IsHost = true,
            Ready = true,
        });
        SyncSlotsFromPlayersLocked();
    }

    private void AddOrRefreshHumanPlayerLocked(string name, bool isLocal)
    {
        CnCNetGameRoomPlayer? existing = FindPlayerLocked(name);
        if (existing != null)
            return;

        _players.Add(new CnCNetGameRoomPlayer
        {
            Name = name,
            IsHost = isLocal && IsHost,
            Ready = isLocal && IsHost,
        });
        SyncSlotsFromPlayersLocked();
    }

    private CnCNetGameRoomPlayer? FindPlayerLocked(string name)
        => _players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private bool IsGameChannel(string channel)
        => NormalizeChannel(channel).Equals(NormalizeChannel(Room.ChannelName), StringComparison.OrdinalIgnoreCase);

    private void SendCtcp(string ctcpMessage)
    {
        if (_connection == null || !_localJoined || !_connection.IsLocalOnChannel(Room.ChannelName))
            return;

        _connection.SendCtcpNotice(CnCNetIrcChannelNames.Preserve(Room.ChannelName), ctcpMessage);
    }

    private void LogNotice(string message) => NoticeLogged?.Invoke(message);

    /// <summary>Room chat timeline (in arrival order). Empty when not in a room.</summary>
    public IReadOnlyList<CnCNetChatLine> ChatLines
    {
        get
        {
            lock (_sync)
                return _chatLines.ToList();
        }
    }

    /// <summary>Result of attempting to send/handle room chat input.</summary>
    public enum RoomChatSendResult
    {
        Failed = 0,
        SentAsChat = 1,
        HandledAsCommand = 2,
    }

    /// <summary>
    /// Room chat-box command dispatcher (lazy). Host-only checks use <see cref="IsHost"/>.
    /// Notices land in the room timeline via <see cref="AddRoomNotice"/>.
    /// </summary>
    public GameRoomChatCommandDispatcher ChatCommands
        => _chatCommands ??= new GameRoomChatCommandDispatcher(
            isHost: () => IsHost,
            addNotice: AddRoomNotice,
            onChangeTunnelRequested: () => ChangeTunnelRequested?.Invoke(),
            tunnelInfoProvider: FormatTunnelInfoNotice);

    /// <summary>Raised when the host runs <c>/changetunnel</c> so UI can open a tunnel picker.</summary>
    public event Action? ChangeTunnelRequested;

    /// <summary>
    /// Send a user chat message to the room channel, or handle a slash command.
    /// Mirrors DX <c>TbChatInput_EnterPressed</c> + <c>SendChatMessage</c>.
    /// </summary>
    public RoomChatSendResult TrySendChat(string message, int ircColorId)
    {
        if (string.IsNullOrWhiteSpace(message))
            return RoomChatSendResult.Failed;

        string trimmed = message.Trim();
        if (trimmed.StartsWith('/'))
            return ChatCommands.TryHandle(trimmed)
                ? RoomChatSendResult.HandledAsCommand
                : RoomChatSendResult.Failed;

        if (_connection == null || !_connection.IsConnected || !_localJoined)
            return RoomChatSendResult.Failed;

        if (!_connection.IsLocalOnChannel(Room.ChannelName))
            return RoomChatSendResult.Failed;

        _connection.SendChatMessage(Room.ChannelName, trimmed, ircColorId);
        return RoomChatSendResult.SentAsChat;
    }

    /// <summary>
    /// Append a remote message (PRIVMSG from another user) to the room timeline.
    /// Called by <c>CnCNetSession</c> after resolving that the message belongs to this room.
    /// </summary>
    public void AppendRemoteChat(
        string sender,
        string displayText,
        bool isSystem = false,
        Color? textColor = null)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return;

        lock (_sync)
        {
            _chatLines.Add(new CnCNetChatLine
            {
                Scope = CnCNetChatScope.GameRoom,
                Sender = sender ?? string.Empty,
                DisplayText = displayText,
                IsSystem = isSystem,
                TextColor = textColor
                    ?? (isSystem ? CnCNetIrcChatText.SystemNoticeColor : CnCNetIrcChatText.DefaultChatColor),
            });
            TrimRoomChatLocked();
        }
        ChatChanged?.Invoke();
    }

    /// <summary>
    /// Append the local user's just-sent message as an echo into the room timeline.
    /// Mirrors DX's <c>Channel.SendChatMessage</c> which calls <c>AddMessage</c> before
    /// sending the IRC line, so the writer sees their own text immediately.
    /// </summary>
    public void AppendLocalChat(string displayText, Color? textColor = null)
    {
        if (string.IsNullOrWhiteSpace(displayText))
            return;

        lock (_sync)
        {
            _chatLines.Add(new CnCNetChatLine
            {
                Scope = CnCNetChatScope.GameRoom,
                Sender = _localNick,
                DisplayText = displayText,
                TextColor = textColor ?? CnCNetIrcChatText.DefaultChatColor,
            });
            TrimRoomChatLocked();
        }
        ChatChanged?.Invoke();
    }

    /// <summary>
    /// System notice appended to the room timeline. Mirrors DX
    /// <c>CnCNetGameLobby.AddNotice -> channel.AddMessage(new ChatMessage(color, message))</c>.
    /// </summary>
    public void AddRoomNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (_sync)
        {
            _chatLines.Add(new CnCNetChatLine
            {
                Scope = CnCNetChatScope.GameRoom,
                Sender = string.Empty,
                DisplayText = message.Trim(),
                IsSystem = true,
                TextColor = CnCNetIrcChatText.SystemNoticeColor,
            });
            TrimRoomChatLocked();
        }
        ChatChanged?.Invoke();
    }

    /// <summary>Clear the room chat timeline (called when leaving the room).</summary>
    public void ClearChat()
    {
        lock (_sync)
            _chatLines.Clear();
        ChatChanged?.Invoke();
    }

    private void TrimRoomChatLocked()
    {
        while (_chatLines.Count > MaxRoomChatLines)
            _chatLines.RemoveAt(0);
    }

    private static string NormalizeChannel(string channel)
        => CnCNetIrcChannelNames.Normalize(channel);

    private static string StripPrefixes(string user)
    {
        int index = 0;
        while (index < user.Length && (user[index] == '@' || user[index] == '+' || user[index] == '%' || user[index] == '~' || user[index] == '&'))
            index++;

        return user[index..];
    }

    private static int PackOptions(int team, int start, int color, int side)
    {
        byte[] bytes = [(byte)team, (byte)start, (byte)color, (byte)side];
        return BitConverter.ToInt32(bytes, 0);
    }

    /// <summary>OR CTCP uses side, color, start, team (XNA RequestPlayerOptions).</summary>
    private static int PackOptionsRequest(int side, int color, int start, int team)
    {
        byte[] bytes = [(byte)side, (byte)color, (byte)start, (byte)team];
        return BitConverter.ToInt32(bytes, 0);
    }

    private static void UnpackOptionsRequest(int packed, out int side, out int color, out int start, out int team)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        side = bytes[0];
        color = bytes[1];
        start = bytes[2];
        team = bytes[3];
    }

    private static void UnpackOptions(int packed, out int team, out int start, out int color, out int side)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        team = bytes[0];
        start = bytes[1];
        color = bytes[2];
        side = bytes[3];
    }

    private static bool PlayerListsEquivalent(
        IReadOnlyList<CnCNetGameRoomPlayer> current,
        IReadOnlyList<CnCNetGameRoomPlayer> next)
    {
        if (current.Count != next.Count)
            return false;

        for (int i = 0; i < current.Count; i++)
        {
            CnCNetGameRoomPlayer a = current[i];
            CnCNetGameRoomPlayer b = next[i];
            if (a.IsAi != b.IsAi
                || a.AiLevel != b.AiLevel
                || a.Ready != b.Ready
                || a.AutoReady != b.AutoReady
                || a.IsHost != b.IsHost
                || a.TeamId != b.TeamId
                || a.StartingLocation != b.StartingLocation
                || a.ColorId != b.ColorId
                || a.SideId != b.SideId
                || !a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int ResolveDefaultFrameSendRate()
    {
        try { return EnvironmentServices.Resolve<IGameConfiguration>().DefaultFrameSendRate; }
        catch (InvalidOperationException)
        {
            // TODO(phase-A): inject IGameConfiguration — 字段初始化器早于注册
            return AppState.Configuration.Legacy.DefaultFrameSendRate;
        }
    }

    private static int ResolveDefaultMaxAhead()
    {
        try { return EnvironmentServices.Resolve<IGameConfiguration>().DefaultMaxAhead; }
        catch (InvalidOperationException)
        {
            // TODO(phase-A): inject IGameConfiguration — 字段初始化器早于注册
            return AppState.Configuration.Legacy.DefaultMaxAhead;
        }
    }

    private static int ResolveDefaultProtocolVersion()
    {
        try { return EnvironmentServices.Resolve<IGameConfiguration>().DefaultProtocolVersion; }
        catch (InvalidOperationException)
        {
            // TODO(phase-A): inject IGameConfiguration — 字段初始化器早于注册
            return AppState.Configuration.Legacy.DefaultProtocolVersion;
        }
    }
}
