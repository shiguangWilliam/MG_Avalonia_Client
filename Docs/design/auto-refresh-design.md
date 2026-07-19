# Auto-Refresh 设计文档：UI 操作后统一刷新机制

> **状态**：待审批。批准后再实施。
> **作者**：根据用户描述的 bug（设置颜色后位置节点不刷新颜色）整理。

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

调研结果显示当前有 **5 处独立的"refresh"路径**，互不统一：

| 调用点 | 文件 | 刷新范围 |
|---|---|---|
| `RefreshMapStartMarkersAndPlayerUi` | `MainWindow.axaml.cs:1163` | 完整（map markers + player UI + launch button） |
| `LobbyPlayerBindingApplier.SyncUiFromState` | `LobbyPlayerBindingApplier.cs:431` | 仅 player dropdowns |
| `ApplyLobbyData` | `MainWindow.axaml.cs:947` | 完整（含数据绑定重做） |
| `RefreshCnCNetGameRoomPlayers` | `MainWindow.axaml.cs:1249` | player UI + status |
| `UpdateLaunchButtonState` | `MainWindow.axaml.cs:1421` | 仅 launch button |

**每个 UI 事件回调各自决定调哪个 refresh**，所以颜色改了不会触发 `RefreshMapStartMarkersAndPlayerUi`。这就是用户感知的 bug。

## 2. 方案评估

用户提出两种方案，下面是详细评估。

### 方案 A：每个 action 内手动补 refresh

```csharp
// LobbyPlayerBindingApplier.SyncOptionsFromUi
ddColor.SelectionChanged += () => {
    SyncOptionsFromUi();
    RefreshMapStartMarkersAndPlayerUi();   // ← 补
    UpdateLaunchButtonState();             // ← 补
    RefreshCnCNetGameListing();            // ← 补
};
```

| 维度 | 评分 | 说明 |
|---|---|---|
| 实现成本 | ★★★★★ | 5 分钟，改 5-10 处 |
| 短期正确性 | ★★★★☆ | 现有 bug 立即消失 |
| 长期可维护 | ★☆☆☆☆ | **每个新 action 都要记得补**，遗漏就回归 |
| 扩展性 | ★☆☆☆☆ | 新增 refresh 类型时所有 action 都要改 |
| 测试难度 | ★★★☆☆ | 每个 action 单测都要验证所有 refresh 调用 |

**适用场景**：紧急 hotfix、实验性功能验证、行将弃用的代码。

**风险**：3 个月后新加一个 action（比如"重置槽位"），开发者忘了调 refresh，同样的 bug 回归。

### 方案 B：抽象 `LobbyAction` 基类 + 统一执行入口

核心思想：**所有 lobby 操作都走同一个执行管道**，管道末尾自动跑全套 refresh。

```csharp
// 抽象层
public abstract class LobbyAction
{
    public abstract void Execute(LobbyActionContext ctx);
}

public sealed class LobbyActionContext
{
    public LobbyPlayerState PlayerState { get; init; }
    public UiNodeViewModel Root { get; init; }
    public CnCNetSessionService Session { get; init; }
    public GameResourceCatalog Resources { get; init; }
    // ... 其他依赖
}

public sealed class LobbyActionExecutor
{
    private readonly LobbyActionContext _ctx;
    private readonly Action<LobbyActionContext> _refreshAll;

    public LobbyActionExecutor(LobbyActionContext ctx, Action<LobbyActionContext> refreshAll)
    {
        _ctx = ctx;
        _refreshAll = refreshAll;
    }

    public void Execute(LobbyAction action)
    {
        action.Execute(_ctx);
        _refreshAll(_ctx);   // ← 统一 refresh，不可能漏
    }
}
```

具体 action 用 override 实现：

```csharp
public sealed class SetPlayerColorAction : LobbyAction
{
    private readonly int _slotIndex;
    private readonly int _colorIndex;

    public SetPlayerColorAction(int slotIndex, int colorIndex) { ... }

    public override void Execute(LobbyActionContext ctx)
    {
        ctx.PlayerState.Slots[_slotIndex].ColorIndex = _colorIndex;
        // 仅做状态变更，不刷新 UI
    }
}
```

UI 事件挂载点：

```csharp
// LobbyPlayerBindingApplier.cs
ddColor.SelectionChanged += () =>
    _executor.Execute(new SetPlayerColorAction(slotIndex, ddColor.SelectedIndex));
```

| 维度 | 评分 | 说明 |
|---|---|---|
| 实现成本 | ★★★☆☆ | 1-2 天，引入基类 + 改造现有事件挂载 |
| 短期正确性 | ★★★★☆ | 现有 bug 消失 |
| 长期可维护 | ★★★★★ | 新 action 只写状态变更，refresh 永远不漏 |
| 扩展性 | ★★★★★ | 新增 refresh 类型只改 `_refreshAll` 一处 |
| 测试难度 | ★★★★☆ | 单元测试每个 action 只验状态，集成测试验 refresh |

**额外收益**：
- **可录制/回放**：所有 lobby 操作都是对象，可以序列化做回放调试。
- **可撤销**：`LobbyAction` 加 `Undo(LobbyActionContext)` 方法即可（未来 feature）。
- **可审计**：执行管道可以统一 log，便于排查"为什么这个 PO 推到 IRC 了"。

### 方案 B 的两个子变体

用户提到"反射" vs "override"。评估如下：

| 变体 | 写法 | 优缺点 |
|---|---|---|
| **B1: override**（推荐） | 每个 action 一个 class，override `Execute` | 类型安全，IDE 可重构，**编译期检查**。代价是类多。 |
| **B2: 反射** | `[LobbyAction("SetPlayerColor")]` 标注方法，executor 通过反射找 | 类少。代价是**失去编译期检查**，重构改名易漏；反射性能稍差（虽然此处不敏感）。 |

**推荐 B1**。理由：
1. lobby action 不多（~20 个），多 20 个 class 不是负担。
2. 类型安全 + 可重构性 >> 节省的 20 行 boilerplate。
3. C# 现代实践（Roslyn、Source Generator）都推荐显式 class 而非反射。
4. 单元测试更直接：`new SetPlayerColorAction(1, 2).Execute(ctx)` 一行测完。

## 3. 推荐方案：B1（LobbyAction 基类 + override）

### 3.1 类图

```
┌──────────────────────────┐
│   LobbyAction (abstract) │
├──────────────────────────┤
│ + Execute(ctx) : void    │  ← 子类 override
│ + Undo(ctx)   : void     │  ← 可选，未来扩展
└──────────────────────────┘
            ▲
            │
   ┌────────┼────────────┬───────────────┐
   │        │            │               │
┌──┴───┐ ┌──┴────────┐ ┌─┴──────────┐ ┌──┴──────────┐
│SetPla│ │SetPlayerSi│ │SetPlayerTea│ │AssignStart  │
│yerCo-│ │deAction   │ │mAction     │ │LocationAction│
│lorAc-│ │           │ │            │ │             │
│tion  │ │           │ │            │ │             │
└──────┘ └───────────┘ └────────────┘ └─────────────┘
   ...
约 15-20 个具体 action
```

### 3.2 LobbyActionContext 完整定义

```csharp
public sealed class LobbyActionContext
{
    // 状态
    public LobbyPlayerState PlayerState { get; init; } = null!;
    public LobbySessionState Session { get; init; } = null!;

    // 资源
    public GameResourceCatalog Resources { get; init; } = null!;
    public ResourceResolver ResourceResolver { get; init; } = null!;

    // UI 根
    public UiNodeViewModel Root { get; init; } = null!;

    // 网络会话（host 推送 PO 用）
    public CnCNetSessionService CnCNet { get; init; } = null!;

    // Behavior registry（用于触发 side-effect behavior）
    public BehaviorRegistry Behaviors { get; init; } = null!;

    // 当前窗口名（用于判断是 skirmish 还是 cncnet）
    public string WindowName { get; init; } = string.Empty;
}
```

### 3.3 LobbyActionExecutor 完整定义

```csharp
public sealed class LobbyActionExecutor
{
    private readonly LobbyActionContext _ctx;

    // refresh pipeline：按顺序执行，每一步可独立失败
    private readonly Action<LobbyActionContext>[] _refreshSteps;

    public LobbyActionExecutor(LobbyActionContext ctx, Action<LobbyActionContext>[] refreshSteps)
    {
        _ctx = ctx;
        _refreshSteps = refreshSteps;
    }

    public void Execute(LobbyAction action)
    {
        try
        {
            action.Execute(_ctx);
        }
        catch (Exception ex)
        {
            Logger.Log($"LobbyAction {action.GetType().Name} threw: {ex}");
            throw;
        }

        // Unified refresh: same pipeline for every action, no omissions possible.
        foreach (Action<LobbyActionContext> step in _refreshSteps)
        {
            try { step(_ctx); }
            catch (Exception ex) { Logger.Log($"Refresh step threw: {ex}"); }
        }
    }

    // Test seam: execute without refresh (for unit-testing action state changes only).
    internal void ExecuteWithoutRefresh(LobbyAction action) => action.Execute(_ctx);
}
```

### 3.4 Refresh Pipeline 装配（启动时）

```csharp
// MainWindow 构造时
var ctx = new LobbyActionContext {
    PlayerState = _lobbySession.PlayerState,
    Session = _lobbySession,
    Resources = _gameResources,
    ResourceResolver = _mainEngine!.Resources,
    Root = vm,
    CnCNet = CnCNetSessionService.Instance,
    Behaviors = _mainBehaviors,
    WindowName = windowName,
};

var executor = new LobbyActionExecutor(ctx, refreshSteps: new Action<LobbyActionContext>[] {
    RefreshPlayerUiStep,          // LobbyPlayerBindingApplier.Apply
    RefreshMapStartMarkersStep,   // GameDataBindingApplier.RefreshMapStartMarkers
    RefreshLaunchButtonStep,      // UpdateLaunchButtonState
    RefreshCnCNetGameListingStep, // 仅 cncnet 模式生效
    RefreshBroadcastStep,         // 推送 PO 到 IRC（仅 host）
});

// 注入到 binding applier
_lobbyBindingApplier.Bind(vm, executor);
```

### 3.5 现有事件挂载点的改造

| 文件 / 行号 | 现状 | 改造后 |
|---|---|---|
| `LobbyPlayerBindingApplier.cs:423-428` `ddColor.SelectionChanged += SyncOptionsFromUi` | 直接调 sync | `_executor.Execute(new SetPlayerColorAction(slotIndex, ddColor.SelectedIndex))` |
| `MainWindow.axaml.cs:1056` `OnMapStartMarkerLeftClicked` | 直接 mutate state + refresh | `_executor.Execute(new AssignStartLocationAction(slotIndex, startLocation))` |
| `MainWindow.axaml.cs:1010` `lbMapList.SelectionChanged` | 直接 update + refresh | `_executor.Execute(new SelectMapAction(...))` |
| `MainWindow.axaml.cs:1616` `OnHostGameOptionControlChanged` | 标记 dirty | `_executor.Execute(new SetGameOptionAction(...))` |
| ... | ... | ... |

约 12-15 个挂载点改造。

### 3.6 渐进迁移策略

不要一次性改造。建议分 3 个 commit：

1. **Commit 1（基线）**：引入 `LobbyAction` / `LobbyActionContext` / `LobbyActionExecutor`，先**不接入任何事件**。加单元测试验证空 action 触发 refresh pipeline。
2. **Commit 2（接入 lobby）**：把 `LobbyPlayerBindingApplier` 的 5 个 dropdown 事件改为 action。修复用户报告的颜色 bug。
3. **Commit 3（接入 map / options）**：把 `OnMapStartMarkerLeftClicked` / `OnHostGameOptionControlChanged` / `lbMapList.SelectionChanged` 改为 action。

每个 commit 都跑 `ThreeModCompatibilityTests` + 新加的 `LobbyActionTests`。

## 4. 测试策略

### 4.1 Action 单元测试（无 UI）

```csharp
[Fact]
public void SetPlayerColorAction_Updates_StateColorIndex()
{
    var ctx = NewTestContext();
    var executor = ctx.NewExecutorWithoutRefresh();

    executor.ExecuteWithoutRefresh(new SetPlayerColorAction(slotIndex: 1, colorIndex: 3));

    ctx.PlayerState.Slots[1].ColorIndex.Should().Be(3);
}
```

### 4.2 Refresh Pipeline 集成测试

```csharp
[Fact]
public void SetPlayerColorAction_Triggers_MapMarkerRefreshStep()
{
    var ctx = NewTestContext();
    bool refreshCalled = false;
    var executor = new LobbyActionExecutor(ctx, new Action<LobbyActionContext>[] {
        _ => refreshCalled = true
    });

    executor.Execute(new SetPlayerColorAction(1, 3));

    refreshCalled.Should().BeTrue("color change must refresh map markers");
}
```

### 4.3 端到端测试（模拟用户操作顺序）

```csharp
[Fact]
public void Color_After_StartLocation_Assignment_Refreshes_MarkerColor()
{
    // 重现用户报告的 bug：先点 start marker 再改 color，marker 颜色必须立即变化
    var ctx = NewTestContext();
    var executor = ctx.NewFullExecutor();

    executor.Execute(new AssignStartLocationAction(slotIndex: 0, startLocation: 1));
    executor.Execute(new SetPlayerColorAction(slotIndex: 0, colorIndex: 2));  // red

    ctx.LatestMarkerColor(1).Should().Be(2, "marker color must reflect latest state");
}
```

## 5. 风险与缓解

| 风险 | 缓解 |
|---|---|
| refresh 步骤之间有时序依赖（如 player UI 必须先于 marker） | `_refreshSteps` 数组顺序就是执行顺序，加 XML 注释 |
| 某些 action 不应触发某些 refresh（如 joiner 改 color 不应触发 PO broadcast） | `LobbyAction` 加 `virtual bool ShouldTrigger<TStep>() => true`，子类按需 override |
| 多个 action 连续触发导致 refresh 风暴（如批量 repopulate） | `ExecuteBatch(IEnumerable<LobbyAction>)`，只 refresh 一次 |
| 与 IRC 协议交互复杂（host vs joiner 路径不同） | broadcast step 内部判断 `IsHost`，与现状一致 |

## 6. 实施工作量预估

| 阶段 | 工时 | 产出 |
|---|---|---|
| 1. 基础设施 | 4h | `LobbyAction` + `Executor` + `Context` + 基础测试 |
| 2. Lobby 接入 | 4h | 5 个 dropdown 改 action，用户 bug 修复 |
| 3. Map / options 接入 | 6h | map marker + game options 全部走 executor |
| 4. 测试覆盖 | 4h | 20+ 个 action 单测 + 集成测试 |
| **总计** | **~18h**（2-3 个工作日） | |

## 7. 结论

**推荐方案 B1**（`LobbyAction` 基类 + override）。理由：
1. 根除"忘了补 refresh"这一类 bug。
2. 比 A 多花 1-2 天，但消除 5 处散点 refresh 的技术债。
3. 为 undo / replay / audit 等未来 feature 打基础。
4. 类型安全，便于 IDE 重构。

如果短期资源紧张，可以先做方案 A 的部分接入（仅改 color / start location 路径）作为 hotfix，再排期做方案 B 重构。但**不建议长期保留方案 A**。

---

**请审批以下问题后开始实施**：
1. 选 A（快速 hotfix）、B1（推荐重构）、还是 B2（反射）？
2. 若选 B1，是否同意分 3 个 commit 渐进迁移？
3. `LobbyActionContext` 字段是否齐全（见 §3.2）？
