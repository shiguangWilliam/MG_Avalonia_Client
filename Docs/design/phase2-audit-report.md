# Phase 2 盘点报告 — 重构进度、回收清单与路线图

**日期**：2026-07-20  
**性质**：只读盘点，**不动代码**  
**前置**：[phase1-final-report.md](phase1-final-report.md) · [layered-architecture.md](layered-architecture.md)  
**用户决策**：回收时机 = 最后一次性删除（本报告只标注可回收项，不执行删除）

---

## 0. 一句话结论

**尚未接近尾声。** Phase 1 完成的是「抽象铺底」（接口 / Service / Revision / Host 入口），生产路径仍几乎全部黏在 `LobbyPlayerState` 上。整体进度粗估约 **35–40%**；删掉 `LobbyPlayerState` 并收回 MainWindow 胶水之前，不能算收尾。

---

## 1. 进度仪表盘

| 指标 | 现状 | 终极目标 | 差距 |
|------|------|---------|------|
| 测试（过滤已知坏集成测） | 645 通过 / 0 失败 | 保持绿 | — |
| `LobbyPlayerState` 引用文件（生产） | **23** | 0 | 23 |
| `LobbyPlayerState` 引用文件（含测试） | **27** | 0 | 27 |
| `MainWindow.axaml.cs` 行数 | **2076** | ~200（壳） | ~1870 |
| `_applyingCnCNetGameRoomPlayers` | 仍在用 | 删除（改 `Revision`） | 未切 |
| `ApplyCnCNetGameRoomPlayers*` 胶水 | ~100 行（1802–1899） | 删除 | 未切 |
| `LobbySessionState` 新 UI 字段生产引用 | **0** | 替代旧字段 | 未切 |
| Session 新 Host API 生产引用 | **0**（仅定义+单测） | 替代旧 API | 未切 |

### 阶段完成度粗估

```
████████░░░░░░░░░░░░  Phase 1 抽象铺底     ~40%  ← 已完成
░░░░░░░░░░░░░░░░░░░░  Phase 2 生产迁移     ~0%   ← 本报告对象
░░░░░░░░░░░░░░░░░░░░  Phase 3 删除+回收    ~0%
────────────────────────────────
整体重构进度约 35–40%
```

Phase 1 报告里写「路径已铺平」是对的；**不等于迁移已开始**。

---

## 2. 关键诚实发现（比 Phase 1 报告更严）

### 2.1 Slice 4「迁到 LobbySessionState」实际是复制，不是切换

`LobbySessionState` 上新增了：

- `UIMode`
- `AllowHostPlayerOptions`
- `LocalPlayerName`
- `HostPlayerName`
- `PlayerUpdatingInProgress`

**生产代码对这些字段的引用数 = 0。**

全部读写仍走 `LobbyPlayerState` 同名字段。结果：双份真相，新字段是死代码。

### 2.2 Slice 6 新 Session API 生产侧未接通

| API | 所在 | 生产调用 |
|-----|------|---------|
| `ICnCNetGameSession.EnsureHostFirst` | Session | **0**（仅单测） |
| `ICnCNetGameSession.MarkLocalHuman` | Session | **0**（仅单测） |
| `LobbyPlayerState.EnsureHostAsFirstHuman` | 旧 | MainWindow ×2 |
| `LobbyPlayerState.MarkLocalHuman` | 旧 | MainWindow ×1 |

新旧 API **并存且语义略有不同**（旧版带 `hostName`/`localNick`；新版 `EnsureHostFirst` 带 `maxPlayers`）。不是简单 rename。

### 2.3 Slice 5 Session 重载是「门面」，不是真实迁移

- `LobbyPlayerBindingApplier.ApplyWithSession` → 内部仍调 `Apply(..., session.Player, ...)`
- `MultiplayerSlotCoordinator.HandleHostSlotEdit(ICnCNetGameSession, ...)` → 临时 `new LobbyPlayerState` 再调旧方法

新入口可测，但**没有减少对 `LobbyPlayerState` 的依赖面**。

---

## 3. `LobbyPlayerState` 成员分类（回收视角）

`LobbyPlayerState.cs` ≈ **382 行**，公开成员如下。

### A. 已可标弃用（有替代物；生产仍在用旧路径）

| 成员 | 替代 | 生产仍用？ | 回收条件 |
|------|------|-----------|---------|
| `Mode` | `IGameSession.Mode` | 是（BindingApplier / SlotUiRules / Coordinator / StatusApplier / MainWindow） | 全部切到 Session 后 |
| `PlayerUpdatingInProgress` | `IGameSession.Revision` | 是（BindingApplier 读写） | BindingApplier 改 Revision 后 |
| `AllowHostPlayerOptions` | `LobbySessionState.AllowHostPlayerOptions`（已复制未切） | 是 | 切到 LobbySessionState 后 |
| `LocalPlayerName` | `LobbySessionState` / `IGameEnvironment.PlayerName` | 是 | 同上 |
| `HostPlayerName` | `LobbySessionState` / `ICnCNetGameSession.HostName` | 是 | 同上 |
| `SideNames` / `SideEntries` / `AiNames` / `TeamNames` | `ILobbyCatalogService`（LoadCatalogs 已委托，字段仍缓存） | 是 | 调用方改读 Catalog 后 |
| `LoadCatalogs` | `ILobbyCatalogService.Reload` | 是（MainWindow） | 同上 |
| `TryLoadSkirmishSettings` / `SaveSkirmishSettings` | `ISkirmishSettingsService`（已委托） | 是（MainWindow） | 调用方改直接 Resolve Service 后 |
| `HumanRowCount` 等派生查询 | `GameSessionExtensions`（已委托） | 是（经 LobbyPlayerState 属性） | 调用方改扩展方法后 |
| `EnsureHostAsFirstHuman` / `MarkLocalHuman` | `ICnCNetGameSession.*` | 是（MainWindow） | 切到 Session API 后 |

### B. 仍属核心、必须迁到 Session 私有存储（不能「弃用后直接删」）

| 成员 | 说明 |
|------|------|
| `Slots` | 真相数组；SkirmishSession / CnCNetGameRoomSession 仍间接依赖 |
| `ClearSlots` / `RepopulateRows` / `RebuildAiRowsFromUi` | 槽位布局写操作 → 应收口到 `IPlayerSlotSink` 或 Session 方法 |
| `LoadDefaultSkirmishSlots` / `LoadDefaults` | 默认槽位填充 → 应收口到 `ResetSlotsForMap` / Session |
| `GetRowKind` / `FirstEmptySlotIndex` / `Occupied*` | 已有扩展方法，可删包装属性 |
| `TryParsePlayerLine` / `FormatPlayerLine` / `HouseHandicapFromAiLevel` | 静态工具；可迁到 `SkirmishSettingsService` 或独立 helpers |

### C. 命名空间级待回收

| 项 | 现状 | 建议 |
|----|------|------|
| `enum LobbyPlayerMode` | 在 `Services` | 迁到 `Session`（与 `IGameSession.Mode` 同处）；最后一步改 using |
| `LobbySessionState` 上的 5 个新 UI 字段 | **死代码**（0 引用） | 要么接通生产，要么在 Phase 3 连同旧字段一起删，避免长期双份 |

---

## 4. 生产引用热力图（按文件）

按 `LobbyPlayerState` 命中次数（生产，不含测试）：

| 文件 | 命中 | 角色 | 迁移难度 |
|------|------|------|---------|
| `LobbyPlayerBindingApplier.cs`（840 行） | 12 | UI↔槽位双向绑定；强依赖 Mode / PlayerUpdatingInProgress / LocalPlayerName | **高** |
| `MultiplayerSlotCoordinator.cs`（145 行） | 10 | Host/Joiner 槽位编辑；强依赖 Mode / AllowHost* / HostPlayerName | **高** |
| `LobbyPlayerSlotUiRules.cs`（183 行） | 10 | ConfigureForSkirmish/Multiplayer；写 Mode/AllowHost*/Names | **高** |
| `SkirmishSession.cs` | 7 | 兼容桥：`Player` 属性暴露 LobbyPlayerState | **中**（删类型前必须改） |
| `CnCNetMultiplayerSpawnWriter.cs` | 5 | 读 Slots 写 spawn | **中** |
| `MultiplayerSlotLayout.cs` | 4 | ApplyToState 写 Slots | **中** |
| `SkirmishSpawnWriter.cs` | 4 | 读 Slots | **中** |
| `GameDataBindingApplier.cs` | 3 | 读 Mode / AllowHostPlayerOptions | **中** |
| `LobbyPlayerStatusApplier.cs` | 3 | 读 Mode | **中** |
| `MainWindow.axaml.cs`（2076 行） | 2 字面 + **大量** `_lobbySession.PlayerState.*` | 胶水总枢纽 | **最高** |
| 其余（注释/文档式引用、接口说明） | 1–3 | 文档/类型参数 | 低 |

**测试侧**另有：`GameSessionExtensionsTests`、`GameSessionModeAndRevisionTests`、`DefaultAiSlotPolicyTests`、`ActionExecutorTests` 等。

---

## 5. MainWindow 胶水盘点

### 5.1 目标块：`ApplyCnCNetGameRoomPlayers*`

| 符号 | 约行 | 职责 |
|------|------|------|
| `_applyingCnCNetGameRoomPlayers` | 1 字段 | 布尔重入保护（应对标 `Revision`） |
| `RefreshCnCNetGameRoomUiFromSession` | ~25 | 读 Session→UI；调 Core + chat |
| `ApplyCnCNetGameRoomPlayers` | ~20 | 带 updateStatus 的入口 |
| `ApplyCnCNetGameRoomPlayersCore` | ~50 | ConfigureForMultiplayer → ApplyToState → EnsureHost* → BindingApplier → StatusApplier → Toolbar |

调用点：约 **1008 / 1325 / 1633** 及事件刷新路径。

### 5.2 与 Session 的错位

Core 仍操作：

1. `_lobbySession.PlayerState`（旧真相）
2. `LobbyPlayerState.EnsureHostAsFirstHuman` / `MarkLocalHuman`（旧 API）
3. `LobbyPlayerBindingApplier.Apply(..., PlayerState, ...)`（旧重载）
4. 布尔 `_applyingCnCNetGameRoomPlayers`（旧防环）

而 Phase 1 已备好、**未被 Core 使用**的：

1. `CnCNetGameRoomSession.PlayerSlots` / `SlotSink` / `Revision` / `StateChanged`
2. `EnsureHostFirst` / `MarkLocalHuman`（Session）
3. `ApplyWithSession`（BindingApplier）

### 5.3 行数目标（现实修正）

| 目标 | 评估 |
|------|------|
| MainWindow → ~200 行（早期 9 步规划） | **本 Phase 达不到**；属更大 MainWindow 拆分 |
| 仅替换玩家同步胶水（本 Phase 合理目标） | MainWindow 减少 ~80–120 行；逻辑进 Session 订阅 |
| 删除 `LobbyPlayerState` | 依赖上表 23 文件全部迁完；属 Phase 3 |

---

## 6. 重复 / 死代码清单（末尾一次性回收候选）

按用户决策「最后一次性删除」，下列项应在**迁移完成且引用为 0** 后成批删除：

### 6.1 类级

1. **`LobbyPlayerState` 整个类型**（382 行）— 终极目标  
2. **`LobbyPlayerState` 上已委托但仍缓存的目录字段** — 若 Catalog 成为唯一源，可先删字段再删类  

### 6.2 成员级（类删之前可先 Obsolete）

| 可回收成员 | 前提 |
|-----------|------|
| `Mode` / `PlayerUpdatingInProgress` / `AllowHost*` / `*PlayerName` | 生产改读 Session / LobbySessionState |
| `EnsureHostAsFirstHuman` / `MarkLocalHuman`（LobbyPlayerState） | MainWindow 改调 Session |
| `LoadCatalogs` / `TryLoad*` / `Save*` 包装 | 调用方直接 Resolve Service |
| 派生属性包装（HumanRowCount 等） | 调用方用扩展方法 |
| `LobbySessionState` 上若始终未接通的副本字段 | 若决定「不迁 View 态、直接删旧字段」则副本一并删 |

### 6.3 API 重复（必须二选一，避免长期双轨）

| 旧 | 新 | 建议保留 |
|----|----|---------|
| `LobbyPlayerState.EnsureHostAsFirstHuman` | `ICnCNetGameSession.EnsureHostFirst` | **新**（需对齐参数语义） |
| `LobbyPlayerState.MarkLocalHuman` | `ICnCNetGameSession.MarkLocalHuman` | **新** |
| `_applyingCnCNetGameRoomPlayers` | `session.Revision` | **新** |
| `playerState.Mode` | `session.Mode` | **新** |
| `playerState.PlayerUpdatingInProgress` | `Revision` 脏读 | **新** |

### 6.4 假迁移入口（Phase 3 前应收紧实现）

- `ApplyWithSession` 若仍只转发 `session.Player`，在删 `LobbyPlayerState` 前必须改成真吃 `PlayerSlots`+`SlotSink`+`ILobbyCatalogService`，否则删类会连带炸掉。

---

## 7. 推荐 Phase 2 切片（仍不动代码，仅路线图）

按风险递增；每片结束后全测；**删除集中在 Phase 3 末尾**。

| 切片 | 内容 | 风险 | 预估触及文件 | 退出标准 |
|------|------|------|-------------|---------|
| **P2-1** | 接通 `LobbySessionState` UI 字段：`LobbyPlayerSlotUiRules` / MainWindow 写 UI 态改写到 SessionState；`Mode` 读改为 `session.Mode` | 中 | ~5 | LobbyPlayerState.Mode 生产引用 ↓ |
| **P2-2** | BindingApplier：`PlayerUpdatingInProgress` → Revision 脏读；写路径走 SlotSink | 高 | 1–2 + 测试 | Applier 不再读写 PlayerUpdatingInProgress |
| **P2-3** | Coordinator + SlotUiRules + StatusApplier + GameDataBindingApplier 改吃 Session / Catalog | 高 | ~6 | 这些文件 0 直接依赖 Mode/AllowHost* |
| **P2-4** | SpawnWriter / LaunchValidator / MultiplayerSlotLayout 改吃 `IReadOnlyList<IPlayerSlot>` | 中 | ~4 | 同上 |
| **P2-5** | MainWindow：`ApplyCnCNetGameRoomPlayers*` → 订阅 `StateChanged` + Session Host API + Revision | 最高 | 1（大） | 胶水方法删除；`_applying*` 删除 |
| **P2-6** | SkirmishSession 去掉公开 `Player`；内部私有槽位数组 | 中 | 若干 | `SkirmishSession.Player` 消失 |

**Phase 3（回收批次，用户已定「最后删」）**

1. `[Obsolete]` 一轮警告（可选，便于扫漏）  
2. 确认 `LobbyPlayerState` 引用 = 0  
3. 删除 `LobbyPlayerState.cs` + 旧 Host API + 布尔防环 + 死副本字段  
4. `LobbyPlayerMode` 迁命名空间  
5. 全测 + 最终报告  

---

## 8. 「是否接近尾声」判定标准

满足以下**全部**才算接近尾声：

- [ ] 生产 `LobbyPlayerState` 引用 = 0  
- [ ] `ApplyCnCNetGameRoomPlayers*` / `_applyingCnCNetGameRoomPlayers` 已删  
- [ ] BindingApplier / Coordinator 只依赖 Session + Catalog + SlotSink  
- [ ] `LobbySessionState` 与 `LobbyPlayerState` 无双份 UI 态  
- [ ] Session Host API 为唯一 Host/Local 标记路径  
- [ ] 全测绿；MainWindow 明显变薄（不必已到 200 行）  

**当前：0/6。** 故结论不变：**未接近尾声。**

---

## 9. 风险与注意

1. **双轨状态最危险**：新旧字段并存且只写一半 → 难查的同步 bug。P2-1 应尽快「接通或删副本」，不要长期双写。  
2. **Host API 语义差**：`EnsureHostFirst(local, maxPlayers)` vs `EnsureHostAsFirstHuman(host, local)` — 切换前先对齐行为，用现有单测 + 补场景。  
3. **MainWindow 仍是上帝对象**：玩家状态统一 ≠ MainWindow 拆完；本 Phase 只解玩家真相，不要夹带 Launch/Overlay/Nav 大拆。  
4. **回收纪律**：按用户决策，弃用项先标清单、迁移完再成批删；避免中途半删导致编译绿但运行态残缺。

---

## 10. 建议的下一步（仍先确认再写码）

可选：

1. **按 §7 从 P2-1 开工**（接通 LobbySessionState / 消 Mode 双轨）  
2. **先只做 P2-5 设计草案**（MainWindow 订阅 StateChanged 的伪代码 + Revision 防环时序图），确认后再动  
3. **调整路线**（例如先 SpawnWriter 等低扇出文件，后 BindingApplier）  

本报告不包含代码改动。确认范围后即可进入 Phase 2 实现。
