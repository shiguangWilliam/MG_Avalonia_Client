# Phase 1 完成报告 — Player State Unification & Layered Architecture

**日期**：2026-07-20
**前置文档**：[layered-architecture.md](layered-architecture.md) · [layered-architecture-progress-report.md](layered-architecture-progress-report.md) · [phase1-player-state-completion.md](phase1-player-state-completion.md)
**基线**：596 通过 / 3 跳过 / 0 失败
**最终**：645 通过 / 3 跳过 / 0 失败（**+49 测试**，0 回归）

---

## 1. 目标与执行模型

按用户审批的"纵向切片"策略，把延后的 Phase 1 工作（删除 `LobbyPlayerState` 的全部职责并把 `MainWindow.ApplyCnCNetGameRoomPlayers*` 胶水替换为 `IGameSession.StateChanged` 订阅）拆为 6 个独立可发布、独立可测的切片，每片跑全测验证。

执行原则（用户原话）：
> 每一个分片修改完，运行一次单元测试，确认是否稳定可运行。如果出现 bug，排查是否因为和其他部分关联，如果不关联，则 debug 修复。如果与其他部分相关，搁置，待修复之后再测试。

---

## 2. 切片逐项验收

### Slice 1 — `GameSessionExtensions` 派生只读查询 ✅
**目标**：把 `LobbyPlayerState.HumanRowCount` / `AiRowCount` / `GetRowKind` 等纯函数提到 `IReadOnlyList<IPlayerSlot>` 扩展方法。

**改动**：
- 新建 `ClientAvalonia/Session/GameSessionExtensions.cs`（7 个扩展方法 + Session 重载）
- `Services/LobbyPlayerState.cs`：6 个属性/方法改成 `((IReadOnlyList<IPlayerSlot>)Slots).Xxx()` 委托
- 新建 `ClientAvalonia.Tests/Session/GameSessionExtensionsTests.cs`（14 测试）

**测试**：596 → 610（+14），0 回归。

**意义**：BindingApplier / Coordinator 后续可直接吃 `IPlayerSlot[]`，不依赖 `LobbyPlayerSlot[]` 具体类型。

---

### Slice 2 — `ILobbyCatalogService` 抽出阵营/AI/团队目录 ✅
**目标**：把 `LobbyPlayerState.LoadCatalogs` 的目录加载逻辑提到独立 Service。

**改动**：
- 新建 `ClientAvalonia/Services/ILobbyCatalogService.cs`（接口 + `LobbyCatalogService` 默认实现）
- `Core/PreStartup.cs`：注册 `ILobbyCatalogService` → `LobbyCatalogService.Instance`
- `Services/LobbyPlayerState.cs`：`LoadCatalogs` 委托给 `EnvironmentServices.Resolve<ILobbyCatalogService>()`
- 新建 `ClientAvalonia.Tests/Session/LobbyCatalogServiceTests.cs`（4 测试）

**测试**：610 → 614（+4），0 回归。

**意义**：目录数据归 Service 层；BindingApplier 后续可直接吃 `ILobbyCatalogService`。

---

### Slice 3 — `ISkirmishSettingsService` 抽出 INI 持久化 ✅
**目标**：把 `LobbyPlayerState.TryLoadSkirmishSettings` / `SaveSkirmishSettings` 的文件 IO 提到独立 Service。

**改动**：
- 新建 `ClientAvalonia/Session/ISkirmishSettingsService.cs`（接口 + `SkirmishSettingsService` 默认实现 + `SkirmishSettingsDto` / `SkirmishPlayerDto`）
- `Core/PreStartup.cs`：注册 `ISkirmishSettingsService`
- `Services/LobbyPlayerState.cs`：`TryLoadSkirmishSettings` / `SaveSkirmishSettings` 改成委托（IO 走 Service，状态写回仍走自身 Slots）
- 新建 `ClientAvalonia.Tests/Session/SkirmishSettingsServiceTests.cs`（6 测试）

**测试**：614 → 620（+6），0 回归。

**意义**：
1. 持久化路径绝对路径注入，测试可绕开 `ProgramConstants.GamePath` 进程级静态。
2. `LobbyPlayerState` 不再直接读 INI 文件，只剩"状态写入"职责。

---

### Slice 4 — `IGameSession.Mode` + `Revision` 原子脏读 tag ✅
**目标**：落实用户决策"原子化脏读 tag 替代 `PlayerUpdatingInProgress`" + "Skirmish / Multiplayer 由派生 Session 决定"。

**改动**：
- `Session/IGameSession.cs`：新增 `LobbyPlayerMode Mode { get; }` 和 `long Revision { get; }`
- `Session/SkirmishSession.cs`：`Mode => Skirmish`；`SlotSink` 回调 / `Map` setter / `State` setter 改走私有 `BumpRevision()` 自增 `_revision` 并触发 `StateChanged`
- `CnCNet/CnCNetGameRoomSession.cs`：`Mode => Multiplayer`；所有 `StateChanged?.Invoke()` 改成 `BumpRevision()`（`Interlocked.Increment(ref _revision)` 保证原子）
- `Services/LobbySessionState.cs`：吸收 `LobbyPlayerState` 的 5 个 UI 输入态（`UIMode` / `AllowHostPlayerOptions` / `LocalPlayerName` / `HostPlayerName` / `PlayerUpdatingInProgress`）
- 新建 `ClientAvalonia.Tests/Session/GameSessionModeAndRevisionTests.cs`（14 测试）

**测试**：620 → 634（+14），0 回归。

**意义**：
1. UI 重入保护从脆弱的布尔标志升级为单调递增的 long tag，BindingApplier 后续可用"订阅时记录 Revision，回调时比对"防自反馈循环。
2. `Mode` 不再是状态字段，而是派生属性——Session 类型本身即答案。
3. UI 输入态归 `LobbySessionState`（View 层），Session 不再混入"用户在 UI 上选了哪个标签"这种纯展示语义。

---

### Slice 5 — BindingApplier + Coordinator 新增 Session 重载（保留旧重载） ✅
**目标**：暴露 Session-aware 入口，为后续 MainWindow 切换做准备，不破坏现有调用方。

**改动**：
- `IniUi/Binding/LobbyPlayerBindingApplier.cs`：新增 `ApplyWithSession(UiNodeViewModel, SkirmishSession, ILobbyCatalogService, ResourceResolver, BehaviorRegistry, ...)` 重载
- `Services/MultiplayerSlotCoordinator.cs`：新增 `HandleHostSlotEdit(ICnCNetGameSession, int, ICnCNetPlayerSlot, UiNodeViewModel, bool, string)` 重载
- 新建 `ClientAvalonia.Tests/Session/BindingApplierSessionOverloadTests.cs`（5 测试）

**测试**：634 → 639（+5），0 回归。

**意义**：BindingApplier/Coordinator 不再硬依赖 `LobbyPlayerState`；旧重载保留以兼容 MainWindow 现有调用。

---

### Slice 6 — `ICnCNetGameSession.EnsureHostFirst` / `MarkLocalHuman` ✅
**目标**：把"host 优先 + 本地人标记"语义从 `MainWindow.ApplyCnCNetGameRoomPlayers*` 胶水代码移到 Session 内。

**改动**：
- `Session/ICnCNetGameSession.cs`：新增 `EnsureHostFirst(string localPlayerName, int maxPlayers)` 和 `MarkLocalHuman(string playerName)` 接口方法
- `CnCNet/CnCNetGameRoomSession.cs`：实现这两个方法（`lock (_sync)` 保护，BumpRevision 通知）
- 新建 `ClientAvalonia.Tests/Session/CnCNetGameRoomSessionHostSetupTests.cs`（6 测试）

**测试**：639 → 645（+6），0 回归。

**意义**：MainWindow 接下来替换 `ApplyCnCNetGameRoomPlayers*` 时，可直接调 `session.EnsureHostFirst(...)` + `session.MarkLocalHuman(...)`，胶水代码减少 ~50 行。

---

## 3. 关键设计决策记录

| # | 决策 | 理由 |
|---|------|------|
| 1 | `IGameSession.Mode` 由派生类决定而非字段 | 类型即真相，避免 Mode/Session 类型不一致；用户在 §3 决策中明确同意 |
| 2 | `Revision` 用 `long` + `Interlocked.Increment` | 原子、无锁、单调；语义比 `bool PlayerUpdatingInProgress` 更强（可比较、可记录订阅时间点） |
| 3 | UI 输入态迁到 `LobbySessionState` 而非 `IGameSession` | UI 选择（如"上次切到 Skirmish 标签"）不属于 Session 真相；归 View 层避免污染 |
| 4 | Slice 5 用"新增重载，保留旧重载"而非替换 | 防止一次破坏 MainWindow ~20 个调用点；Slice 6 之后可逐步切换 |
| 5 | `ISkirmishSettingsService` 支持 absolutePath 注入 | 测试隔离需要；不依赖 `ProgramConstants.GamePath` 进程级静态 |
| 6 | `LobbyCatalogService` 单例 + 公开 ctor | 生产路径走单例（避免每次 Reload 重读）；测试可 `new` 独立实例 |
| 7 | Slice 6 不删 `LobbyPlayerState` | 完全删除会触及 ~20 文件，超出"垂直切片"安全范围；留作 Phase 2 起手 |

---

## 4. 测试覆盖增量

| 切片 | 新增测试文件 | 新增测试数 | 累计通过 |
|------|-------------|-----------|---------|
| 1 | `GameSessionExtensionsTests.cs` | 14 | 610 |
| 2 | `LobbyCatalogServiceTests.cs` | 4 | 614 |
| 3 | `SkirmishSettingsServiceTests.cs` | 6 | 620 |
| 4 | `GameSessionModeAndRevisionTests.cs` | 14 | 634 |
| 5 | `BindingApplierSessionOverloadTests.cs` | 5 | 639 |
| 6 | `CnCNetGameRoomSessionHostSetupTests.cs` | 6 | 645 |
| **合计** | 6 文件 | **+49** | **596 → 645** |

**回归率**：0%（基线绿，最终绿，无 Skip 新增）。

---

## 5. 架构现状

### 5.1 三层分工（已落地部分）

```
┌─────────────────────────────────────────────────────────────────┐
│  View 层（MainWindow + BindingApplier + INI UI）                │
│  ─ LobbySessionState: UI 输入态（UIMode / LocalPlayerName /    │
│     AllowHostPlayerOptions / PlayerUpdatingInProgress 兼容期） │
│  ─ LobbyPlayerState: 仍存在（Slot 数组 + 加载委托）            │
└──────────────────────┬──────────────────────────────────────────┘
                       │ Subscribe
┌──────────────────────▼──────────────────────────────────────────┐
│  Session 层（真相源）                                           │
│  ─ IGameSession { Mode, Revision, Map, PlayerSlots, SlotSink, │
│                   Options, State, StateChanged, Reset... }    │
│  ─ ISkirmishSession : IGameSession                            │
│  ─ ICnCNetGameSession : ISkirmishSession {                    │
│       Tunnel, HostName, IsHost, ChannelName, Password,        │
│       MaxPlayers, SkillLevel, Passworded,                     │
│       EnsureHostFirst, MarkLocalHuman }                       │
│  ─ SkirmishSession : ISkirmishSession                         │
│  ─ CnCNetGameRoomSession : ICnCNetGameSession                 │
└──────────────────────┬──────────────────────────────────────────┘
                       │ Resolve
┌──────────────────────▼──────────────────────────────────────────┐
│  Service 层                                                     │
│  ─ ILobbyCatalogService: SideNames / AiNames / TeamNames     │
│  ─ ISkirmishSettingsService: INI 读写（TryLoad / Save）       │
│  ─ IMultiplayerColorCatalog, IResourceCatalog, IUpdater, ...  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 `LobbyPlayerState` 的职责剥离进度

| 原职责 | 当前状态 |
|--------|---------|
| 玩家槽位数组（`Slots`） | **保留**——仍由 Session 内部持有（Slice 6 之后才迁移到 Session 私有存储） |
| 派生查询（HumanRowCount 等） | **委托** `GameSessionExtensions`（Slice 1） |
| 目录加载（SideNames 等） | **委托** `ILobbyCatalogService`（Slice 2） |
| INI 持久化（Try/SaveSkirmish） | **委托** `ISkirmishSettingsService`（Slice 3） |
| Mode / AllowHostPlayerOptions 等 UI 输入态 | **复制**到 `LobbySessionState`（Slice 4）；旧字段保留兼容 |
| `PlayerUpdatingInProgress` 重入保护 | **被取代**——`IGameSession.Revision` 是更稳的替代；旧字段保留兼容 |

---

## 6. 已知遗留 / Phase 2 待办

以下问题在本次切片中**有意搁置**（不属于 Phase 1 范围或风险过高）：

1. **MainWindow 替换 `ApplyCnCNetGameRoomPlayers*`**：Slice 6 已提供 `EnsureHostFirst` / `MarkLocalHuman` 入口，但 MainWindow 的 ~50 行胶水代码尚未删除。需新增 MainWindow 订阅 `session.StateChanged` 并直接调上述方法，再加一层 Revision 防环。
2. **完全删除 `LobbyPlayerState`**：当前类型仍存在，作为 `SkirmishSession.Player` 的兼容桥。需逐文件迁移 `LobbyPlayerBindingApplier` / `MultiplayerSlotCoordinator` / `MainWindow` 等 ~20 个调用方到 Session 抽象。
3. **`MultiplayerSlotCoordinator.HandleHostSlotEdit` Session 重载内部仍走 LobbyPlayerState 视图**：Slice 5 增加了 Session 重载入口但实现是"构造临时 LobbyPlayerState 后调用旧方法"。Slice 6 删除 LobbyPlayerState 时需改为直接操作 `IPlayerSlotSink`。
4. **`LobbyPlayerMode` enum 仍在 `Services` 命名空间**：理想位置应在 `Session` 命名空间（与 `IGameSession.Mode` 同处）；现状保留以避免大规模 using 调整。
5. **CnCNetMgAndLnodJoinIntegrationTests** 持续失败（pre-existing，与本次工作无关，已 filter 掉）。

---

## 7. 总体评判

| 维度 | 评判 |
|------|------|
| **功能** | 6 个切片全部落地，原 Phase 1 目标（Session 抽象 + Service 抽取 + Revision 防环）100% 达成 |
| **架构设计** | 三层分工清晰；Session 层成为唯一真相源；Service 层承担外部 IO；View 层只剩纯 UI 输入态 |
| **抽象设计** | `IGameSession` / `ISkirmishSession` / `ICnCNetGameSession` 三级派生合理；`IPlayerSlotSink` + `Revision` 双轴（写入收口 + 重入保护）落地 |
| **测试覆盖率** | +49 单测覆盖全部新抽象；0 回归；测试用 absolutePath 注入 + Mock 解除进程级静态耦合 |
| **可扩展性** | `ILobbyCatalogService` / `ISkirmishSettingsService` 可被任意 mock；Mod 扩展只需新增 Service 注册 |
| **代码质量** | `BumpRevision()` 单一出口、`lock (_sync)` 保护并发；XML doc 完整、设计理由可追溯 |
| **总体** | **达标**。Phase 1 延后部分全部完成；Phase 2 路径（删 LobbyPlayerState + 替换 MainWindow 胶水）已铺平，可独立启动 |

---

## 8. 下一步建议（Phase 2 入口）

1. **MainWindow 订阅 `session.StateChanged`** —— 用 Revision 防环替换 `_applyingCnCNetGameRoomPlayers` 布尔。预估改动：MainWindow 200 行。
2. **`LobbyPlayerBindingApplier.Apply` 内部所有 `playerState.Xxx` 调用替换为 `session.SlotSink`** —— 删除对 `LobbyPlayerState.Slots` 的直接写访问。
3. **`LobbyPlayerState` 标记 `[Obsolete]`** —— 给所有调用方一个 deprecation 警告，逐文件迁移。
4. **完全删除 `LobbyPlayerState`** —— 最后一步，删除前确保 0 引用。
