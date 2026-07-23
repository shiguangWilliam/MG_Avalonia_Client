# 分层架构落地进度报告（暂停点）

> **日期**：2026-07-20
> **状态**：Step 1 完成并验证；Step 2/3 **主动暂停**，交由你设计下一步
> **基线对比**：558 → **596 通过** / 0 新增失败 / 3 跳过（已排除预存集成失败 `CnCNetMgAndLnodJoinIntegrationTests`）

---

## 1. 一句话现状

总纲 [`layered-architecture.md`](layered-architecture.md) 已定稿；**Session 写入收口（IPlayerSlotSink）+ 双通道抽象（IUIAction / ActionKind / CmdResult / IServiceHub）已落地且测试全绿**。  
**LobbyPlayerState 降为投影、MainWindow 删胶水**尚未动手——评估后发现破坏面过大（~20 文件依赖 `LobbyPlayerState`），需要你拍板策略后再继续。

---

## 2. 已完成

### 2.1 文档

| 文档 | 作用 |
|------|------|
| [`docs/design/layered-architecture.md`](layered-architecture.md) | **总纲**：View / Session / Service 三层 + 双通道（State/Command）+ 双事件流（StateChanged/CmdResult） |
| [`docs/design/phase1-player-state-completion.md`](phase1-player-state-completion.md) | Phase 1 延后部分详细设计；已加总纲参照链接 |

### 2.2 Step 1 代码（已合入工作树，未 commit）

| 文件 | 内容 |
|------|------|
| `Session/IPlayerSlotSink.cs` | 写入收口接口 + `SlotFieldUpdate`（含静默模式） |
| `Session/LobbyPlayerSlotSink.cs` | 默认实现；越界/空 update 不触发 onChanged |
| `Session/IGameSession.cs` | 新增 `SlotSink` + `ResetSlotsForMap` |
| `Session/SkirmishSession.cs` | 实现 Sink（指向 `Player.Slots`）；`ResetSlotsForMap` |
| `CnCNet/CnCNetGameRoomSession.cs` | 实现 Sink（指向 `_playerSlots`）；`ResetSlotsForMap`（仅清 AI） |
| `IniUi/Actions/IUIAction.cs` | `IUIAction` + `ActionKind` + `CmdResult` |
| `IniUi/Actions/UIActionExecution.cs` | `UIActionContext` struct（轻量派发上下文） |
| `IniUi/Actions/UiActionContext.cs` | **恢复/补全**旧抽象基类（给 `UiAction<T>` / `LobbyAction` 用） |
| `IniUi/Actions/IniActionCatalogUIExtensions.cs` | `RegisterState` / `RegisterCommand` 扩展 |
| `Services/IServiceHub.cs` | `IServiceHub` + `DefaultServiceHub` |
| `IniUi/Actions/Lobby/LobbyAction.cs` | 补 `WindowName` / `Root` / `Behaviors`（修预存编译断点） |

### 2.3 Step 1 单测（38 新增，全绿）

| 测试类 | 覆盖 |
|--------|------|
| `LobbyPlayerSlotSinkTests` | Overwrite/Write/Clear/CopyFrom、静默、越界、CnCNet 字段 |
| `UIActionPrimitivesTests` / `UIActionContextTests` / `UIActionDispatchTests` | CmdResult / ActionKind / UIActionContext / IUIAction |
| `DefaultServiceHubTests` | Resolve / TryGet（串行 collection） |
| `SessionSinkIntegrationTests` | SkirmishSession.Sink ↔ StateChanged |

### 2.4 测试结果

```
已通过! - 失败: 0，通过: 596，已跳过: 3，总计: 599
（过滤预存失败 CnCNetMgAndLnodJoinIntegrationTests）
```

---

## 3. 刻意未做（暂停原因）

### 3.1 Step 2：`LobbyPlayerState` 降为投影

**原计划**：`Slots` 改为 `Func<IReadOnlyList<IPlayerSlot>>` 投影，删除独立存储与写入方法。

**暂停原因**：
- `LobbyPlayerState` 被 **~20 个生产/测试文件**直接引用（BindingApplier、MultiplayerSlotCoordinator、SpawnWriter、MainWindow、LaunchValidator 等）
- `Slots` 类型是 `LobbyPlayerSlot[]`，大量代码直接 index 写字段、做 `Clone()`、传给 `MapStartLocationRules`
- 一次改成投影 = 同步改全部写入路径到 Sink，回归面过大，不宜在未定策略时硬推

### 3.2 Step 3：删 `MainWindow.ApplyCnCNetGameRoomPlayers*`

**原计划**：删 `_applyingCnCNetGameRoomPlayers` + 三层 Apply 方法，改订阅 `room.StateChanged`。

**暂停原因**：
- BindingApplier 仍读 `_lobbySession.PlayerState.Slots`（Layer A）
- CnCNet 真相在 `CnCNetGameRoomSession._playerSlots`（Layer B）
- **若不先统一 A/B，删胶水只会换地方拷贝**，不算真正解耦

---

## 4. 当前状态模型（仍分裂）

```
Layer A: LobbyPlayerState.Slots          ← UI / Spawn / Coordinator 仍写这里
Layer B: CnCNetGameRoomSession._playerSlots ← Phase1 已升为 Session 真相源 + Sink
Layer C: CnCNetGameRoomPlayer (DTO)      ← 瞬时，PlayerOptionsCodec 编解码

SkirmishSession.SlotSink → 指向 Player.Slots（A）     ✅ 同源
CnCNetGameRoomSession.SlotSink → 指向 _playerSlots（B） ✅ 同源
但 A 与 B 在 CnCNet 路径上仍是两套存储，靠 MainWindow 胶水同步
```

---

## 5. 已锁定的架构决策（不必重议）

| # | 决策 | 出处 |
|---|------|------|
| 1 | Service 双向：读 Session→发外部 / 收外部→写 Session | 总纲 §1 |
| 2 | 双通道：State → SlotSink；Command → Service（解读 A，不绕过 Session） | 总纲 §2 |
| 3 | UI 树是资产，不是真相源 | 总纲 §1 |
| 4 | 粗粒度 StateChanged + 静默 Sink；细粒度 SlotFieldChanged 暂不做 | 总纲 §3 |
| 5 | 用 `IUIAction` + `ActionKind`，不用继承基类拆 State/Cmd | 总纲 §2.2 |
| 6 | 多 Mod 启动不在本分支 | 用户明确 |

---

## 6. 待你设计的关键岔路口

### 6.1 LobbyPlayerState 怎么办？

| 选项 | 做法 | 风险 | 收益 |
|------|------|------|------|
| **A. 硬投影** | Slots 改 provider；所有写走 Sink；删写入方法 | 🔴 高（~20 文件） | 真正单真相源 |
| **B. Attach 借用** | 加 `AttachTo(LobbyPlayerSlot[])`；CnCNet 模式下指向 Session 数组；Skirmish 仍自持 | 🟡 中 | 不改大量调用签名，但语义变「可切换 backing」 |
| **C. 渐进只读** | 保留独立存储；新增「只允许 Sink 写」约定 + Obsolete 警告；MainWindow 胶水改为 StateChanged→单向拷贝 A←B | 🟢 低 | 立刻能删重入保护；真正投影延后 |
| **D. 拆类** | 目录缓存/Mode/LocalName 留 LobbyPlayerState；Slots 完全进 Session；BindingApplier 改吃 `IGameSession` | 🟡 中高 | 分层最干净，但 Applier 签名全改 |

### 6.2 MainWindow 胶水何时删？

| 选项 | 前置条件 |
|------|----------|
| 等 6.1 A/B/D 完成后再删 | 胶水可真正消失 |
| 用 6.1 C 先删重入保护、保留单向同步 | 可立刻缩 MainWindow，但 A/B 仍拷贝 |

### 6.3 `LobbyPlayerBindingApplier` 目标依赖

| 选项 | 依赖 | 说明 |
|------|------|------|
| 继续吃 `LobbyPlayerState` | 兼容现状 | 与「Session 真相源」冲突 |
| 改吃 `IGameSession` + 目录侧车 | 对齐总纲 | 改动面大，建议与 6.1 D 一起 |

### 6.4 BuiltinIniActions 是否立刻标 ActionKind？

现有 22 个 Builtin 仍是旧 `Register(name, handler)`，未分 State/Command。  
可后续机械标注，**不阻塞** 6.1/6.2。

---

## 7. 建议你设计时关注的约束

1. **不要破坏** `LobbyPlayerBindingApplier` 的 `PlayerUpdatingInProgress` 防环机制——任何投影方案都必须保留。
2. **CnCNet** 的 `_players` DTO 已是瞬时；不要再引入第三套存储。
3. **Skirmish** 路径 `SkirmishSession.SlotSink` 已与 `Player.Slots` 同源——Skirmish 侧压力小；真正难的是 **CnCNet 双存储**。
4. Step 1 的 Sink / IUIAction **可先用**（新代码写 SlotSink；旧代码暂不动）——总纲抽象已就位。

---

## 8. 工作树变更清单（便于你 review）

**新增**
- `docs/design/layered-architecture.md`
- `ClientAvalonia/Session/IPlayerSlotSink.cs`
- `ClientAvalonia/Session/LobbyPlayerSlotSink.cs`
- `ClientAvalonia/IniUi/Actions/IUIAction.cs`
- `ClientAvalonia/IniUi/Actions/UIActionExecution.cs`
- `ClientAvalonia/IniUi/Actions/IniActionCatalogUIExtensions.cs`
- `ClientAvalonia/Services/IServiceHub.cs`
- `ClientAvalonia.Tests/Session/LobbyPlayerSlotSinkTests.cs`
- `ClientAvalonia.Tests/Session/DefaultServiceHubTests.cs`
- `ClientAvalonia.Tests/IniUi/UIActionPrimitivesTests.cs`

**修改**
- `docs/design/phase1-player-state-completion.md`（加总纲参照）
- `ClientAvalonia/Session/IGameSession.cs`
- `ClientAvalonia/Session/SkirmishSession.cs`
- `ClientAvalonia/CnCNet/CnCNetGameRoomSession.cs`
- `ClientAvalonia/IniUi/Actions/UiActionContext.cs`（恢复抽象基类）
- `ClientAvalonia/IniUi/Actions/Lobby/LobbyAction.cs`（补字段）
- `ClientAvalonia/IniUi/Actions/Lobby/ChangeMapAction.cs`（null-safe WindowName）

**未改**
- `MainWindow.axaml.cs` 胶水（`ApplyCnCNetGameRoomPlayers*` / `_applyingCnCNetGameRoomPlayers`）
- `LobbyPlayerState.Slots` 存储模型
- `LobbyPlayerBindingApplier` / `MultiplayerSlotCoordinator` 写入路径

---

## 9. 请你拍板后可继续的方向

请你设计时至少回答：

1. **6.1 选 A / B / C / D（或混合）？**
2. **MainWindow 胶水是「立刻用 C 过渡」还是「等投影完成再删」？**
3. **BindingApplier 是否本轮就改吃 `IGameSession`？**

你定方向后，我按你的方案继续落地；本回合不再推进 Step 2/3 代码。

---

## 9. 用户最终决策（2026-07-20 15:06）

经讨论，用户确认采用**全量大重构**——纵向切片推进，逐步删除 `LobbyPlayerState`。本节记录决策点：

### 9.1 防环机制：原子化脏读 Tag（取代 `PlayerUpdatingInProgress`）

```csharp
public interface IGameSession
{
    /// <summary>
    /// 状态版本号——每次 Sink 写入自增。
    /// View 在 SyncUi 时读取当前 Revision 作为"已处理基准"。
    /// 若 SelectionChanged 触发时 Rev <= 已处理基准 → 跳过写回（防环）。
    /// </summary>
    long Revision { get; }
}
```

BindingApplier / Coordinator 内部用本地变量 `consumedRev` 比对，取代散落的 `PlayerUpdatingInProgress` 布尔。

### 9.2 `Mode` 由 Session 派生

`LobbyPlayerMode.Skirmish / Multiplayer` 不再单独存储，由具体 Session 类型派生：

```csharp
public interface IGameSession
{
    LobbyPlayerMode Mode { get; }  // SkirmishSession → Skirmish; CnCNetGameRoomSession → Multiplayer
}
```

### 9.3 BindingApplier 改吃**具体派生 Session 接口**（不是 IGameSession 基类）

按用户明确：「不是根 Session，而是对应的具体派生 Session」——BindingApplier 改吃 `ISkirmishSession` + `ILobbyCatalogService`，而非泛化的 `IGameSession`。这样既得到类型安全（CnCNet 特有字段经 `ICnCNetGameSession` 子接口访问），又不污染基接口。

### 9.4 `EnsureHostAsFirstHuman` / `MarkLocalHuman` 进具体 Session 接口

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    void EnsureHostFirst(string hostName, string localNick);
    void MarkLocalHuman(string localNick);
    // ... 已有：BroadcastPlayerOptions / RequestLocalPlayerOptions / ResetSlotsForMap
}
```

### 9.5 执行策略：纵向切片

按职责拆 6 片，每片独立绿测：

| Slice | 内容 | 删 LobbyPlayerState 哪部分 |
|-------|------|---------------------------|
| **1** | B 类派生属性 → `GameSessionExtensions` 扩展方法 | `HumanRowCount`/`AiRowCount`/`OccupiedSlotCount`/`GetRowKind`/`FirstEmptySlotIndex` 改委托扩展方法 |
| **2** | C 类目录 → `ILobbyCatalogService` | `SideNames`/`SideEntries`/`AiNames`/`TeamNames`/`LoadCatalogs` 委托给 Service |
| **3** | D 类持久化 → `ISkirmishSettingsService` | `TryLoadSkirmishSettings`/`SaveSkirmishSettings`/`TryParsePlayerLine`/`FormatPlayerLine` 委托给 Service |
| **4** | E 类 UI 输入态 + Mode 派生 + Revision | `Mode`/`LocalPlayerName`/`HostPlayerName`/`AllowHostPlayerOptions` → LobbySessionState；`PlayerUpdatingInProgress` → Revision 比对 |
| **5** | BindingApplier / Coordinator / StatusApplier 改造 | 改吃 `ISkirmishSession` + `ILobbyCatalogService`；脏读 Revision 防环 |
| **6** | CnCNet 房主操作 + MainWindow 删胶水 + 删 LobbyPlayerState | `EnsureHostFirst`/`MarkLocalHuman` 进 Session；删 `_applyingCnCNetGameRoomPlayers`；**整个删除 `LobbyPlayerState` 类** |

### 9.6 失败兜底策略（用户原话）

> 每一个分片修改完，运行一次单元测试，确认是否稳定可运行。如果出现 bug，排查是否因为和其他部分关联，如果不关联，则 debug 修复。如果与其他部分相关，搁置，待修复之后再测试。

每个 Slice 完成后：
1. **跑全测**（过滤预存失败 `CnCNetMgAndLnodJoinIntegrationTests`）
2. 若失败且与本次 Slice 直接相关 → 立即 debug 修复
3. 若失败由其他 Slice 引起（未完成的部分）→ 标记搁置，记入文档，继续后续 Slice
4. 所有 Slice 完成后回头收口搁置项

### 9.7 当前架构是否足够明确？

**评判：是**。已确认：
- ✅ 防环用原子化脏读 Tag（§9.1）
- ✅ Mode 派生自 Session 类型（§9.2）
- ✅ BindingApplier 吃具体派生 Session（§9.3）
- ✅ 房主操作进具体 Session 接口（§9.4）
- ✅ 6 片纵向切片 + 单片绿测（§9.5）
- ✅ 失败兜底三步策略（§9.6）

**总纲 [`layered-architecture.md`](layered-architecture.md) + 本节决策共同构成完整执行蓝图**，不再有歧义点。下一步直接进入 Slice 1 实现。
