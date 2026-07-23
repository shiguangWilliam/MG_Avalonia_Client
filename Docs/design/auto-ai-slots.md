# Auto AI Slots 设计文档：按地图 MaxPlayers 自动填充 AI 槽位

> **状态**：v2 — 简化为"直接填充"模式（用户 2026-07-19 反馈）。
> 不保留用户既定 AI 配置；如未来有保留需求再追加。

## 1. 问题陈述

遭遇战（Skirmish）模式下，玩家进入 lobby 或切图后，**默认 AI 槽位数量与地图容量不匹配**：

- 小地图（2 人）：默认仍有 7 个 AI 槽（共 8 槽）→ 看似合理，但启动后大部分 AI 没位置
- 大地图（8 人）：默认 AI 数量不变 → 玩家得手动一个个加 AI

当前 `LobbyPlayerState.LoadDefaultSkirmishSlots()` 固定填充到 8 槽，与具体地图无关。

## 2. 目标（用户方案）

> "对于 AI 槽位问题，不需要保留用户既定的配置，**直接填充槽位即可**。如果有保留的需求，后续会追加。"

简化后的规则：
- 进入 lobby / 切图时：**清空所有 AI 槽位**，按地图 `MaxPlayers` **填满** AI
- 不判断"用户当前 AI 数 vs 新地图容量"
- 不保留任何用户既有 AI 配置（难度、阵营、颜色、起点）
- 单人模式（Skirmish / Campaign）生效；联机模式（CnCNetGameLobby / LANGameLobby）**不自动填充**（玩家可自由加入）

## 3. 与原方案的对比

| 维度 | 旧方案（保留用户配置） | 新方案（直接填充） |
|---|---|---|
| 切图行为 | 只在用户 AI 数 > MaxPlayers-1 时裁剪 | 一律清空并按新地图填满 |
| 用户调过的难度/阵营/颜色 | 保留 | 覆盖 |
| 初始进入 lobby | 填到 MaxPlayers-1 个 AI | 填到 MaxPlayers-1 个 AI（相同） |
| 实现复杂度 | 2 个方法（ApplyInitialFill + TrimExcessAiOnMapChange） | 1 个方法（AutoFillToMapCapacity）|
| 工时 | 5h | 3h |
| 测试用例 | 5 个 | 3 个 |
| 用户预期 | 复杂但可能失望（"为啥我调的难度没了"） | 一致可预期（每次切图都重置） |

## 4. 设计

### 4.1 核心规则

```
OnLobbyEnter(map):
    AutoFillToMapCapacity(state, map.MaxPlayers)   # 见 4.3

OnMapChange(oldMap, newMap):                       # 仅 Skirmish/Lobby（非联机）
    AutoFillToMapCapacity(state, newMap.MaxPlayers)

AutoFillToMapCapacity(state, maxPlayers):
    state.Slots.Clear()                            # 不保留用户配置
    state.Slots.Add(LocalHuman())                  # 槽 0：本地玩家
    for i in 1 .. maxPlayers-1:
        state.Slots.Add(DefaultAi(i))              # 其余槽位填默认 AI
```

**填满定义**：本地玩家占 1 槽，AI 占 `MaxPlayers - 1` 槽，总计 = `MaxPlayers`。

### 4.2 触发时机

| 时机 | 现状 hook | 改造 |
|---|---|---|
| 进入 SkirmishLobby | `MainWindow.OpenSkirmishLobby()` → `LoadDefaultSkirmishSlots()` | 把 `LoadDefaultSkirmishSlots` 改为调用 `AutoFillToMapCapacity(defaultMap.MaxPlayers)` |
| 切图（地图列表点击） | `MainWindow.lbMapList_SelectionChanged` | 切图后调用 `AutoFillToMapCapacity(newMap.MaxPlayers)` |
| 切图（轮换/Random） | `mwRandomMapButton_Click` | 同上 |
| 进入联机 lobby | `MultiplayerGameLobby` 加载 | **不变**（不自动填 AI） |

### 4.3 `DefaultAiSlotPolicy` 类设计

```csharp
// 新文件：ClientAvalonia/IniUi/Lobby/DefaultAiSlotPolicy.cs
namespace ClientAvalonia.IniUi.Lobby;

/// <summary>
/// Single-mode skirmish AI slot policy: clear and fill to map capacity.
/// 
/// This policy is intentionally non-preserving — every map change resets AI
/// slots to defaults. If preservation becomes a requirement later, a separate
/// <c>PreservingAiSlotPolicy</c> can be added behind an <c>IAiSlotPolicy</c>
/// interface without modifying call sites.
/// </summary>
public static class DefaultAiSlotPolicy
{
    /// <summary>
    /// Clears all slots and refills them: 1 local human + (maxPlayers - 1) default AIs.
    /// </summary>
    public static void AutoFillToMapCapacity(LobbyPlayerState state, int maxPlayers, GameResourceCatalog resources)
    {
        if (maxPlayers < 1) maxPlayers = 1;
        if (maxPlayers > 8) maxPlayers = 8;  // CnCNet lobby slot hard cap

        state.Slots.Clear();

        // Slot 0: local human player
        var localHuman = new LobbySlot
        {
            PlayerType = LobbyPlayerType.Human,
            Name = ProgramConstants.PLAYERNAME1,
            SideIndex = -1,           // Random
            ColorIndex = 0,
            StartIndex = -1,          // assigned by map start marker
            TeamIndex = 0,
            IsLocal = true,
        };
        state.Slots.Add(localHuman);

        // Slots 1..maxPlayers-1: default AI
        for (int i = 1; i < maxPlayers; i++)
        {
            state.Slots.Add(NewDefaultAi(i, resources));
        }
    }

    private static LobbySlot NewDefaultAi(int slotIndex, GameResourceCatalog resources)
    {
        // Defaults: medium difficulty, random side, deterministic color by slot, auto start.
        // Color is assigned by slot index modulo palette size to avoid immediate conflicts.
        return new LobbySlot
        {
            PlayerType = LobbyPlayerType.AI,
            Name = AIPlayerName(slotIndex),
            AiDifficulty = 1,                 // 0=easy, 1=medium, 2=hard
            SideIndex = -1,                   // Random
            ColorIndex = slotIndex,           // simplest deterministic scheme
            StartIndex = -1,
            TeamIndex = 0,
        };
    }

    private static string AIPlayerName(int slotIndex)
        => $"AI {slotIndex}";
}
```

### 4.4 与 Auto-Refresh 设计的协同

由于本设计落地时 [`auto-refresh-design.md`](./auto-refresh-design.md) 也将实施，切图触发的 AI 重填应封装为 `LobbyAction`：

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/Lobby/ChangeMapAction.cs
public sealed class ChangeMapAction : LobbyAction
{
    private readonly int _mapIndex;

    public ChangeMapAction(int mapIndex) { _mapIndex = mapIndex; }

    public override void Execute(LobbyActionContext ctx)
    {
        var newMap = ctx.Resources.Maps[_mapIndex];
        ctx.Session.SelectedMapIndex = _mapIndex;

        // Skirmish/Lobby: refill AI slots (per DefaultAiSlotPolicy v2).
        // CnCNet/LAN: do NOT touch (multiplayer slots are managed by join/part events).
        if (IsSinglePlayerWindow(ctx.WindowName))
        {
            DefaultAiSlotPolicy.AutoFillToMapCapacity(ctx.Player, newMap.MaxPlayers, ctx.Resources);
        }
    }

    private static bool IsSinglePlayerWindow(string name)
        => name.Equals("SkirmishLobby", StringComparison.OrdinalIgnoreCase);
}
```

`ActionExecutor` 的 refresh pipeline 自动刷新 dropdown 和 start marker，无需在 policy 内手动 refresh。

## 5. 触发流程图

```
进入 SkirmishLobby
       │
       ▼
LoadDefaultSkirmishSlots()            ← 改造点
       │
       ▼
DefaultAiSlotPolicy.AutoFillToMapCapacity(state, defaultMap.MaxPlayers, resources)
       │
       ▼
[ActionExecutor 触发 refresh]
       │
       ▼
UI 显示：1 玩家 + (maxPlayers-1) AI


切图（用户点地图列表）
       │
       ▼
mwLbMapList_SelectionChanged
       │
       ▼
executor.Execute(new ChangeMapAction(newMapIndex))
       │
       ▼
ChangeMapAction.Execute(ctx)
       ├──► ctx.Session.SelectedMapIndex = newMapIndex
       └──► DefaultAiSlotPolicy.AutoFillToMapCapacity(ctx.Player, newMap.MaxPlayers, ...)
       │
       ▼
[refresh pipeline 全量刷新]
       │
       ▼
UI 显示：新地图 + 重置后的 AI 槽位
```

## 6. 边界情况

| 情况 | 处理 |
|---|---|
| 地图 `MaxPlayers` 缺失或 0 | 视为 1（仅本地玩家） |
| `MaxPlayers > 8` | 截断到 8（CnCNet 槽位上限） |
| `MaxPlayers == 1` | 仅本地玩家，0 AI（训练/教学地图） |
| 切回同一张图 | 也清空重填（简单一致） |
| 用户中途加 AI 后切图 | 全部清空（明确：不保留） |
| 联机模式 | 不触发（`IsSinglePlayerWindow` 守卫） |
| Campaign 模式 | Campaign 用 mission script，不走 skirmish lobby，**不触发** |

## 7. 测试用例

```csharp
// 新文件：ClientAvalonia.Tests/IniUi/Lobby/DefaultAiSlotPolicyTests.cs

[Fact]
public void AutoFill_2PlayerMap_Leaves_1_Local_1_Ai()
{
    var state = new LobbyPlayerState();
    var resources = NewTestCatalog();

    DefaultAiSlotPolicy.AutoFillToMapCapacity(state, maxPlayers: 2, resources);

    state.Slots.Should().HaveCount(2);
    state.Slots[0].PlayerType.Should().Be(LobbyPlayerType.Human);
    state.Slots[1].PlayerType.Should().Be(LobbyPlayerType.AI);
}

[Fact]
public void AutoFill_8PlayerMap_Leaves_1_Local_7_Ai()
{
    var state = new LobbyPlayerState();
    var resources = NewTestCatalog();

    DefaultAiSlotPolicy.AutoFillToMapCapacity(state, maxPlayers: 8, resources);

    state.Slots.Should().HaveCount(8);
    state.Slots[0].IsLocal.Should().BeTrue();
    state.Slots.Skip(1).All(s => s.PlayerType == LobbyPlayerType.AI).Should().BeTrue();
}

[Fact]
public void AutoFill_Clears_Existing_User_Edits()
{
    // User had tweaked colors/difficulties; on map change everything resets.
    var state = new LobbyPlayerState();
    state.Slots.Add(NewHuman());
    state.Slots.Add(NewAi(colorIndex: 5, aiDifficulty: 2));
    state.Slots.Add(NewAi(colorIndex: 7, aiDifficulty: 0));

    DefaultAiSlotPolicy.AutoFillToMapCapacity(state, maxPlayers: 3, NewTestCatalog());

    state.Slots.Should().HaveCount(3);
    state.Slots[1].ColorIndex.Should().Be(1, "default color scheme by slot index");
    state.Slots[1].AiDifficulty.Should().Be(1, "default medium");
}

[Fact]
public void AutoFill_Clamps_Illegal_MaxPlayers()
{
    var state = new LobbyPlayerState();

    DefaultAiSlotPolicy.AutoFillToMapCapacity(state, maxPlayers: 0, NewTestCatalog());
    state.Slots.Should().HaveCount(1);

    DefaultAiSlotPolicy.AutoFillToMapCapacity(state, maxPlayers: 99, NewTestCatalog());
    state.Slots.Should().HaveCount(8);
}

[Fact]
public void ChangeMapAction_Refills_On_Map_Switch()
{
    var ctx = NewLobbyContext(windowName: "SkirmishLobby");
    var executor = ctx.NewFullExecutor();
    ctx.Player.Slots.Add(NewHuman());
    ctx.Player.Slots.Add(NewAi(colorIndex: 5));  // user tweak

    executor.Execute(new ChangeMapAction(mapIndex: 1));  // map 1 has MaxPlayers=2

    ctx.Player.Slots.Should().HaveCount(2);
    ctx.Player.Slots[1].ColorIndex.Should().Be(1, "user tweak was reset");
}

[Fact]
public void ChangeMapAction_Does_Not_Refill_In_CnCNet_Lobby()
{
    var ctx = NewLobbyContext(windowName: "CnCNetGameLobby");
    var executor = ctx.NewFullExecutor();
    ctx.Player.Slots.Add(NewHuman());
    ctx.Player.Slots.Add(NewHuman());  // another player joined

    executor.Execute(new ChangeMapAction(mapIndex: 1));

    ctx.Player.Slots.Should().HaveCount(2, "multiplayer slots must not be auto-filled");
}
```

## 8. 工时预估

| 阶段 | 工时 |
|---|---|
| `DefaultAiSlotPolicy` 实现 | 1h |
| `ChangeMapAction` 接入（依赖 auto-refresh 基础设施） | 0.5h |
| `LoadDefaultSkirmishSlots` 改造 | 0.5h |
| 单元测试（6 个用例） | 1h |
| 手动测试 3 个 mod（MG / LNOD / QEC） | 1h |
| **总计** | **~4h**（半个工作日，比原方案少 1h） |

## 9. 关键决策记录

| 决策 | 理由 |
|---|---|
| 不保留用户配置 | 用户明确确认（2026-07-19）。简化实现 + 一致可预期 |
| 切图时清空所有槽位 | 简单一致；如果只动 AI 不动人，逻辑复杂且用户困惑 |
| 联机模式不触发 | 多人槽位由玩家加入/离开事件驱动，自动填 AI 会破坏联机体验 |
| Campaign 不触发 | Campaign 有独立 mission script 管理 slots |
| ColorIndex 用 slot index 取模 | 最简单的确定性配色，避免开局即冲突；用户随时可调 |
| 触发点封进 `ChangeMapAction` | 与 auto-refresh 设计协同，统一 refresh 管线 |
| 提供 `IAiSlotPolicy` 预留点（未引入） | 等到真有"保留"需求再加；YAGNI |

## 10. 未来扩展

如用户后续追加"保留用户配置"需求：

```csharp
public interface IAiSlotPolicy
{
    void OnMapChange(LobbyPlayerState state, int newMaxPlayers, GameResourceCatalog resources);
}

public sealed class PreservingAiSlotPolicy : IAiSlotPolicy { /* future */ }
public sealed class ResettingAiSlotPolicy : IAiSlotPolicy { /* current v2 */ }
```

调用点从 `DefaultAiSlotPolicy.AutoFillToMapCapacity(...)` 改为 `_aiSlotPolicy.OnMapChange(...)`，注入即可切换。其他代码不变。
