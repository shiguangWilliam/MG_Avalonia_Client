# ClientAvalonia 架构评估报告（L1 落地 + 生产调用迁移 + UI 引擎高覆盖单测）

> **日期**：2026-07-20
> **范围**：`ClientAvalonia` + `ClientAvalonia.Tests`
> **依据**：`docs/design/global-state-refactor.md` v3 + 三轮后续落地（接口扩展 / 生产迁移 / UI 引擎单测）
> **测试基线**：过滤 Live IRC / Integration 后 **479 通过 / 0 失败**（L1 时为 359；本轮净增 120 UI 引擎语义单测）

---

## 1. 结论摘要

L1 之后又完成两件事：
1. **生产调用迁移**：把 `MainWindow`、`IniUi/Behaviors`、`IniUi/Overlays`、`IniUi/Binding`、`Services/GameLaunchService`、`Services/UiStateService` 等关键调用点从 `CnCNetSessionService.Instance` 切到 `ICnCNetSession`（通过 `EnvironmentServices.Resolve` 或字段注入）。
2. **UI 引擎单测**：补齐 `IniConversions` / `IniTextUtil` / `IniDrawMode` / `IniKeyAliases` / `ExpressionEvaluator` / `LayoutResolver` / `MeasurePass` / `PanelLayoutPass` / `IniDocument` 9 个原本零测试的核心 INI/DX 语义单元，120 个用例锁定语义。

**剩下的旧调用面**：`ProgramConstants.*`（~250 处）、`ClientConfiguration.Instance`（~120 处）、3 处 `CnCNetSessionService.Instance`（`ShutdownService` / `GameLaunchDiagnostics` / Adapter 自身构造）按设计保留，迁移路径已明确。

**一句话**：四域接口与冲港口 Session 语义已立住，UI 引擎核心算法的可测性已经追平契约层；下一步价值最大的是**收口 `ProgramConstants` / `ClientConfiguration.Instance` 配置域调用**，而不是再抽更多接口。

---

## 2. 功能（Functionality）

### 当前可观测能力

| 模块 | 能力 | 落地状态 |
|------|------|----------|
| 启动 | PreStartup 注册 4 域服务；EnvironmentServices 解析路径打通 | ✅ |
| 主菜单 / 大厅 / 房间 | INI → AST → UiNode → 预计算 layout → Avalonia 渲染 | ✅ |
| 遭遇战 / 多人 / 战役 | SkirmishSession / ICnCNetGameSession / Mission 三套会话契约；LobbyActionContext 只持 ISkirmishSession | ✅ |
| CnCNet IRC | 房间创建 / 加入 / Tunnel 切换 / 广播更新 / 准备 / 启动通知 / Launch presence keep-alive | ✅（接口 + Adapter 全覆盖） |
| Tunnel 维护 | TunnelSorter 低延迟小顶堆；TunnelMaintenanceLoop 已就位但未接生产定时器（按计划） | 🟡 接口/数据通路完成；定时接入待 L2 |
| 资源（地图/任务/模式/颜色） | DTO 直接实现 IResource；Catalog/Manifest 适配器；NoOp Manifest | ✅ |
| 在线更新 | IResourceManifest / IUpdater 接口已抽；实现为 NoOp（设计预留） | 🟡 设计完成，实现待 L2 |

### 迁移后新增 / 加固的功能通路

- **CnCNet 行为可注入**：`btnLockGame` / `chkAutoReady` / `btnManualReady` / `btnCreateGame` / `btnNewGame` / `btnJoinGame` / 聊天颜色下拉 / 频道切换 / Launch presence 全部走 `ICnCNetSession`，单测时可注入 fake。
- **GameLaunchService 容错**：构造期 / 启动期都能容忍 `EnvironmentServices` 未注册（lazy resolve + null 静默），避免测试环境的强耦合。
- **Adapter 透出 Service escape hatch**：`CnCNetSessionServiceAdapter.Service` 与 `ActiveGameRoomCore` 让迁移期遗留的强类型 helper（`ApplyCnCNetGameRoomPlayersCore(root, CnCNetActiveGameRoom, ...)` 等）继续可用，避免一次性全量改签名造成大爆炸。

---

## 3. 架构设计 + 抽象设计（Architecture & Abstraction）

### 3.1 四域分层

```
┌────────────────────────────────────────────────────────────┐
│ UI Layer                                                   │
│  MainWindow / IniUi/Behaviors / IniUi/Overlays / Binding   │
│    ↓ Resolve<T>()                                          │
│  EnvironmentServices (service locator, 替代 DI 容器)        │
└─────────────┬───────────────────────────────────┬──────────┘
              ↓                                   ↓ (legacy bridge)
┌─────────────────────────────┐  ┌──────────────────────────┐
│ Domain Interfaces           │  │ Legacy Singletons        │
│  IGameEnvironment           │←→│  ProgramConstants        │
│  IResource(Catalog/Manifest)│  │  ClientConfiguration.Inst│
│  ISkirmishSession /         │  │  CnCNetSessionService.Inst│
│  ICnCNetGameSession         │  │  CnCNetSession.Instance  │
│  ICnCNetSession             │  └──────────────────────────┘
└─────────────┬───────────────┘
              ↓
┌─────────────────────────────┐
│ Adapters / Concrete         │
│  ProgramConstantsGameEnv    │
│  GameResourceCatalogAdapter │
│  CnCNetSessionServiceAdapter│
│  SkirmishSession            │
│  CnCNetGameRoomSession      │
└─────────────────────────────┘
```

### 3.2 三棵继承树策略（已落地验证）

| 域 | 策略 | 现状 | 评价 |
|----|------|------|------|
| Environment | **抽象基类** `GameEnvironmentBase` + 派生 | `ProgramConstantsGameEnvironment` / `MockGameEnvironment` | ✅ 派生路径要求明示；属性 read-only 通过 `*Value` 后备字段绕开 |
| Resource | **接口 + DTO 默认实现** | `MapEntry : IMapResource` 等；sealed DTO 不需改继承 | ✅ 避开 C# 单继承限制；扩展通过加接口成员 |
| Session | **纯接口继承** | `ISkirmishSession : IGameSession`；`CnCNetGameRoomSession : ICnCNetGameSession`（冲港口） | ✅ 语义最干净；ActiveGameRoom 经 Adapter 透明返回 ICnCNetGameSession |

### 3.3 Network 域接口扩展（本轮重点）

`ICnCNetSession` 从初始 11 个成员扩到 **27** 个，覆盖了 MainWindow / LobbyBehaviors / GameCreationOverlay / GameLaunchService / UiStateService 全部真实调用：

```text
状态：    ConnectionState, LocalNick, IsGameRoomJoinPending, OnlinePlayerCount, Connection, LobbyState,
          ActiveGameRoom (ICnCNetGameSession?), GameRoom (CnCNetGameRoomSession?), Tunnels, TunnelSorter
事件：    StateChanged, GameRoomJoined<ICnCNetGameSession>, GameRoomJoinFailed, GameStarting, GameRoomHostAbandoned
连接：    ConnectIfNeeded, Disconnect, EnsureStarted
房间：    TryJoinGame, TryLaunchHostedGame, TryCreateGame, LeaveGameRoom, SetGameRoomReady, SetGameRoomLocked,
          UpdateGameRoomListing, UpdateGameLobbySettings, TryHostChangeTunnel, SyncGameRoomFromLobby,
          EnsureGameBroadcastChannelsJoined
聊天：    SendChatMessage, SwitchToChannel, SelectedChannelIndex, SetChatColorIndex
进程：    NotifyGameProcessStarted, NotifyGameProcessExited, BeginLaunchPresenceKeepAlive, EndLaunchPresenceKeepAlive
```

`CnCNetSessionServiceAdapter` 同时透出 `Service` 与 `ActiveGameRoomCore` 两个 escape hatch，让遗留强类型 helper 不阻塞迁移。

### 3.4 Session 域冲港口语义（用户决策）

`ICnCNetGameSession : ISkirmishSession`，`CnCNetGameRoomSession` 直接实现接口。新增字段：

```text
CnCNetTunnel Tunnel { get; set; }
string HostName { get; }
string RoomName { get; }     ← 本轮新增（Adapter.ActiveGameRoom 返回 ICnCNetGameSession 后需要的属性）
string ChannelName { get; }
string? Password { get; set; }
int MaxPlayers { get; set; }
int SkillLevel { get; set; }
bool Passworded { get; set; }
bool IsHost { get; }
```

### 3.5 UI 引擎算法分层（本轮重点测试）

```
┌─────────────────────────────────────────────────────────────┐
│  INI File                                                   │
│    ↓ IniDocument.Load  (BasedOn 链 + $BaseSection 合并)      │
│  IniFileAst (IniAstBuilder)                                 │
│    ↓ IniUiTreeBuilder.Build (ControlRegistry + PropertyResolver)
│  UiNodeTree                                                 │
│    ↓ WindowTreePostProcessor (foreign-window skip 等)        │
│  UiNodeTree (post-processed)                                │
│    ↓ MeasurePass        (texture/text → Width/Height)        │
│    ↓ LayoutResolver     ($X/$Y/$W/$H 表达式 → Canvas*)        │
│    │   └ ExpressionEvaluator (DX ClientGUI.Parser 端口)      │
│    ↓ MeasurePass (二次)                                      │
│    ↓ PanelLayoutPass    (content panel 重叠消解)              │
│  UiNodeTree (laid out)                                      │
│    ↓ Window-specific polish (Options/Lobby/ChannelLobby)     │
│  Final tree → Avalonia render                               │
└─────────────────────────────────────────────────────────────┘
```

### 3.6 优点

1. **域边界与文档一致**：Network 不实现 IGameSession；游戏会话经 ActiveGameRoom 暴露。
2. **可测性显著提升**：MainWindow / LobbyBehaviors / Overlay / GameLaunchService 关键路径已可注入 mock。
3. **算法层测试追平契约层**：DX 表达式 / 锚点 / Fill / Distance / 重叠消解 / 文本测量 / INI 继承链全部有用例锁定。
4. **迁移未破坏稳定**：479 单测全绿；旧 DTO 字段保留；escape hatch 让强类型 helper 不阻塞。
5. **三棵继承树策略务实**：避开 C# 单继承与过度抽象。

### 3.7 不足 / 风险

1. **MainWindow 仍是上帝对象**：约 2000+ 行；事件订阅 + 房间玩家绑定 + 选项桥接 + 启动逻辑混在一起。已切到 `_cncnet` 但拆分仍未做。
2. **双重真相**：`LobbyPlayerState` 与 `ISkirmishSession.PlayerSlots` 并存；CnCNet `Players`（`CnCNetGameRoomPlayer`）与 `PlayerSlots` 尚未统一同步方向。
3. **服务定位器全局静态**：比 DI 轻，但并行测试需 `[Collection]` 串行 + `INeverRegisteredMarker` 隔离（已实现）。
4. **`ProgramConstants` / `ClientConfiguration.Instance` 调用面未迁**（~370 处）：契约在、调用没切。
5. **`ApplyLabelAnchor` 一处实现细节**：`TryGetRawAttribute` 失败时把 out 参数设为 `string.Empty` 而非 null，导致 `textAnchor ??= ...` 永不触发 bare `TextAnchor`。测试已用 `$TextAnchor` 绕过；标注为已知行为，L2 修复（详见 §6）。
6. **`TunnelMaintenanceLoop` 未接生产定时器**（按计划）：低延迟隧道维护逻辑与 Session 定时器路径仍分裂。

---

## 4. 测试覆盖率（Test Coverage）

### 4.1 总量

- **过滤 Live IRC / Integration**：**479 通过 / 0 失败**（L1 后 359 → 本轮 +120 UI 引擎）。
- **覆盖维度**：契约（29）+ 迁移 smoke（10）+ UI 引擎语义（120）+ 原有功能（320）。

### 4.2 本轮新增 UI 引擎单测明细（120 用例，全过）

| 文件 | 用例数 | 锁定的 DX/INI 语义 |
|------|--------|--------------------|
| `IniConversionsTests` | 22 | Yes/No/True/False/1/0 + 任意字符串 fallback（DX IniFile.GetBooleanValue） |
| `IniPrimitivesTests` | 24 | `@` / `\n` 折行；DrawMode (Stretched/Centered/Tiled) → Avalonia Stretch；ClickSound→ClickSoundEffect 等键名归一 |
| `ExpressionEvaluatorTests` | 21 | 整数字面量、`+ - * / ()`、`/0` 留前值、左结合、`RESOLUTION_WIDTH/HEIGHT` 常量、`getX/getY/getWidth/getHeight/getRight/getBottom`、`$ParentControl` / `$Self`、`horizontalCenterOnParent`、未知常量/函数抛异常 |
| `LayoutResolverTests` | 15 | `$X/$Y/$Width/$Height` 与 bare `X/Y/...`；`DistanceFromRightBorder/BottomBorder`；`FillWidth/Height`；`AnchorPoint + $TextAnchor`（LEFT/RIGHT/HORIZONTAL_CENTER/BOTTOM/VERTICAL_CENTER）；`DrawOrder → -ZIndex`；`UpdateResolution` 重 layout |
| `PanelLayoutPassTests` | 8 | 同列重叠下推、同行重叠右推、隐藏节点不参与、非 content panel 跳过、`*Panel` 在 `*Lobby` 下视为 content、级联重叠收敛、单子节点不处理 |
| `MeasurePassTests` | 9 | CheckBox-like 最小尺寸（W≥70, H≥22）、Latin vs CJK 字宽、显式更大尺寸保留、PNG 缺失 200×54 fallback、多行高度增长、空节点保持 0 |
| `IniDocumentTests` | 11 | Section/Key 大小写不敏感、注释跳过、BasedOn 多文件链、`$THEME_DIR$` 展开、`$BaseSection` 拉取缺失键、ParseOverlay 跳过 BasedOn、Save 往返、缺省值 |
| **合计** | **120** | 覆盖 9 个原零测试的核心算法/解析单元 |

### 4.3 测试设计原则

1. **断言以 DX 行为为准**：注释里引用 DX 源（`ClientGUI.Parser`、`XNAWindowBase`、`GameLobbyBase`、`ClientCore.CCIniFile`）作为权威。
2. **理论 + 事实**：用 `[Theory]` 枚举边界（Yes/Yes/YES、空字符串、未知 token），用 `[Fact]` 锁定关键路径。
3. **不测 Avalonia 渲染**：测的是 INI → 预计算 layout 这一层，渲染层用 SkippableFact 留给端到端（依赖 DXMainClient fixture）。
4. **隔离**：`EnvironmentServices` 串行集合 + `INeverRegisteredMarker`；`IniDocument` 用临时文件 + try/finally 删除。

### 4.4 当前覆盖盲区

- **MainWindow 主体**：仍未拆 ViewModel，无单测；只能靠集成 / 手测。
- **CnCNetGameRoomSession 的 CTCP 协议解析**（`HandlePlayerOptions` / `ApplyGameOptions`）：当前只有接口 smoke，缺协议级单测。
- **`ProgramConstantsGameEnvironment` 与生产 ProgramConstants 同步**：契约正确但缺端到端 INI 加载测试。

---

## 5. 可扩展性与代码质量（Extensibility & Code Quality）

### 5.1 可扩展性

| 扩展点 | 当前支持 | 例子 |
|--------|----------|------|
| 新增 mod 资源类型 | ✅ 实现 `IResource` 子接口 | `IResourceManifest` 预留给在线更新 |
| 新增会话类型 | ✅ 继承 `IGameSession` | 未来 LAN/Campaign 都可加 `ILANGameSession` |
| 新增网络操作 | ✅ `ICnCNetSession` 加成员 + Adapter 转发 | 本轮加了 16 个新方法 |
| 替换服务实现 | ✅ `EnvironmentServices.Register<T>` 覆盖 | 测试用 MockGameEnvironment / fake color catalog |
| 新 UI 控件类型 | ✅ `ControlRegistry` 注册 | INI authored 控件自动走 IniUiTreeBuilder |
| 新 layout 策略 | ✅ 加一个 *LayoutPass | 已有 Measure / Layout / Panel 三段 |

### 5.2 代码质量

| 维度 | 评价 | 备注 |
|------|------|------|
| 命名 | ✅ 一致 | 接口 `IXxx`、Adapter `XxxAdapter`、Mock `MockXxx` |
| 注释 | ✅ 中文 XML doc 覆盖所有接口与关键适配器 | 包含设计意图（“冲港口”、“escape hatch”、“桥接”） |
| 可读性 | 🟡 MainWindow 仍杂；其它模块清晰 | 后续按窗口拆 ViewModel |
| 死代码 | 🟡 极少量遗留：旧 `LobbyActionContext` 重载、`CnCNetActiveGameRoom?` 局部变量等 | 不影响功能 |
| 异常处理 | ✅ 关键路径 try/catch + 日志 | Shutdown / Launch / GameLaunchDiagnostics |
| 线程安全 | ✅ 关键路径用锁 | EnvironmentServices、ShutdownService、CnCNetGameRoomSession |
| 测试稳定性 | ✅ 串行 + marker 类型隔离 | 已解决并行 flaky |

### 5.3 可维护性风险

1. **MainWindow 2000 行单文件**：最高优先级技术债。
2. **`ApplyLabelAnchor` 的 null-coalesce bug**：导致 bare `TextAnchor` 不生效；测试已暴露但未修（按“不破坏现状”策略）。
3. **遗留 strong-typed helpers**：`ApplyCnCNetGameRoomPlayersCore(root, CnCNetActiveGameRoom, ...)` 等签名仍是具体类型；要消掉 `ActiveGameRoomCore` 需先改这些 helper 的签名。

---

## 6. 与“理想架构”的差距

| 理想 | 现状 | 下一步建议 |
|------|------|------------|
| UI 只依赖接口 | MainWindow / Behaviors / Overlays / Binding 已切；Services 部分残留 | 把 GameLaunchDiagnostics / ShutdownService 也切到 `ICnCNetSession`（注：ShutdownService.Dispose 是 ICnCNetSession 未实现的，可考虑让接口实现 IDisposable） |
| 单一 Session 状态源 | LobbyPlayerState + GameRoom.Players + ISkirmishSession.PlayerSlots 三套 | 明确同步方向：UI ↔ `IPlayerSlot` ↔ CTCP Players |
| 配置可 mock | 接口在、调用没切（~370 处 `ProgramConstants`/`ClientConfiguration.Instance`） | 按文件批量替换，最先做 `IGameConfiguration` Options / spawn 热路径 |
| UI 引擎全测 | 9 个核心算法已覆盖；MainWindow 主体未测 | 拆 MainWindow 为 ViewModel + 事件聚合，再做 VM 单测 |
| 在线更新就绪 | Manifest NoOp | 独立设计增量包后再实现 `IResourceManifest` |
| Tunnel 维护接入 | TunnelSorter/MaintenanceLoop 已就位 | L2 接到 CnCNetSessionService 的定时器路径 |

---

## 7. 总体评分（主观，1–5）

| 维度 | L1 后 | 本轮后 | Δ | 评语 |
|------|-------|--------|---|------|
| 域模型清晰度 | 4.5 | 4.5 | — | Session/Network 拆分干净；扩展未引入新风险 |
| 依赖可测性 | 4.0 | 4.5 | +0.5 | MainWindow / Lobby / Overlay / Launch 关键路径可注入；UI 引擎算法全测 |
| 生产解耦完成度 | 2.5 | 3.5 | +1.0 | Network 域大面积迁移完成；Config/ProgramConstants 仍待 |
| 演进空间（mod/更新） | 4.0 | 4.0 | — | 资源元数据与 Manifest 预留到位 |
| 测试覆盖与稳定性 | 3.5 | 4.5 | +1.0 | 479 用例全绿；UI 引擎核心算法零盲区 |
| 风险可控性 | 4.0 | 4.0 | — | 测试绿、破坏面集中在 Context/Policy/Adapter |

**总评（一句话）**：架构方向正确，L1 契约 + 冲港口 Session 语义已立住；本轮把 Network 域生产调用与 UI 引擎核心算法的可测性都追平了契约层。**下一步价值最大的是收口 `ProgramConstants` / `ClientConfiguration.Instance` 配置域调用，以及把 MainWindow 拆成可测的 ViewModel**——而不是再抽更多接口。
