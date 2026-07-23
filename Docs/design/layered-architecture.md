# 分层架构总纲 (Layered Architecture)

> **日期**：2026-07-20
> **状态**：定稿，作为后续所有重构（Phase 1 延后、MainWindow 拆分、Multi-Mod 启动）的统一参照
> **范围**：明确 ClientAvalonia 整体的分层模型——View / Session / Service 三层 + 双通道（StateAction / CmdAction）+ 双事件流（StateChanged / CmdResult）
> **基线**：558 通过 / 1 预存失败 / 3 跳过

---

## 1. 三层定义

```
┌─────────────────────────────────────────────────────────────────┐
│  View 层                                                         │
│  ├── INI 模板（资产，只读）                                       │
│  ├── UiNodeTree 结构（资产，由 INI 决定）                         │
│  ├── UiNodeViewModel 控件状态（Session 投影）                    │
│  ├── LobbyPlayerBindingApplier（投影器）                         │
│  ├── BehaviorRegistry（控件 ID → 行为绑定）                       │
│  └── MainWindow.axaml.cs（壳）                                   │
└─────────────────────────────────────────────────────────────────┘
                              ▲ 只读投影
                              │ 回写（StateAction / CmdAction）
┌─────────────────────────────────────────────────────────────────┐
│  Session 层（真相源 / Truth）                                    │
│  ├── 纯领域状态：PlayerSlots / Options / Map / State             │
│  ├── 无外部依赖（不知 Network / IO / Avalonia）                  │
│  ├── 唯一写入入口：IPlayerSlotSink + 公开 mutator                │
│  └── 事件：StateChanged（粗）/ SlotFieldChanged（细）            │
└─────────────────────────────────────────────────────────────────┘
                              ▲ ▼
┌─────────────────────────────────────────────────────────────────┐
│  Service 层（Core / 外部交互）                                   │
│  ├── NetworkService：PO/GAME/GO CTCP 收发                        │
│  ├── IoService：INI/Save/Map 文件读写                            │
│  ├── UiFileService：UI 模板加载（INI→UiNodeTree）                │
│  ├── ProcessService：游戏进程启动/退出                            │
│  ├── Mode：读 Session→发送 / 接收外部→写 Session                 │
│  └── 唯一持有外部资源（IRC、文件句柄、Process）的层               │
└─────────────────────────────────────────────────────────────────┘
```

### 1.1 依赖方向（硬约束）

```
View  ───►  Session  ◄───  Service
  │           ▲
  └──►  Service（仅通过 IIniActionCatalog 派发的 CmdAction）
```

**规则**：
- **Session 不知道 View 也不知道 Service**：纯领域，零外部依赖
- **View 可以读 Session 抽象 + 调 Service 命令**：但不能直接持有 Service 的内部状态
- **Service 读 Session + 写 Session**：但不能依赖 View

**反例（禁止）**：
- ❌ Session 里出现 `IrcConnection`、`File.ReadAllText`、`Dispatcher.UIThread`
- ❌ View 里出现 `CTCP`、`IniFile`、`Process.Start`
- ❌ Service 里订阅 `UiNodeViewModel.SelectionChanged`

---

## 2. 双通道模型

用户操作分两类，性质完全不同，**不允许合并**：

| 通道 | 性质 | 例子 | 数据流 |
|------|------|------|--------|
| **StateAction（状态）** | 幂等、可逆、纯数据 | 改玩家颜色、改队伍、选地图、改选项 checkbox | `View → Session.SlotSink` |
| **CmdAction（命令）** | 非幂等、有副作用、不可逆 | 启动游戏、踢人、退出、加入房间、保存设置 | `View → Service.X.Run() → 读写 Session` |

### 2.1 灰色地带判定规则

| 操作 | 通道 | 理由 |
|------|------|------|
| 选地图 | State | 可改回（点别的地图）；纯数据 |
| 启动游戏 | Command | 启 Syringe 进程；不可逆 |
| 改玩家颜色 | State | 纯数据 |
| 踢人 | Command | 发 KICK CTCP + 改 Session；不可逆 |
| 切窗口 | Command | 加载 INI 模板（IO）；改 View 结构 |
| 改队伍 | State | 纯数据 |
| Lock Game | Command | 发广播 + 改 Session；网络副作用 |

**通用规则**：**可逆/幂等/纯数据 = State；不可逆/副作用/外部交互 = Command**。

### 2.2 统一入口：IUIAction

虽然 StateAction 和 CmdAction 走不同路径，但 **INI 里写的都是 `$LeftClickAction = XXX`，catalog 必须有统一入口**。

```csharp
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// UI 动作的统一抽象。
/// 
/// 设计理由：INI 派发不区分 State/Cmd——catalog 看到的只是字符串名 + 参数。
/// 二分语义通过 ActionKind 标记，catalog 内部按 kind 路由（日志、限流、统计）。
/// 
/// 实现建议：90% 的 action 用 catalog.Register(name, kind, handler) 工厂委托；
/// 仅当 action 携带大量配置（如 LaunchGame 带 mod 列表）才建具体 sealed class 实现 IUIAction。
/// </summary>
public interface IUIAction
{
    /// <summary>动作名（大小写不敏感，与 INI 对应）。</summary>
    string Name { get; }

    /// <summary>动作分类（路由依据）。</summary>
    ActionKind Kind { get; }

    /// <summary>
    /// 执行。返回结果（成功/失败/消息）；不抛异常。
    /// </summary>
    CmdResult Execute(in UIActionContext context);
}

/// <summary>动作分类。</summary>
public enum ActionKind
{
    /// <summary>
    /// 状态变更：直接写 Session.SlotSink；触发 Session.StateChanged。
    /// 不允许调用 Service。幂等可逆。
    /// </summary>
    State,

    /// <summary>
    /// 命令派发：调用 Service；Service 内部读写 Session。
    /// 触发 CmdResult 回调 + 可能间接触发 StateChanged。
    /// </summary>
    Command,
}

/// <summary>动作执行结果。</summary>
public readonly struct CmdResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }   // 失败原因 / 成功提示
    public object? Data { get; init; }       // 附加数据（如启动的 PID）

    public static CmdResult Ok() => new() { Success = true };
    public static CmdResult Ok(string msg) => new() { Success = true, Message = msg };
    public static CmdResult Fail(string reason) => new() { Success = false, Message = reason };
}
```

### 2.3 上下文：UIActionContext

```csharp
namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// 动作执行上下文。catalog 在派发时构造，注入所需的依赖。
/// 
/// State 和 Command 共享同一上下文类型——StateAction 只用 Session；CmdAction 用 ServiceHub。
/// 这样 catalog 实现简单，且未来扩展上下文字段不破坏 action 签名。
/// </summary>
public readonly struct UIActionContext
{
    /// <summary>INI 里冒号后的参数（如 "NavigateTo:SkirmishLobby" 的 "SkirmishLobby"）。</summary>
    public string Args { get; init; }

    /// <summary>触发动作的 UI 节点（用于读控件 ID、$Tag 等）。</summary>
    public UiNodeViewModel? Source { get; init; }

    /// <summary>当前 Session 抽象（StateAction 主用，CmdAction 只读）。</summary>
    public IGameSession? Session { get; init; }

    /// <summary>Service 容器（CmdAction 主用）。</summary>
    public IServiceHub Services { get; init; }

    /// <summary>UI 导航主机（旧代码兼容）。</summary>
    public IUiNavigationHost? Host { get; init; }
}
```

### 2.4 catalog 扩展（与现有 IIniActionCatalog 共存）

现有 `IIniActionCatalog` 不动（向后兼容）。新增扩展方法把它接到 IUIAction：

```csharp
public static class IniActionCatalogUIExtensions
{
    /// <summary>注册 StateAction（写 Session）。</summary>
    public static void RegisterState(this IIniActionCatalog catalog, string name,
        Action<string, IGameSession, UiNodeViewModel?> handler)
    {
        catalog.Register(name, (args, host) =>
        {
            // catalog 内部用 EnvironmentServices.Resolve<IGameSession>()
            // 现有 catalog 的签名是 (args, host)，故 host 必须 forward；Services 通过 host 解析
            IGameSession? session = host.GetService<IGameSession>();
            if (session == null) return;
            handler(args, session, /* source */ null);
        });
    }

    /// <summary>注册 CmdAction（调 Service）。</summary>
    public static void RegisterCommand(this IIniActionCatalog catalog, string name,
        Action<string, IServiceHub, IGameSession?, UiNodeViewModel?> handler)
    {
        catalog.Register(name, (args, host) =>
        {
            IServiceHub services = host.GetService<IServiceHub>() 
                ?? throw new InvalidOperationException("IServiceHub not registered");
            IGameSession? session = host.GetService<IGameSession>();
            handler(args, services, session, /* source */ null);
        });
    }
}
```

> **设计权衡**：现有 catalog 的 handler 签名是 `Action<string, IUiNavigationHost>`，没有 IGameSession / IServiceHub 参数。我们用 `host.GetService<T>()` 把 host 当 IoC 入口，避免改 catalog 签名。未来如果 catalog 真要换签名，这些扩展方法直接迁移过去。

---

## 3. 双事件流

### 3.1 StateChanged（粗粒度广播）

由 Session 发出，所有订阅者收到：

```csharp
public interface IGameSession
{
    // ... 其他成员
    event Action? StateChanged;
}
```

**订阅者**：
- View（LobbyPlayerBindingApplier）：刷新 UI 控件
- Service（CnCNetPlayerOptionsService）：广播 PO 到网络
- Service（CnCNetMapService）：广播 GAME CTCP
- Service（MapPreviewService）：重算地图预览

**特性**：
- 多订阅者广播
- 触发时机：任何 SlotSink 写入、Map 改变、State 改变、Options 改变
- **不带变更详情**（粗粒度）

### 3.2 SlotFieldChanged（细粒度，可选）

如果粗粒度性能不足，扩展细粒度事件：

```csharp
public interface IGameSession
{
    event Action<SlotFieldChange>? SlotFieldChanged;
}

public readonly struct SlotFieldChange
{
    public int SlotIndex { get; init; }
    public SlotField Field { get; init; }    // Name/Side/Color/Team/Start/...
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}
```

**当前阶段不实现**——粗粒度足够（最多 8 槽 × 7 字段，刷新成本可忽略）。留接口位置，未来需要时加。

### 3.3 CmdResult（命令回调单播）

CmdAction 执行完返回 CmdResult，catalog 派发给发起者：

```csharp
public interface IIniActionCatalog
{
    // 现有：bool TryDispatch(string raw, IUiNavigationHost host);

    // 新增：带结果回调的重载（向后兼容）
    bool TryDispatch(string raw, IUiNavigationHost host, out CmdResult result);
}
```

**订阅者**：
- View（MainWindow）：显示 toast / 错误条
- Catalog 自身：写日志

**特性**：
- 单播（只发起者关心）
- 与 StateChanged **不冲突**：时机不同、携带信息不同

### 3.4 路径示例

**例 1**：用户改玩家颜色（StateAction）
```
View.ddPlayerColor.SelectionChanged
    → catalog.Dispatch("ChangeColor", args)
    → StateAction.Execute → session.SlotSink.WriteSlot(idx, { ColorIndex })
    → session.StateChanged ▲ 广播
        ├── View 收到 → 刷新 UI
        ├── NetworkService 收到 → 广播 PO
        └── MapService 收到 → 重算 start markers
```

**例 2**：用户点 LaunchGame（CmdAction）
```
View.btnLaunch.Click
    → catalog.Dispatch("LaunchGame", args)
    → CmdAction.Execute(services.Launch)
        ├── 读 session.PlayerSlots（只读）
        ├── 启动游戏进程（外部副作用）
        ├── session.SlotSink.ClearAll() + session.State = InGame
        └── 返回 CmdResult.Ok(pid)
    → CmdResult ▼ 单播
        ├── View 收到 → 显示 "Game launched (PID: 123)"
        └── Catalog 收到 → 写日志
    [并行] session.StateChanged 也会被触发（因 Sink 写入）
```

---

## 4. Service 层

### 4.1 IServiceHub

```csharp
namespace ClientAvalonia.Services;

/// <summary>
/// Service 容器接口（CmdAction 通过它访问 Service）。
///
/// 设计理由：CmdAction 不应直接 static 调 EnvironmentServices.Resolve<T>()——
/// 那样测试时无法注入。Hub 接口允许 mock，且明确列出可用的 Service。
/// 
/// 默认实现：DefaultServiceHub 用 EnvironmentServices 解析。
/// </summary>
public interface IServiceHub
{
    T Get<T>() where T : notnull;
    bool TryGet<T>(out T? service) where T : notnull;
}

public sealed class DefaultServiceHub : IServiceHub
{
    public T Get<T>() where T : notnull
        => EnvironmentServices.Resolve<T>();
    public bool TryGet<T>(out T? service) where T : notnull
        => EnvironmentServices.TryResolve(out service);
}
```

### 4.2 核心 Service（按职责拆分）

| Service | 职责 | 现有对应物 |
|---------|------|-----------|
| `ICnCNetSessionService` | IRC 连接、频道、CTCP 路由 | CnCNetSessionService（已部分抽象为 ICnCNetSession） |
| `ICnCNetPlayerOptionsService` | PO 收发、广播 | `PlayerOptionsCodec` + `MultiplayerSlotCoordinator` 部分 |
| `IMapCatalogService` | 地图列表加载、过滤、选图 | `GameResourceCatalog` |
| `IMultiplayerColorService` | 颜色目录加载 | `MultiplayerColorCatalog` |
| `ISideCatalogService` | 阵营目录加载 | `LobbySideCatalog` |
| `ISkirmishSettingsService` | SkirmishSettings.ini 读写 | `LobbyPlayerState.TryLoad/Save` 部分 |
| `IGameLaunchService` | 进程启动、退出监听 | `GameLaunchService`（已在） |
| `IUiFileService` | INI → UiNodeTree | `UiViewModelFactory` + `LayoutEngine` |
| `IUpdaterService` | 客户端更新 | `UpdaterAdapter` |

**当前阶段不强制全部抽象出来**——只在改造涉及到的部分新建对应 Service。其余保持现状。

---

## 5. 现有代码的分层映射

### 5.1 已合规

| 类 | 层 | 状态 |
|----|----|----|
| `IGameSession` / `ISkirmishSession` / `ICnCNetGameSession` | Session | ✅ |
| `LobbyPlayerSlot` / `IPlayerSlot` / `ICnCNetPlayerSlot` | Session（实体） | ✅ |
| `PlayerOptionsCodec` | Service（纯函数） | ✅ |
| `GameLaunchService` | Service | ✅ |
| `IniActionCatalog` / `BuiltinIniActions` | View（命令通道入口） | ✅ |
| `UiNodeViewModel` / `UiViewModelFactory` / `LayoutEngine` | View | ✅ |
| `LobbyPlayerBindingApplier` | View（投影器） | ✅ |
| `DefaultAiSlotPolicy` | Session（默认装载策略） | ✅ |

### 5.2 跨界（需要改造）

| 类 | 当前位置 | 应在层 | 改造点 |
|----|---------|--------|------|
| `LobbyPlayerState` | 跨 View/Session | Session（保留）+ View（拆出 UI 输入态） | Step 2（Phase 1 延后） |
| `MultiplayerSlotCoordinator` | static helper | Service | 改为 `ICnCNetPlayerOptionsService` |
| `MultiplayerSlotLayout` | static helper | Service | 与 PlayerOptionsCodec 合并 |
| `MainWindow.axaml.cs` 业务方法 | View | Service（拆出） | Step 3 + 后续 9 步切片 |
| `CnCNetSessionService` | Service | ✅，但被 MainWindow 直接用 | 通过 ICnCNetSession 抽象访问 |
| `CnCNetGameRoomSession` | Session（实现 ICnCNetGameSession） | ✅，但混了 IRC 调用 | 长期把 IRC 调用迁到 Service |

### 5.3 已合规但需要"正名"

| 类 | 真正的层 |
|----|--------|
| `LobbySessionState` | View（UI 输入态：选图索引、campaign filter 等） |
| `UiBindingSession` | View（绑定桥） |

---

## 6. Phase 1 延后在此总纲下的位置

`phase1-player-state-completion.md` 描述的改造，对应本总纲的：
- **Step 1**：建 Session 层的写入收口（IPlayerSlotSink）+ View 层的双通道入口（IUIAction + ActionKind）
- **Step 2**：把 LobbyPlayerState 从「跨 View/Session 的中间产物」改造为「Session 投影 + View UI 输入态」
- **Step 3**：删除 MainWindow 中违反「View 不直接调 Service 内部」的胶水代码

每一步都让现有代码更接近本总纲的目标形态。

---

## 7. MainWindow 9 步切片在此总纲下的位置

`mainwindow-analysis.md` 描述的 9 步渐进切片，每步都对应本总纲的一个具体合规化：

| Step | 抽出的 Service | 总纲层 |
|------|---------------|--------|
| 1 | `ICnCNetSessionEvents` | Service（订阅 Session 事件转发给 View） |
| 2 | `IGameLaunchCoordinator` | Service（已部分在 GameLaunchService） |
| 3 | `IOverlayService` | View（纯 UI，但封装在 Service 接口后） |
| 4 | `INavigationService` | View |
| 5 | `ICnCNetGameRoomSyncService` | Service（订阅 Session → 广播 PO） |
| 6 | `ICnCNetGameOptionsBridge` | Service |
| 7 | `ILobbyMapService` + `IMapStartMarkerService` | Service + View |
| 8 | `IUiHostService` | View |
| 9 | INI `$LeftClickAction` 全覆盖 | View → 命令通道 |

---

## 8. 设计决策记录

### 8.1 为什么不用 MVVM（CommunityToolkit.Mvvm）？

- 现有项目是 INI 驱动 UI，UI 结构由 INI 决定，不是 ViewModel
- 引入 MVVM 会形成「INI → UiNode → ViewModel」三层冗余
- INI 模板 + Session 投影已经达到 MVVM 想要的 View/State 分离
- **决定**：不引入，继续用 INI + Session 投影

### 8.2 为什么 Service 不通过 DI 容器（MS.DI）？

- 现有 `EnvironmentServices` 已是 service locator，工作良好
- 切换到 MS.DI 需改 50+ 处 Resolve 调用，且收益仅在「构造期注入」场景
- 当前所有 Service 都是 singleton 语义，locator 够用
- **决定**：保留 EnvironmentServices；`IServiceHub` 作为 CmdAction 注入点（其实现转发给 EnvironmentServices）

### 8.3 为什么不删 `LobbyPlayerState`，而是降为投影？

- 它持有目录缓存（SideNames/AiNames/TeamNames）+ UI 输入态（Mode/LocalPlayerName/HostPlayerName/PlayerUpdatingInProgress）
- 这些不属于 Session（Session 是纯领域），也不属于 Service
- **决定**：保留 `LobbyPlayerState`，但 `Slots` 改为 Session 投影；其余字段保留为 View 层输入态

### 8.4 为什么允许 View 调 Service？

- View 通过 CmdAction 调 Service 是必要的（启动游戏、切窗口）
- 但 View **不能直接调 Service 的内部 API**（如 `IrcConnection.Send`），只能调 CmdAction 暴露的语义级方法
- **决定**：View → `IIniActionCatalog.Dispatch` → CmdAction → Service 是允许的；View → `Service.RawCall` 是禁止的

---

## 9. 验收标准

最终形态（所有 Phase 完成后）应满足：

- [ ] Session 层零外部依赖（grep 不到 `IrcConnection`、`IniFile`、`Dispatcher`、`Process`）
- [ ] View 层不直接持有 Service 内部状态（grep 不到 View 类里有 `_cncnetService.IrcConnection`）
- [ ] 所有 View 用户操作通过 `IIniActionCatalog.Dispatch` 或 `Session.SlotSink`（双通道）
- [ ] Session 是所有状态的唯一真相源（`LobbyPlayerState.Slots` 是投影）
- [ ] 所有 Service 通过 `EnvironmentServices` 或 `IServiceHub` 访问（不直接 new）
- [ ] 现有全量测试不回归

---

## 10. 与其他设计文档的关系

| 文档 | 关系 |
|------|------|
| `phase1-player-state-completion.md` | 本总纲的具体应用之一（玩家状态统一） |
| `player-unification-and-ini-action-catalog.md` | Phase 1 已完成部分，本总纲在它之上扩展 |
| `player-unification-and-ini-action-report.md` | Phase 1 部分完成的报告 |
| `mainwindow-analysis.md` | MainWindow 9 步切片，每步对应本总纲的一个合规化 |
| `architecture-evaluation-l1.md` | L1 评估，本总纲是 L2 的目标形态 |
| `global-state-refactor.md` | 最初的全局状态重构方案，本总纲是它的演进 |

后续所有重构都应先引用本文档定位自己的位置，再展开具体设计。

---

## 附录 A：典型数据流图（目标终态）

```
用户点击 ddPlayerColor (Avalonia 控件)
    │
    ▼
UiNodeViewModel.SelectionChanged (View)
    │
    ▼
IniBehaviorApplier → IIniActionCatalog.Dispatch("ChangeColor", args)  ★ 命令通道入口
    │
    ▼
IUIAction { Name="ChangeColor", Kind=State }.Execute(context)
    │
    │  [State 路径]
    ▼
IGameSession.SlotSink.WriteSlot(idx, { ColorIndex = newIdx })
    │
    ▼
LobbyPlayerSlot.ColorIndex 更新（Session 层）
    │
    ▼
IGameSession.StateChanged ▲ 广播
    │
    ├──► LobbyPlayerBindingApplier (View) → 更新 ddPlayerColor.SelectedIndex 静默
    ├──► ICnCNetPlayerOptionsService (Service) → PlayerOptionsCodec.ToDto → 发 PO CTCP
    └──► IMapStartMarkerService (View) → 重算 start markers

用户点击 btnLaunch (Avalonia 控件)
    │
    ▼
IniBehaviorApplier → IIniActionCatalog.Dispatch("LaunchGame", args)
    │
    ▼
IUIAction { Name="LaunchGame", Kind=Command }.Execute(context)
    │
    │  [Command 路径]
    ▼
IServiceHub.Get<IGameLaunchService>().Run(session)
    │
    ├── 读 session.PlayerSlots（只读）
    ├── 写 spawn.ini（IoService）
    ├── Process.Start("Syringe.exe")（外部副作用）
    ├── session.SlotSink.ClearAll()
    ├── session.State = InGame
    └── return CmdResult.Ok(pid)
    │
    ▼
CmdResult ▼ 单播
    │
    ├──► MainWindow.ShowStatus($"Launched PID={pid}")
    └──► Catalog.Logger.Log("LaunchGame ok")

[并行] session.StateChanged 也触发 → 各 Service 响应状态切换
```

---

## 附录 B：术语表

| 术语 | 定义 |
|------|------|
| **View 层** | UI 渲染与用户输入捕获；包含 INI 模板资产、UiNodeTree、BindingApplier |
| **Session 层** | 纯领域真相源；不知外部依赖 |
| **Service 层** | 外部交互（Network/IO/Process）；读写 Session |
| **StateAction** | 幂等可逆的状态变更；走 Session.SlotSink |
| **CmdAction** | 有副作用的命令；走 Service.X.Run() |
| **StateChanged** | 粗粒度状态变更广播事件 |
| **CmdResult** | 命令执行结果单播回调 |
| **Sink** | 写入收口接口（CQRS-like） |
| **投影** | View 从 Session 只读派生的视图 |
| **真相源** | 唯一权威状态存储（Session） |
