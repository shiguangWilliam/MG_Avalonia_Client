using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Session;

/// <summary>
/// CnCNet 多人游戏会话 = 联网遭遇战 + Tunnel + Host 元数据。
///
/// 作用：表达"当前进入的 CnCNet 游戏房间"。字段从
/// CnCNetActiveGameRoom.cs 反推。实现类建议为 CnCNetGameRoomSession
///（或适配器包装 ActiveGameRoom + GameRoomSession）。
/// </summary>
public interface ICnCNetGameSession : ISkirmishSession
{
    /// <summary>当前选用的 NAT Tunnel。对应 CnCNetActiveGameRoom.Tunnel。</summary>
    CnCNetTunnel Tunnel { get; set; }

    /// <summary>房主名。对应 CnCNetActiveGameRoom.HostName。</summary>
    string HostName { get; }

    /// <summary>游戏房间显示名（host 设置的 room name）。对应 CnCNetActiveGameRoom.RoomName。</summary>
    string RoomName { get; }

    /// <summary>本机是否房主。对应 CnCNetActiveGameRoom.IsHost。</summary>
    bool IsHost { get; }

    /// <summary>IRC 游戏频道名。对应 CnCNetActiveGameRoom.ChannelName。</summary>
    string ChannelName { get; }

    /// <summary>IRC 频道密钥（明文或 SHA1 默认 key）。对应 CnCNetActiveGameRoom.Password。</summary>
    string? Password { get; set; }

    /// <summary>最大玩家数。对应 CnCNetActiveGameRoom.MaxPlayers。</summary>
    int MaxPlayers { get; set; }

    /// <summary>技能等级。对应 CnCNetActiveGameRoom.SkillLevel。</summary>
    int SkillLevel { get; set; }

    /// <summary>
    /// 是否使用自定义密码（房主 isCustomPassword）。
    /// 对应 CnCNetActiveGameRoom.Passworded；GAME/GSETTINGS 广播 flags 第 2 位。
    /// </summary>
    bool Passworded { get; set; }

    // ===== Phase 2 缺口 2.2：玩家列表 + 房间锁定 只读视图 =====

    /// <summary>
    /// 当前房间 PO DTO（CTCP 收到的最新顺序；host 在前）。只读视图。
    /// 对应 MainWindow.Core 旧代码 <c>gameRoom?.Players</c>。
    /// </summary>
    IReadOnlyList<CnCNetGameRoomPlayer> Players { get; }

    /// <summary>房间是否被房主锁定（GAME LOCKED 广播）。</summary>
    bool Locked { get; }

    // ===== Phase 2 缺口 2.3：网络回推入口 =====

    /// <summary>
    /// 把 CTCP 收到的 PO DTO 应用到 <see cref="IGameSession.PlayerSlots"/>。
    /// 内部步骤：
    /// <list type="number">
    /// <item>ClearAll（清掉旧状态）</item>
    /// <item>ApplyToSlots（按 entries 顺序写入）</item>
    /// <item>ReorderHostFirst（如果本机是 host，把 host 移到 [0]）</item>
    /// <item>MarkLocalHuman（标记本机玩家）</item>
    /// <item>BumpRevision + StateChanged</item>
    /// </list>
    /// 替代旧 <c>MultiplayerSlotLayout.ApplyToState</c> + <c>EnsureHostAsFirstHuman</c> + <c>MarkLocalHuman</c> 三步胶水。
    /// </summary>
    /// <param name="entries">CTCP PO DTO 列表。</param>
    /// <param name="hostName">房主名。</param>
    /// <param name="localNick">本地玩家名。</param>
    void ApplyPlayersFromNetwork(IReadOnlyList<CnCNetGameRoomPlayer> entries, string hostName, string localNick);

    // ===== Phase 2 缺口 2.4：Host 广播入口 =====

    /// <summary>
    /// 房主：从当前 <see cref="IGameSession.PlayerSlots"/> 重建 PO DTO 并广播（BO CTCP）。
    /// 替代旧 <c>SyncPlayersFromLobby(LobbyPlayerState, string)</c>。
    /// </summary>
    /// <param name="hostName">房主名。</param>
    /// <param name="aiNames">AI 名字目录（按 AiLevel 索引；来自 <c>ILobbyCatalogService</c>）。</param>
    void BroadcastPlayerOptionsFromSlots(string hostName, IReadOnlyList<string> aiNames);

    /// <summary>
    /// 房主：根据玩家名找到槽位并更新 side/color/team/start。
    /// 替代旧 <c>UpdateHumanFromSlot(LobbyPlayerSlot)</c>。
    /// </summary>
    /// <param name="playerName">玩家名。</param>
    /// <param name="update">要更新的字段（其余字段保留）。</param>
    void UpdateHuman(string playerName, in SlotFieldUpdate update);

    /// <summary>房主：把玩家踢出 IRC 频道。</summary>
    /// <param name="playerName">玩家名。</param>
    void KickPlayer(string playerName);

    // ===== Phase 2 缺口 2.1：Host 槽位语义（拆成两个） =====

    /// <summary>
    /// 房间**初次创建**时调用：清空所有槽位，把本地玩家写到 slot[0]。
    /// 对应 XNA <c>CnCNetGameLobby.GameHost_StartGame</c> 进入大厅的初始化。
    /// </summary>
    /// <param name="localPlayerName">本机玩家名（即 <c>AppState.Environment.PlayerName</c>）。</param>
    void InitHostSlots(string localPlayerName);

    /// <summary>
    /// 房间**已有玩家**时调用：保留现有 humans/ais，把 host 强制移到 slot[0]，其余人类后移。
    /// 替代旧 <c>LobbyPlayerState.EnsureHostAsFirstHuman(hostName, localNick)</c>。
    /// </summary>
    /// <param name="hostName">房主名。</param>
    /// <param name="localNick">本地玩家名（用于 IsHumanLocal 标记）。</param>
    void ReorderHostFirst(string hostName, string localNick);

    /// <summary>
    /// Joiner / Host：把指定名字的槽位标为本地（IsHumanLocal=true）。
    /// 对应 XNA <c>CnCNetGameLobby.AddPlayerOptionsRequest</c> 收到自身 PO 后的处理。
    /// </summary>
    /// <param name="playerName">本机玩家名。</param>
    void MarkLocalHuman(string playerName);
}

