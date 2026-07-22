# Auto-Refresh 设计文档：UI 操作后统一刷新机制

> **状态**：v2 — 已审批采用 B1 方案 + 顶层 `UiAction` 抽象基类（用户 2026-07-19 反馈）。
> 待全部设计审批通过后实施。

## 1. 问题陈述

### 1.1 用户报告的 bug

> 启动器应该在用户每一次操作之后，都 refresh 同步用户操作的状态更改。比如用户在遭遇战中先选择位置，再设置颜色，但设置颜色之后，选择的位置节点不会同步颜色，需要额外点击。

### 1.2 根因（调研结果）

当前 lobby 的 UI 同步路径：

```
用户点 ddColor（dropdown）
   ▼
UiNodeViewModel.SelectionChanged 事件
   ▼
LobbyPlayerBindingApplier.SyncOptionsFromUi
   ├─► 把 ddColor.SelectedIndex 写入 playerState.Slots[i].ColorIndex
   ├─► 若是 host：MultiplayerSlotCoordinator.HandleHostOptionsEdit → 推送 PO 到 IRC
   └─► SyncUiFromState(panel, playerState)         ← 这里只刷新 dropdown 的 Items
                                                      不刷新 MapPreviewBox 上的 start marker！

用户点 MapPreviewBox 上的 start marker
   ▼
OnMapStartMarkerLeftClicked
   ▼
MapStartLocationRules.TryApplyHostAssignment
   ▼
RefreshMapStartMarkersAndPlayerUi             ← 这里才完整刷新（含颜色）

=> 用户最后一步必须落在"点 start marker"上才能看到正确颜色。
   倒过来操作（先位置后颜色）颜色就停在旧值。
```

### 1.3 现有代码的散点式 refresh

5 处独立的 refresh 路径，互不统一：`RefreshMapStartMarkersAndPlayerUi`、`LobbyPlayerBindingApplier.SyncUiFromState`、`ApplyLobbyData`、`RefreshCnCNetGameRoomPlayers`、`UpdateLaunchButtonState`。每个 UI 事件回调各自决定调哪个 refresh，遗漏就回归。

## 2. 方案评估

| 方案 | 评分 | 选择 |
|---|---|---|
| A. 每个 action 内手动补 refresh | 短期 5 分钟、长期技术债 | ❌ 否决 |
| B1. `LobbyAction` 基类 + override | 类型安全、IDE 可重构 | ✅ **采用（扩展）** |
| B2. 反射 | 失去编译期检查 | ❌ 否决 |

**用户反馈**（2026-07-19）：B1 方案再抽象高一层——引入顶层 `UiAction` 基类，`LobbyAction` 继承之并定义 lobby 独有字段。这样未来 `MenuAction` / `OptionsAction` / `CampaignAction` 可以并行扩展，不需要回头修改 LobbyAction。

## 3. 采用方案：分层 Command Pattern（用户方案 1）

### 3.1 类层次

```
                  ┌──────────────────────────┐
                  │   UiAction  (abstract)    │  ← 顶层通用契约
                  │   generic: <TContext>     │
                  ├──────────────────────────┤
                  │ + Execute(ctx) : void     │
                  │ + Undo(ctx)   : void      │  ← 可选，默认 throw NotSupportedException
                  │ + DisplayName : string    │  ← 日志/审计/调试
                  │ + Timestamp   : DateTime  │  ← 录制/回放
                  └──────────────────────────┘
                                ▲
                                │
            ┌───────────────────┼────────────────────────┐
            │                   │                        │
   ┌────────┴─────────┐  ┌──────┴───────┐  ┌─────────────┴──────────┐
   │ LobbyAction      │  │ MenuAction   │  │ OptionsAction          │
   │ (sealed abstract)│  │              │  │                        │
   │                  │  │              │  │                        │
   │ + Player         │  │ + MainMenu   │  │ + SettingsStore        │
   │ + Session        │  │   -state     │  │ + DirtyTracker         │
   │ + Resources      │  │              │  │                        │
   │ + Root           │  │              │  │                        │
   │ + CnCNet         │  │              │  │                        │
   │ + WindowName     │  │              │  │                        │
   └──────────────────┘  └──────────────┘  └────────────────────────┘
            ▲
            │
   ┌────────┼─────────────┬───────────────┬──────────────┐
   │        │             │               │              │
┌──┴──────┐ ┌─┴─────────┐ ┌┴────────────┐ ┌┴───────────┐ ┌┴──────────────┐
│SetPlayer│ │SetPlayerSi│ │AssignStartLo│ │ChangeMap    │ │SetGameOption  │
│ColorAct │ │deAction   │ │cationAction │ │Action       │ │Action         │
│ion      │ │           │ │             │ │             │ │               │
└─────────┘ └───────────┘ └─────────────┘ └─────────────┘ └───────────────┘
约 15-20 个 LobbyAction 具体类
```

### 3.2 顶层基类 `UiAction<TContext>`

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/UiAction.cs
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Root abstraction for all UI-driven state mutations. Subclasses are responsible
/// for the *state change only* — the unified refresh pipeline is invoked by
/// <see cref="ActionExecutor{TAction,TContext}"/> after Execute returns.
/// </summary>
/// <typeparam name="TContext">The action's dependency bundle (subclass of <see cref="UiActionContext"/>).</typeparam>
public abstract class UiAction<TContext> where TContext : UiActionContext
{
    /// <summary>Human-readable label for logging / audit / replay.</summary>
    public virtual string DisplayName => GetType().Name;

    /// <summary>When the action was created (UTC). For replay/debug.</summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>Mutate state. Do NOT refresh UI here — the executor handles it.</summary>
    public abstract void Execute(TContext ctx);

    /// <summary>
    /// Reverse the state change. Default implementation throws because most actions
    /// are not naturally reversible. Override in actions that participate in undo.
    /// </summary>
    public virtual void Undo(TContext ctx)
        => throw new NotSupportedException($"{GetType().Name} does not support Undo.");
}
```

### 3.3 上下文基类 `UiActionContext`

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/UiActionContext.cs
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Base dependency bundle. Subclasses add domain-specific dependencies.
/// All fields are <c>init</c>-only and required; tests construct them inline.
/// </summary>
public abstract class UiActionContext
{
    /// <summary>UI root that the action affects. May be re-bound on window navigation.</summary>
    public required UiNodeViewModel Root { get; init; }

    /// <summary>Behavior registry for triggering side-effect behaviors after refresh.</summary>
    public required BehaviorRegistry Behaviors { get; init; }

    /// <summary>Active window name (e.g. "SkirmishLobby", "MainMenu").</summary>
    public required string WindowName { get; init; }
}
```

### 3.4 领域基类 `LobbyAction`

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/Lobby/LobbyAction.cs
namespace ClientAvalonia.IniUi.Actions.Lobby;

public abstract class LobbyAction : UiAction<LobbyActionContext>
{
    // Inherits Execute(LobbyActionContext) from UiAction<TContext>.
    // Common conveniences can be added here if patterns emerge across most actions.
    // Keep this class thin to avoid forcing all lobby actions into one shape.
}

/// <summary>
/// Lobby-specific dependencies. Fields are referenced by lobby actions directly
/// (this.Player, this.Session, this.Root, ...). Add a field here only when at least
/// one LobbyAction needs it — premature generalization is worse than duplication.
/// </summary>
public sealed class LobbyActionContext : UiActionContext
{
    /// <summary>Lobby player/slot state (humans + AIs + start locations).</summary>
    public required LobbyPlayerState Player { get; init; }

    /// <summary>Lobby session state (filter index, visible maps, search text).</summary>
    public required LobbySessionState Session { get; init; }

    /// <summary>Game resource catalog (maps, modes, missions).</summary>
    public required GameResourceCatalog Resources { get; init; }

    /// <summary>Texture / file resolver for the current theme.</summary>
    public required ResourceResolver ResourceResolver { get; init; }

    /// <summary>CnCNet session (host vs joiner path divergence).</summary>
    public required CnCNetSessionService CnCNet { get; init; }
}
```

### 3.5 具体示例：`SetPlayerColorAction`

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/Lobby/SetPlayerColorAction.cs
namespace ClientAvalonia.IniUi.Actions.Lobby;

/// <summary>
/// Changes a player's color. State-only; the executor refreshes UI + IRC broadcast
/// after this returns. Maps to ddColor.SelectionChanged in LobbyPlayerBindingApplier.
/// </summary>
public sealed class SetPlayerColorAction : LobbyAction
{
    private readonly int _slotIndex;
    private readonly int _colorIndex;
    private int _previousColorIndex = -1;   // for Undo

    public SetPlayerColorAction(int slotIndex, int colorIndex)
    {
        _slotIndex = slotIndex;
        _colorIndex = colorIndex;
    }

    public override string DisplayName => $"Set player {_slotIndex} color → {_colorIndex}";

    public override void Execute(LobbyActionContext ctx)
    {
        _previousColorIndex = ctx.Player.Slots[_slotIndex].ColorIndex;
        ctx.Player.Slots[_slotIndex].ColorIndex = _colorIndex;
    }

    public override void Undo(LobbyActionContext ctx)
    {
        ctx.Player.Slots[_slotIndex].ColorIndex = _previousColorIndex;
    }
}
```

### 3.6 通用执行器 `ActionExecutor<TAction, TContext>`

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/ActionExecutor.cs
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Unified execution pipeline: runs the action, then invokes a configurable list of
/// refresh steps. Subclass or parameterize to apply to LobbyAction / MenuAction / etc.
/// </summary>
public sealed class ActionExecutor<TAction, TContext>
    where TAction : UiAction<TContext>
    where TContext : UiActionContext
{
    private readonly TContext _ctx;
    private readonly IReadOnlyList<Action<TContext>> _refreshSteps;

    public ActionExecutor(TContext ctx, IReadOnlyList<Action<TContext>> refreshSteps)
    {
        _ctx = ctx;
        _refreshSteps = refreshSteps;
    }

    public void Execute(TAction action)
    {
        try
        {
            action.Execute(_ctx);
            Logger.Log($"[Action] {action.DisplayName}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Action] {action.DisplayName} threw: {ex}");
            throw;
        }

        // Unified refresh: same pipeline for every action, no omissions possible.
        // Each step swallows its own exceptions so a failure in one does not skip the rest.
        foreach (Action<TContext> step in _refreshSteps)
        {
            try { step(_ctx); }
            catch (Exception ex) { Logger.Log($"[Action] refresh step threw: {ex}"); }
        }
    }

    /// <summary>Test seam: run the action without triggering refresh.</summary>
    internal void ExecuteWithoutRefresh(TAction action)
    {
        action.Execute(_ctx);
    }
}
```

### 3.7 LobbyExecutor 构造（启动时装配 refresh pipeline）

```csharp
// MainWindow 构造 lobby view 时
var lobbyCtx = new LobbyActionContext
{
    Root = vm,
    Behaviors = _mainBehaviors,
    WindowName = windowName,
    Player = _lobbySession.PlayerState,
    Session = _lobbySession,
    Resources = _gameResources,
    ResourceResolver = _mainEngine!.Resources,
    CnCNet = CnCNetSessionService.Instance,
};

var refreshSteps = new Action<LobbyActionContext>[]
{
    LobbyRefreshSteps.RefreshPlayerUi,        // LobbyPlayerBindingApplier.Apply
    LobbyRefreshSteps.RefreshMapStartMarkers, // GameDataBindingApplier.RefreshMapStartMarkers
    LobbyRefreshSteps.RefreshLaunchButton,    // UpdateLaunchButtonState
    LobbyRefreshSteps.RefreshCnCNetGameListing, // cncnet 模式生效
    LobbyRefreshSteps.BroadcastCnCNetState,   // host 推送 PO 到 IRC
};

var lobbyExecutor = new ActionExecutor<LobbyAction, LobbyActionContext>(lobbyCtx, refreshSteps);
```

### 3.8 Refresh 步骤封装（不污染 MainWindow）

```csharp
// 新文件：ClientAvalonia/IniUi/Actions/Lobby/LobbyRefreshSteps.cs
internal static class LobbyRefreshSteps
{
    public static void RefreshPlayerUi(LobbyActionContext ctx)
    {
        LobbyPlayerBindingApplier.Apply(ctx.Root, ctx.Player, ctx.ResourceResolver, ctx.Behaviors);
    }

    public static void RefreshMapStartMarkers(LobbyActionContext ctx)
    {
        GameDataBindingApplier.ResolveStartInteractionFlags(
            ctx.Player, out bool canAssign, out bool canSelectLocal);
        GameDataBindingApplier.RefreshMapStartMarkers(
            ctx.Root, /* map */ null, ctx.Player, canAssign, canSelectLocal);
    }

    public static void RefreshLaunchButton(LobbyActionContext ctx)
    {
        // extracted from MainWindow.UpdateLaunchButtonState, operates on ctx.Root
    }

    public static void RefreshCnCNetGameListing(LobbyActionContext ctx)
    {
        if (!ctx.WindowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            return;
        // listing update logic
    }

    public static void BroadcastCnCNetState(LobbyActionContext ctx)
    {
        if (ctx.CnCNet.GameRoom is not { IsHost: true, IsLocalJoined: true })
            return;
        ctx.CnCNet.SyncGameRoomFromLobby(ctx.Player);
    }
}
```

## 4. 现有事件挂载点改造清单

| 文件 / 行号 | 现状 | 改造后 |
|---|---|---|
| `LobbyPlayerBindingApplier.cs:423-428` `ddName.SelectionChanged` | 直接 `SyncNameFromUi` | `executor.Execute(new SetPlayerNameAction(slot, ddName.SelectedIndex))` |
| `LobbyPlayerBindingApplier.cs:424` `ddSide.SelectionChanged` | `SyncOptionsFromUi` | `new SetPlayerSideAction(...)` |
| `LobbyPlayerBindingApplier.cs:425` `ddColor.SelectionChanged` | `SyncOptionsFromUi` | `new SetPlayerColorAction(...)` |
| `LobbyPlayerBindingApplier.cs:426` `ddTeam.SelectionChanged` | `SyncOptionsFromUi` | `new SetPlayerTeamAction(...)` |
| `LobbyPlayerBindingApplier.cs:428` `ddStart.SelectionChanged` | `SyncOptionsFromUi` | `new SetPlayerStartAction(...)` |
| `MainWindow.axaml.cs:1056` `OnMapStartMarkerLeftClicked` | 直接 mutate + refresh | `new AssignStartLocationAction(slot, loc, leftClick: true)` |
| `MainWindow.axaml.cs:1138` `OnMapStartMarkerRightClicked` | 同上 | `new AssignStartLocationAction(slot, loc, leftClick: false)` 或 `new ClearStartLocationAction(loc)` |
| `MainWindow.axaml.cs:1010` `lbMapList.SelectionChanged` | update + refresh | `new ChangeMapAction(mapIndex)` |
| `MainWindow.axaml.cs:1616` `OnHostGameOptionControlChanged` | dirty flag | `new SetGameOptionAction(name, value)` |

约 12 个挂载点。

## 5. 渐进迁移策略

3 个 commit：

1. **Commit 1（基础设施）**：引入 `UiAction<TContext>` + `UiActionContext` + `ActionExecutor` + `LobbyAction` / `LobbyActionContext`。先**不接入任何事件**。加单元测试验证空 action 触发 refresh pipeline。
2. **Commit 2（接入 lobby）**：改造 `LobbyPlayerBindingApplier` 的 5 个 dropdown 事件。**修复用户报告的颜色 bug**。
3. **Commit 3（接入 map / options）**：改造 `OnMapStartMarkerLeftClicked` / `OnMapStartMarkerRightClicked` / `lbMapList.SelectionChanged` / `OnHostGameOptionControlChanged`。

每个 commit 都跑 `ThreeModCompatibilityTests` + 新加的 `LobbyActionTests`。

## 6. 测试策略

### 6.1 Action 单元测试（无 UI）

```csharp
[Fact]
public void SetPlayerColorAction_Updates_StateColorIndex()
{
    var ctx = NewTestContext();
    var executor = ctx.NewExecutorWithoutRefresh();

    executor.ExecuteWithoutRefresh(new SetPlayerColorAction(slotIndex: 1, colorIndex: 3));

    ctx.Player.Slots[1].ColorIndex.Should().Be(3);
}

[Fact]
public void SetPlayerColorAction_Undo_RestoresPrevious()
{
    var ctx = NewTestContext();
    ctx.Player.Slots[1].ColorIndex = 5;
    var action = new SetPlayerColorAction(slotIndex: 1, colorIndex: 3);

    action.Execute(ctx);
    action.Undo(ctx);

    ctx.Player.Slots[1].ColorIndex.Should().Be(5);
}
```

### 6.2 Refresh Pipeline 集成测试

```csharp
[Fact]
public void SetPlayerColorAction_Triggers_AllRefreshSteps()
{
    int refreshCount = 0;
    var ctx = NewTestContext();
    var executor = new ActionExecutor<LobbyAction, LobbyActionContext>(
        ctx,
        refreshSteps: new Action<LobbyActionContext>[]
        {
            _ => refreshCount++,
            _ => refreshCount++,
            _ => refreshCount++,
        });

    executor.Execute(new SetPlayerColorAction(1, 3));

    refreshCount.Should().Be(3, "every refresh step must run, regardless of action");
}
```

### 6.3 端到端测试（用户报告的 bug 复现）

```csharp
[Fact]
public void Color_After_StartLocation_Assignment_Refreshes_MarkerColor()
{
    var ctx = NewTestContext();
    var executor = ctx.NewFullExecutor();

    executor.Execute(new AssignStartLocationAction(slotIndex: 0, startLocation: 1));
    executor.Execute(new SetPlayerColorAction(slotIndex: 0, colorIndex: 2));  // red

    ctx.LatestMarkerColor(1).Should().Be(2, "marker color must reflect latest state");
}
```

## 7. 风险与缓解

| 风险 | 缓解 |
|---|---|
| refresh 步骤之间有时序依赖（如 player UI 必须先于 marker） | `_refreshSteps` 数组顺序就是执行顺序，加 XML 注释 |
| 某些 action 不应触发某些 refresh（如 joiner 改 color 不应触发 PO broadcast） | `UiAction` 加 `virtual bool ShouldTrigger(int stepIndex) => true`，子类按需 override；或更精细：refresh step 自己判断（如 `BroadcastCnCNetState` 内检查 IsHost） |
| 多个 action 连续触发导致 refresh 风暴（如批量 repopulate） | 加 `ExecuteBatch(IEnumerable<TAction>)`，只 refresh 一次 |
| 与 IRC 协议交互复杂（host vs joiner 路径不同） | broadcast step 内部判断 `IsHost`，与现状一致 |
| 顶层 `UiAction` 过度抽象导致通用 `Undo` 失控 | `Undo` 默认 `throw NotSupportedException`，只对真正可逆的 action override |
| 未来 `MenuAction` / `OptionsAction` 想接入但字段不一样 | 各自定义 `<Xxx>ActionContext : UiActionContext`，互不干扰 |

## 8. 工时预估

| 阶段 | 工时 | 产出 |
|---|---|---|
| 1. 基础设施（UiAction + Executor + LobbyAction） | 5h | 顶层 + 领域 + 执行器 + 测试 |
| 2. Lobby 接入 | 4h | 5 个 dropdown 改 action，用户 bug 修复 |
| 3. Map / options 接入 | 6h | map marker + game options 全部走 executor |
| 4. 测试覆盖 | 4h | 20+ 个 action 单测 + 集成测试 |
| **总计** | **~19h**（2-3 个工作日） | |

## 9. 设计要点总结

| 维度 | 决定 |
|---|---|
| 顶层抽象 | `UiAction<TContext>` 泛型基类，提供 Execute / Undo / DisplayName / Timestamp |
| 领域基类 | `LobbyAction : UiAction<LobbyActionContext>`，承载 lobby 共有依赖 |
| 上下文 | `UiActionContext`（abstract）+ `LobbyActionContext`（sealed），用 `required init` 强制必填 |
| 执行器 | `ActionExecutor<TAction, TContext>`，泛型 + refresh pipeline |
| Refresh 步骤 | 静态 `LobbyRefreshSteps` 类，每步独立 catch |
| 渐进迁移 | 3 个 commit，每个可独立回滚 |
| Undo | 默认 `NotSupportedException`，仅可逆 action override |

---

## 10. 待确认问题

1. **`UiAction` 是否需要泛型**？还是 `Execute(object)` + 子类 cast？
   - 推荐：泛型（类型安全、IDE 可重构）
2. **`Undo` 是否本期实现**？
   - 推荐：保留接口，本期只对 `SetPlayerColorAction` / `SetPlayerSideAction` 等可逆 action 实现；其他保持 `NotSupportedException`
3. **批量 action**：`ExecuteBatch(IEnumerable<UiAction>)` 是否本期做？
   - 推荐：本期做，因为 map repopulate 场景会用到

确认后开始实施 Commit 1（基础设施）。
