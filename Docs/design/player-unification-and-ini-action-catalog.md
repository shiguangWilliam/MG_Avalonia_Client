# 玩家状态统一 + UIAction 行为绑定表 设计文档

> **日期**：2026-07-20
> **范围**：`ClientAvalonia` Session 域、IniUi/Actions 域、MainWindow 中相关胶水代码
> **目标**：解决两个根因——
>   1. 三套玩家状态并存（`LobbyPlayerState` ↔ `ISkirmishSession.PlayerSlots` ↔ `CnCNetGameRoomPlayer`）
>   2. View 与行为耦合（按控件 ID 硬绑 Behavior；INI 声明式 `$LeftClickAction` 几乎未用）
> **关联**：`mainwindow-analysis.md`、`architecture-evaluation-l1.md` §3.7

---

## 0. TL;DR

- **第一项（玩家状态统一）**：以 `IGameSession.PlayerSlots` 为唯一真相源；`CnCNetGameRoomPlayer` 降为编解码 DTO；`LobbyPlayerState.Slots` 改为对 Session 的只读投影，自身只留目录缓存与 UI 输入态。
- **第二项（UIAction 行为绑定表）**：引入一个**轻量**的 `IIniActionCatalog`（动作名 → 工厂），把 INI 声明的 `$LeftClickAction` 字符串接到现有 `ActionExecutor` 上。**不是新引擎**，只是接通已存在的两块（INI 声明 vs Action 执行）。

---

## 第一部分：玩家状态统一

### 1.1 现状（三套并存）

| 层 | 类型 | 持有方 | 字段命名 |
|----|------|--------|----------|
| UI | `LobbyPlayerState` + `LobbyPlayerSlot[]`（8 槽） | `MainWindow._lobbySession`、`SkirmishSession.Player` | `SideIndex / ColorIndex / TeamIndex / StartIndex` |
| Session | `IPlayerSlot[]`（L1 抽象） | `SkirmishSession.PlayerSlots` / `CnCNetGameRoomSession._playerSlots` | 同上（接口投影） |
| Network | `List<CnCNetGameRoomPlayer>` | `CnCNetGameRoomSession._players` | `SideId / ColorId / TeamId / StartingLocation / Ready / AutoReady / Ping / Port` |

**症状**：
- `MultiplayerSlotLayout.ApplyToState` / `SyncPlayersFromLobby` 在三套之间来回拷贝。
- MainWindow 的 `ApplyCnCNetGameRoomPlayersCore` + `_applyingCnCNetGameRoomPlayers` 重入保护，本质是在阻止「UI→Session→网络→Session→UI」的死循环。
- `LobbyPlayerState.Slots` 与 `CnCNetGameRoomSession._playerSlots` 是两份独立副本，必须显式同步。

### 1.2 目标模型

```
                 ┌──────────────────────────────────┐
                 │  IGameSession.PlayerSlots         │  ← 唯一真相源
                 │  (Skirmish / CnCNet)              │
                 └────────────┬─────────────────────┘
        UI 读写（Action）    │             网络读写（Codec）
              │               │                    │
              ▼               │                    ▼
   LobbyPlayerBindingApplier  │       CnCNetGameRoomPlayer（瞬时 DTO）
   直接绑 IPlayerSlot         │       仅用于 PO CTCP 编解码
                              │
            LobbyPlayerState 降级：
              - SideNames / AiNames / TeamNames（目录缓存）
              - Mode / LocalPlayerName / HostPlayerName
              - 筛选 / 搜索文本等 UI 输入态
              - 不再持有 Slots[] 副本
```

### 1.3 接口扩展

#### 1.3.1 新增 `ICnCNetPlayerSlot`

```csharp
// ClientAvalonia/Session/ICnCNetPlayerSlot.cs
namespace ClientAvalonia.Session;

/// <summary>
/// CnCNet 房间槽位：在 IPlayerSlot 基础上增加网络协议字段。
/// 这些字段不参与 Skirmish（本地遭遇战无 Ready/Ping/Port 概念）。
/// </summary>
public interface ICnCNetPlayerSlot : IPlayerSlot
{
    /// <summary>是否房主（CTCP PO 的 host 标记）。</summary>
    bool IsHost { get; set; }

    /// <summary>本机玩家是否已准备（CTCP READY）。</summary>
    bool Ready { get; set; }

    /// <summary>是否启用自动准备（joiner 端 UI 选项）。</summary>
    bool AutoReady { get; set; }

    /// <summary>网络延迟（毫秒，-1 = 未知）。</summary>
    int Ping { get; set; }

    /// <summary>NAT 端口（tunnel 分配）。</summary>
    ushort Port { get; set; }
}
```

#### 1.3.2 默认实现：让 `LobbyPlayerSlot` 同时实现两个接口

```csharp
// 修改 ClientAvalonia/Domain/LobbyPlayerSlot.cs
public sealed class LobbyPlayerSlot : IPlayerSlot, ICnCNetPlayerSlot
{
    // ... 已有字段 ...

    // 新增（CnCNet 专用，默认值与现有 CnCNetGameRoomPlayer 默认值对齐）
    public bool IsHost { get; set; }
    public bool Ready { get; set; }
    public bool AutoReady { get; set; }
    public int Ping { get; set; } = -1;
    public ushort Port { get; set; }

    public override void Clear()
    {
        // ... 已有清理 ...
        IsHost = false;
        Ready = false;
        AutoReady = false;
        Ping = -1;
        Port = 0;
    }
}
```

**理由**：`LobbyPlayerSlot` 已经是默认实现；让它同时实现两个接口，可以零成本复用，避免新建并行类型。`Clear()` 也要重置新字段。

### 1.4 CnCNetGameRoomSession 重构

#### 1.4.1 删除并行 `_players` 列表

**现状**：
```csharp
private readonly List<CnCNetGameRoomPlayer> _players = [];
private readonly LobbyPlayerSlot[] _playerSlots = ...;
public IReadOnlyList<CnCNetGameRoomPlayer> Players => _players;
public IReadOnlyList<IPlayerSlot> PlayerSlots => _playerSlots;
```

**目标**：
```csharp
private readonly ICnCNetPlayerSlot[] _playerSlots = ...;
// 不再有 _players 长期列表
public IReadOnlyList<IPlayerSlot> PlayerSlots => _playerSlots;
public IReadOnlyList<ICnCNetPlayerSlot> CnCNetPlayerSlots => _playerSlots;

// Players 属性保留为「投影」，仅用于向后兼容（如 GameLaunchSessions）
public IEnumerable<CnCNetGameRoomPlayer> Players => _playerSlots
    .Where(s => s.IsOccupied)
    .Select(s => new CnCNetGameRoomPlayer
    {
        Name = s.Name,
        IsHost = s.IsHost,
        IsAi = s.IsAi,
        // ... 其余字段
    });
```

**注意**：`Players` 改为 `IEnumerable` 投影可能影响现有调用点（LINQ 查询等）。迁移时逐一检查。

#### 1.4.2 PO 编解码抽到 `PlayerOptionsCodec`

新建 `ClientAvalonia/CnCNet/Protocol/PlayerOptionsCodec.cs`：

```csharp
namespace ClientAvalonia.CnCNet.Protocol;

/// <summary>
/// PlayerOptions (PO) CTCP 消息 ↔ 槽位数组的双向转换。
/// 纯函数，无状态，可单测。
/// </summary>
public static class PlayerOptionsCodec
{
    /// <summary>把 Session 槽位编码成 PO DTO 列表（用于 CTCP 广播）。</summary>
    public static IReadOnlyList<CnCNetGameRoomPlayer> ToDto(
        IReadOnlyList<ICnCNetPlayerSlot> slots, string hostName);

    /// <summary>把收到的 PO DTO 应用到 Session 槽位（覆盖式）。</summary>
    public static void ApplyDto(
        IReadOnlyList<CnCNetGameRoomPlayer> dto,
        IList<ICnCNetPlayerSlot> slots,
        string localNick);

    /// <summary>判断两个列表是否等价（用于避免无变化的广播）。</summary>
    public static bool AreEquivalent(
        IReadOnlyList<CnCNetGameRoomPlayer> a,
        IReadOnlyList<CnCNetGameRoomPlayer> b);
}
```

**理由**：把现在散落在 `MultiplayerSlotLayout` + `CnCNetGameRoomSession.SyncPlayersFromLobby` 的双向拷贝逻辑收敛成纯函数，可独立单测。

#### 1.4.3 `SyncPlayersFromLobby` 重写

```csharp
// Host 路径：UI 改槽位 → 直接写 _playerSlots → 编码 PO 广播
public void SyncFromSlotsAndBroadcast(string hostName)
{
    if (!IsHost) return;
    var dto = PlayerOptionsCodec.ToDto(_playerSlots, hostName);
    // 复用 AreEquivalent 判断是否真的变了
    // ... broadcast ...
}
```

收 PO 路径（joiner）：
```csharp
public void ApplyReceivedPo(IReadOnlyList<CnCNetGameRoomPlayer> dto, string localNick)
{
    PlayerOptionsCodec.ApplyDto(dto, _playerSlots, localNick);
    StateChanged?.Invoke();  // UI 经事件刷新
}
```

### 1.5 LobbyPlayerState 降级

**移除**：`LobbyPlayerSlot[] Slots { get; }` 作为独立存储。

**替换为**：
```csharp
public sealed class LobbyPlayerState
{
    private readonly Func<IReadOnlyList<IPlayerSlot>> _slotsProvider;

    public LobbyPlayerState(Func<IReadOnlyList<IPlayerSlot>> slotsProvider)
    {
        _slotsProvider = slotsProvider;
    }

    /// <summary>当前槽位（投影自 Session，非独立副本）。</summary>
    public IReadOnlyList<IPlayerSlot> Slots => _slotsProvider();

    // 保留：目录缓存、Mode、LocalPlayerName、HostPlayerName、PlayerUpdatingInProgress
    // 保留：HumanRowCount / AiRowCount 等 LINQ 投影属性（基于 Slots 计算）

    // 移除：ClearSlots / LoadDefaultSkirmishSlots（改为操作 Session）
}
```

**迁移策略**：
- `SkirmishSession` 构造时传入 `() => Player.Slots`（即自身）。
- 旧调用点（`ClearSlots`、`LoadDefaultSkirmishSlots`）改为通过 `DefaultAiSlotPolicy` / 新 Action 直接写 `session.PlayerSlots[i]`。

### 1.6 MainWindow 拆胶水

`ApplyCnCNetGameRoomPlayersCore` 等 4 个方法删除，改由：
- Session 的 `StateChanged` 事件触发 UI 刷新（`LobbyPlayerBindingApplier.Apply` 已经支持）。
- 删除 `_applyingCnCNetGameRoomPlayers` 字段——`PlayerUpdatingInProgress` 已在 `LobbyPlayerState` 提供（迁到 Session 级 `ISlotSyncGuard` 或直接复用）。

### 1.7 验收标准

- `MainWindow` 内 `CnCNetGameRoomPlayer` 引用：**6 → 0**
- `MainWindow` 内 `((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore` 引用：**4 → 0**
- `MainWindow` 内 `_applyingCnCNetGameRoomPlayers`：**删除**
- `CnCNetGameRoomSession._players` 长期列表：**删除**
- `MultiplayerSlotLayout` 双向拷贝逻辑：**迁入 `PlayerOptionsCodec`**
- 新增 `PlayerOptionsCodecTests`（≥ 8 用例）
- 全量测试不破（479 → 目标 ≥ 490）

---

## 第二部分：UIAction 行为绑定表

### 2.1 设计原则

**核心约束**：这一层**不是新引擎**，只是把已存在的两块接通——

```
INI 声明                          已有执行管道
─────────                         ──────────────
$LeftClickAction=LaunchSkirmish   UiAction + ActionExecutor
       ↓                                  ↑
       └──── IIniActionCatalog ───────────┘
             （要补的这一层）
```

**职责**：
- 只做：动作名字符串 → 构造对应 `UiAction` → 交给 `ActionExecutor`
- 不做：布局、Session 同步、IRC、按控件 ID 找节点

**边界**：仍然保留 `BehaviorRegistry`（按 ID 注册），因为它处理的是「事件 → Action 构造」这一步，而 `IIniActionCatalog` 处理的是「动作名 → Action 工厂」这一步。两者互补，不替代。

### 2.2 接口定义

```csharp
// ClientAvalonia/IniUi/Actions/IIniActionCatalog.cs
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// INI 动作名 → UiAction 工厂的注册表。
///
/// 作用：把 INI 里 $LeftClickAction=LaunchSkirmish 这类声明接到
/// 现有 UiAction / ActionExecutor 上。Mod 可通过 INI 把任意按钮
/// 绑到已注册的动作名，无需改 C# 代码。
///
/// 设计原则：
///   - 字符串名大小写不敏感（与 DX INI 习惯一致）
///   - 后注册的覆盖先注册的（与 BehaviorRegistry 一致）
///   - 未注册的名返回 false（让调用方 fallback 到 ID 匹配）
/// </summary>
public interface IIniActionCatalog
{
    /// <summary>注册动作名 → 工厂。</summary>
    /// <param name="actionName">INI 里写的名字（大小写不敏感）。</param>
    /// <param name="factory">
    /// 给定触发源控件和上下文，返回新的 UiAction 实例。
    /// 工厂内不应执行副作用——执行由 ActionExecutor 负责。
    /// </param>
    void Register(string actionName, Func<UiNodeViewModel, UiActionContext, UiAction> factory);

    /// <summary>尝试按动作名构造并执行 Action。返回是否命中注册。</summary>
    bool TryDispatch(string actionName, UiNodeViewModel source, UiActionContext context);

    /// <summary>查询动作名是否已注册（用于测试 / 诊断）。</summary>
    bool IsRegistered(string actionName);
}
```

### 2.3 默认实现

```csharp
// ClientAvalonia/IniUi/Actions/IniActionCatalog.cs
public sealed class IniActionCatalog : IIniActionCatalog
{
    private readonly Dictionary<string, Func<UiNodeViewModel, UiActionContext, UiAction>> _factories
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string actionName, Func<UiNodeViewModel, UiActionContext, UiAction> factory)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            throw new ArgumentException("Action name must not be empty.", nameof(actionName));
        _factories[actionName] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public bool TryDispatch(string actionName, UiNodeViewModel source, UiActionContext context)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return false;
        if (!_factories.TryGetValue(actionName, out var factory))
            return false;

        UiAction action = factory(source, context);

        // ActionExecutor 是泛型类；这里用非泛型入口包装。
        // 简化策略：所有 INI 注册的 Action 都是非泛型 UiAction（无类型化 Context），
        // 通过 UiActionContext 派生类访问字段。
        // 见 §2.5 关于 ActionExecutor 改造。
        return ActionDispatcher.Execute(action, context);
    }

    public bool IsRegistered(string actionName)
        => !string.IsNullOrWhiteSpace(actionName) && _factories.ContainsKey(actionName);
}
```

### 2.4 ActionDispatcher：非泛型执行入口

为避免每个 Action 都要绑定一个具体的 `TContext`，新增非泛型入口：

```csharp
// ClientAvalonia/IniUi/Actions/ActionDispatcher.cs
public static class ActionDispatcher
{
    private static readonly List<Action<ActionExecutorRun>> _globalRefresh = new();

    /// <summary>执行任意 UiAction，应用全局 refresh 管道。</summary>
    public static bool Execute(UiAction action, UiActionContext context)
    {
        if (action == null) return false;
        try
        {
            action.Execute(context);
            Logger.Log($"[IniAction] {action.DisplayName}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[IniAction] {action.DisplayName} threw: {ex}");
            return false;
        }

        foreach (var step in _globalRefresh)
        {
            try { step(new ActionExecutorRun(action, context)); }
            catch (Exception ex) { Logger.Log($"[IniAction] refresh threw: {ex}"); }
        }
        return true;
    }

    public static void RegisterRefresh(Action<ActionExecutorRun> step) => _globalRefresh.Add(step);

    public sealed record ActionExecutorRun(UiAction Action, UiActionContext Context);
}
```

**说明**：
- 这是现有泛型 `ActionExecutor<TAction,TContext>` 的**非泛型薄包装**。
- 现有 `LobbyAction` / `ChangeMapAction` 仍走泛型路径（带类型化 refresh），不受影响。
- INI 注册的 Action 大多无自定义 refresh 需求，走 `ActionDispatcher` 即可。

### 2.5 扩展 IniBehaviorApplier

**现状**：只处理 `DISABLE` 一种 `$LeftClickAction`。

**目标**：

```csharp
private static void ApplyIniClickAction(
    UiNodeViewModel root, UiNodeViewModel vm,
    BehaviorRegistry registry, IUiNavigationHost host,
    IIniActionCatalog actionCatalog,    // 新增参数
    UiActionContext actionContext)      // 新增参数
{
    if (!TryGetIniAction(vm, out string? action))
        return;

    string name = action.Trim().ToUpperInvariant();
    if (name == "DISABLE")
    {
        registry.RegisterAfter(vm.Id, _ => DisableRoot(root, host, vm.Id));
        return;
    }

    // 命中 catalog：注册一个点击 handler，派发到对应 Action。
    if (actionCatalog.IsRegistered(name))
    {
        registry.Register(vm.Id, _ => actionCatalog.TryDispatch(name, vm, actionContext));
    }
    // 未命中：忽略（与 DX 行为一致，避免 crash）
}
```

**调用点**：`MainWindow.NavigateTo` 在 `IniBehaviorApplier.Apply` 处传入 catalog + context（从 `EnvironmentServices.Resolve`）。

### 2.6 Action 注册（首批）

落地时注册以下动作（覆盖 MainMenu + 关键 Lobby 入口）：

| Action 名 | 类 | 触发场景 | 旧调用 |
|-----------|-----|----------|--------|
| `LaunchSkirmish` | `LaunchSkirmishAction` | btnLaunchGame（SkirmishLobby） | `host.TryLaunchSkirmish` |
| `LaunchCampaign` | `LaunchCampaignAction` | btnLaunchGame（CampaignSelector） | `host.TryLaunchCampaign` |
| `ExitApplication` | `ExitApplicationAction` | btnExit | `host.ExitApplication` |
| `CheckForUpdates` | `CheckForUpdatesAction` | lblUpdateStatus | `host.CheckForUpdates` |
| `OpenChangelogUrl` | `OpenChangelogUrlAction` | lblVersion | 现内联 |
| `NavigateTo` | `NavigateToAction`（带 `Window` 属性） | btnSkirmish / btnOptions / ... | 现内联 |

`NavigateTo` 是参数化动作（需要目标窗口名）。两种实现方式：
- **方案 a**：INI 写 `$LeftClickAction=NavigateTo:SkirmishLobby`，工厂解析冒号后的参数。
- **方案 b**：保留 `BehaviorRegistry` ID 匹配作为 fallback；只有无参动作走 catalog。

**推荐 a**：让工厂收到完整字符串后自行解析。`IIniActionCatalog` 加一个 overload：

```csharp
void Register(string actionName, Func<string, UiNodeViewModel, UiActionContext, UiAction> factoryWithArgs);
```

第一个 `string` 是 `$LeftClickAction` 的完整原始值（含冒号后参数）。工厂决定是否解析。

### 2.7 与 BehaviorRegistry 的关系

| 维度 | BehaviorRegistry | IIniActionCatalog |
|------|------------------|--------------------|
| 触发源 | 控件 ID 字符串 | INI `$LeftClickAction` 值 |
| 注册时机 | C# 启动 / 窗口加载时 | C# 启动时（一次） |
| Mod 可改 | 改 ID 会失效 | 改 ID 不影响（看 INI 声明） |
| 适用 | 复杂交互（拖拽、hover、双击） | 简单点击意图 |
| 关系 | 仍保留；复杂 Behavior 留在这里 | 与之并存，简单点击优先走 catalog |

**迁移策略**：旧 `registry.Register("btnXxx", ...)` 暂时保留，逐个改成 INI 声明 + catalog 注册。每改一个跑全测。

### 2.8 注册流程（PreStartup）

```csharp
// ClientAvalonia/Core/PreStartup.cs
private static void RegisterIniActionCatalog()
{
    var catalog = new IniActionCatalog();

    // 简单无参动作
    catalog.Register("ExitApplication", (_, ctx) => new ExitApplicationAction());
    catalog.Register("CheckForUpdates", (_, ctx) => new CheckForUpdatesAction());
    catalog.Register("OpenChangelogUrl", (_, ctx) => new OpenChangelogUrlAction());

    // 带参数动作
    catalog.Register("NavigateTo", (raw, _, _) =>
    {
        string target = raw.Contains(':') ? raw[(raw.IndexOf(':') + 1)..].Trim() : string.Empty;
        return new NavigateToAction(target);
    });

    catalog.Register("LaunchSkirmish", (_, ctx) => new LaunchSkirmishAction());
    catalog.Register("LaunchCampaign", (_, ctx) => new LaunchCampaignAction());

    EnvironmentServices.Register<IIniActionCatalog>(() => catalog);
}
```

### 2.9 验收标准

- `IIniActionCatalog`、`IniActionCatalog`、`ActionDispatcher` 实现，覆盖率 ≥ 90%
- 至少 5 个 INI Action 落地（`ExitApplication` / `CheckForUpdates` / `OpenChangelogUrl` / `NavigateTo` / `LaunchSkirmish`）
- `IniBehaviorApplier` 能识别 catalog 注册的动作名
- Mod 可写 `$LeftClickAction=NavigateTo:SkirmishLobby` 把任意按钮绑到导航
- 全量测试不破

---

## 第三部分：落地步骤

### Phase 1：玩家状态统一（独立可发布）

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1.1 | 新增 `ICnCNetPlayerSlot` 接口 | — |
| 1.2 | `LobbyPlayerSlot` 实现两个接口 + 扩 `Clear()` | 1.1 |
| 1.3 | 新增 `PlayerOptionsCodec`（从 `MultiplayerSlotLayout` 抽出） | 1.1 |
| 1.4 | 单测 `PlayerOptionsCodecTests`（≥ 8 用例） | 1.3 |
| 1.5 | `CnCNetGameRoomSession` 删 `_players`，`_playerSlots` 改为 `ICnCNetPlayerSlot[]`，重写 `SyncPlayersFromLobby` | 1.1–1.3 |
| 1.6 | `LobbyPlayerState` 降级为投影 + 目录缓存 | 1.5 |
| 1.7 | `MainWindow` 删除 `ApplyCnCNetGameRoomPlayers*` 4 个方法 + `_applyingCnCNetGameRoomPlayers` | 1.5–1.6 |
| 1.8 | 全量测试 + 修复回归 | 1.7 |

**预计**：2 天。**风险**：1.5 最复杂，需小心保留 `Players` 投影兼容性。

### Phase 2：UIAction 行为绑定表（独立可发布）

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 2.1 | `IIniActionCatalog` + `IniActionCatalog` + `ActionDispatcher` | — |
| 2.2 | 单测 `IniActionCatalogTests` + `ActionDispatcherTests`（≥ 12 用例） | 2.1 |
| 2.3 | 实现 5 个首批 Action 类 | 2.1 |
| 2.4 | 单测 5 个 Action | 2.3 |
| 2.5 | 扩展 `IniBehaviorApplier` 接 `IIniActionCatalog` | 2.1 |
| 2.6 | `PreStartup.RegisterIniActionCatalog` 注册 | 2.1, 2.3 |
| 2.7 | 迁移 `MainMenuBehaviors` 中 5 个按钮到 INI 声明 | 2.5, 2.6 |
| 2.8 | 全量测试 + 修复回归 | 2.7 |

**预计**：1.5 天。**风险**：低。

### Phase 3：验证与报告

- 跑全量测试（含 Phase 1/2 新增）
- 用 coverlet 收集覆盖率，目标：新增代码 ≥ 85%
- 写重构报告（玩家状态统一前后对比 + Action 表落地效果）

---

## 第四部分：测试策略

### 4.1 玩家状态统一测试

| 文件 | 用例数 | 覆盖点 |
|------|--------|--------|
| `PlayerOptionsCodecTests` | ≥ 8 | ToDto / ApplyDto 双向、AreEquivalent 边界、空列表、超长列表 |
| `LobbyPlayerSlotTests`（新） | ≥ 4 | ICnCNetPlayerSlot 字段默认值、Clear 重置 |
| `CnCNetGameRoomSessionTests`（扩） | ≥ 4 | PlayerSlots 唯一真相源、SyncFromSlotsAndBroadcast、ApplyReceivedPo |

### 4.2 UIAction 表测试

| 文件 | 用例数 | 覆盖点 |
|------|--------|--------|
| `IniActionCatalogTests` | ≥ 8 | Register / TryDispatch / IsRegistered / 大小写不敏感 / 覆盖注册 / 空名 |
| `ActionDispatcherTests` | ≥ 4 | Execute 成功/异常/refresh 顺序/refresh 异常隔离 |
| `NavigateToActionTests` | ≥ 3 | 参数解析、空参数、目标窗口派发 |
| `LaunchSkirmishActionTests` 等 | ≥ 5 | 各 Action 执行 + 调用 Service 验证 |

### 4.3 端到端验证

- 现有 479 用例全绿
- 新增 ≥ 36 用例
- 总数 ≥ 515

---

## 第五部分：风险与缓解

| 风险 | 严重度 | 缓解 |
|------|--------|------|
| `CnCNetGameRoomSession._players` 删除引发大量回归 | 🔴 高 | Phase 1.5 单独提交 + 立即跑全测；保留 `Players` 投影属性做兼容 |
| `LobbyPlayerState.Slots` 改投影破坏现有调用 | 🟡 中 | 用 `IReadOnlyList<IPlayerSlot>` 保持类型兼容；逐文件改 |
| Action 名冲突（mod 与内置同名） | 🟡 中 | 文档化保留动作名前缀（如 `Sys:`）；后注册覆盖前注册，与 DX 一致 |
| INI 行为改变影响 mod 兼容 | 🟡 中 | 保留 `BehaviorRegistry` ID 匹配作 fallback；新 INI 动作只增不删 |
| 单测跟不上 | 🟡 中 | 每个 Phase 子步骤完成后立即补单测，CI 卡覆盖率 |

---

## 第六部分：目标终态

| 指标 | 当前 | 目标 |
|------|------|------|
| `MainWindow.axaml.cs` 行数 | ~2050 | ≤ ~1900（仅 Phase 1 减胶水） |
| `CnCNetGameRoomPlayer` 在 MainWindow 的引用 | 6 | **0** |
| `((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore` 在 MainWindow | 4 | **0** |
| `_applyingCnCNetGameRoomPlayers` 字段 | 存在 | **删除** |
| `CnCNetGameRoomSession._players` 长期列表 | 存在 | **删除** |
| INI 声明的可绑动作数 | 1（DISABLE） | **≥ 6** |
| 全量测试 | 479 | **≥ 515** |
| 新增代码覆盖率 | — | **≥ 85%** |

---

## 第七部分：决策点

本设计文档已锁定以下决策（基于前几轮讨论）：

1. ✅ `LobbyPlayerSlot` 同时实现 `IPlayerSlot` + `ICnCNetPlayerSlot`，不新建并行类型
2. ✅ `CnCNetGameRoomPlayer` 降为编解码 DTO，不进 Session 长期状态
3. ✅ `LobbyPlayerState` 降级为目录缓存 + UI 输入态，`Slots` 改为投影
4. ✅ `IIniActionCatalog` 是轻量注册表，不是新引擎
5. ✅ `ActionDispatcher` 是 `ActionExecutor` 的非泛型薄包装，不替代它
6. ✅ 保留 `BehaviorRegistry` 作 fallback，渐进迁移
7. ✅ 带参数动作（如 `NavigateTo:SkirmishLobby`）由工厂解析参数

接下来按 §3 的 Phase 1 → Phase 2 → Phase 3 顺序落地。
