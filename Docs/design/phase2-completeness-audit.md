# 架构完备性评估 — Phase 2 动工前

**日期**：2026-07-21  
**结论**：**不完备**。继续大刀阔斧改胶水会卡在 5 处已识别的接口/语义缺口上，必须先补；这些都是 5–60 分钟级的小补丁，不是设计重构。

---

## 0. 一句话结论

**抽象方向和分层是对的，但「最后一公里」的 5 个接口缺口没补**——直接动 MainWindow 会写第二次胶水（用 Session API 但仍绕弯路），收尾时还得再改一轮。建议**先补 5 处缺口（半天内）**，再开工改胶水。

---

## 1. 完备性核查（按判定标准）

| 判定项 | 状态 | 备注 |
|--------|------|------|
| 三层分工定义 | ✅ | layered-architecture.md 明确 |
| `IGameSession` 基础抽象 | ✅ | Mode / Revision / PlayerSlots / SlotSink / Map / Options / State / StateChanged |
| 派生 Session 抽象 | ✅ | ISkirmishSession / ICnCNetGameSession |
| 槽位写入收口 | ✅ | IPlayerSlotSink + SlotFieldUpdate + Silent 模式 |
| 防环机制 | ✅ | Revision + Interlocked.Increment |
| Service 抽取 | ✅ | ILobbyCatalogService / ISkirmishSettingsService |
| Host 标记语义 | ⚠️ | EnsureHostFirst 语义与旧 EnsureHostAsFirstHuman 不一致（见 §2.1） |
| **CnCNet 玩家状态入口** | ❌ | ICnCNetGameSession 缺 `Players` / `Locked`（见 §2.2） |
| **网络回推入口** | ❌ | 缺 Session 层 `ApplyPlayersFromNetwork`（见 §2.3） |
| **Host 广播入口** | ❌ | `KickPlayer` / `UpdateHumanFromSlot` / `SyncPlayersFromLobby` 仍在具体类，未进接口（见 §2.4） |
| **UI 配置入口** | ❌ | `ConfigureForMultiplayer` / `ApplyToState` 仍吃 `LobbyPlayerState`，没切到 Session（见 §2.5） |
| 死代码回收 | ✅ 计划已定 | 用户决策「最后一次性删」，本评估不展开 |

**判定**：12 项中 5 项 ⚠️/❌ → **不完备**。

---

## 2. 五个接口/语义缺口（必须先补）

### 2.1 ❌ `EnsureHostFirst` 语义与生产路径不符

**现状**：

```csharp
// 新接口（Phase 1 Slice 6）
void EnsureHostFirst(string localPlayerName, int maxPlayers);

// 实现（CnCNetGameRoomSession.cs:207）
public void EnsureHostFirst(string localPlayerName, int maxPlayers)
{
    // 清空所有槽位 → slot[0] 写本地人
    lock (_sync) { /* ClearSlotLocked + slot[0].Name=localPlayerName */ }
    BumpRevision();
}
```

**生产实际做的事**（`LobbyPlayerState.EnsureHostAsFirstHuman`，MainWindow 1393 / 1870 调用）：

```
读现有 humans/ais → 找出 host → host 移到 [0] → 其余人类按原顺序后移 → ais 跟在后面 → RepopulateRows
```

**缺口**：新 API 是「清空 + 写一个」，旧 API 是「保留现有 + 重排序」。MainWindow 切过去会**丢掉已 join 的人类玩家和 AI 设置**——这是网络可见的行为回归。

**补法**：

```csharp
// ICnCNetGameSession 改成
void EnsureHostFirst(string hostName, string localNick);
```

或者保留两套：
- `EnsureHostFirst(localPlayerName, maxPlayers)` —— **房间初次创建时**用（清空 + 自己坐 [0]）
- `ReorderHostFirst(hostName, localNick)` —— **已有玩家时**用（保留 + 重排）

**风险**：低。改 1 个接口签名 + 1 个实现 + 6 个单测断言。

---

### 2.2 ❌ `ICnCNetGameSession` 缺 `Players` / `Locked` 只读视图

**现状**：MainWindow Core 依赖：

```csharp
IReadOnlyList<CnCNetGameRoomPlayer> entries = gameRoom?.Players ?? [];  // line 1859
bool locked = gameRoom?.Locked ?? false;                                  // line 1877
```

`Players` / `Locked` **只在具体类 `CnCNetGameRoomSession` 上**，没进 `ICnCNetGameSession`。

BindingApplier / LobbyBehaviors / LobbyUiHelper 也都直接拿 `cncnet.GameRoom?.Players`，**绕开 Session 抽象**。

**缺口**：MainWindow 切到 Session API 后，仍要 `(CnCNetGameRoomSession)session` 强转取这两个字段 → 等于没切干净。

**补法**：

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    // ... 已有 ...

    /// <summary>当前房间 PO DTO（按 CTCP 收到的顺序；host 在前）。只读视图。</summary>
    IReadOnlyList<CnCNetGameRoomPlayer> Players { get; }

    /// <summary>房间是否被房主锁定（GAME LOCKED 广播）。</summary>
    bool Locked { get; }
}
```

**风险**：低。CnCNetGameRoomSession 已经有 `Players` 字段（`_players`）和 Locked 概念；只是 expose。

---

### 2.3 ❌ 缺 Session 层的「网络回推」入口

**现状**：MainWindow Core 的关键流程是：

```
1. 读 gameRoom.Players（CTCP 收到的最新 PO DTO）
2. MultiplayerSlotLayout.ApplyToState(playerState, entries, localNick)  ← 把 DTO 写进 LobbyPlayerState.Slots
3. EnsureHostAsFirstHuman / MarkLocalHuman
4. LobbyPlayerBindingApplier.Apply(...)  ← UI 刷新
```

Step 2 是**网络 → 状态**的回推，目前写 `LobbyPlayerState`。新架构下应该写 Session 的 `SlotSink`。

**缺口**：`ICnCNetGameSession` 没有声明「把网络 DTO 应用到槽位」的方法。结果就是：MainWindow 即使切了，也得在 MainWindow 里手动转 DTO→SlotFieldUpdate，又写一遍胶水。

**补法**：

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    // ...

    /// <summary>
    /// 房间收到新 PO DTO 后调用：把 DTO 应用到 PlayerSlots（走 SlotSink，触发 StateChanged）。
    /// 内部处理 host 排序、本地人标记、广播抑制（防回环）。
    /// </summary>
    /// <param name="entries">CTCP 收到的 PO DTO 列表。</param>
    /// <param name="hostName">房主名。</param>
    /// <param name="localNick">本地玩家名。</param>
    void ApplyPlayersFromNetwork(IReadOnlyList<CnCNetGameRoomPlayer> entries, string hostName, string localNick);
}
```

实现里调 `MultiplayerSlotLayout.ApplyToSlots(_playerSlots, entries, localNick)` + 内部 ReorderHostFirst + MarkLocalHuman，一次完成 Step 1–3。

**风险**：中。需要新增 `MultiplayerSlotLayout.ApplyToSlots(IReadOnlyList<IPlayerSlot>, ...)` 重载（保留旧 `ApplyToState(LobbyPlayerState, ...)` 一段时间）。

---

### 2.4 ❌ Host 广播方法未进接口

**现状**：MainWindow 和 Coordinator 用到：

| 方法 | 所在 | 调用方 |
|------|------|-------|
| `KickPlayer(string)` | CnCNetGameRoomSession 具体 | Coordinator |
| `UpdateHumanFromSlot(LobbyPlayerSlot)` | CnCNetGameRoomSession 具体 | Coordinator |
| `SyncPlayersFromLobby(LobbyPlayerState, string)` | CnCNetGameRoomSession 具体 | Coordinator |

签名也带 `LobbyPlayerSlot` / `LobbyPlayerState`，是同一个问题。

**缺口**：MainWindow / Coordinator 切到 `ICnCNetGameSession` 后，这些方法不可达。

**补法**：

```csharp
public interface ICnCNetGameSession : ISkirmishSession
{
    // ...

    /// <summary>房主：从当前 PlayerSlots 重建 PO DTO 并广播（BO CTCP）。</summary>
    void BroadcastPlayerOptionsFromSlots();

    /// <summary>房主：根据名字找到玩家并更新其 side/color/team/start。</summary>
    void UpdateHuman(string playerName, in SlotFieldUpdate update);

    /// <summary>房主：把玩家踢出 IRC 频道。</summary>
    void KickPlayer(string playerName);
}
```

把 `UpdateHumanFromSlot(LobbyPlayerSlot)` 改成 `UpdateHuman(string, in SlotFieldUpdate)`——签名层面就脱离 LobbyPlayerSlot。

**风险**：中。涉及 `MultiplayerSlotCoordinator.HandleHostSlotEdit` 整体改造。

---

### 2.5 ❌ `ConfigureForMultiplayer` / `ApplyToState` 仍硬吃 `LobbyPlayerState`

**现状**：

```csharp
// LobbyPlayerSlotUiRules
public static void ConfigureForMultiplayer(LobbyPlayerState state, ...) { state.Mode = ...; state.AllowHost... }
public static void ConfigureForSkirmish(LobbyPlayerState state)        { state.Mode = ...; }

// MultiplayerSlotLayout
public static void ApplyToState(LobbyPlayerState state, IReadOnlyList<CnCNetGameRoomPlayer> entries, string localNick)
public static List<CnCNetGameRoomPlayer> BuildPoListFromState(LobbyPlayerState state, string hostName)
```

这些方法的全部副作用都是写 `LobbyPlayerState.Mode / AllowHostPlayerOptions / LocalPlayerName / HostPlayerName / Slots`。

**问题**：MainWindow 切 Session 后无法调这些方法（参数类型对不上）。

**补法**：

```csharp
// LobbyPlayerSlotUiRules
public static void ConfigureForMultiplayer(LobbySessionState ui, ICnCNetGameSession session, ...)
public static void ConfigureForSkirmish(LobbySessionState ui, ISkirmishSession session)

// MultiplayerSlotLayout
public static void ApplyToSlots(IReadOnlyList<IPlayerSlot> slots, IReadOnlyList<CnCNetGameRoomPlayer> entries, string localNick)
public static List<CnCNetGameRoomPlayer> BuildPoList(IReadOnlyList<IPlayerSlot> slots, string hostName, IReadOnlyList<string> aiNames)
```

旧重载保留为门面（构造一个临时 `LobbyPlayerState` 转发），等 Phase 3 一起删。

**风险**：低。纯机械改签名。

---

## 3. 缺口优先级与工时

| # | 缺口 | 工时 | 阻塞什么 |
|---|------|------|---------|
| 2.1 | `EnsureHostFirst` 语义对齐 | 30 min | MainWindow 切 Session Host API |
| 2.2 | `Players` / `Locked` 进接口 | 15 min | MainWindow Core 切 Session |
| 2.3 | `ApplyPlayersFromNetwork` 入口 | 1 h | MainWindow Step 2 替换 |
| 2.4 | Host 广播方法进接口 | 1 h | Coordinator 切 Session |
| 2.5 | UI Rules / Layout 改吃 Session | 1 h | 上述全部的依赖 |
| **合计** | | **~4 h** | Phase 2 全部切片 |

补完后 MainWindow 改造可以从 ~200 行减到 ~50 行「干净订阅」代码。

---

## 4. 还有几个小坑（不阻塞，但建议补改）

### 4.1 `LobbyPlayerSlotUiRules.ConfigureFor*` 写 UI 态到 PlayerState

这两个方法把 UI 态（Mode / AllowHost* / Names）写到 `LobbyPlayerState`，是 Slice 4 双份真相问题的源头之一。改造时（缺口 2.5）应直接写 `LobbySessionState`，否则切了接口也没消除双份真相。

### 4.2 `SyncPlayersFromLobby` 吃 `state.AiNames`

`CnCNetGameRoomSession.SyncPlayersFromLobby(LobbyPlayerState state, ...)` 内部用 `state.AiNames` 做 DTO 重建。改造后 AiNames 来源是 `ILobbyCatalogService`，要么 Session 持有 catalog 引用，要么方法签名加 `IReadOnlyList<string> aiNames` 参数。建议后者（少耦合）。

### 4.3 `LobbyPlayerBindingApplier` 写路径未收口到 SlotSink

`SyncFromUi` / 事件回调里直接 `playerState.Slots[i].Name = ...`。Slice 5 加了 `ApplyWithSession` 重载但只解决读路径。**改胶水时这一步要顺手做掉**，否则 SlotSink 收口有漏洞。

### 4.4 `_localNick` / `_connection` 等 CnCNetGameRoomSession 私有字段

`KickPlayer` / `SyncPlayersFromLobby` 等方法依赖 `_localNick`。新接口的 `UpdateHuman(playerName, ...)` 实现里也要用，签名要不要也加 `localNick`？建议不要——让 Session 内部持有 `_localNick`，外部只传业务参数。

---

## 5. 建议执行顺序

### 第 0 步：补缺口（半天，不动 MainWindow）

1. **缺口 2.5**：先改 `LobbyPlayerSlotUiRules` / `MultiplayerSlotLayout` 加 Session/Slots 重载（旧保留门面）  
2. **缺口 2.2 + 2.3**：`ICnCNetGameSession` 加 `Players` / `Locked` / `ApplyPlayersFromNetwork`  
3. **缺口 2.4**：加 `BroadcastPlayerOptionsFromSlots` / `UpdateHuman` / `KickPlayer`  
4. **缺口 2.1**：`EnsureHostFirst` 拆成 `InitHostSlots`（初次）+ `ReorderHostFirst`（重排）两个语义  
5. 全测；单测补新接口  
6. 出「补丁报告」

### 第 1 步起：MainWindow 大刀阔斧（用户已批准）

按 [phase2-audit-report.md](phase2-audit-report.md) §7 的 P2-1…P2-6 推进，但每片的实现可以直接调新接口，不再绕弯。

---

## 6. 总评

**架构骨架完备；接口面差 5 块拼图。**

这 5 块不是设计层面的争议，而是「抽象做了一半、对接口时漏了几条」的工程问题。每块都是小工时、低风险、可独立验证。

**不建议现在直接改 MainWindow 胶水**——会写出第二次胶水（Session 外面包一层转换），Phase 3 回收时还要再改一轮。

**建议**：批准第 0 步（补 5 处接口缺口）后立即开工。预计 4 小时内补完 + 单测全绿，然后进入 MainWindow 实质改造。
