using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;

namespace ClientAvalonia.Session;

/// <summary>
/// 玩家语义上的一场游戏（基接口）。
///
/// 作用：统一 Skirmish / CnCNet 房间 / LAN / Mission 的共同面——地图、
/// 玩家槽位、游戏选项、状态机。UI Applier / Action 应依赖此接口（或其
/// 子接口），而不是 LobbyPlayerState + LobbyPlayerMode 枚举切换。
///
/// 对应现状：LobbyPlayerState（槽位）+ LobbySessionState（选图）+
/// CnCNetGameOptionsState（选项）的交集。
/// </summary>
public interface IGameSession
{
    /// <summary>
    /// 当前会话模式（Skirmish / Multiplayer）。
    /// 由具体派生 Session 决定（SkirmishSession → Skirmish；CnCNetGameRoomSession → Multiplayer），
    /// 而非外部存储。BindingApplier / Coordinator 据此派生 UI 分支。
    /// </summary>
    LobbyPlayerMode Mode { get; }

    /// <summary>
    /// 会话级原子脏读 tag（防 UI 重入）。
    ///
    /// 语义（见 layered-architecture.md §4）：
    /// <list type="bullet">
    /// <item>每次通过 <see cref="SlotSink"/> 或 <see cref="Map"/> setter 写入时，值单调递增。</item>
    /// <item>UI 订阅 <see cref="StateChanged"/> 时记录读到的 Revision；
    /// 事件回调开始时若发现读到的 Revision 与订阅时相同（未变），说明本次刷新是冗余的——直接 skip。</item>
    /// <item>替代旧 <c>LobbyPlayerState.PlayerUpdatingInProgress</c> 布尔标志。</item>
    /// </list>
    /// </summary>
    long Revision { get; }

    /// <summary>当前选中地图。对应 LobbySessionState 选图 + ChangeMapAction 目标。</summary>
    IMapResource? Map { get; set; }

    /// <summary>
    /// 玩家槽位（最多 LobbyPlayerSlot.MaxSlots = 8）。
    /// 对应 LobbyPlayerState.Slots。
    /// </summary>
    IReadOnlyList<IPlayerSlot> PlayerSlots { get; }

    /// <summary>游戏选项状态（checkbox / dropdown / 协议参数）。</summary>
    IGameOptionsState Options { get; }

    /// <summary>会话生命周期状态。</summary>
    GameSessionState State { get; }

    /// <summary>状态或槽位变化时触发（UI 刷新）。</summary>
    event Action? StateChanged;

    /// <summary>
    /// 槽位写入收口（见 <see cref="IPlayerSlotSink"/>）。
    ///
    /// 所有外部对 <see cref="PlayerSlots"/> 的修改必须经此接口，
    /// 不允许直接强转 <c>LobbyPlayerSlot</c> 后写其 setter。
    /// </summary>
    IPlayerSlotSink SlotSink { get; }

    /// <summary>
    /// 切换地图时重置槽位（封装 <c>DefaultAiSlotPolicy.AutoFillToMapCapacity</c>）。
    /// 实现差异：
    /// <list type="bullet">
    /// <item>Skirmish：清空 → 1 本地人 + (maxPlayers-1) AI</item>
    /// <item>CnCNet 房主：保留所有人类玩家，按 maxPlayers 调整 AI/空位</item>
    /// <item>CnCNet Joiner：无效操作（不改槽位）</item>
    /// </list>
    /// </summary>
    /// <param name="maxPlayers">新地图最大玩家数。</param>
    void ResetSlotsForMap(int maxPlayers);
}

/// <summary>游戏会话生命周期。</summary>
public enum GameSessionState
{
    /// <summary>大厅配置中。</summary>
    Lobby,

    /// <summary>正在启动（写 spawn / 等 Syringe）。</summary>
    Launching,

    /// <summary>游戏进程运行中。</summary>
    InGame,

    /// <summary>已结束 / 已离开。</summary>
    Finished,
}
