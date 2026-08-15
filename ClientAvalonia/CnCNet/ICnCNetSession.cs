using ClientAvalonia.CnCNet.Tunnels;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>CnCNet IRC 连接状态。</summary>
public enum CnCNetConnectionState
{
    /// <summary>未连接。</summary>
    Disconnected,

    /// <summary>正在连接。</summary>
    Connecting,

    /// <summary>已连接。</summary>
    Connected,
}

/// <summary>
/// CnCNet 网络会话接口（Network 域）。
///
/// 作用：把 AppState.CnCNet 单例封装为接口，让 MainWindow /
/// LobbyBehaviors / GameCreationOverlay 等所有调 IRC 的代码可注入 mock。
///
/// ★ 本接口是网络层，不实现 IGameSession。当前活动的游戏房间通过
/// ActiveGameRoom : ICnCNetGameSession? 暴露。
///
/// 后补的 Auto-Refresh 与 Low-Latency Tunnel（TunnelSorter）目前都直接读
/// AppState.CnCNet，迁移后通过此接口注入即可单测。
/// </summary>
public interface ICnCNetSession
{
    /// <summary>IRC 连接状态（Disconnected / Connecting / Connected）。</summary>
    CnCNetConnectionState ConnectionState { get; }

    /// <summary>当前 IRC 昵称（= PlayerName 或登录名）。</summary>
    string LocalNick { get; }

    /// <summary>
    /// 当前活动的游戏会话（host 或 joiner）。null 表示未在房间。
    /// v3：类型为 ICnCNetGameSession?（替代原 CnCNetActiveGameRoom?）。
    /// </summary>
    ICnCNetGameSession? ActiveGameRoom { get; }

    /// <summary>
    /// 房间内 lobby 逻辑对象（CTCP / 玩家列表等）。迁移期可保留具体类型；
    /// 长期可并入 ICnCNetGameSession 实现。
    /// </summary>
    CnCNetGameRoomSession? GameRoom { get; }

    /// <summary>所有已知 tunnel 服务器列表。原顺序保留（按 IRC 广播）。</summary>
    IReadOnlyList<CnCNetTunnel> Tunnels { get; }

    /// <summary>
    /// Tunnel 延迟小顶堆（Low-Latency Tunnel v2，已落地）。
    /// 暴露在此接口上，让 UI / 测试可以订阅 BestTunnelChanged 事件。
    /// TunnelMaintenanceLoop 通过此属性 + ActiveGameRoom.Tunnel 装配。
    /// </summary>
    TunnelSorter TunnelSorter { get; }

    /// <summary>在线玩家总数（CnCNet 主频道广播）。-1 = 未知。</summary>
    int OnlinePlayerCount { get; }

    /// <summary>是否正在等待加入房间（避免重复发起 Join 请求）。</summary>
    bool IsGameRoomJoinPending { get; }

    /// <summary>
    /// IRC 连接对象（迁移期保留具体类型；UI/状态栏需要 IsConnected/IsConnecting）。
    /// </summary>
    CnCNetIrcConnection? Connection { get; }

    /// <summary>
    /// 多人游戏大厅状态（频道列表、HostedGame 列表等）。迁移期保留具体类型。
    /// </summary>
    MultiplayerLobbyState LobbyState { get; }

    /// <summary>会话状态变化时触发（UI 用于刷新所有 CnCNet 相关面板）。</summary>
    event Action? StateChanged;

    /// <summary>成功加入游戏房间时触发。</summary>
    event Action<ICnCNetGameSession>? GameRoomJoined;

    /// <summary>加入游戏房间失败时触发（参数为失败原因）。</summary>
    event Action<string>? GameRoomJoinFailed;

    /// <summary>游戏即将启动时触发（参数含启动配置）。</summary>
    event Action<CnCNetStartGameInfo>? GameStarting;

    /// <summary>房主离开房间时触发（joiner 检测到 host 消失）。</summary>
    event Action? GameRoomHostAbandoned;

    /// <summary>建立 CnCNet 连接（幂等，已连接则 no-op）。</summary>
    void ConnectIfNeeded();

    /// <summary>主动断开（用户退出或关窗）。</summary>
    void Disconnect();

    /// <summary>尝试加入指定主机的游戏房间。返回是否成功 + 失败原因。</summary>
    bool TryJoinGame(CnCNetHostedGameSummary game, string? password, out string message);

    /// <summary>尝试启动已加入的游戏（host 已 START 后 joiner 调用）。</summary>
    bool TryLaunchHostedGame(out string message);

    /// <summary>发送聊天消息到当前频道。</summary>
    void SendChatMessage(string message);

    /// <summary>发送私信（PRIVMSG nick）。</summary>
    void SendPrivateMessage(string recipient, string message);

    /// <summary>私信会话摘要（按最近活动排序）。</summary>
    IReadOnlyList<(string Nick, int Unread)> GetPrivateConversationSummaries();

    /// <summary>指定对方的私信历史。</summary>
    IReadOnlyList<CnCNetChatLine> GetPrivateMessages(string peerNick);

    /// <summary>未读私信总数。</summary>
    int UnreadPrivateMessageCount { get; }

    /// <summary>最近私信对象（用于 F4 打开时定位）。</summary>
    string? LastPrivateMessagePartner { get; }

    /// <summary>私信面板当前聚焦的对方；来自该对方的消息不增加未读。</summary>
    string? ViewingPrivateMessagePeer { get; }

    /// <summary>设置/清除私信面板聚焦对象（打开/关闭/切换会话时调用）。</summary>
    void SetViewingPrivateMessagePeer(string? peerNick);

    /// <summary>新私信到达（已入库）；用于状态栏提醒。参数：对方 nick、预览正文。</summary>
    event Action<string, string>? PrivateMessageArrived;

    /// <summary>确保与对方有会话并标记已读。</summary>
    void EnsurePrivateConversation(string peerNick);

    /// <summary>标记私信已读；peerNick 为空则全部已读。</summary>
    void MarkPrivateMessagesRead(string? peerNick = null);

    /// <summary>设置本机玩家准备状态（CTCP READY/AIDLE）。</summary>
    void SetGameRoomReady(bool ready, bool autoReady = false);

    /// <summary>切换房间锁定状态（仅 host 调用）。</summary>
    void SetGameRoomLocked(bool locked);

    /// <summary>离开当前游戏房间（恢复广播频道）。</summary>
    void LeaveGameRoom();

    /// <summary>更新当前游戏房间的 hosting 信息（地图/模式/Sha1）。</summary>
    void UpdateGameRoomListing(string mapName, string gameModeName, string mapSha1);

    /// <summary>更新房间配置（仅 host 调用）。</summary>
    void UpdateGameLobbySettings(string roomName, int maxPlayers, int skillLevel, string? password);

    /// <summary>Host 尝试切换 tunnel（用于 Low-Latency / 自动维护）。</summary>
    bool TryHostChangeTunnel(CnCNetTunnel tunnel);

    /// <summary>确保游戏广播频道已加入（房间关闭/恢复时使用）。</summary>
    void EnsureGameBroadcastChannelsJoined();

    /// <summary>启动 CnCNet 后台任务（日志、心跳、tunnel ping）。已启动则 no-op。</summary>
    void EnsureStarted();

    /// <summary>尝试创建新游戏房间（host 路径）。</summary>
    bool TryCreateGame(CnCNetGameCreationRequest request, out string message);

    /// <summary>切换到指定频道索引（聊天 Tab）。</summary>
    void SwitchToChannel(int channelIndex);

    /// <summary>当前选中的聊天频道索引。</summary>
    int SelectedChannelIndex { get; }

    /// <summary>设置聊天颜色索引（持久化到 INI）。</summary>
    void SetChatColorIndex(int index);

    /// <summary>游戏进程启动时通知 CnCNet（用于 presence keep-alive）。</summary>
    void NotifyGameProcessStarted();

    /// <summary>游戏进程退出时通知 CnCNet。</summary>
    void NotifyGameProcessExited();

    /// <summary>开始 Launch Presence keep-alive（避免游戏期间被 IRC 误判离线）。</summary>
    void BeginLaunchPresenceKeepAlive();

    /// <summary>结束 Launch Presence keep-alive。</summary>
    void EndLaunchPresenceKeepAlive();
}
