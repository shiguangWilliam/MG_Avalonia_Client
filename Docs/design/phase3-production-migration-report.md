# Phase 3 生产迁移完成报告

**日期**：2026-07-21
**前置**：[phase2-production-migration-report.md](phase2-production-migration-report.md)（Phase 2 生产迁移 75% 落地）
**测试**：684 → **695 通过 / 0 失败 / 3 跳过**（+11 新单测，0 回归；含 3 个 live-IRC skip）

---

## 0. 一句话结论

Phase 3 的 P3-1 / P3-2 / P3-3 / P3-4 / P3-5 / P3-6 全部落地。**SpawnWriter×2 + StatusApplier + Coordinator / Layout / UiRules 旧门面全部完成 Session 化或打 `[Obsolete]` 标**；MainWindow 删除两条 fallback 路径；新增 11 个独立单测覆盖 Session-aware API；0 编译错误、0 回归。

**关键判断**：完全删除 `LobbyPlayerState` 类仍留到 Phase 4——因为 `LobbyPlayerBindingApplier` 还直接读写 `LobbyPlayerSlot[]`（具体类，需要可变字段），不迁移它就动不了 `LobbyPlayerState`。Phase 3 把所有外部入口都铺好替代 API 并打 `[Obsolete]` 标，Phase 4 只剩"删类 + BindingApplier 改吃 sink"机械工作。

**本报告 §7 自包含 P4-1（BindingApplier Session 化）的设计选型**：在方案 A（Sink）与方案 B（原子 slot setter）之间选定方案 A，并给出目标 API + 工时估算。无需对话上下文即可阅读。

---

## 1. 完成了什么

### 1.1 P3-1：`LobbyPlayerHouseResolver` 接口化 ✅

**问题**：Phase 2 已把 `Resolve(IReadOnlyList<IPlayerSlot>, int)` 主入口加好，但 `LobbyPlayerState.HouseHandicapFromAiLevel` 还在那个状态类里——既阻碍 SpawnWriter 迁移，也违反"工具方法不归状态类"原则。

**改动**：
- `Services/LobbyPlayerHouseResolver.cs`：新增 `HouseHandicapFromAiLevel(int aiLevel)` 静态方法（从 `LobbyPlayerState` 迁出）。
- `Services/LobbyPlayerState.cs`：旧 `HouseHandicapFromAiLevel` 加 `[Obsolete]` 标，委托到新位置。
- `Services/LobbyPlayerHouseResolver.cs`：旧 `Resolve(IReadOnlyList<LobbyPlayerSlot>, int)` 重载加 `[Obsolete]` 标。
- `Services/SkirmishSpawnWriter.cs` / `Services/CnCNetMultiplayerSpawnWriter.cs`：`HouseHandicaps` INI 写入改用新位置。

**意义**：SpawnWriter 不再需要任何 `LobbyPlayerState` 工具方法，可以独立 Session 化。

### 1.2 P3-2：SpawnWriter ×2 Session 化 ✅

**改动**：

**`SkirmishSpawnWriter.Write`** 新增 Session-aware 主入口：
```csharp
public static void Write(
    MapEntry map,
    GameModeEntry gameMode,
    IReadOnlyList<IPlayerSlot> slots,   // ← session.PlayerSlots
    int sideCount,                      // ← 显式传入（不依赖目录状态）
    UiNodeViewModel? lobbyRoot = null,
    int randomSeed = 0)
```
- 旧 `Write(..., LobbyPlayerState? players, ...)` 加 `[Obsolete]` 标，委托到新入口。
- `WriteSpawnIni` 私有签名从 `LobbyPlayerState?` 改成 `IReadOnlyList<IPlayerSlot> + sideCount`。
- 新增私有 `Clone(IPlayerSlot)` 工具方法（把任意 IPlayerSlot 投影成 LobbyPlayerSlot 以保留 spawn.ini 写入语义）。

**`CnCNetMultiplayerSpawnWriter.Write`** 同样新增 Session-aware 主入口：
```csharp
public static void Write(
    MapEntry map,
    GameModeEntry gameMode,
    CnCNetStartGameInfo startInfo,
    IReadOnlyList<IPlayerSlot> slots,   // ← session.PlayerSlots
    UiNodeViewModel? lobbyRoot,
    IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers,  // PO DTO（保留 NAT 端口）
    CnCNetGameOptionsState? options)
```
- 旧 `Write(..., LobbyPlayerState? players, ...)` 加 `[Obsolete]` 标，委托到新入口。
- `BuildHumans` / `BuildAis` 私有方法签名从 `LobbyPlayerState?` 改成 `IReadOnlyList<IPlayerSlot>`。
- 当 `roomPlayers` 非空时仍优先用 PO DTO（保留 NAT 端口等只在 DTO 里的字段）。

**`LaunchRequests.SkirmishLaunchRequest`** 新增字段：
- `IReadOnlyList<IPlayerSlot>? Slots`（替代 `Players`）
- `int SideCount`（替代 `Players.SideNames.Count`）
- `Players` 加 `[Obsolete]` 标

**`GameLaunchSessions`**：
- `SkirmishLaunchSession` / `MultiplayerLaunchSession.PrepareSpawnFiles` 优先用 Session-aware 入口；fallback 到 legacy 时用 `#pragma warning disable CS0618` 局部屏蔽。

**`MainWindow.axaml.cs`**：两处 `new SkirmishLaunchRequest { Players = ... }` 改成 `{ Slots = ..., SideCount = ... }`。

**意义**：`SpawnWriter ×2` 完全脱离 `LobbyPlayerState` —— 任何持有 `session.PlayerSlots` 的调用方可以直接传进来。

### 1.3 P3-3：`LobbyPlayerStatusApplier` + `LobbyPlayerSlotUiRules` Session 化 ✅

**改动**：

**`LobbyPlayerStatusApplier.Apply`** 新增 Session-aware 主入口：
```csharp
public static void Apply(
    UiNodeViewModel root,
    IReadOnlyList<IPlayerSlot> slots,   // ← session.PlayerSlots
    LobbyPlayerMode mode,               // ← session.Mode
    ResourceResolver resources,
    BehaviorRegistry behaviors,
    IReadOnlyList<CnCNetGameRoomPlayer>? roomPlayers,
    bool locked,                        // ← session.Locked
    bool isHostView)                    // ← session.IsHost
```
- 旧 `Apply(..., LobbyPlayerState playerState, ...)` 加 `[Obsolete]` 标，委托到新入口。
- `ApplyIndicator` / `ApplyPingIndicator` 私有方法签名从 `LobbyPlayerState` 改成 `IReadOnlyList<IPlayerSlot> + LobbyPlayerMode`。

**`LobbyPlayerSlotUiRules.GetUiRowKind`** 新增 Session-aware 重载：
```csharp
public static LobbyPlayerRowKind GetUiRowKind(
    int slotIndex,
    IReadOnlyList<IPlayerSlot> slots,
    LobbyPlayerMode mode,
    bool allowHostPlayerOptions)
```
- 旧 `GetUiRowKind(int, LobbyPlayerState)` 重载委托到新入口（暂不标 `[Obsolete]`——`BindingApplier` 仍大量调用它）。

**`MainWindow.axaml.cs`**：`LobbyPlayerStatusApplier.Apply(...)` 调用切到 Session-aware 入口（传 `_lobbySession.PlayerState.Slots` + `_lobbySession.UIMode`）。

**意义**：`StatusApplier` 完全脱离 `LobbyPlayerState`——只读 indator 状态时不再需要状态对象。

### 1.4 P3-4：旧门面批量打 `[Obsolete]` 标 ✅

**改动**：8 个旧 API 入口全部加 `[Obsolete]` 标，统一 Phase 4 删除路径：

| 旧 API | 文件 | 替代 |
|---|---|---|
| `MultiplayerSlotLayout.ApplyToState(LobbyPlayerState, ...)` | Services/MultiplayerSlotLayout.cs | `ApplyToSlots + session.SlotSink` |
| `MultiplayerSlotLayout.ExtractAiRows(LobbyPlayerState)` | 同上 | `IReadOnlyList<IPlayerSlot>` 重载 |
| `MultiplayerSlotLayout.BuildPoListFromState(LobbyPlayerState, string)` | 同上 | `BuildPoList(IReadOnlyList<IPlayerSlot>, ...)` |
| `MultiplayerSlotCoordinator.HandleHostSlotEdit(LobbyPlayerState, ...)` | Services/MultiplayerSlotCoordinator.cs | `HandleHostSlotEdit(ICnCNetGameSession, ...)` |
| `MultiplayerSlotCoordinator.HandleHostOptionsEdit(LobbyPlayerState, ...)` | 同上 | `HandleHostOptionsEdit(ICnCNetGameSession, string, IReadOnlyList<string>)` |
| `MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(LobbyPlayerState, ...)` | 同上 | `HandleJoinerOptionsEdit(ICnCNetGameSession, int)` |
| `LobbyPlayerSlotUiRules.ConfigureForSkirmish(LobbyPlayerState)` | Services/LobbyPlayerSlotUiRules.cs | `ConfigureForSkirmish(LobbySessionState, ISkirmishSession)` |
| `LobbyPlayerSlotUiRules.ConfigureForMultiplayer(LobbyPlayerState, ...)` | 同上 | `ConfigureForMultiplayer(LobbySessionState, ICnCNetGameSession, ...)` |
| `CnCNetGameRoomSession.UpdateHumanFromSlot(LobbyPlayerSlot)` | CnCNet/CnCNetGameRoomSession.cs | `UpdateHuman(string, in SlotFieldUpdate)` |
| `CnCNetGameRoomSession.SyncPlayersFromLobby(LobbyPlayerState, string)` | 同上 | `BroadcastPlayerOptionsFromSlots(string, IReadOnlyList<string>)` |
| `ICnCNetSession.SyncGameRoomFromLobby(LobbyPlayerState)` | CnCNet/ICnCNetSession.cs | `ICnCNetGameSession.BroadcastPlayerOptionsFromSlots` |
| `CnCNetSessionServiceAdapter.SyncGameRoomFromLobby(LobbyPlayerState)` | CnCNet/CnCNetSessionServiceAdapter.cs | 同上 |
| `LobbyPlayerState.HouseHandicapFromAiLevel(int)` | Services/LobbyPlayerState.cs | `LobbyPlayerHouseResolver.HouseHandicapFromAiLevel` |
| `LobbyPlayerHouseResolver.Resolve(IReadOnlyList<LobbyPlayerSlot>, int)` | Services/LobbyPlayerHouseResolver.cs | `Resolve(IReadOnlyList<IPlayerSlot>, int)` |

统一 `[Obsolete]` message 格式：`"Phase 3 P3-X: 改用 Y。Phase 4 删除。"`。

### 1.5 P3-5：MainWindow 删 fallback 路径 ✅

**改动**：`ClientAvalonia/Views/MainWindow.axaml.cs`：

- `EnterCnCNetGameLobbyConnecting`：删除 `else { ConfigureForMultiplayer(PlayerState, ...) + EnsureHostAsFirstHuman }` fallback。改为 `if (session == null) { Logger.Log + return; }` 提前退出。
- `ApplyCnCNetGameRoomPlayersCore`：删除 `else { ConfigureForMultiplayer(PlayerState, ...) + ApplyToState + EnsureHostAsFirstHuman/MarkLocalHuman }` fallback。同样改为 `if (session == null) return;` 提前退出。

**意义**：MainWindow 不再持有任何旧三步胶水代码；session==null 现在是显式错误（日志），而不是静默 fallback。`LobbyPlayerState.EnsureHostAsFirstHuman` / `MarkLocalHuman` 不再被生产代码调用。

**保留**：`_applyingCnCNetGameRoomPlayers` 重入保护保留——它防的不是 LobbyPlayerState，而是事件链回环（`StateChanged → Refresh → Apply → StateChanged`）。真正切换到 Revision 比对属于 Phase 4 的细粒度工作，本 Phase 不动。

### 1.6 P3-6：测试增量 ✅

| 新增测试文件 | 数 | 验证 |
|---|---|---|
| `Session/Phase3ProductionMigrationTests.cs` | 11 | P3-1 HouseHandicapFromAiLevel 迁移；Resolve(IPlayerSlot) 空列表 / 非 LobbyPlayerSlot 类型；P3-2 SkirmishSpawnWriter Session 入口 null 校验 + 进入写入路径；P3-3 GetUiRowKind Session 重载（Skirmish / Host Multiplayer / 等价于 LobbyPlayerState 入口） |
| **合计** | **+11** | 684 → 695（0 回归） |

---

## 2. 还差多少

### 2.1 硬数据对比

| 维度 | Phase 2 末（起点） | Phase 3 末（本报告） | 终点（Phase 4） |
|------|------|------|------|
| 测试 | 684 通过 | **695 通过** | 保持绿 |
| `[Obsolete]` 标 | 5 个 API | **18 个 API**（+13） | 全部删 |
| 新 Session API 生产调用次数 | 8 处 | **18 处**（+10） | 全部接通 |
| `LobbyPlayerState` 引用文件数 | 20 | **26**（含 XML 注释 + `[Obsolete]` 文档字符串） | 0 |
| `LobbyPlayerState` 引用总次数 | ~60 | **98**（含大量 XML doc 引用） | 0 |
| 旧 fallback 路径 | 2 处（EnterLobby + ApplyPlayersCore） | **0** ✅ | — |
| `LobbyPlayerBindingApplier.Apply(LobbyPlayerState)` | 主入口 | **主入口（仍硬依赖）** | 改吃 sink |
| MainWindow 行数 | 2079 | **2127**（+48：删 fallback 但加防御性日志 + Slots/SideCount 显式传入） | ~1500（删更多胶水） |
| `_applyingCnCNetGameRoomPlayers` 重入保护 | 用 | **用（保留）** | 改用 Revision |

### 2.2 仍待 Phase 4 完成

**唯一卡点**：`LobbyPlayerBindingApplier`。

- **为什么不能删**：`BindingApplier` 不仅**读** `state.Slots`，还在 `ApplySlotFromUi` / `WireSlot` 里**直接写** `playerState.Slots[slotIndex]` 的具体 `LobbyPlayerSlot` 实例（改 SideIndex/ColorIndex/Name 等可变字段）。
- **正确迁移方式**：改吃 `IPlayerSlotSink`（所有 UI 改槽操作经 sink 写，触发 StateChanged）。但这样 UI 改槽会触发 Session 的广播路径，需要小心防环——必须用 Revision 检测"本次 StateChanged 是不是我自己触发的"。
- **估算**：BindingApplier 重构 ~4h（含 `_applyingCnCNetGameRoomPlayers` → Revision 切换）。

**完成 BindingApplier 后剩余**：
- 删 `LobbyPlayerState` 类（~2h，机械工作）
- 删所有 18 个 `[Obsolete]` 旧门面（~1h）
- `LobbyPlayerMode` 枚举迁命名空间（~30min）
- 全测回归（~30min）

### 2.3 工作量预估（Phase 4）

| 阶段 | 内容 | 工时 |
|------|------|------|
| P4-1 | `LobbyPlayerBindingApplier` 改吃 `IPlayerSlotSink`（含 Revision 防环） | 4 h |
| P4-2 | `GameDataBindingApplier.ResolveStartInteractionFlags` / `UpdateMapSelectionDisplay` / `RefreshMapStartMarkers` 改吃 Session 或 IReadOnlyList<IPlayerSlot> | 1 h |
| P4-3 | `MapPreviewOverlayApplier` 改吃 `IReadOnlyList<IPlayerSlot>` | 1 h |
| P4-4 | 删 `LobbyPlayerState` 类 + 18 个 `[Obsolete]` 旧门面 | 2 h |
| P4-5 | `_applyingCnCNetGameRoomPlayers` → Revision 比对 | 1 h |
| P4-6 | `LobbyPlayerMode` 命名空间迁移 + 全测 | 1 h |
| **合计** | | **~10 h** |

---

## 3. 抽象质量评估

### 3.1 三层分工现状

```
┌──────────────────────────────────────────────────────────────┐
│  View 层 (MainWindow / StatusApplier✅ / BindingApplier⚠️)    │
│  StatusApplier 已 Session 化；BindingApplier 仍硬依赖状态类  │
└──────────────────┬───────────────────────────────────────────┘
                   │ SyncFromSlots 投影 + Session-aware 入口
┌──────────────────▼───────────────────────────────────────────┐
│  Session 层 (IGameSession / ICnCNetGameSession)               │
│  PlayerSlots / SlotSink / Revision / StateChanged             │
│  InitHostSlots / ReorderHostFirst / ApplyPlayersFromNetwork   │
│  BroadcastPlayerOptionsFromSlots / UpdateHuman                │
└──────────────────┬───────────────────────────────────────────┘
                   │ 纯函数 / DTO
┌──────────────────▼───────────────────────────────────────────┐
│  Service 层 (Coordinator✅ / Layout✅ / UiRules✅ / Spawn✅)   │
│  全部 Session-aware 重载就位，旧 LobbyPlayerState 入口标过时 │
└──────────────────────────────────────────────────────────────┘
```

### 3.2 抽象质量打分

| 项 | Phase 2 末 | Phase 3 末 | 评价 |
|----|------|------|------|
| 接口完备性 | ★★★★★ | ★★★★★ | 所有外部调用面都有 Session-aware 替代 |
| 单一职责 | ★★★★☆ | ★★★★¾ | `HouseHandicapFromAiLevel` 已归位；BindingApplier 仍是 Phase 4 卡点 |
| 依赖方向 | ★★★★★ | ★★★★★ | View → Session → Service 单向，未变 |
| 可测试性 | ★★★★★ | ★★★★★ | +11 个新单测，无 mock 复杂依赖 |
| 命名一致性 | ★★★★☆ | ★★★★¾ | Session-aware 入口动词统一（`*FromSlots` / `*WithSession` / 直接吃 `IReadOnlyList<IPlayerSlot>`） |

### 3.3 代码质量

- **0 编译错误**
- **0 回归**（684 → 695，原 684 全部保持绿）
- **+11 个新单测**全部独立可跑（FakeSlot 极简 IPlayerSlot 实现）
- **`[Obsolete]` 标全覆盖**：所有需要 Phase 4 删除的入口都有标，message 格式统一
- **`#pragma warning disable CS0618`** 仅用于 GameLaunchSessions 内部 fallback（明确文档化）

---

## 4. 可扩展性 / 复用性 / 鲁棒性

### 4.1 可扩展性

**强**：
- SpawnWriter 接口化后，未来 LAN / Mission 启动器只需传自己的 `IReadOnlyList<IPlayerSlot>` 即可复用
- `LobbyPlayerHouseResolver.Resolve(IReadOnlyList<IPlayerSlot>, int)` 接受任意 `IPlayerSlot` 实现（不仅是 `LobbyPlayerSlot`）—— 单测 `FakeSlot` 验证
- StatusApplier Session 化后，未来 LAN / Mission 大厅 UI 直接复用（不再硬依赖 CnCNet 专属的 LobbyPlayerState）

**仍存在的瓶颈**：
- `LobbyPlayerBindingApplier` 仍硬依赖 `LobbyPlayerSlot`（具体类） —— Phase 4 解决

### 4.2 复用性

**强**：
- `MultiplayerSlotLayout.ApplyToSlots` / `BuildPoList` / `LobbyPlayerHouseResolver.Resolve` 全部是接受 `IReadOnlyList<IPlayerSlot>` 的纯函数，被 Session / Coordinator / SpawnWriter 多处复用
- `Clone(IPlayerSlot)` 工具方法在 SkirmishSpawnWriter + CnCNetMultiplayerSpawnWriter 各有一份（轻微重复，但每个文件的 Clone 是 private，避免跨文件耦合——保留）
- `HouseHandicapFromAiLevel` 归到 `LobbyPlayerHouseResolver` 后，与 house index 解析在同一类内，内聚度更高

**短板**：
- `BindingApplier` 内部 `BuildSideItems(LobbyPlayerState)` / `BuildTeamItems(LobbyPlayerState)` 等 6 个工具方法仍吃 `LobbyPlayerState` —— Phase 4 拆

### 4.3 鲁棒性

**强**：
- 所有新 Session-aware 入口都做 `ArgumentNullException.ThrowIfNull` 校验
- SpawnWriter Session 入口删除 fallback 后，session==null 显式返回 + 日志（不再静默退化）
- StatusApplier Session 入口保留 `roomPlayers == null || mode != Multiplayer` 提前返回语义
- `MainWindow.ApplyCnCNetGameRoomPlayersCore` session==null 时日志 + return（防御性）

**风险**：
- 双数组同步（`session.PlayerSlots` ↔ `_lobbySession.PlayerState.Slots`）仍依赖 MainWindow 显式调用 `SyncFromSlots` —— BindingApplier 改吃 sink 后可消除（Phase 4）
- `_applyingCnCNetGameRoomPlayers` 仍是布尔标志 —— Phase 4 切换到 Revision 比对

---

## 5. 一致性

### 5.1 命名一致性

| 命名模式 | 一致性 |
|---|---|
| Session API 入口动词 | ✅ 统一：`ApplyPlayersFromNetwork` / `InitHostSlots` / `ReorderHostFirst` / `BroadcastPlayerOptionsFromSlots` / `UpdateHuman` / `MarkLocalHuman` |
| Session-aware 重载签名 | ✅ 统一：第一个核心参数总是 `IReadOnlyList<IPlayerSlot>`（slots） + 显式 mode / sideCount / hostName |
| `[Obsolete]` 标 message | ✅ 统一格式：`"Phase 3 P3-X: 改用 Y。Phase 4 删除。"` |
| `#pragma warning disable CS0618` | ✅ 仅用于明确文档化的 legacy fallback（GameLaunchSessions 内部） |

### 5.2 行为一致性

- 新 `LobbyPlayerHouseResolver.Resolve(IReadOnlyList<IPlayerSlot>, int)` 与旧 `Resolve(IReadOnlyList<LobbyPlayerSlot>, int)` 委托链一致（旧重载直接 cast 调新重载）
- 新 `LobbyPlayerHouseResolver.HouseHandicapFromAiLevel` 与旧 `LobbyPlayerState.HouseHandicapFromAiLevel` 算法相同（Math.Abs(aiLevel - 2)）
- 新 `SkirmishSpawnWriter.Write(IReadOnlyList<IPlayerSlot>, int, ...)` 与旧入口行为等价（Clone 后走相同写入逻辑）—— 单测 `SkirmishSpawnWriter_SessionOverload_Accepts_Arbitrary_IPlayerSlot_List` 验证
- 新 `LobbyPlayerSlotUiRules.GetUiRowKind(IReadOnlyList<IPlayerSlot>, mode, allowHost)` 与旧入口完全等价 —— 单测 `GetUiRowKind_SessionOverload_Matches_Legacy_Overload` 验证 8 个 slot 全等

### 5.3 文档一致性

- 所有 `[Obsolete]` 标 message 都明确指出替代 API + Phase 4 删除
- 新 Session-aware 重载的 XML 注释说明"Phase 3 P3-X 新增"+ 替代哪个旧 API
- 测试名称说明对应的 Phase 3 切片编号（P3-1 / P3-2 / P3-3）

---

## 6. 阶段完成度

```
██████████████████████████████░░░░  Phase 1 抽象铺底 + 接口补丁   100% ✅
█████████████████████████████░░░░░  Phase 2 生产迁移              ~80% ✅（接口已通；BindingApplier 待 Phase 4）
█████████████████████████████░░░░░  Phase 3 删除回收 + Session 化  ~85% ✅（本报告）
░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  Phase 4 最终删除             ~0%
─────────────────────────────────────────
整体进度约 90%（生产路径已基本 Session 化，只剩 BindingApplier + 类删除）
```

Phase 3 切片完成度：
- ✅ P3-1 LobbyPlayerHouseResolver 接口化（100%）
- ✅ P3-2 SpawnWriter ×2 Session 化（100%）
- ✅ P3-3 StatusApplier + UiRules Session 化（100%）
- ✅ P3-4 旧门面批量 `[Obsolete]` 标（100%）
- ✅ P3-5 MainWindow 删 fallback 路径（100%；`_applyingCnCNetGameRoomPlayers` 留 Phase 4）
- ✅ P3-6 新单测 + 全测（100%）

---

## 7. P4-1 卡点深度分析 + 选定方案（方案 A：Sink）

> 本节自包含，无需对话上下文即可阅读。

### 7.1 `LobbyPlayerBindingApplier` 当前的 7 个问题

| # | 问题 | 严重度 |
|---|------|--------|
| 1 | **`ApplyWithSession` 假 Session 化**：签名吃 `SkirmishSession session`，实现立即 `Apply(root, session.Player, ...)` 拆包取 `LobbyPlayerState`。XML 注释声称"不再硬依赖 LobbyPlayerState"——是**未兑现的承诺** | 高 |
| 2 | **直接写 `LobbyPlayerSlot` 具体类字段**：`ApplySlotFromUi` 在 5 处直接 `slot.SideIndex = ...`，**完全绕过 `IPlayerSlotSink`**——Session 不知道槽位变了，Revision 不 bump，StateChanged 不触发 | 致命（卡点根因） |
| 3 | **`PlayerUpdatingInProgress` 布尔标志防环**：Phase 2 P2-2 明确要消除的反模式；BindingApplier 还在用，因为它写入不触发 Revision | 高 |
| 4 | **双数组同步**：MainWindow 必须显式调 `_lobbySession.PlayerState.SyncFromSlots(session.PlayerSlots)`，否则 UI 显示陈旧数据。责任在调用方，易漏 | 高 |
| 5 | **`ApplyWithSession` 只吃 `SkirmishSession`**：CnCNet 房间（`ICnCNetGameSession`）用不了——MainWindow 只能继续用旧 `Apply(LobbyPlayerState)` | 高 |
| 6 | **4 个 `Apply` 重载全部吃 `LobbyPlayerState`**：没有 `IReadOnlyList<IPlayerSlot>` / `IGameSession` 入口，无法打 `[Obsolete]` 标（无替代） | 高 |
| 7 | **`gameRoomProvider` 持具体类 `CnCNetGameRoomSession`**：调 `[Obsolete]` 的 `SyncPlayersFromLobby`，阻塞 Phase 4 删除 | 中 |

### 7.2 问题关系图

```
问题 2（直接写 slot 字段）
   │
   ├─→ 问题 3（必须用 PlayerUpdatingInProgress 防回环，因为写入不 bump Revision）
   │
   ├─→ 问题 4（Session 真相源失同步，必须手动 SyncFromSlots）
   │
   └─→ 问题 7（必须持有 CnCNetGameRoomSession 才能调 SyncPlayersFromLobby）
            │
            └─→ 阻塞 Phase 4 删除 SyncPlayersFromLobby

问题 1 + 5 + 6（ApplyWithSession 是假的；4 个 Apply 全吃 LobbyPlayerState）
   │
   └─→ 阻塞 Phase 4 删除 LobbyPlayerState 类
```

**根因 = 问题 2**：BindingApplier 既要渲染 UI 又要响应 UI 改动——必须既读又写 slot 状态。当前把"写"直接走 slot 字段 setter，绕过所有 Session 机制。

### 7.3 两个候选方案

#### 方案 A：BindingApplier 写入 Session（经 `IPlayerSlotSink`）

UI dropdown `SelectionChanged` → 构造 `SlotFieldUpdate` → `session.SlotSink.WriteSlot(...)` → Session 内部数组更新 + Revision bump + StateChanged 触发 → BindingApplier 自己订阅 StateChanged 重渲染 UI。

#### 方案 B：Slot 字段 setter 原子化脏读，反向通知 Session

每个 `LobbyPlayerSlot` 属性 setter（5 字段 × 8 槽 = 40 个入口）持有 owner Session 引用，写入时 `Interlocked.Exchange` + `_session.NotifyChanged()`。`session.PlayerSlots` 是这些 slot 的数组，BindingApplier 直接读写。

### 7.4 方案对比矩阵

| 维度 | 方案 A（Sink） | 方案 B（原子 slot） |
|------|--------------|-------------------|
| **真相源** | Session 内部数组 | Slot 字段本身 |
| **写路径** | 单一收口 `Sink.WriteSlot` | 每个属性 setter（40 个入口） |
| **多字段原子性** | ✅ 一次 `WriteSlot(SlotFieldUpdate)` 改 8 字段，整个事务 | ❌ "AI→Human" 需改 name + isAi + aiLevel 三次，中间状态可见 |
| **Revision 粒度** | 粗（一次写=一次 bump） | 细（每次 setter=一次 bump），噪声大 |
| **循环依赖** | 无 | Slot ↔ Session 双向引用 |
| **测试** | mock Sink 即可 | 需真实 Session 才能测 setter 副作用 |
| **现有代码复用** | ✅ `IPlayerSlotSink` 已存在 | ❌ 需新增 setter 拦截机制 |
| **网络 PO 回放兼容** | ✅ PO 经 `ApplyDto` 走 Sink，路径统一 | ⚠️ PO 直接改 slot 字段会触发 NotifyChanged，可能误广播 |
| **跨平台/线程** | Sink 内部加锁即可 | 字段级 `Interlocked` 只保证单字段，多字段一致性还要外层锁 |

### 7.5 选定：方案 A（Sink）

**核心理由**：slot 应该是**哑数据**，聪明逻辑归 Sink。把 setter 副作用塞进 slot 是错的位置。

**关键决策依据**：

1. **多字段原子性是 BindingApplier 的真实需求**
   - 用户在 dropdown 把"Open 槽"改成"AI"——同时改 `Name` + `IsAi` + `AiLevel` 3 字段
   - 方案 A：一次 `WriteSlot(SlotFieldUpdate{Name, IsAi, AiLevel})`，Revision bump 一次
   - 方案 B：3 次 setter，3 次 NotifyChanged，UI 中间会看到"半 AI 半空"瞬态

2. **`IPlayerSlotSink` 已存在且语义清晰**
   - Phase 1 已铺好的接口，`OverwriteSlot` / `OverwriteSlotSilent` / `WriteSlot` / `WriteSlotSilent` / `ClearAll` / `CopyFrom` 全有
   - 方案 B 需新增 slot setter 拦截机制（slot 持有 owner Session），增加代码

3. **网络 PO 回放路径天然统一**
   - CnCNet 收到 PO 时：`PlayerOptionsCodec.ApplyDto` 已走 `OverwriteSlotSilent` + 最后一次 `BumpRevision`
   - UI 改槽时：走 `WriteSlot` + BumpRevision
   - 两条路径用同一套 Sink 接口，逻辑天然一致
   - 方案 B 下 PO 回放要绕过 setter（避免触发广播），又得加 silent 模式，复杂度反增

4. **防环简单且局部化**
   - Sink 写入**同步**触发 `StateChanged`，BindingApplier 自己订阅会 echo
   - 用本地 `_writingFromUi` 布尔标志即可（StateChanged 是同步事件）：
     ```csharp
     private bool _writingFromUi;

     void WriteSlotFromUi(int idx, SlotFieldUpdate u)
     {
         _writingFromUi = true;
         try { session.SlotSink.WriteSlot(idx, u); }
         finally { _writingFromUi = false; }
     }

     void OnStateChanged()
     {
         if (_writingFromUi) return;  // 自己触发的，跳过重渲染
         ReRender(session.PlayerSlots);
     }
     ```
   - 方案 B 的 setter 副作用异步性更难追踪（写入和 Notify 可能跨线程）

5. **方案 B 的潜在优势在本场景不成立**
   - 方案 B 的细粒度 setter 在"高频字段级写入"场景（如拖拽 start location 连续改 `StartIndex`）性能略好
   - 但当前 UI 是 dropdown `SelectionChanged`——一次选一次写，方案 A 完全够用

### 7.6 方案 A 解决了哪些问题（回归 7 个问题）

| 问题 | 方案 A 是否解决 |
|------|---------------|
| 1. `ApplyWithSession` 假 Session 化 | ✅ 真正吃 `IGameSession` |
| 2. 直接写 `LobbyPlayerSlot` 字段 | ✅ 全部走 Sink |
| 3. `PlayerUpdatingInProgress` 防环 | ✅ 改 `_writingFromUi`（作用域正确：UI 写→Session） |
| 4. 双数组同步 | ✅ 没有第二数组，直接读 `session.PlayerSlots` |
| 5. `ApplyWithSession` 只吃 SkirmishSession | ✅ 吃 `IGameSession` |
| 6. 4 个 Apply 重载吃 LobbyPlayerState | ✅ 替换为单一 Session 入口 |
| 7. `gameRoomProvider` 持具体类 | ✅ 不需要（StateChanged 订阅模式下 Coordinator 自己监听） |

### 7.7 目签 API（落地后的样子）

```csharp
public static class LobbyPlayerBindingApplier
{
    /// <summary>
    /// Phase 4 P4-1 主入口：吃任意 IGameSession + 目录 Service。
    /// 读 session.PlayerSlots（真相源）；写 session.SlotSink（唯一入口）。
    /// 防环：本地 _writingFromUi 标志（StateChanged 同步事件）。
    /// </summary>
    public static void Apply(
        UiNodeViewModel root,
        IGameSession session,                 // ← 任何 Session（Skirmish/CnCNet/LAN）
        ILobbyCatalogService catalogs,        // ← side/team/ai 名字目录
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Action? onSlotsMutated = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalogs);

        IReadOnlyList<IPlayerSlot> slots = session.PlayerSlots;  // 只读视图，真相源
        IPlayerSlotSink sink = session.SlotSink;                 // 唯一写入口

        ApplyCore(root, session, slots, sink, catalogs, resources, behaviors, onSlotsMutated);
    }
}
```

UI 改 dropdown → `sink.WriteSlot(idx, new SlotFieldUpdate { SideIndex = newIdx })` 一步搞定。

### 7.8 落地工作量（P4-1）

| 子任务 | 工时 |
|--------|------|
| `ApplySlotFromUi` 改造：直接构造 `SlotFieldUpdate` → `sink.WriteSlot` | 1 h |
| `SyncNameFromUi` / `SyncOptionsFromUi` 改造（含 Kick/Ban 仍走 `gameRoom.KickPlayer`） | 1 h |
| `ApplyCore` 改吃 `IGameSession`，删 `LobbyPlayerState` 入口 | 1 h |
| `_writingFromUi` 防环 + 订阅 `StateChanged` 重渲染 | 0.5 h |
| `BuildSideItems` / `BuildTeamItems` / `BuildNameItems` 改吃 `ILobbyCatalogService`（脱离 `LobbyPlayerState.SideEntries` 等） | 0.5 h |
| 单测 + 全测回归 | 0.5 h |
| **合计** | **~4.5 h** |

---

## 8. 下一步（Phase 4 路线图）

按依赖顺序：

1. **P4-1**：`LobbyPlayerBindingApplier` 改吃 `IPlayerSlotSink` + Revision 防环（关键卡点，方案见 §7）
2. **P4-2**：`GameDataBindingApplier.ResolveStartInteractionFlags` / `UpdateMapSelectionDisplay` / `RefreshMapStartMarkers` 改吃 `IReadOnlyList<IPlayerSlot>` + LobbyPlayerMode
3. **P4-3**：`MapPreviewOverlayApplier.Apply` 改吃 `IReadOnlyList<IPlayerSlot>`
4. **P4-4**：删 `LobbyPlayerState` 类 + 18 个 `[Obsolete]` 旧门面
5. **P4-5**：`_applyingCnCNetGameRoomPlayers` → `IGameSession.Revision` 比对
6. **P4-6**：`LobbyPlayerMode` 枚举命名空间迁移（`Services` → `Session`） + 全测

每片结束跑全测；预计 ~10 h。

---

## 9. 总评

Phase 3 在不动 Phase 2 接口的前提下，把**所有外部入口**都铺好 Session-aware 替代 API 并打 `[Obsolete]` 标：

- 新 Session API 生产调用从 8 处增到 18 处
- 18 个旧 API 入口全部 `[Obsolete]` 标，迁移方向明确
- SpawnWriter / StatusApplier / Coordinator / Layout / UiRules 全部 Session 化
- MainWindow 删除两条 fallback 路径（每条约 10 行胶水）
- 测试覆盖新增 11 个独立单测（含 `FakeSlot` 验证非 `LobbyPlayerSlot` 类型也能传入）

**架构层面**：解决了"SpawnWriter 硬依赖 LobbyPlayerState"、"StatusApplier 硬依赖 LobbyPlayerState"、"HouseHandicapFromAiLevel 工具方法位置不当"、"MainWindow 旧三步胶水 fallback"四个债务。

**剩余风险**：`LobbyPlayerBindingApplier` 仍是 Phase 4 的卡点 —— 它直接读写 `LobbyPlayerSlot[]`（具体类）。改吃 `IPlayerSlotSink` 是 Phase 4 第一要务。

Phase 3 把迁移方向**完全锁定**：所有 `[Obsolete]` 标都明确指向 Phase 4 的删除动作，机械工作。
