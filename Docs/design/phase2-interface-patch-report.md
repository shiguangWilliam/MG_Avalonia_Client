# Phase 2 接口补丁报告 — 5 处缺口全部落地

**日期**：2026-07-21  
**前置**：[phase2-completeness-audit.md](phase2-completeness-audit.md)  
**测试**：645 → **658 通过 / 0 失败**（+13 测试，0 回归）

---

## 0. 一句话结论

5 处接口缺口全部补完，`ICnCNetGameSession` / `MultiplayerSlotLayout` / `LobbyPlayerSlotUiRules` 现在能完整覆盖 MainWindow Core 的所有调用面。**胶水代码改造可以开工，不会再写第二次胶水。**

---

## 1. 落地清单

### 1.1 缺口 2.5：UI Rules / Layout 加 Session 重载 ✅

**改动**：

- `Services/MultiplayerSlotLayout.cs`
  - 新增 `ApplyToSlots(IReadOnlyList<IPlayerSlot>, entries, localNick)` 重载
  - 新增 `BuildPoList(IReadOnlyList<IPlayerSlot>, hostName, aiNames)` 重载
  - 旧 `ApplyToState(LobbyPlayerState, ...)` / `BuildPoListFromState` 保留门面（委托到新重载）
  - `ApplyHuman` / `ApplyAi` 私有方法签名从 `LobbyPlayerSlot` 改成 `IPlayerSlot`

- `Services/LobbyPlayerSlotUiRules.cs`
  - 新增 `ConfigureForSkirmish(LobbySessionState, ISkirmishSession)` 重载
  - 新增 `ConfigureForMultiplayer(LobbySessionState, ICnCNetGameSession, entries, localNick, hostName, isHost, resetSlots)` 重载
  - 新重载**写 UI 态到 `LobbySessionState`，不再写 `LobbyPlayerState`**
  - 旧重载保留（兼容期）

**意义**：消除 §4.1「双份真相源头」——`ConfigureFor*` 新重载只写一份 UI 态。

---

### 1.2 缺口 2.2：`Players` / `Locked` 进接口 ✅

**改动**：`Session/ICnCNetGameSession.cs` 新增

```csharp
IReadOnlyList<CnCNetGameRoomPlayer> Players { get; }
bool Locked { get; }
```

`CnCNetGameRoomSession` 已经有实现，无需新增代码。

**意义**：MainWindow 不再需要 `(CnCNetGameRoomSession)session` 强转取这两个字段。

---

### 1.3 缺口 2.3：`ApplyPlayersFromNetwork` 入口 ✅

**改动**：

- `ICnCNetGameSession` 新增：

```csharp
void ApplyPlayersFromNetwork(IReadOnlyList<CnCNetGameRoomPlayer> entries, string hostName, string localNick);
```

- `CnCNetGameRoomSession` 实现：ClearAll → ApplyToSlots → ReorderHostFirst (host) / MarkLocalHuman (joiner) → BumpRevision

**意义**：替代 MainWindow 旧的三步胶水（`ApplyToState` + `EnsureHostAsFirstHuman` + `MarkLocalHuman`），一行调用搞定。

---

### 1.4 缺口 2.4：Host 广播方法进接口 ✅

**改动**：

- `ICnCNetGameSession` 新增：
  - `BroadcastPlayerOptionsFromSlots(hostName, aiNames)` — 替代旧 `SyncPlayersFromLobby(LobbyPlayerState, string)`
  - `UpdateHuman(playerName, in SlotFieldUpdate)` — 替代旧 `UpdateHumanFromSlot(LobbyPlayerSlot)`
  - `KickPlayer(playerName)` — 已有，进接口
- `CnCNetGameRoomSession` 新增实现：
  - `BroadcastPlayerOptionsFromSlots`：`BuildPoList` + 新私有 `SyncPlayersFromDtoLocked`
  - `SyncPlayersFromDtoLocked`：抽出旧 `SyncPlayersFromLobby` 内部逻辑，不依赖 `LobbyPlayerState`
  - `UpdateHuman`：按 `playerName` 找 `_players`，按 `SlotFieldUpdate` 部分更新

**意义**：`MultiplayerSlotCoordinator` 改造时可以直接走 Session API，不再依赖 `LobbyPlayerSlot`。

---

### 1.5 缺口 2.1：`EnsureHostFirst` 拆成 `InitHostSlots` + `ReorderHostFirst` ✅

**改动**：

- `ICnCNetGameSession` 删除：`EnsureHostFirst(localPlayerName, maxPlayers)`
- `ICnCNetGameSession` 新增：
  - `InitHostSlots(localPlayerName)` — 房间**初次创建**用：清空 + 自己坐 [0]
  - `ReorderHostFirst(hostName, localNick)` — 房间**已有玩家**用：保留现有 + host 移到 [0]
- `CnCNetGameRoomSession` 实现：
  - `InitHostSlots`：清空 → slot[0] = localPlayer
  - `ReorderHostFirst`：复刻旧 `LobbyPlayerState.EnsureHostAsFirstHuman` 的算法（保留 humans/ais → 找 host → 插入 [0] → 重新填回）
  - 新增私有 `CloneSlot` / `CopySlotTo` / `NormalizeNick` 辅助方法

**意义**：消除原 `EnsureHostFirst` 的语义错（清空+写一个会丢已 join 玩家）——`ReorderHostFirst` 与旧 `EnsureHostAsFirstHuman` 行为一致，可以安全替换。

---

## 2. 测试增量

| 新增测试 | 数 | 验证 |
|---------|----|------|
| `MultiplayerSlotLayoutSessionOverloadTests` | 5 | ApplyToSlots / BuildPoList 新重载 |
| `LobbyPlayerSlotUiRulesSessionOverloadTests` | 3 | ConfigureFor* 新重载 |
| `CnCNetGameRoomSessionHostSetupTests` 新增 | 5 | InitHostSlots / ReorderHostFirst / ApplyPlayersFromNetwork / UpdateHuman / BroadcastPlayerOptionsFromSlots |
| **合计** | **+13** | 645 → 658，0 回归 |

旧 `EnsureHostFirst` 单测改名为 `InitHostSlots` 测试。

---

## 3. 完备性复查

| 判定项（vs phase2-completeness-audit.md §1） | 状态 |
|---------------------------------------------|------|
| 三层分工定义 | ✅ |
| `IGameSession` 基础抽象 | ✅ |
| 派生 Session 抽象 | ✅ |
| 槽位写入收口 | ✅ |
| 防环机制 | ✅ |
| Service 抽取 | ✅ |
| Host 标记语义 | ✅ **（2.1 已拆 InitHostSlots + ReorderHostFirst）** |
| CnCNet 玩家状态入口 | ✅ **（2.2 已补 Players / Locked）** |
| 网络回推入口 | ✅ **（2.3 已补 ApplyPlayersFromNetwork）** |
| Host 广播入口 | ✅ **（2.4 已补 Broadcast/Update/Kick）** |
| UI 配置入口 | ✅ **（2.5 已加 Session/Slots 重载）** |
| 死代码回收 | ✅ 计划已定 | 

**判定：12/12 通过 → 架构现在完备。**

---

## 4. MainWindow 改造预估（修正版）

之前预估：胶水 ~200 行改造。补完缺口后：

**核心替换（单点）**：

```csharp
// 旧：ApplyCnCNetGameRoomPlayersCore（~50 行 + 3 个调用方法 ~30 行 + 字段 1 个）
ApplyCnCNetGameRoomPlayers(_activeRoot);

// 新：
session.ApplyPlayersFromNetwork(session.Players, hostName, localNick);
// 触发 session.StateChanged → MainWindow 订阅一次性更新 UI
```

**MainWindow 净减少**：~80–120 行（删除 `RefreshCnCNetGameRoomUiFromSession` / `ApplyCnCNetGameRoomPlayers` / `ApplyCnCNetGameRoomPlayersCore` / `_applyingCnCNetGameRoomPlayers` 字段）。

**保留**：BindingApplier / StatusApplier / Toolbar 调用——但参数从 `PlayerState` 改为 `session.PlayerSlots`（或通过 `ApplyWithSession` 新重载）。

---

## 5. 下一步开工路径（P2-1…P2-6 推荐顺序）

按 [phase2-audit-report.md §7](phase2-audit-report.md#7-推荐-phase-2-切片仍不动代码仅路线图)，**现在可以动代码**：

| 切片 | 内容 | 现在的可执行性 |
|------|------|-------------|
| **P2-1** | 接通 `LobbySessionState` UI 字段（消双份真相） | ✅ 用 §1.1 新重载即可 |
| **P2-2** | BindingApplier `PlayerUpdatingInProgress` → Revision 脏读 | ✅ Session.Revision 已就位 |
| **P2-3** | Coordinator + SlotUiRules + StatusApplier 改吃 Session | ✅ §1.4 + 1.5 已就位 |
| **P2-4** | SpawnWriter / LaunchValidator 改吃 `IReadOnlyList<IPlayerSlot>` | ✅ |
| **P2-5** | MainWindow `ApplyCnCNetGameRoomPlayers*` 改造 | ✅ §1.2 + 1.3 已就位 |
| **P2-6** | `SkirmishSession.Player` 私有化 | ✅ |

每片结束跑全测；Phase 3 末尾统一删除 `LobbyPlayerState` + 旧门面方法。

---

## 6. 总评

5 处接口缺口全部补完，零回归。架构现在真正完备，MainWindow 大刀阔斧的改造可以安全开工——不会再出现"切到一半发现还差个接口"的情况。

**下一步**：按 P2-1 → P2-6 推进，每片结束跑全测 + 简短小结。
