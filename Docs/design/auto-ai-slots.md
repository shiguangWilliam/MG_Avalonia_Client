# 新功能设计：地图玩家数 → 默认 AI 数量匹配

> **状态**：设计稿，待审批。

## 1. 需求

> 地图玩家数量 - 默认 AI 数量匹配。比如地图只有两个人，则只添加一个 AI，其余槽位空，由此实现更方便的操作。

例如：
- 1v1 地图（2 个 spawn）→ 自动添加 1 个 AI
- 2v2 地图（4 个 spawn）→ 自动添加 3 个 AI（默认）
- 8 人 free-for-all 地图 → 自动添加 7 个 AI

避免用户每次进入 SkirmishLobby 都要手动删多余的 AI 槽位或添加不足的 AI。

## 2. 现状调研

### 2.1 默认槽位初始化

`LobbyPlayerState.LoadDefaultSkirmishSlots()`：

```csharp
public void LoadDefaultSkirmishSlots()
{
    ClearSlots();
    Slots[0].Name = ProgramConstants.PLAYERNAME;
    Slots[0].IsHumanLocal = true;
    // ...

    if (AiNames.Count == 0)
        return;

    // 现状：永远只添加 1 个 AI 到 slot[1]
    Slots[1].Name = AiNames[0];
    Slots[1].IsAi = true;
    // ...
}
```

**问题**：不管地图支持多少玩家，都只放 1 个 AI。8 人图也得手动加 6 个。

### 2.2 调用路径

```
MainWindow.ApplyLobbyData(windowName="SkirmishLobby")
   └─► LobbyPlayerSlotUiRules.ConfigureForSkirmish(_lobbySession.PlayerState);
   └─► if (!_lobbySession.PlayerState.TryLoadSkirmishSettings())
            _lobbySession.PlayerState.LoadDefaultSkirmishSlots();   ← 触发点
```

**关键判断**：只在"没有已保存的 skirmish 设置"时才走默认。所以老用户（已经玩过一次）不会受影响。

### 2.3 可用数据

`MapEntry` 已经有 `MaxPlayers` 字段（详见 `GameResourceCatalog` 加载）：

```csharp
public sealed class MapEntry
{
    public string DisplayName { get; set; }
    public string UntranslatedName { get; set; }
    public int MaxPlayers { get; set; }       // ← 关键
    public string Sha1 { get; set; }
    public bool EnforceMaxPlayers { get; set; }
    public int[] StartLocations { get; set; }
    // ...
}
```

`SkirmishLaunchValidator.Validate(map, gameMode, players)` 已经用 `MaxPlayers` 做校验。

### 2.4 设置持久化

`TryLoadSkirmishSettings()` / `SaveSkirmishSettings()` 把当前槽位状态写入 INI 文件（`spawn.ini`）。AI 数量也是持久化的。

## 3. 设计方案

### 3.1 触发时机选择

两个备选触发时机：

| 时机 | 优点 | 缺点 |
|---|---|---|
| **A. 进 lobby 时**（首次未存设置） | 与现状一致，零侵入 | 用户换地图后不会自动重新匹配（除非清空存档） |
| **B. 选地图时**（每次切换地图） | 真正"自动匹配"的语义；用户不用动 | 与用户手动加的 AI 冲突；行为可能让用户困惑 |

**推荐方案**：**A + B 混合**。
- 首次进 lobby（无存档）→ 按"当前选中的地图（通常是上次玩的）"匹配
- 切换地图时（`lbMapList.SelectionChanged`）→ 仅当**当前 AI 数量大于新地图 MaxPlayers - 1**时自动减；**不主动加**（避免覆盖用户故意设的少量 AI）

### 3.2 匹配规则

```
设 N = map.MaxPlayers

首次进 lobby（无存档）：
   slot[0] = 本地玩家
   slot[1..N-1] = AI（默认中等难度）
   slot[N..] = 空

切换地图时：
   if (当前 AI 数 > N - 1)
       把多余的 AI 移除（保留靠前的）
   // 否则不动
```

例：
- 切到 1v1 图（N=2），当前 4 个 AI → 移除 3 个，留 1 个
- 切到 1v1 图（N=2），当前 0 个 AI → 不动（保持 0，因为可能用户故意）
- 切到 8 人图（N=8），当前 1 个 AI → 不动（用户可手动加）

### 3.3 边界情况

| 情况 | 处理 |
|---|---|
| 地图 `MaxPlayers` 未知（=0） | fallback：保留现状（1 个 AI） |
| 用户从"少 AI"切到"多 AI"图 | 不主动加 AI，避免覆盖用户意图 |
| 用户清空所有 AI 后切图 | 不补 AI |
| 多人联机模式 | **不应用此规则**（联机槽位由玩家加入决定） |
| 地图 enforce `MaxPlayers`（`EnforceMaxPlayers=true`） | 必须严格匹配，移除多余 AI 是强制的 |

## 4. 类设计

### 4.1 新增类：`DefaultAiSlotPolicy`

```csharp
// 新文件：ClientAvalonia/Domain/DefaultAiSlotPolicy.cs
namespace ClientAvalonia.Domain;

/// <summary>
/// Decides how many AI slots to populate based on the currently selected
/// map's MaxPlayers. Two modes:
///   - InitialFill:   used on first skirmish entry (no saved settings).
///   - TrimOnMapChange: used when user switches maps; only removes excess AIs,
///                      never adds (preserves user intent).
/// </summary>
public static class DefaultAiSlotPolicy
{
    /// <summary>
    /// Populate slot[1..N-1] with default-difficulty AI for the first skirmish entry.
    /// Slot 0 is the local human player. Slots beyond N are left empty.
    /// </summary>
    public static void ApplyInitialFill(LobbyPlayerState state, int mapMaxPlayers, string defaultAiName)
    {
        if (mapMaxPlayers <= 0)
            mapMaxPlayers = 2;   // fallback: assume 1v1

        state.Slots[0].Name = ProgramConstants.PLAYERNAME;
        state.Slots[0].IsHumanLocal = true;
        state.Slots[0].SideIndex = 0;
        state.Slots[0].ColorIndex = 0;
        state.Slots[0].TeamIndex = 0;
        state.Slots[0].StartIndex = 0;

        // Fill slot[1..N-1] with default AI
        for (int i = 1; i < mapMaxPlayers && i < LobbyPlayerSlot.MaxSlots; i++)
        {
            state.Slots[i].Name = defaultAiName;
            state.Slots[i].IsAi = true;
            state.Slots[i].AiLevel = 0;   // easy/medium per DX convention
            state.Slots[i].SideIndex = 0;
            state.Slots[i].ColorIndex = i;
            state.Slots[i].TeamIndex = 0;
            state.Slots[i].StartIndex = 0;
        }

        // Clear slots beyond map's MaxPlayers
        for (int i = mapMaxPlayers; i < LobbyPlayerSlot.MaxSlots; i++)
        {
            state.Slots[i].Name = string.Empty;
            state.Slots[i].IsAi = false;
            state.Slots[i].IsHumanLocal = false;
        }
    }

    /// <summary>
    /// When the user switches maps, trim AI slots if current AI count exceeds
    /// the new map's MaxPlayers - 1. Never adds AIs.
    /// </summary>
    public static void TrimExcessAiOnMapChange(LobbyPlayerState state, int newMapMaxPlayers)
    {
        if (newMapMaxPlayers <= 0)
            return;   // unknown, do nothing

        int maxAi = newMapMaxPlayers - 1;
        int currentAi = 0;
        foreach (LobbyPlayerSlot slot in state.Slots)
            if (slot.IsAi) currentAi++;

        if (currentAi <= maxAi)
            return;   // not exceeding, leave user intent alone

        // Remove excess AIs, keeping the first `maxAi` of them
        int aiKept = 0;
        for (int i = 0; i < state.Slots.Length; i++)
        {
            if (!state.Slots[i].IsAi)
                continue;

            if (aiKept < maxAi)
            {
                aiKept++;
            }
            else
            {
                state.Slots[i].Name = string.Empty;
                state.Slots[i].IsAi = false;
                state.Slots[i].IsHumanLocal = false;
            }
        }
    }
}
```

### 4.2 接入点

#### 4.2.1 `LobbyPlayerState.LoadDefaultSkirmishSlots` 改造

```csharp
// 旧
public void LoadDefaultSkirmishSlots() { /* 1 个 AI */ }

// 新
public void LoadDefaultSkirmishSlots(int mapMaxPlayers)
{
    ClearSlots();
    string defaultAiName = AiNames.Count > 0 ? AiNames[0] : "AI";
    DefaultAiSlotPolicy.ApplyInitialFill(this, mapMaxPlayers, defaultAiName);
}
```

#### 4.2.2 `MainWindow.ApplyLobbyData` 改造

```csharp
// 旧
if (!_lobbySession.PlayerState.TryLoadSkirmishSettings())
    _lobbySession.PlayerState.LoadDefaultSkirmishSlots();

// 新
if (!_lobbySession.PlayerState.TryLoadSkirmishSettings())
{
    MapEntry? defaultMap = _gameResources.Maps.FirstOrDefault();
    int maxPlayers = defaultMap?.MaxPlayers ?? 0;
    _lobbySession.PlayerState.LoadDefaultSkirmishSlots(maxPlayers);
}
```

#### 4.2.3 地图切换时调用 Trim

`MainWindow` 现有的 `lbMapList.SelectionChanged` 回调：

```csharp
lbMapList.SelectionChanged += () =>
{
    MapEntry? newMap = _lobbySession.GetSelectedMap(lbMapList.SelectedIndex);
    if (newMap != null && CurrentWindow == "SkirmishLobby")
    {
        DefaultAiSlotPolicy.TrimExcessAiOnMapChange(_lobbySession.PlayerState, newMap.MaxPlayers);
        // 然后走 Auto-Refresh 设计的统一 refresh（见 auto-refresh-design.md）
    }

    // 现有的 UpdateMapSelectionDisplay ...
};
```

**注**：联机模式（`CnCNetGameLobby`）不接入此策略，保留现状。

## 5. 测试用例

```csharp
public sealed class DefaultAiSlotPolicyTests
{
    [Fact]
    public void InitialFill_2PlayerMap_Adds_1Ai()
    {
        var state = new LobbyPlayerState();
        state.AiNames.Add("EasyAI");

        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 2, defaultAiName: "EasyAI");

        state.Slots[0].IsHumanLocal.Should().BeTrue();
        state.Slots[1].IsAi.Should().BeTrue();
        state.Slots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void InitialFill_8PlayerMap_Adds_7Ai()
    {
        var state = new LobbyPlayerState();
        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 8, defaultAiName: "AI");

        state.HumanRowCount.Should().Be(1);
        state.AiRowCount.Should().Be(7);
    }

    [Fact]
    public void InitialFill_UnknownMaxPlayers_FallsBack_To_2Player()
    {
        var state = new LobbyPlayerState();
        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 0, defaultAiName: "AI");

        state.AiRowCount.Should().Be(1, "fallback should assume 1v1");
    }

    [Fact]
    public void TrimOnMapChange_From8To2_Removes_ExcessAis()
    {
        var state = new LobbyPlayerState();
        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 8, defaultAiName: "AI");

        DefaultAiSlotPolicy.TrimExcessAiOnMapChange(state, newMapMaxPlayers: 2);

        state.AiRowCount.Should().Be(1);
    }

    [Fact]
    public void TrimOnMapChange_From2To8_DoesNot_AddAis()
    {
        var state = new LobbyPlayerState();
        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 2, defaultAiName: "AI");

        DefaultAiSlotPolicy.TrimExcessAiOnMapChange(state, newMapMaxPlayers: 8);

        state.AiRowCount.Should().Be(1, "must preserve user intent — never add AIs on map change");
    }

    [Fact]
    public void TrimOnMapChange_UnknownMaxPlayers_NoOp()
    {
        var state = new LobbyPlayerState();
        DefaultAiSlotPolicy.ApplyInitialFill(state, mapMaxPlayers: 4, defaultAiName: "AI");

        DefaultAiSlotPolicy.TrimExcessAiOnMapChange(state, newMapMaxPlayers: 0);

        state.AiRowCount.Should().Be(3, "unknown MaxPlayers should not touch state");
    }
}
```

## 6. 工作量预估

| 步骤 | 工时 |
|---|---|
| `DefaultAiSlotPolicy` 类 | 1h |
| 接入 `LoadDefaultSkirmishSlots` + `ApplyLobbyData` | 1h |
| 接入 `lbMapList.SelectionChanged` trim | 1h |
| 单元测试 | 1h |
| 手动验证（MG/LNOD/QEC 三 mod 试一次） | 1h |
| **总计** | **5h**（半个工作日） |

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 用户已经存了 skirmish 设置，新逻辑被覆盖 | 仅在 `!TryLoadSkirmishSettings()` 时应用，老用户不受影响 |
| 默认 AI 难度选错 | 用 DX 默认值（AiLevel=0），与 `LoadDefaultSkirmishSlots` 现状一致 |
| Trim 策略误删用户故意加的 AI | Trim 只在 AI 数 > MaxPlayers-1 时移除，且只移除靠后的 |
| 与 Auto-Refresh 设计冲突 | 两个设计正交：AI 槽位变更走 `LobbyAction` 后再触发 refresh |

## 8. 与 Auto-Refresh 设计的协同

如果 [auto-refresh-design.md](auto-refresh-design.md) 已实施，地图切换的 `LobbyAction` 可以是：

```csharp
public sealed class ChangeMapAction : LobbyAction
{
    private readonly int _newMapIndex;

    public override void Execute(LobbyActionContext ctx)
    {
        MapEntry? newMap = ctx.Session.GetSelectedMap(_newMapIndex);
        ctx.Session.FilterIndex = ...;
        DefaultAiSlotPolicy.TrimExcessAiOnMapChange(ctx.PlayerState, newMap?.MaxPlayers ?? 0);
    }
}
```

executor 末尾自动 refresh，无需手写。

---

**请确认**：
1. 触发时机选 A（仅首次）、B（每次切图都自动匹配）、还是 A+B 混合（推荐）？
2. Trim 策略是否同意"只减不加"？
3. 默认 AI 难度选 0（DX 默认）还是 1（中等）？
