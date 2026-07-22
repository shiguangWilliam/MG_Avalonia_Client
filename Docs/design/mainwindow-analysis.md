# MainWindow 现状分析与拆分方向

> **日期**：2026-07-20
> **范围**：`ClientAvalonia/Views/MainWindow.axaml(.cs)` + 直接耦合的 Behavior / Binding / Service
> **目的**：在重新设计 MainWindow 抽象之前，先把现状、痛点、前置依赖、候选方案讲清楚
> **关联**：`docs/design/architecture-evaluation-l1.md` §3.7、§6

---

## 1. 规模与构成

| 维度 | 数字 | 解读 |
|------|------|------|
| `MainWindow.axaml.cs` 行数 | **~2050** | 单文件代码量极大 |
| `MainWindow.axaml` 行数 | 74 | 视觉层极简（DX 全靠 INI 渲染），axaml 只放 5 个 `PART_*` 容器 |
| 字段数（含 readonly） | **20+** | 见下表 |
| 方法数 | **~90** | 一半 private、一半 public（实现 IUiNavigationHost） |
| 实现/继承 | `Window, IUiNavigationHost` | 双重职责：Avalonia 视窗 + 行为宿主 |
| `IUiNavigationHost` 公共方法 | **30** | 已是「上帝接口」级别 |

### 1.1 字段清单（按职责聚类）

```
环境/资源
  ClientEnvironment        _environment
  GameResourceCatalog      _gameResources  (static Instance!)
  ResourceResolver         (engine.Resources)

行为/绑定
  BehaviorRegistry         _mainBehaviors
  BehaviorRegistry         _overlayBehaviors
  UiBindingSession         _bindingSession
  LobbySessionState        _lobbySession  (PlayerState + 地图筛选等)

会话
  Session.SkirmishSession  _skirmishSession
  ICnCNetSession           _cncnet

启动/更新
  GameLaunchService        _gameLaunch
  ClientUpdateService      _updateService

渲染/视图模型
  LayoutEngine?            _mainEngine
  LayoutEngine?            _overlayEngine
  UiViewModelFactory?      _mainViewModelFactory
  UiNodeViewModel?         _activeRoot
  UiNodeViewModel?         _overlayRoot
  GameCreationOverlayContext? _gameCreationOverlay

导航/Overlay
  Stack<string>            _navStack
  string?                  _floatingOverlayWindow
  bool                     _restoreWindowAfterGame

并发/重入保护
  bool                     _applyingCnCNetGameRoomPlayers  (★ 隐式状态机标志)
```

---

## 2. 承担的职责（至少 11 项）

这就是它「上帝对象」的根因——一个类做了 11 件本应是独立模块的事：

| # | 职责 | 行数占比（估） | 代表方法 |
|---|------|----------------|----------|
| 1 | **Avalonia 视窗生命周期** | 5% | 构造 / `OnWindowLoaded` / `OnMainWindowClosing` / `ApplyViewportSize` / `OnKeyDown` |
| 2 | **导航栈** | 5% | `NavigateTo` / `NavigateBack` / `LogoutToMainMenu` / `_navStack` |
| 3 | **INI → layout → VM 渲染** | 8% | `LayoutEngine.CreateForWindow` + `LoadWindow` + `UiViewModelFactory.CreateTree` |
| 4 | **Overlay 管理（浮层）** | 12% | `OpenFloatingOverlay` / `CloseFloatingOverlay` / `ShowRawHostOverlay` / `ShowGameCreationOverlay` / `ResetOverlayPanelChrome` |
| 5 | **Game Launch（3 种模式）** | 10% | `TryLaunchSkirmish` / `TryLaunchCampaign` / `TryLaunchCnCNetGame` / `OnGameProcessStarted/Exited` |
| 6 | **Lobby 数据装配（地图/玩家/规则）** | 18% | `ApplyLobbyData` / `RefreshLobbyMapList` / `PickRandomLobbyMap` / `ToggleFavoriteLobbyMap` |
| 7 | **Map Start Marker 交互** | 8% | `WireMapPreviewStartMarkers` / `OnMapStartMarkerLeftClicked` / `RightClicked` / `RefreshMapStartMarkersAndPlayerUi` |
| 8 | **CnCNet 房间生命周期 + UI 同步** | **20%** | `OnCnCNetGameRoomJoined` / `OnCnCNetGameStarting` / `ApplyCnCNetGameRoomPlayers*` / `RefreshCnCNetGameRoomUiFromSession` |
| 9 | **CnCNet GameOptions 双向桥** | 5% | `WireCnCNetGameOptionsBridge` / `CollectCnCNetGameOptions` / `ApplyCnCNetGameOptionsFromHost` / `OnHostGameOptionControlChanged` |
| 10 | **CnCNet Channel Lobby（聊天）** | 4% | `ApplyCnCNetChannelLobby` / `TrySendChannelChat` / `OnCnCNetStateChanged` 部分 |
| 11 | **Top Bar / 状态栏 / Update** | 5% | `UpdateTopBar` / `ShouldShowTopBar` / `ShowStatus` / `OnUpdateStatusChanged` |

---

## 3. 关键耦合点

### 3.1 与 CnCNet 的耦合（最重）

- **事件订阅**（构造函数）：`StateChanged / GameRoomJoined / GameRoomJoinFailed / GameStarting / GameRoomHostAbandoned` 全挂在 `_cncnet` 上。
- **房间玩家同步**：`ApplyCnCNetGameRoomPlayersCore(root, CnCNetActiveGameRoom room, ...)` —— **仍是强类型** `CnCNetActiveGameRoom`，靠 `((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore` 这个 escape hatch 拿。
- **重入保护**：`_applyingCnCNetGameRoomPlayers` 是一个**隐式状态机**——避免「读 session → 改 UI → UI 触发 → 又写回 session」的死循环。这是当前最难抽的部分。
- **GameOptions 双向桥**：`GameOptionsProvider` / `GameOptionsReceiver` / `GameOptionsControlCounts` 是 `CnCNetSessionService` 上的具体回调（不在 `ICnCNetSession` 接口里），目前靠 `.Service` escape hatch 访问。

### 3.2 与 Lobby 数据的耦合

- `LobbySessionState _lobbySession` + `SkirmishSession _skirmishSession` + `ICnCNetGameSession.ActiveGameRoom` —— **三套玩家状态**：UI（`LobbyPlayerState`）、新抽象（`ISkirmishSession.PlayerSlots`）、网络（`CnCNetGameRoomPlayer`）。MainWindow 负责三者之间的同步胶水。
- `GameDataBindingApplier` / `LobbyPlayerBindingApplier` / `StateBindingApplier` —— 三个静态绑定器都从 MainWindow 调用，强耦合 `UiNodeViewModel` 树。

### 3.3 与 Avalonia 视觉的耦合

- 直接拿 `PART_RootView.Content = vm`、`PART_Status.Text = msg`、`PART_FloatingOverlay.IsVisible = true`。
- `BehaviorRegistry` 的事件回调直接闭包捕获 `this`（看 `MultiplayerLobbyBehaviors` / `CnCNetGameLobbyBehaviors`）。

---

## 4. 现存的具体痛点

### 痛点 A：IUiNavigationHost 是上帝接口

30 个公共方法混杂了：导航、Overlay、Launch、CnCNet 同步、Lobby 操作、设置提交、Update 检查。`MainWindow` 是它的唯一实现，所以**任何 Behavior 都能调任何 MainWindow 内部状态**——没有边界。

### 痛点 B：`_applyingCnCNetGameRoomPlayers` 隐式状态机

跨 4 个方法共享，是「防止死循环」的临时方案。一旦 MainWindow 拆分，这个标志必须显式建模成 `GameRoomSyncGuard` 或类似对象，否则拆分会立刻出错。

### 痛点 C：CnCNetActiveGameRoom 强类型漏回生产

迁移时为了不破坏 helper 签名，`CnCNetSessionServiceAdapter` 透出了 `ActiveGameRoomCore`。MainWindow 里仍有 6 处直接读这个属性拿具体类型。这是**抽象漏斗**——下次重构的第一刀。

### 痛点 D：GameOptions 三回调不在接口里

`GameOptionsProvider` / `GameOptionsReceiver` / `GameOptionsControlCounts` 仍然只能在 `CnCNetSessionService` 上挂，意味着 CnCNet 房间选项同步**完全不可单测**。

### 痛点 E：构造函数订阅 + 无对应取消

`_cncnet.StateChanged += OnCnCNetStateChanged;` 等 5 个事件订阅在构造函数里，但只在 `OnMainWindowClosing` 里隐式释放（窗口销毁）。对单测不友好——构造一个 MainWindow 就会真连 IRC。

### 痛点 F：launch 路径与 CnCNet 紧绑

`TryLaunchCnCNetGame` 在 `MainWindow` 里，但 launch 流程应该属于 `GameLaunchService`；CnCNet 特有的「等 host START」逻辑被摊在 MainWindow + Adapter 两处。

---

## 5. 拆分前必须先解决的 4 个前置问题

这 4 个问题如果不先处理，MainWindow 拆分会立刻卡住：

1. **CnCNetGameRoomPlayer ↔ IPlayerSlot 统一**：让 `ICnCNetGameSession.Players` 通过 `IPlayerSlot` 暴露，`ApplyCnCNetGameRoomPlayersCore` 才能改成接口签名。
2. **GameOptions 回调进接口**：把 `GameOptionsProvider/Receiver/ControlCounts` 包成 `ICnCNetGameOptionsBridge { get; set; }` 放到 `ICnCNetSession`，否则 Bridge 永远绑死具体类型。
3. **LobbySessionState 与 SkirmishSession 合并**：或者明确边界（前者管 UI 输入态，后者管命令态），不然 ViewModel 拆出来也分不清归属。
4. **事件订阅接口化**：把 `OnCnCNet*` 5 个回调抽成 `ICnCNetSessionEvents`，MainWindow 持有该接口即可挂/摘钩，单测可注入 fake。

---

## 6. 推荐的拆分方向（3 个候选）

按风险从低到高排列。

### 方案 A：按窗口拆 Page/Controller（保守，推荐先做）

把每个 `CurrentWindow` 的逻辑抽成独立 `PageController`：

```
MainWindow (薄壳，~300 行)
  ├── Window Chrome + PART_* 容器
  ├── NavigationStack
  ├── PageControllerFactory.Resolve(windowName) → IPageController
  └── SharedServices (launch / update / cncnet / lobby state)

Controllers/
  MainMenuController
  SkirmishLobbyController      ← 含 ApplyLobbyData 大部分
  CampaignOverlayController
  CnCNetLobbyController        ← Channel Lobby
  CnCNetGameLobbyController    ← 房间内，最大的一块
  OptionsOverlayController
  GameCreationOverlayController

每个 Controller:
  void OnEnter(UiNodeViewModel root, IUiNavigationHost host)
  void OnExit()
  void BindBehaviors(BehaviorRegistry behaviors)
```

**优点**：渐进、风险可控、`IUiNavigationHost` 可同步瘦身。
**缺点**：`CnCNetGameLobbyController` 仍会比较大（~600 行），但已经比现在好得多。

### 方案 B：MVVM + Mediator（中等）

引入 `CommunityToolkit.Mvvm` 或手写 ViewModel 基类：

```
MainWindow.axaml (View)
  ↓ DataContext
MainWindowViewModel
  ├── INavigationService      (替换 IUiNavigationHost 的导航部分)
  ├── IGameLaunchService      (扩展，吸收 TryLaunch*)
  ├── IOverlayService         (管理浮层)
  ├── ICnCNetLobbyViewModel   (绑定 Players / Chat / GameOptions)
  └── ILobbyViewModel         (绑定地图/玩家槽)

Behavior 不再拿 IUiNavigationHost，而是发消息：
  SendMessage(new LaunchSkirmishCommand());
  → MainWindowViewModel.Receive()
```

**优点**：Behavior 真正解耦、VM 可单测、符合 Avalonia 习惯。
**缺点**：改动量大，所有 Behavior + BindingApplier 都要改；可能要引入 DI 容器（`Microsoft.Extensions.DependencyInjection`）替代 `EnvironmentServices`。

### 方案 C：Feature Slice（激进）

按「功能切片」重组目录，每个切片自带 View + VM + Controller + Service：

```
Features/
  MainMenu/
    MainMenuView.axaml
    MainMenuViewModel.cs
    MainMenuController.cs
  SkirmishLobby/
    ...
  CnCNet/GameLobby/
    CnCNetGameLobbyView.axaml
    CnCNetGameLobbyViewModel.cs
    CnCNetGameRoomSyncService.cs  ← 把 ApplyCnCNetGameRoomPlayers* 全搬来
    CnCNetGameOptionsBridge.cs    ← 把 GameOptions 三回调封进
  Shared/
    Navigation/
    Overlay/
    Launch/
```

**优点**：长期最干净；新功能加切片即可。
**缺点**：等于重写一遍 UI 层，需要先有方案 A/B 的稳定基础。

---

## 7. 建议路径（分 4 步）

1. **第一步（必须先做）**：解决 §5 的 4 个前置问题（接口收口 + 状态合并）。预计 1–2 天。
2. **第二步**：抽 `GameLaunchCoordinator`（吸收 `TryLaunch*` + `OnGameProcessStarted/Exited`）和 `OverlayService`（吸收所有 Overlay 管理）——这两个边界最清晰，风险最低。预计 1 天。
3. **第三步**：按方案 A 抽 PageController——先抽 `CnCNetGameLobbyController`（最大块），再抽 `SkirmishLobbyController`。预计 2–3 天。
4. **第四步**：评估是否进一步走方案 B（MVVM）。如果 Controller 拆完已经够用，可以缓做。

---

## 8. 目标终态（参考量化指标）

拆分完成后应达到：

| 指标 | 当前 | 目标 |
|------|------|------|
| `MainWindow.axaml.cs` 行数 | ~2050 | **≤ 400**（只剩 chrome + nav） |
| `MainWindow` 字段数 | 20+ | **≤ 8**（仅视图引用 + 共享服务） |
| `IUiNavigationHost` 公共方法 | 30 | **≤ 10**（只留导航/状态/退出） |
| 单元测试可覆盖的 Lobby / CnCNet 路径 | ~10% | **≥ 70%** |
| `CnCNetActiveGameRoom` 在 MainWindow 的引用 | 6 | **0** |
| `((CnCNetSessionServiceAdapter)_cncnet).Service` / `ActiveGameRoomCore` 在 MainWindow | 4 | **0** |

---

## 9. 决策点

下一步可选：

- **(1)** 先落地 §5 的 4 个前置问题（接口收口 + 状态合并）
- **(2)** 直接做 §7 第二步（抽 `GameLaunchCoordinator` + `OverlayService`，最低风险）
- **(3)** 出一份详细的方案 A 拆分设计文档（写到 `docs/design/mainwindow-decomposition.md`）
- **(4)** 其他想法
