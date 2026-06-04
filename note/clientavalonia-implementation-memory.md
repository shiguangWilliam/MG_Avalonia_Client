# ClientAvalonia 实现记忆库

> 最后更新：2026-06-04  
> 用途：AI / 开发者快速恢复 ClientAvalonia 架构、已完成功能、关键文件与已知限制。

## 架构概览

| 层 | 职责 |
|---|---|
| **ClientCore** | 启动引导、`ProgramConstants`、`ClientConfiguration`、设置持久化、**CnCNet 网络共享类型** |
| **INI UI 管线** | `IniDocument` → `IniUiTreeBuilder` → `LayoutEngine` → `UiNodeViewModel` → Avalonia 模板 |
| **Behaviors** | `BehaviorRegistry` + `UiBehaviorCatalog`：点击导航、**固定代码绑定**（如 `btnLaunchGame`） |
| **Binding** | `GameDataBindingApplier`、`LobbyPlayerBindingApplier`、`SettingBindingApplier` |
| **Services** | 目录加载、资源解析、`GameLaunchService`、spawn.ini、`CnCNetSessionService` |

## 构建与部署

```powershell
.\Scripts\build-clientavalonia.ps1 -SkipValidate `
  -DeployTo "D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版"
```

MG 测试区：`Resources/ThemeMG/`；遭遇战 `SkirmishLobby.ini` → `GameLobbyBase.ini`。

## 已完成功能（2026-06-04）

### 遭遇战启动（已落实）

- **`btnLaunchGame`**：`LobbyBehaviors` 固定绑定（非 INI）；`SkirmishLaunchValidator` 对齐 `CheckGameValidity`；失败 `ClientDialogService`。
- **按钮启用**：`SetCanLaunchGame` 须在 `ApplyToTree` 之前；`UpdateLaunchButtonState` 随地图选中刷新。
- **spawn 链**：`SkirmishSpawnWriter` + `SpawnIniApplier` + `ForcedSpawnCatalog` + `MapCodeHelper` → `GameLaunchService`（Syringe `-SPAWN`）。
- **左侧栏**：`ApplyMapToolbarLayout` 对齐随机/搜索；`FilterMapsBySearch` + `InputText` 搜索绑定。

### CnCNet 频道大厅（IRC 握手 + UI 绑定已落实）

- **ClientCore/Network/**：`CnCNetHttp`、`CnCNetIrcServerList`、`CnCNetGameChannels`、`CnCNetIdentity`（注册表 Ident）、隧道/人数/CTCP 解析。
- **`CnCNetIrcConnection`**：001 Welcome 后 JOIN；451/433/CAP/353 过滤；NOTICE+PRIVMSG CTCP GAME。
- **`CnCNetSessionService`**：Welcome → JOIN `#cncnet`/聊天/广播 + WHO；玩家列表与 hosted games → `MultiplayerLobbyState`；**`lbChatMessages` 实时连接日志**。
- **`PlayerNameSettings`**：选项 → 游戏页 `tbPlayerName` ↔ `[MultiPlayer] Handle`（对齐 XNA `GameOptionsPanel`）。
- **UI 渲染修复**：`lbPlayerList`/`lbGameList` 由 INI 孤儿节区正确推断为 `ChatListBox`（`DxListBox`）；`ChannelLobbyLayout` 计算三栏宽度；`ApplyChannelLobby` 在 `StateChanged` 时刷新列表。

### 战役 / INI UI / 玩家槽位

（同前版本：CampaignSelector 浮层、资源回退、Skirmish 8 槽位、`Client/SkirmishSettings.ini` 等。）

## 关键文件

| 区域 | 路径 |
|---|---|
| **启动** | `SkirmishSpawnWriter.cs`, `GameLaunchService.cs`, `SkirmishLaunchValidator.cs`, `LobbyBehaviors.cs` |
| **CnCNet** | `ClientCore/Network/*`, `CnCNetIrcConnection.cs`, `CnCNetSessionService.cs` |
| 玩家槽位 | `LobbyPlayerBindingApplier.cs`, `LobbyPlayerState.cs` |
| 主窗口 | `MainWindow.axaml.cs`, `ClientStartupService.cs` |
| 布局后处理 | `LobbyLayout.cs`（遭遇战地图工具条）、`ChannelLobbyLayout.cs`（CnCNet 三栏） |
| XNA 参考 | `GameLobbyBase.cs`, `SkirmishLobby.cs`, `Connection.cs`, `CnCNetLobby.cs` |

## 已知限制

1. **CnCNet 游戏内**：Join/Host、隧道 NAT、LMP、断线重连未移植。
2. **联机游戏大厅** `CnCNetGameLobby`：`btnLaunchGame` 仍为 stub。
3. ~~CnCNet 底部 `btnNewGame`/`btnJoinGame` 水平间距~~ — 已在 `ChannelLobbyLayout` 按 133px 排布并补按钮文字。
4. 起始位置随机化 / `PlayerExtraOptionsPanel` / 合作任务校验未全量移植。
5. 颜色下拉仍为 Random 简化版。

## 相关笔记

- **`note/cncnet-and-launch-implementation.md`** — CnCNet + 遭遇战启动详细对照与验证清单
- `note/mg-avalonia-test-env.md` — MG 测试区
- `note/ini-ui-specification.md` — INI UI 规范
