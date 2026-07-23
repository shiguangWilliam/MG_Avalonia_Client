# ClientAvalonia 架构总览

> 本文档面向新加入的开发者。目标：用一张图 + 一条流水线说清"INI → 屏幕上的按钮"是怎么走的，以及与上游 DX 客户端的本质差异。

## 1. 仓库分层

```
┌─────────────────────────────────────────────────────────────────┐
│                    ClientAvalonia (本仓库主体)                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Views/         Avalonia Window + axaml                  │   │
│  │  IniUi/         INI → UI 引擎 (本项目的核心差异)         │   │
│  │  CnCNet/        IRC 协议 / NAT 穿透 / Game room          │   │
│  │  Core/          启动 / Shutdown / InstallationRegistry   │   │
│  │  Services/      GameLaunch / Update / Resource catalog   │   │
│  │  Domain/        Lobby 规则 / Map / Multiplayer 实体      │   │
│  │  Platform/      Windows/Unix 平台抽象 (待补全)           │   │
│  │  Rendering/     UiNodeViewModel / Dx* 控件工厂           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              ▲                                  │
│                              │ 依赖                             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  ClientCore (上游共享)   ClientConfiguration / Program   │   │
│  │                          Constants / Settings / INI      │   │
│  │  ClientUpdater (上游)    版本检查 / 自更新                │   │
│  │  Rampastring.Tools (上游)  IniFile / Logger              │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

`note/` 目录下的 18 个 Markdown 是**历史决策记录**（ADR, Architecture Decision Records），不是正式文档。新人请先读本文档建立全局观，再按需查阅 `note/`。

## 2. 核心流水线：INI → UiNodeTree → axaml

这是本项目与上游 DX 客户端**最大的架构差异**。理解这条流水线是改任何 UI 相关代码的前提。

### 2.1 DX 客户端的工作方式（背景）

DX 客户端（`DXMainClient`，XNA/DirectX 时代）的做法：
1. C# 代码硬编码创建控件（`new XNAListBox()`）
2. INI 文件提供控件的属性（Location、Size、Texture、...）
3. 通过 `XNAWindowBase.AddChild` 把控件挂到窗口上
4. 控件类型由代码决定，INI 不能改变类型

### 2.2 本项目的做法（Avalonia）

Avalonia 没有 XNA 的控件树，必须走 axaml。因此本项目采用**完全 INI 驱动**的流水线：

```
Resources/MainMenu.ini            原始 INI 文件（modder 编写）
       │
       ▼  IniDocument.Load
IniDocument                       基于 BasedOn 合并后的逻辑 INI 文档
       │                          (see IniUi/Loading/IniDocument.cs)
       ▼  IniAstBuilder.BuildFromFile
IniFileAst                        INI 文件 + 覆盖层 section 名集合
       │                          (see IniUi/Ast/)
       ▼  IniUiTreeBuilder.Build
UiNodeTree                        节点树：root + children + props
       │                          (see IniUi/Loading/IniUiTreeBuilder.cs)
       │                          这里实现 DX 对齐的 R2-R8 语义：
       │                          孤儿 section 收养、$CC 嵌套、
       │                          prefix-based type 推断
       ▼  LayoutEngine.LoadWindow
UiNodeTree (with layout applied)  CanvasLeft/Top/Width/Height 算好
       │                          (see IniUi/Layout/LayoutEngine.cs)
       ▼  UiViewModelFactory.CreateTree
UiNodeViewModel tree              Avalonia 可绑定的 ViewModel 树
       │                          (see Rendering/UiNodeViewModel.cs)
       ▼  IniBehaviorApplier.Apply
ViewModel + Behaviors             行为系统绑定按钮点击等事件
       │                          (see IniUi/Behaviors/)
       ▼  UiBindingSession.ApplyToTree
ViewModel + 数据绑定              地图列表、玩家槽位、设置等数据填充
       │                          (see IniUi/Binding/)
       ▼  PART_RootView.Content = vm
axaml 渲染                        Avalonia 通过 DataTemplate 渲染
                                  (see Views/MainWindow.axaml)
```

**关键文件指针**（每行末尾的文件路径就是这一步的实现位置）。

### 2.3 Dx* 前缀的语义

`DxNodeCanvasView`、`DxControlFactory`、`DxControlStyles.axaml` 中的 **`Dx` 不是 DirectX**。它代表 **"对齐 DX 客户端语义"**——这些控件/样式是 Avalonia 端为对齐上游 DX 客户端的视觉行为而实现的等价物。理解成 "DX-compatible Avalonia control" 即可。

## 3. 启动流程（MG-only）

```
Program.Main
   │
   ├─► TryValidateIni (命令行模式：--validate-ini / --dump-tree / ...)
   │
   └─► PreStartup.Initialize       (Core/PreStartup.cs)
          │  InstallationRegistry.ResolveAndHealMgInstallPath
          │  → 检查 HKCU 注册表，若 MG 路径无效则写入 CWD
          │
          └─► App.OnFrameworkInitializationCompleted
                 │
                 ├─► ClientStartupService.Run
                 │      └─► Startup.Execute (Core/Startup.cs)
                 │             ├─► Updater.Initialize
                 │             ├─► GameResourceCatalog.EnsureLoaded
                 │             └─► BackgroundTasks (硬件探针、身份生成等)
                 │
                 └─► new MainWindow()
                        └─► NavigateTo("MainMenu")
                               └─► IniUi 流水线 (见 §2)
```

`main` 分支是 **MG-only**：注册表 key 固定为 `MomentOfGenesis`，可执行文件固定 `gamemd.exe`。`feature/multi-mod-registry-workspace` 分支有多 mod 注册机，但**未合入 main**。

## 4. CnCNet 联机分层

```
┌────────────────────────────────────────────────┐
│  CnCNetSessionService  (singleton facade)      │  ← UI 唯一入口
│  ┌──────────────────────────────────────────┐  │
│  │  CnCNetSession  (singleton, IRC 核心)    │  │
│  │  ┌────────────────────────────────────┐  │  │
│  │  │  CnCNetIrcConnection               │  │  │  TCP / IRC 协议
│  │  └────────────────────────────────────┘  │  │
│  │  ┌────────────────────────────────────┐  │  │
│  │  │  CnCNetGameRoomSession              │  │  │  房间状态机
│  │  │  (player list / options / ready)    │  │  │
│  │  └────────────────────────────────────┘  │  │
│  │  ┌────────────────────────────────────┐  │  │
│  │  │  CnCNetTunnel / TunnelMaintenance  │  │  │  NAT 穿透服务器
│  │  └────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────┘  │
└────────────────────────────────────────────────┘
```

UI 永远只调用 `CnCNetSessionService.Instance`，不直接触达 `CnCNetSession`。这是为了未来能 mock 测试（见 §6）。

## 5. 行为系统（Behaviors）

每个窗口（MainMenu / SkirmishLobby / CnCNetGameLobby / ...）有自己的 `BehaviorRegistry`。`IniBehaviorCatalog.RegisterForWindow` 根据窗口名注册对应的按钮回调。

```
MainWindow.NavigateTo("SkirmishLobby")
   │
   ├─► UiBehaviorCatalog.RegisterForWindow(_mainBehaviors, "SkirmishLobby", this)
   │      └─► 注册 btnLaunchGame → TryLaunchSkirmish
   │                 btnLeaveGame → NavigateBack
   │                 ...
   │
   └─► IniBehaviorApplier.Apply(vm, _mainBehaviors, this)
          └─► 把 vm 上的按钮事件挂到 behaviors 注册的回调
```

**新增按钮**：只需在 INI 加 section + 在 `XxxBehaviors.cs` 注册回调，无需改 ViewModel 或 axaml。

## 6. 已知技术债（详见 `docs/` 设计文档）

| 主题 | 文档 | 状态 |
|---|---|---|
| 全局可变状态重构 | [docs/design/global-state-refactor.md](design/global-state-refactor.md) | 设计稿，待人工实施 |
| UI Auto-Refresh 统一入口 | [docs/design/auto-refresh-design.md](design/auto-refresh-design.md) | 设计稿，待审批 |
| 错误处理统一策略 | [docs/design/error-handling.md](design/error-handling.md) | 待补 |
| 地图默认 AI 数量匹配 | [docs/design/auto-ai-slots.md](design/auto-ai-slots.md) | 设计稿 |
| 联机默认选低延迟 Tunnel | [docs/design/low-latency-tunnel.md](design/low-latency-tunnel.md) | 设计稿 |

## 7. 测试策略

- **`ClientAvalonia.Tests/IniUi/`**：INI → UI 引擎的单元测试。优先级最高。
- **`ClientAvalonia.Tests/Core/`**：启动 / Shutdown / InstallationRegistry。
- **`ClientAvalonia.Tests/CnCNet/`**：IRC 协议解析、name 验证、tunnel 列表加载。
- **`ClientAvalonia.Tests/Integration/`**：与真实 mod 资源（MG / LNOD / QEC）的端到端测试。

跨测试的静态状态串扰问题（`ProgramConstants.LocalGame` 等）目前用 `[Collection("ProgramConstantsSerial")]` 强制串行（见 [docs/design/global-state-refactor.md](design/global-state-refactor.md) §短期缓解）。

## 8. 上游同步策略

- `main` 分支：MG-only，可发布。
- `feature/multi-mod-registry-workspace` 分支：多 mod 注册机实验场，**不合入 main**。
- 上游 DX 客户端修复（INI 解析、IRC 协议）：手工 cherry-pick，**不**合入多 mod 注册机相关代码。

每次 cherry-pick 后跑 `ClientAvalonia.Tests/IniUi/ThreeModCompatibilityTests` 确认 MG / LNOD / QEC 三 mod 不回归。
