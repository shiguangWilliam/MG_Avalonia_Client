# Phase 1 玩家状态完全统一 设计文档

> **日期**：2026-07-20
> **范围**：完成 [player-unification-and-ini-action-catalog.md](player-unification-and-ini-action-catalog.md) §1.5–1.7 中延后的部分
> **目标**：把 `LobbyPlayerState.Slots` 从独立存储降级为 `IGameSession.PlayerSlots` 的**只读投影**，同时拆掉 `MainWindow.ApplyCnCNetGameRoomPlayers*` 三层胶水
> **当前分支**：main（多 Mod 启动不归属本分支，不考虑其影响）
> **基线**：558 通过 / 1 预存失败 / 3 跳过
>
> **架构参照**：本设计是 [layered-architecture.md](layered-architecture.md) 总纲的具体应用——
> Step 1 对应总纲 §2.2「IUIAction 统一入口」+ Session 层的 IPlayerSlotSink 写入收口；
> Step 2 对应总纲 §5.2「LobbyPlayerState 跨界改造」；
> Step 3 对应总纲 §1.1「View 不直接调 Service 内部」的合规化。

---

## 0. 问题衡量：为什么必须做这一步

### 0.1 现存的三套状态（Phase 1 部分完成后仍然存在）

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer A: UI 层                                                  │
│  LobbyPlayerState.Slots (LobbyPlayerSlot[8])                     │
│  ├── 独立存储：MainWindow._lobbySession.PlayerState.Slots        │
│  ├── 被绑定到 ddPlayerName/Side/Color/Team/Start 下拉框          │
│  └── 包含目录缓存（SideNames/AiNames/TeamNames）+ Skirmish 持久化│
└─────────────────────────────────────────────────────────────────┘
                            ▲ ▼  双向同步
┌─────────────────────────────────────────────────────────────────┐
│  Layer B: Session 层                                             │
│  IGameSession.PlayerSlots (IPlayerSlot[])                        │
│  ├── SkirmishSession.PlayerSlots = Player.Slots （投影回 A）     │
│  └── CnCNetGameRoomSession._playerSlots (ICnCNetPlayerSlot[8])   │
│      ↑ Phase 1 已升格为真相源                                    │
└─────────────────────────────────────────────────────────────────┘
                            ▲ ▼  PO 编解码
┌─────────────────────────────────────────────────────────────────┐
│  Layer C: Network 层                                             │
│  CnCNetGameRoomPlayer (List<DTO>)                                │
│  └── Phase 1 已降为瞬时 DTO                                      │
└─────────────────────────────────────────────────────────────────┘
```

### 0.2 核心病灶：A 与 B 仍然并存

`SkirmishSession.PlayerSlots => Player.Slots`——Layer A 仍是真相源。
但 `CnCNetGameRoomSession._playerSlots` 是独立的 Layer B 存储。

`MainWindow.ApplyCnCNetGameRoomPlayersCore` 干的事：
```csharp
// 把 Layer C → Layer A（覆盖！）
MultiplayerSlotLayout.ApplyToState(_lobbySession.PlayerState, entries, localNick);

// 然后 Layer A → Layer B（通过 SyncPlayersFromLobby）
// 但 SyncPlayersFromLobby 又会反向写回 _players...
```

**结果**：MainWindow 需要 `_applyingCnCNetGameRoomPlayers` 重入保护来阻止死循环。这是「症状在 MainWindow，病根在状态分裂」。

### 0.3 不做的代价

| 不做 | 后果 |
|------|------|
| `MainWindow.ApplyCnCNetGameRoomPlayers*` (3 方法 ~80 行) 无法删 | MainWindow 拆分卡住，9 步切片的第 5 步（CnCNet Game Room Sync Service）阻塞 |
| `_applyingCnCNetGameRoomPlayers` 字段必须留 | MainWindow 内隐式状态机继续存在 |
| PO 收到后必须先经过 Layer A 才能渲染 | 多一次无谓拷贝；无法独立测试「收到 PO → UI 显示」链路 |
| `LobbyPlayerState.Slots[]` 仍是可变独立存储 | 任何写它的代码都可能引发 A/B 不一致 |

### 0.4 做完的收益

| 指标 | 当前 | 目标 |
|------|------|------|
| 玩家状态存储点 | 2 套（A + B） | **1 套**（B；A 是 B 的只读视图） |
| `MainWindow` 内 `CnCNetGameRoomPlayer` 引用 | 6 | **0** |
| `MainWindow` 内 `MultiplayerSlotLayout.ApplyToState` 调用 | 1 | **0** |
| `MainWindow._applyingCnCNetGameRoomPlayers` | 存在 | **删除** |
| `MainWindow.ApplyCnCNetGameRoomPlayersCore` 行数 | ~50 | **0**（事件订阅 + BindingApplier 替代） |
| 状态变更点 | N 处散落 | **1 处**（Session.PlayerSlots） |
| `LobbyPlayerState.Slots` 写入路径 | UI 直接写 + PO 覆盖 + 默认装载 | **只读视图**；所有写入转 Session |

---

## 1. 总体架构

### 1.1 目标分层

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer C: Network DTO                                           │
│  CnCNetGameRoomPlayer (PO 编解码 DTO，瞬时)                     │
│       │                                                         │
│       ▼ PlayerOptionsCodec.ApplyDto / ToDto                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Layer B: Session (唯一真相源)                          │   │
│  │  IGameSession.PlayerSlots (IPlayerSlot[])               │   │
│  │  ├── SkirmishSession  →  LobbyPlayerSlot[8]             │   │
│  │  └── CnCNetGameRoomSession  →  ICnCNetPlayerSlot[8]     │   │
│  │       ▲                                                 │   │
│  │       │ UI 写：通过 Session 暴露的 mutable API          │   │
│  └───────┼─────────────────────────────────────────────────┘   │
│          │                                                      │
│          ▼ 投影（只读）                                          │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Layer A: UI 视图 + 目录缓存                            │   │
│  │  LobbyPlayerState                                        │   │
│  │  ├── Slots → 投影自 Session.PlayerSlots                 │   │
│  │  ├── SideNames/AiNames/TeamNames（目录，UI 只读）       │   │
│  │  ├── Mode / LocalPlayerName / HostPlayerName            │   │
│  │  ├── PlayerUpdatingInProgress（防 UI 双向循环）         │   │
│  │  └── SkirmishSettings.ini 持久化（通过 Session 写回）   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 数据流方向（目标终态）

| 路径 | 当前 | 目标 |
|------|------|------|
| 用户在下拉框选择 → 状态变更 | UI → LobbyPlayerState.Slots | UI → Session.PlayerSlots（通过 SlotMutationAdapter） |
| 房主变更 → 广播 PO | LobbyPlayerState → SyncPlayersFromLobby → _players | Session.PlayerSlots → PlayerOptionsCodec.ToDto → 广播 |
| 收到 PO → UI 刷新 | PO → ApplyToState 覆盖 LobbyPlayerState.Slots → BindingApplier | PO → PlayerOptionsCodec.ApplyDto → Session.PlayerSlots → StateChanged → BindingApplier |
| Skirmish 默认装载 | LobbyPlayerState.LoadDefaultSkirmishSlots | Session.ResetSlots(mapMaxPlayers) → LobbyPlayerState 投影看到 |
| Skirmish 设置持久化 | LobbyPlayerState.SaveSkirmishSettings / TryLoad | 同左，但读写都通过 Session.PlayerSlots |

---

## 2. 抽象设计

### 2.1 关键原则

1. **Session 是唯一真相源**：`IGameSession.PlayerSlots` 是状态权威。
2. **LobbyPlayerState 没有独立 Slots 存储**：它的 `Slots` 属性变成投影（getter 转发）。
3. **目录与 UI 输入态留在 LobbyPlayerState**：SideNames/AiNames/TeamNames、Mode、LocalPlayerName、SkirmishSettings 持久化路径不变。
4. **PO 编解码仅用 PlayerOptionsCodec**：不再有第二处拷贝路径。
5. **写入 Session 走 typed API**：避免外部代码 index 直写引发的不一致。

### 2.2 新接口：`IPlayerSlotSink`

`IPlayerSlot` 现在只有读 + 个别 set 属性。为了把所有写入收口，引入 sink：

```csharp
// ClientAvalonia/Session/IPlayerSlotSink.cs
namespace ClientAvalonia.Session;

/// <summary>
/// 玩家槽位"写入端"——把所有外部对槽位的修改集中到一个接口。
///
/// 设计理由：
///   - <see cref="IPlayerSlot"/> 的属性都是 { get; set; }，外部任意代码都能写，
///     难以追踪。Sink 让写入路径变得显式且可观测（事件、日志、审计）。
///   - 配合 <see cref="IGameSession.PlayerSlots"/> 的只读视图，构成
///     "写入经 sink / 读取经 slots"的 CQRS-like 模式。
///   - 实现可加日志、广播、撤销栈等横切关注，不影响读路径性能。
/// </summary>
public interface IPlayerSlotSink
{
    /// <summary>直接覆盖整个槽位（用于 PO ApplyDto / Skirmish 默认装载）。</summary>
    void OverwriteSlot(int index, IPlayerSlot source);

    /// <summary>覆盖单个槽位的可变字段（Name/Side/Color/Team/Start/AiLevel/IsAi）。</summary>
    void WriteSlot(int index, SlotFieldUpdate update);

    /// <summary>清空槽位（等价于写一个未占用的空槽）。</summary>
    void ClearSlot(int index);

    /// <summary>清空所有槽位。</summary>
    void ClearAll();

    /// <summary>从其他 sink/source 批量复制（用于切换 Session 时迁移）。</summary>
    void CopyFrom(IReadOnlyList<IPlayerSlot> source);
}

/// <summary>
/// 单字段更新包：把"改 side/color/team/start 中的哪几个"显式化。
/// </summary>
public readonly struct SlotFieldUpdate
{
    public string? Name { get; init; }
    public int? SideIndex { get; init; }
    public int? ColorIndex { get; init; }
    public int? TeamIndex { get; init; }
    public int? StartIndex { get; init; }
    public int? AiLevel { get; init; }
    public bool? IsAi { get; init; }
    public bool? IsHumanLocal { get; init; }

    // CnCNet 专用（仅在 ICnCNetPlayerSlot 上写入）
    public bool? IsHost { get; init; }
    public bool? Ready { get; init; }
    public bool? AutoReady { get; init; }
    public int? Ping { get; init; }
    public ushort? Port { get; init; }
}
```

### 2.3 扩展 `IGameSession`

```csharp
public interface IGameSession
{
    IMapResource? Map { get; set; }
    IReadOnlyList<IPlayerSlot> PlayerSlots { get; }  // 已存在（只读视图）
    IGameOptionsState Options { get; }
    GameSessionState State { get; set; }
    event Action? StateChanged;

    // 新增：暴露写入端
    IPlayerSlotSink SlotSink { get; }

    // 新增：切换地图时重置槽位（封装 DefaultAiSlotPolicy 调用）
    void ResetSlotsForMap(int maxPlayers);
}
```

### 2.4 扩展 `ICnCNetGameSession`

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    // 已存在：PlayerSlots 继承自 IGameSession
    // 新增：typed 视图（让 PO Codec / 广播代码读到 CnCNet 字段）
    IReadOnlyList<ICnCNetPlayerSlot> CnCNetPlayerSlots { get; }

    // 新增：广播 hook（host 改槽位后调用）
    void BroadcastPlayerOptions();

    // 新增：joiner 改槽位后调用（OR CTCP）
    void RequestLocalPlayerOptions(int side, int color, int start, int team);
}
```

---

## 3. 类与实现

### 3.1 `LobbyPlayerSlotSink`（默认 sink 实现）

```csharp
// ClientAvalonia/Session/LobbyPlayerSlotSink.cs
namespace ClientAvalonia.Session;

/// <summary>
/// 通用 <see cref="IPlayerSlotSink"/> 实现：直接操作一个 IPlayerSlot[]。
/// 
/// 不发事件——事件由 owning Session 在 sink 调用后统一发。
/// 这样多个 sink 调用可以合并成一次 StateChanged。
/// </summary>
public sealed class LobbyPlayerSlotSink : IPlayerSlotSink
{
    private readonly Func<IPlayerSlot[]> _slotsAccessor;

    public LobbyPlayerSlotSink(Func<IPlayerSlot[]> slotsAccessor)
    {
        _slotsAccessor = slotsAccessor;
    }

    public void OverwriteSlot(int index, IPlayerSlot source)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;
        IPlayerSlot target = slots[index];
        target.Name = source.Name;
        target.SideIndex = source.SideIndex;
        target.ColorIndex = source.ColorIndex;
        target.TeamIndex = source.TeamIndex;
        target.StartIndex = source.StartIndex;
        target.AiLevel = source.AiLevel;
        target.IsAi = source.IsAi;
        target.IsHumanLocal = source.IsHumanLocal;

        if (target is ICnCNetPlayerSlot cnc && source is ICnCNetPlayerSlot src)
        {
            cnc.IsHost = src.IsHost;
            cnc.Ready = src.Ready;
            cnc.AutoReady = src.AutoReady;
            cnc.Ping = src.Ping;
            cnc.Port = src.Port;
        }
    }

    public void WriteSlot(int index, SlotFieldUpdate u)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;
        IPlayerSlot s = slots[index];
        if (u.Name is not null) s.Name = u.Name;
        if (u.SideIndex is not null) s.SideIndex = u.SideIndex.Value;
        if (u.ColorIndex is not null) s.ColorIndex = u.ColorIndex.Value;
        if (u.TeamIndex is not null) s.TeamIndex = u.TeamIndex.Value;
        if (u.StartIndex is not null) s.StartIndex = u.StartIndex.Value;
        if (u.AiLevel is not null) s.AiLevel = u.AiLevel.Value;
        if (u.IsAi is not null) s.IsAi = u.IsAi.Value;
        if (u.IsHumanLocal is not null) s.IsHumanLocal = u.IsHumanLocal.Value;

        if (s is ICnCNetPlayerSlot cnc)
        {
            if (u.IsHost is not null) cnc.IsHost = u.IsHost.Value;
            if (u.Ready is not null) cnc.Ready = u.Ready.Value;
            if (u.AutoReady is not null) cnc.AutoReady = u.AutoReady.Value;
            if (u.Ping is not null) cnc.Ping = u.Ping.Value;
            if (u.Port is not null) cnc.Port = u.Port.Value;
        }
    }

    public void ClearSlot(int index)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        if ((uint)index >= (uint)slots.Length) return;
        ClearSlot(slots[index]);
    }

    public void ClearAll()
    {
        foreach (IPlayerSlot s in _slotsAccessor())
            ClearSlot(s);
    }

    public void CopyFrom(IReadOnlyList<IPlayerSlot> source)
    {
        IPlayerSlot[] slots = _slotsAccessor();
        for (int i = 0; i < slots.Length; i++)
        {
            if (i < source.Count)
                OverwriteSlot(i, source[i]);
            else
                ClearSlot(i);
        }
    }

    private static void ClearSlot(IPlayerSlot s)
    {
        s.Name = string.Empty;
        s.IsAi = false;
        s.IsHumanLocal = false;
        s.SideIndex = 0;
        s.ColorIndex = 0;
        s.StartIndex = 0;
        s.TeamIndex = 0;
        s.AiLevel = 0;
        if (s is ICnCNetPlayerSlot cnc)
        {
            cnc.IsHost = false;
            cnc.Ready = false;
            cnc.AutoReady = false;
            cnc.Ping = -1;
            cnc.Port = 0;
        }
    }
}
```

### 3.2 `SkirmishSession` 改造

```csharp
public sealed class SkirmishSession : ISkirmishSession
{
    private readonly LobbyPlayerSlot[] _slots =
        Enumerable.Range(0, LobbyPlayerSlot.MaxSlots).Select(_ => new LobbyPlayerSlot()).ToArray();
    private IMapResource? _map;
    private GameSessionState _state = GameSessionState.Lobby;

    public SkirmishSession() 
    {
        SlotSink = new LobbyPlayerSlotSink(() => _slots);
    }

    public IPlayerSlotSink SlotSink { get; }

    public IReadOnlyList<IPlayerSlot> PlayerSlots => _slots;

    public void ResetSlotsForMap(int maxPlayers)
    {
        SlotSink.ClearAll();
        // 复用现有 DefaultAiSlotPolicy
        DefaultAiSlotPolicy.AutoFillToMapCapacity(
            this, maxPlayers,
            ResolvePlayerName(), ResolveColorCatalog(),
            ProgramConstants.AI_PLAYER_NAMES.ToList());
        StateChanged?.Invoke();
    }

    // 移除：public LobbyPlayerState Player { get; }
    // 理由：Session 自身就是真相源，不再包装 LobbyPlayerState。
    // 兼容期保留过时属性，标 [Obsolete] 桥接。

    // 其余不变
}
```

### 3.3 `CnCNetGameRoomSession` 改造

`_playerSlots` 字段不变（Phase 1 已经是 `ICnCNetPlayerSlot[]`），新增：

```csharp
public IPlayerSlotSink SlotSink { get; }

public IReadOnlyList<ICnCNetPlayerSlot> CnCNetPlayerSlots => _playerSlots;

public void BroadcastPlayerOptions()
{
    if (!IsHost || !_localJoined || _connection == null) return;
    lock (_sync)
    {
        if (_players.Count == 0) EnsureHostPlayerLocked();
        // ... 原有 BroadcastPlayerOptionsLocked 内容
    }
}

public void RequestLocalPlayerOptions(int side, int color, int start, int team)
{
    if (IsHost || _connection == null) return;
    int packed = PackOptionsRequest(side, color, start, team);
    SendCtcp($"OR {packed}");
}

public void ResetSlotsForMap(int maxPlayers)
{
    // CnCNet 房间里改地图不重置槽位（玩家不会消失）
    // 但 AI 槽位会随 maxPlayers 调整
    // 由 host 触发，等价于原 SyncPlayersFromLobby 的子集
    if (!IsHost) return;
    SlotSink.ClearAll();
    EnsureHostPlayerLocked();  // 房主先入座
    StateChanged?.Invoke();
}
```

### 3.4 `LobbyPlayerState` 降级

```csharp
public sealed class LobbyPlayerState
{
    private readonly Func<IReadOnlyList<IPlayerSlot>> _slotsProvider;

    public LobbyPlayerState(Func<IReadOnlyList<IPlayerSlot>> slotsProvider)
    {
        _slotsProvider = slotsProvider;
    }

    /// <summary>当前槽位（投影自 Session，非独立副本）。</summary>
    public IReadOnlyList<IPlayerSlot> Slots => _slotsProvider();

    /// <summary>
    /// 向后兼容：旧代码用 LobbyPlayerSlot[] 做 LINQ。
    /// 通过 Cast 实现——但写入会失败（因为返回的是 copy）。
    /// 迁移期：所有写入转 SlotSink。
    /// </summary>
    public LobbyPlayerSlot[] SlotClones => Slots
        .Select(s =>
        {
            var clone = new LobbyPlayerSlot();
            // copy fields
            return clone;
        })
        .ToArray();

    // 保留：目录缓存、Mode、LocalPlayerName、HostPlayerName、PlayerUpdatingInProgress

    // 保留但改造：写入路径改为通过 Session
    public void LoadDefaultSkirmishSlots(int maxPlayers)
    {
        // 改为：调用方持有 Session，调 Session.ResetSlotsForMap(maxPlayers)
        // 此方法标 [Obsolete]
        throw new NotSupportedException("Use IGameSession.ResetSlotsForMap instead.");
    }

    // 保留只读属性：HumanRowCount / AiRowCount / OccupiedSlotCount / GetRowKind
    // 这些都基于 Slots 投影计算，无需改

    // 持久化方法保留：TryLoadSkirmishSettings / SaveSkirmishSettings
    // 但内部读写都通过 SlotSink，不再直接 _slots[i] =
}
```

### 3.5 `MultiplayerSlotLayout` 简化

```csharp
public static class MultiplayerSlotLayout
{
    // 删除：ApplyToState —— 由 PlayerOptionsCodec.ApplyDto 替代
    // 删除：BuildPoListFromState —— 由 PlayerOptionsCodec.ToDto 替代

    // 保留：ExtractAiRows（基于 LobbyPlayerState.Slots 投影，逻辑不变）
    // 保留：ApplySkirmishAiSelection（写入转 SlotSink）
}
```

### 3.6 `MultiplayerSlotCoordinator` 改造

```csharp
public static class MultiplayerSlotCoordinator
{
    public static void HandleHostSlotEdit(
        IGameSession session,        // 替换 LobbyPlayerState
        LobbyPlayerState uiState,    // 只用它的 Mode/AllowHostPlayerOptions 标志
        int slotIndex,
        LobbyPlayerSlot previous,
        UiNodeViewModel ddName,
        ICnCNetGameSession? gameRoom)
    {
        if (uiState.Mode != LobbyPlayerMode.Multiplayer 
            || !uiState.AllowHostPlayerOptions 
            || gameRoom == null) return;

        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName))
        {
            gameRoom.KickPlayer(previous.Name);
            session.SlotSink.OverwriteSlot(slotIndex, previous);
            return;
        }
        // ... 类似改造
        gameRoom.BroadcastPlayerOptions();
    }

    // 同理改 HandleHostOptionsEdit / HandleJoinerOptionsEdit
}
```

---

## 4. MainWindow 改造

### 4.1 删除的胶水

```csharp
// 删除字段
private bool _applyingCnCNetGameRoomPlayers;

// 删除方法
private void RefreshCnCNetGameRoomUiFromSession(UiNodeViewModel root);  // ~30 行
private void ApplyCnCNetGameRoomPlayers(UiNodeViewModel root);          // ~20 行
private void ApplyCnCNetGameRoomPlayersCore(UiNodeViewModel root, CnCNetActiveGameRoom room, bool updateStatus);  // ~50 行
```

### 4.2 替代方案：事件订阅

```csharp
private void OnCnCNetGameRoomJoined(ICnCNetGameSession room)
{
    // 订阅 Session 的 StateChanged——任何 PO 应用、host 编辑、joiner 编辑都触发
    room.StateChanged += OnCnCNetGameRoomStateChanged;
    
    // 初次进入：直接刷新 UI
    RefreshCnCNetGameRoomUi();
}

private void OnCnCNetGameRoomStateChanged()
{
    // StateChanged 可能在工作线程触发——派发到 UI 线程
    Dispatcher.UIThread.Post(RefreshCnCNetGameRoomUi);
}

private void RefreshCnCNetGameRoomUi()
{
    if (_activeRoot == null) return;
    ICnCNetGameSession? room = _cncnet.GameRoom;
    if (room == null) return;

    // 没有重入保护——因为 LobbyPlayerState.Slots 现在是只读投影，
    // BindingApplier.SyncUiFromState 不会反向写 Session。
    ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
    LobbyPlayerBindingApplier.Apply(_activeRoot, _lobbySession.PlayerState, resources, _mainBehaviors);
    
    bool locked = room.Locked;
    bool isHost = room.IsHost;
    LobbyPlayerStatusApplier.Apply(
        _activeRoot, _lobbySession.PlayerState, resources, _mainBehaviors,
        room.Players, locked, isHost);

    CnCNetGameLobbyUiHelper.ApplyToolbarRole(_activeRoot, resources, _mainBehaviors, isJoiner: !isHost);
    CnCNetGameLobbyUiHelper.UpdateManualReadyLabel(_activeRoot, isJoiner: !isHost);
    ApplyLockGameButtonLabel(_activeRoot, isHost, locked);
    UpdateLaunchButtonState(_activeRoot);
    RefreshCurrentMapStartMarkers();
}
```

**关键**：`LobbyPlayerBindingApplier.SyncUiFromState` 现在读 `playerState.Slots`（投影）写入 UI 控件。但 UI 控件 SelectionChanged 会反向调 `ApplySlotFromUi` → 直接写 `playerState.Slots[slotIndex]`。

这是死循环风险点。**必须**让 `ApplySlotFromUi` 也走 `SlotSink`，并且 `playerState.PlayerUpdatingInProgress` 标志阻止反向同步。

### 4.3 关键改造：`LobbyPlayerBindingApplier.ApplySlotFromUi`

```csharp
private static void ApplySlotFromUi(
    int slotIndex,
    LobbyPlayerState playerState,
    IGameSession session,    // 新增参数
    UiNodeViewModel ddName,
    UiNodeViewModel? ddSide,
    UiNodeViewModel? ddColor,
    UiNodeViewModel? ddTeam,
    UiNodeViewModel? ddStart)
{
    if (LobbyPlayerSlotUiRules.IsKickSelection(ddName) 
        || LobbyPlayerSlotUiRules.IsBanSelection(ddName))
        return;

    LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slotIndex, playerState);

    if (rowKind == LobbyPlayerRowKind.Human)
    {
        // 走 sink，避免直接写 playerState.Slots[i]
        session.SlotSink.WriteSlot(slotIndex, new SlotFieldUpdate
        {
            SideIndex = ddSide?.SelectedIndex >= 0 ? ddSide.SelectedIndex : 0,
            ColorIndex = ddColor?.SelectedIndex >= 0 ? ddColor.SelectedIndex : 0,
            TeamIndex = ddTeam?.SelectedIndex >= 0 ? ddTeam.SelectedIndex : 0,
            StartIndex = ddStart?.SelectedIndex >= 0 ? ddStart.SelectedIndex : 0,
        });
        return;
    }

    string name = ReadSelectedText(ddName);
    if (string.IsNullOrWhiteSpace(name) || name == "-")
    {
        session.SlotSink.ClearSlot(slotIndex);
        return;
    }

    session.SlotSink.WriteSlot(slotIndex, new SlotFieldUpdate
    {
        Name = name,
        IsHumanLocal = name.Equals(playerState.LocalPlayerName, StringComparison.OrdinalIgnoreCase),
        IsAi = !name.Equals(playerState.LocalPlayerName, StringComparison.OrdinalIgnoreCase)
             && playerState.AiNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)),
        // ... 其余字段
    });
}
```

---

## 5. 迁移策略

### 5.1 三阶段渐进迁移

```
Step 1: 引入 sink + Session 改造（不破坏现有调用）
   ├── 新增 IPlayerSlotSink / LobbyPlayerSlotSink / SlotFieldUpdate
   ├── IGameSession / ICnCNetGameSession 加新成员（默认实现）
   ├── SkirmishSession / CnCNetGameRoomSession 实现 SlotSink
   └── 单测：sink 行为

Step 2: LobbyPlayerState 改造（破坏性）
   ├── Slots 改为投影（构造接 provider）
   ├── 移除独立的 LobbyPlayerSlot[8] 存储
   ├── SkirmishSettings 持久化改走 sink
   ├── MainWindow: _skirmishSession 改造，给 LobbyPlayerState 传 provider
   └── 单测：投影正确性

Step 3: 删除 MainWindow 胶水（破坏性）
   ├── LobbyPlayerBindingApplier: ApplySlotFromUi 改走 sink
   ├── MultiplayerSlotCoordinator: 改走 sink + Session
   ├── MainWindow: 删 ApplyCnCNetGameRoomPlayers* / _applyingCnCNetGameRoomPlayers
   ├── MainWindow: 订阅 StateChanged 替代显式调用
   └── 全量回归测试

每步独立可发布、独立有单测、独立跑全测。
```

### 5.2 兼容期策略

**Step 1 完成后**：旧代码继续工作，因为 `IGameSession.PlayerSlots` 仍指向同样的 LobbyPlayerSlot[]。新代码可以选用 `SlotSink` 写入。

**Step 2 完成后**：所有 `playerState.Slots[i] = ...` 直接赋值的代码必须改走 sink。用 `[Obsolete]` 标记 LobbyPlayerState 的写入方法，编译期 warning 引导迁移。

**Step 3 完成后**：完全干净的"单真相源"。

### 5.3 测试策略

每个 Step 完成后跑全测，目标：
- Step 1 后：558 → ~570（新增 sink 测试 ~12 个）
- Step 2 后：~570 → ~585（新增投影测试 ~15 个）
- Step 3 后：~585 → ~595（新增 MainWindow 行为测试 ~10 个）

---

## 6. 风险与缓解

| 风险 | 严重度 | 缓解 |
|------|--------|------|
| **死循环**：UI 写 Session → StateChanged → 刷新 UI → 又触发写 | 🔴 高 | 用 `PlayerUpdatingInProgress` 标志；BindingApplier 内部 set flag，sink 内部不触发 refresh |
| **CnCNet PO 收到时房主侧 AlreadyInLoop** | 🔴 高 | PO 应用走 sink 的"静默模式"（不触发 StateChanged），手动在 ApplyDto 后调一次 StateChanged |
| **CnCNetGameRoomSession 内部线程安全** | 🟡 中 | 现有 `_sync` 字段继续保护；sink 调用都在 lock 内 |
| **多订阅者同时响应 StateChanged** | 🟡 中 | 单线程 UI 调度，Dispatcher.UIThread.Post 串行化 |
| **SkirmishSettings 持久化破坏** | 🟡 中 | 保留 TryLoad/Save 方法签名，仅改实现 |
| **多 Mod 启动** | 🟢 低 | 不在本分支考虑 |

### 6.1 死循环防御

最关键的风险。设计：

```csharp
// LobbyPlayerState
public bool PlayerUpdatingInProgress { get; set; }

// LobbyPlayerBindingApplier.SyncUiFromState:
playerState.PlayerUpdatingInProgress = true;
try { /* 写 UI 控件 */ }
finally { playerState.PlayerUpdatingInProgress = false; }

// WireSlot 中的 SyncNameFromUi / SyncOptionsFromUi:
if (playerState.PlayerUpdatingInProgress) return;  // 早期 return
```

这套机制现在已存在。改造后仍然适用——`ApplySlotFromUi` 在 `PlayerUpdatingInProgress=true` 时直接 return。

### 6.2 静默 sink 模式

为 PO 收到时的应用路径设计：

```csharp
public interface IPlayerSlotSink
{
    // 普通模式：写完触发 StateChanged（默认）
    void OverwriteSlot(int index, IPlayerSlot source);

    // 静默模式：仅写不发事件（PO 应用、批量装载）
    void OverwriteSlotSilent(int index, IPlayerSlot source);
}
```

`PlayerOptionsCodec.ApplyDto` 用 `OverwriteSlotSilent`，应用完后 Session 自己决定何时发 `StateChanged`。

---

## 7. 详细接口签名

### 7.1 `IPlayerSlotSink` 完整签名

```csharp
public interface IPlayerSlotSink
{
    void OverwriteSlot(int index, IPlayerSlot source);
    void OverwriteSlotSilent(int index, IPlayerSlot source);
    void WriteSlot(int index, SlotFieldUpdate update);
    void WriteSlotSilent(int index, SlotFieldUpdate update);
    void ClearSlot(int index);
    void ClearAll();
    void CopyFrom(IReadOnlyList<IPlayerSlot> source);
}
```

### 7.2 `IGameSession` 完整签名（改造后）

```csharp
public interface IGameSession
{
    IMapResource? Map { get; set; }
    IReadOnlyList<IPlayerSlot> PlayerSlots { get; }
    IGameOptionsState Options { get; }
    GameSessionState State { get; set; }
    event Action? StateChanged;

    // 新增
    IPlayerSlotSink SlotSink { get; }
    void ResetSlotsForMap(int maxPlayers);
}
```

### 7.3 `ICnCNetGameSession` 完整签名（改造后）

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    // 已有
    string RoomName { get; }
    string ChannelName { get; }
    string? Password { get; set; }
    int MaxPlayers { get; set; }
    int SkillLevel { get; set; }
    bool Passworded { get; set; }
    bool IsHost { get; }
    string HostName { get; }
    bool Locked { get; }
    CnCNetTunnel Tunnel { get; set; }
    IReadOnlyList<CnCNetGameRoomPlayer> Players { get; }
    event Action? HostAbandoned;
    event Action? LocalUserKicked;
    event Action? ChatChanged;
    event Action<string>? NoticeLogged;
    event Action<CnCNetStartGameInfo>? GameStarting;

    // 新增
    IReadOnlyList<ICnCNetPlayerSlot> CnCNetPlayerSlots { get; }
    void BroadcastPlayerOptions();
    void RequestLocalPlayerOptions(int side, int color, int start, int team);
}
```

### 7.4 `LobbyPlayerState` 改造后签名

```csharp
public sealed class LobbyPlayerState
{
    public LobbyPlayerState(Func<IReadOnlyList<IPlayerSlot>> slotsProvider);

    // 投影
    public IReadOnlyList<IPlayerSlot> Slots { get; }

    // 目录缓存（保留）
    public IReadOnlyList<string> SideNames { get; }
    public IReadOnlyList<LobbySideEntry> SideEntries { get; }
    public IReadOnlyList<string> AiNames { get; }
    public IReadOnlyList<string> TeamNames { get; }

    // UI 状态（保留）
    public LobbyPlayerMode Mode { get; set; }
    public bool AllowHostPlayerOptions { get; set; }
    public string LocalPlayerName { get; set; }
    public string HostPlayerName { get; set; }
    public bool PlayerUpdatingInProgress { get; set; }

    // 投影属性（基于 Slots 计算，逻辑不变）
    public int HumanRowCount { get; }
    public int AiRowCount { get; }
    public int OccupiedRowCount { get; }
    public int HumanCount { get; }
    public int AiCount { get; }
    public int OccupiedSlotCount { get; }
    public LobbyPlayerRowKind GetRowKind(int slotIndex);
    public int FirstEmptySlotIndex();

    // 持久化（保留签名，实现改走 sink）
    public bool TryLoadSkirmishSettings(IPlayerSlotSink sink);  // 加 sink 参数
    public void SaveSkirmishSettings();

    // 目录加载（保留）
    public void LoadCatalogs(bool includeSpectator = true);

    // 删除：ClearSlots / LoadDefaultSkirmishSlots* / RepopulateRows / EnsureHostAsFirstHuman / MarkLocalHuman / RebuildAiRowsFromUi
    // 这些都是写入操作，迁移到 SlotSink 调用者侧
}
```

---

## 8. 验收标准

### 8.1 量化目标

| 指标 | 当前 | 目标 |
|------|------|------|
| `MainWindow.axaml.cs` 行数 | ~2070 | ≤ 1950 |
| `MainWindow` 内 `CnCNetGameRoomPlayer` 引用 | 6 | 0 |
| `MainWindow` 内 `MultiplayerSlotLayout.ApplyToState` 调用 | 1 | 0 |
| `MainWindow._applyingCnCNetGameRoomPlayers` | 存在 | 删除 |
| `MainWindow.ApplyCnCNetGameRoomPlayers*` 方法数 | 3 | 0 |
| `LobbyPlayerState` 写入方法数 | 8+ | 0（全删除） |
| 玩家状态存储点 | 2 | 1 |
| 全量测试 | 558 | ≥ 590 |
| 新增代码覆盖率 | — | ≥ 90% |

### 8.2 质化目标

- [ ] Skirmish 模式：地图切换 → 槽位重置 → UI 刷新 链路通畅
- [ ] CnCNet 模式：收到 PO → 槽位更新 → UI 刷新 无死循环
- [ ] CnCNet 模式：房主编辑 → 广播 PO 无重发
- [ ] CnCNet 模式：Joiner 编辑 → OR CTCP → 房主收到 → PO 回广播 → Joiner UI 刷新
- [ ] SkirmishSettings.ini 持久化往返无丢失

---

## 9. 与前序设计的关系

### 9.1 与 `player-unification-and-ini-action-catalog.md` 的关系

本文档是前者 §1.5–1.7 的**详细设计版**，加入了：
- IPlayerSlotSink 抽象（前者只说"投影"，没说怎么收口写入）
- StateChanged 事件驱动（前者没说 MainWindow 怎么替代胶水）
- 静默 sink 模式（前者没考虑 PO 应用的死循环防御）
- 三阶段迁移（前者只给单步目标）

### 9.2 与 `mainwindow-analysis.md` 的关系

本文档完成后，`mainwindow-analysis.md` 的 **Step 5（CnCNetGameRoomSyncService）** 阻塞解除：
- 不再有 `_applyingCnCNetGameRoomPlayers` 隐式状态机
- 状态变更点统一到 Session.StateChanged 事件
- 抽 SyncService 就是把 `RefreshCnCNetGameRoomUi` + 订阅管理移到独立类

### 9.3 与 `architecture-evaluation-l1.md` 的关系

完成后，原报告中的"短期问题"消除：
- ~~三套玩家状态并存~~ → 单一真相源
- ~~MainWindow.ApplyCnCNetGameRoomPlayers 隐式状态机~~ → 事件驱动
- ~~MultiplayerSlotLayout 双向拷贝~~ → PlayerOptionsCodec 单向

---

## 10. 决策点

### 10.1 已锁定决策

1. ✅ `IPlayerSlotSink` 作为写入收口机制（替代直接 index 赋值）
2. ✅ StateChanged 事件驱动替代 MainWindow 显式调用
3. ✅ 静默 sink 模式用于 PO 应用（避免死循环）
4. ✅ 三阶段渐进迁移（每步独立可发布）
5. ✅ `LobbyPlayerState` 保留目录缓存 + UI 输入态（不彻底删）
6. ✅ 多 Mod 启动不在本分支考虑

### 10.2 待用户确认

1. **`SlotFieldUpdate` 的字段集**：是否需要加 `Index`（槽位移动）？
   - 当前设计：只改字段，不改顺序（顺序由 Source 列表顺序决定）
   - DX 启动器允许 host 拖动玩家换位——但目前 Avalonia 客户端未实现
   - 建议：先不做，未来需要时扩

2. **`ResetSlotsForMap` 是否进 IGameSession 基接口**
   - 当前设计：在 IGameSession 上（Skirmish 和 CnCNet 都需要）
   - 备选：只在 ISkirmishSession 上（CnCNet 房间里改地图不重置玩家）
   - 建议：在基接口，但 CnCNet 实现只清 AI 不清玩家

3. **`LobbyPlayerState.Slots` 返回 `IReadOnlyList<IPlayerSlot>` 还是 `LobbyPlayerSlot[]`**
   - 前者：彻底解耦，但旧代码用 LINQ + 类型转换会断
   - 后者：投影类型仍然强类型，但破坏"Session 不依赖 LobbyPlayerSlot"
   - 建议：返回 `IReadOnlyList<IPlayerSlot>`，旧代码改用 `Slots.Cast<LobbyPlayerSlot>()` 或迁移到 sink

---

## 11. 总结

这份设计文档的核心价值：

1. **量化了"为什么要做"**（§0）——三套状态的具体症状与代价
2. **明确了"做到什么程度"**（§1, §7）——目标分层 + 完整接口签名
3. **给出了"怎么做"**（§3, §5）——三阶段迁移 + 每个 Step 的具体改造
4. **防御了"会出什么问题"**（§6）——死循环、静默模式、线程安全
5. **连接了"上下游设计"**（§9）——与 Phase 1 已完成部分、MainWindow 拆分、L1 评估的关系

按本文档落地后，玩家状态彻底统一，MainWindow 拆分 Step 5 阻塞解除，为后续 9 步切片扫清最大障碍。
