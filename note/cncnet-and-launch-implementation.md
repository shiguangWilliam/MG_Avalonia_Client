# CnCNet 与遭遇战启动 — ClientAvalonia 实现说明

> 最后更新：2026-06-04  
> 对照源码：`DXMainClient/Online/Connection.cs`、`GameLobbyBase.cs`、`SkirmishLobby.cs`、`Startup.cs`  
> Core 约定：`ClientCore/Network/*`、`ClientCore/ProgramConstants.cs`、`ClientConfiguration`

## 一、遭遇战启动（SkirmishLobby）

### XNA 约定（ClientGUI）

| 步骤 | XNA 位置 | 行为 |
|------|----------|------|
| 点击 | `GameLobbyBase.Initialize` | `btnLaunchGame.LeftClick += BtnLaunchGame_LeftClick`（**代码固定绑定**，非 INI `$LeftClickAction`） |
| 校验 | `SkirmishLobby.CheckGameValidity` | 游戏模式/地图是否仅联机、玩家数上下限、起始位置冲突 |
| 保存 | `SkirmishLobby.SaveSettings` | 写入 `Client/SkirmishSettings.ini` |
| 写 spawn | `GameLobbyBase.WriteSpawnIni` + `WriteMap` | `spawn.ini` + `spawnmap.ini` |
| 启动 | `GameProcessLogic.StartGameProcess` | Syringe/gamemd + `-SPAWN`，等待 INI 预处理 |

### Avalonia 对应

| 步骤 | 文件 | 说明 |
|------|------|------|
| 行为注册 | `IniUi/Behaviors/LobbyBehaviors.cs` | `SkirmishLobby` 下 `btnLaunchGame` → `TryLaunchSkirmish`；失败弹 `ClientDialogService`（对齐 `XNAMessageBox`） |
| 按钮启用 | `MainWindow.axaml.cs` | `SetCanLaunchGame(true)` **先于** `ApplyToTree`；`UpdateLaunchButtonState` 随地图选中更新 |
| 校验 | `Services/SkirmishLaunchValidator.cs` | 移植 `CheckGameValidity` 子集（联机专用地图/模式、Min/MaxPlayers、EnforceMaxPlayers 起始位） |
| 保存 | `Services/LobbyPlayerState.cs` | `SaveSkirmishSettings()` → `Client/SkirmishSettings.ini` |
| spawn | `Services/SkirmishSpawnWriter.cs` | Settings / Other* / HouseHandicaps*；调用 `SpawnIniApplier`、`ForcedSpawnCatalog` |
| spawnmap | 同上 | `MapCodeHelper` + `ApplyMapCodeControls` |
| 启动 | `Services/GameLaunchService.cs` | `GameLauncherExecutableName` + `GetGameExecutableName()` + `-SPAWN`；删 DTA/TI/TS.LOG；等 `PreprocessorBackgroundTask` |

### 左侧栏（地图工具条）

| 控件 | XNA | Avalonia |
|------|-----|----------|
| `btnPickRandomMap` | `GameLobbyBase.BtnPickRandomMap_LeftClick` | `LobbyBehaviors` → `PickRandomLobbyMap`（清空搜索后随机） |
| `tbMapSearch` | `TbMapSearch_InputReceived` → `ListMaps` | `InputText` 双向绑定 → `GameResourceCatalog.FilterMapsBySearch` |
| 布局 | MG `GameLobbyBase.ini` 随机左、搜索右 | `LobbyLayout.ApplyMapToolbarLayout` 后处理：搜索框紧贴随机按钮并延伸至地图列表右缘 |

### 启动命令链（MG 测试区）

```
ClientDefinitions.ini:
  GameLauncherExecutableName=Syringe.exe
  GameExecutableNames=gamemd.exe

实际进程:
  Syringe.exe "gamemd.exe" -SPAWN
  WorkingDirectory = ProgramConstants.GamePath
```

日志：`Client/client.log`（`ClientLogService`，对齐 `PreStartup` 日志轮转）。

---

## 二、CnCNet 在线（频道大厅）

### XNA 约定

| 阶段 | XNA | 要点 |
|------|-----|------|
| 身份 | `Startup.cs` → `Connection.SetId` | 注册表 `HKCU\SOFTWARE\{InstallationPathRegKey}\Ident`，SHA1 截断作 IRC USER 后缀 |
| 连接 | `Connection.ConnectAsync` | 多服务器/端口；TCP 后 `Register()` 发 USER/NICK |
| 欢迎 | numeric **001** | `welcomeMessageReceived=true`；**此后**才 JOIN 频道 |
| 注册重试 | numeric **451** | 再次 `Register()`（不能在已 welcome 后重复） |
| 昵称冲突 | numeric **433** | 改 `ProgramConstants.PLAYERNAME` 加 `_`，重发 NICK |
| 能力 | `CAP LS` | 回复 `CAP END`（GameSurge） |
| 用户列表 | numeric **353** | 仅 `parameters[0]==PLAYERNAME` |
| 游戏广播 | NOTICE/PRIVMSG CTCP `GAME` | 解析 hosted game 列表 |
| JOIN 时机 | `CnCNetLobby.ConnectionManager_WelcomeMessageReceived` | `#cncnet`、聊天频道、广播频道 |
| WHO | `Channel.RequestUserInfo` | 加入聊天频道后对 `#channel` 发 WHO |

### ClientCore（共享）

| 类型 | 路径 |
|------|------|
| IRC 服务器列表 | `CnCNetIrcServerList` ← `ClientConfiguration.IRCServers` |
| HTTP | `CnCNetHttp` |
| 频道名 | `CnCNetGameChannels` ← `Resources/GameCollectionConfig.ini`（**非** `ThemeMG/`；与 XNA `GameCollection.cs` 一致） |
| 身份 | `CnCNetIdentity` ← 注册表 Ident + SHA1（`EnsurePersisted` 于 `ClientStartupService.Run`） |
| 隧道列表 | `CnCNetTunnelListLoader` |
| 在线人数 | `CnCNetPlayerCountService` ← `CnCNetLiveStatusIdentifier` |
| CTCP 解析 | `CnCNetGameMessageParser` |

### ClientAvalonia

| 组件 | 路径 | 职责 |
|------|------|------|
| IRC 客户端 | `Network/CnCNetIrcConnection.cs` | TCP、USER/NICK、001 Welcome、451/433、CAP END、353 过滤、NOTICE+PRIVMSG CTCP |
| 会话 | `Services/CnCNetSessionService.cs` | Welcome 后 JOIN；WHO；玩家列表；游戏广播 → `MultiplayerLobbyState` |
| UI 绑定 | `GameDataBindingApplier.ApplyChannelLobby` | `lbPlayerList`、`lbGameList`、`lblCurrentChannel` |
| 连接触发 | `MainWindow.ApplyLobbyData` | 进入 `CnCNetLobby` 时 `ConnectIfNeeded()` |

### IRC 握手时序（修复后）

```
TCP connect
  → USER {LocalGame}.{systemId} 0 * :{GAME_VERSION} {LocalGame} CnCNet
  → NICK {PLAYERNAME}
  ← 001 Welcome          → WelcomeReceived
  → JOIN #cncnet
  → JOIN #{game}-chat
  → JOIN #{game}-game
  → WHO #{game}-chat
  ← 353 / JOIN / CTCP GAME ...
```

**注意**：ConnectLoop 中的 `CancellationToken` 仅用于本地连接超时/断开，**不是**服务端认证 token。

### 尚未移植（相对 XNA `CnCNetGameLobby`）

- Join Game / Host Game 完整流程
- 隧道 NAT / LMP / 游戏内同步
- 私聊、CTCP 邀请、好友/忽略
- 断线重连（`Connection` MAX_RECONNECT_COUNT）
- `CnCNetGameCheck` 服务

### 频道配置路径 + 连接日志（2026-06-04 修复）

**现象**：IRC 握手成功（log 有 channel / tunnel cache / 353 用户列表），但 `CnCNetLobby` 玩家列表与游戏列表空白。

**根因**：XNA 在 `CnCNetLobby.cs` **代码中创建** `lbPlayerList` / `lbGameList` 等控件；MG 的 INI 只有布局属性（`Location`、`BackgroundTexture`）。Avalonia 的孤儿节区推断把带 `BackgroundTexture` 的 `lb*List` 误判为 **`XNAExtraPanel` → DxPanel**，`SetListItems` 无法绑定到 ListBox。

**修复**：

| 改动 | 文件 |
|------|------|
| 硬编码控件 id → 正确 INI 类型（`ChatListBox` 等） | `IniUiTreeBuilder.TryInferKnownControlType` |
| 仅含 `DistanceFromBottomBorder` 的 `btn*`/`dd*` 也被收养 | `IniUiTreeBuilder.IsKnownHardcodedControlSection` |
| 三栏宽度（游戏列表 / 聊天 / 玩家列表） | `ChannelLobbyLayout.Apply` |
| 绑定日志 + `ddCurrentChannel` 频道名 | `GameDataBindingApplier.ApplyChannelLobby` |

验证：`dotnet ClientAvalonia.dll --dump-tree Resources\CnCNetLobby.ini CnCNetLobby` 应显示 `lbPlayerList` / `lbGameList` 为 **DxListBox**（非 DxPanel）。

### 频道配置路径 + 连接日志（2026-06-04 修复）

**现象**：log 有 `cached 69 tunnels`，但界面显示 `No chat channels configured.`，IRC 从未连接；玩家/游戏列表恒为 0。

**根因**：`CnCNetGameChannels` 误用 `GetResourcePath()`（`Resources/ThemeMG/`），而 MG 的 `GameCollectionConfig.ini` 在 **`Resources/` 根目录**（XNA `GameCollection.GetCustomGames` 使用 `GetBaseResourcePath()`）。

**修复**：

| 改动 | 文件 |
|------|------|
| 优先 `Resources/GameCollectionConfig.ini`，回退主题目录 | `ClientCore/Network/CnCNetGameChannels.ResolveConfigPath` |
| 中央 `lbChatMessages` 显示带时间戳的连接日志 | `MultiplayerLobbyState.ConnectionLog` + `ApplyChannelLobby` |
| IRC 连接进度写入 UI（Trying/JOIN/WHO/353 等） | `CnCNetSessionService.LogActivity`、`CnCNetIrcConnection.ActivityLogged` |
| 底部按钮文字 + 水平间距（对齐 XNA 133px） | `ApplyChannelLobbyButtonLabels`、`ChannelLobbyLayout` |

**MG 频道配置示例**（测试区 `Resources/GameCollectionConfig.ini`）：

```ini
[CustomGame]
InternalName=MG
UIName=创世之刻
ChatChannel=#yuanming-games
GameBroadcastChannel=#yuanming-cg-games
```

### 大厅布局 + 玩家昵称 + 连接保活（2026-06-04）

| 改动 | 说明 |
|------|------|
| `ChannelLobbyLayout` | 按 XNA `CnCNetLobby.cs` 重算三栏（278 / 居中聊天 / 190px 玩家列表）、顶栏频道/颜色、底栏按钮 |
| `OptionsGameControlsBootstrap` | 选项 → **游戏** 页注入 `tbPlayerName`（`[MultiPlayer] Handle`） |
| `PlayerNameSettings` | 启动时从 UserINI 加载 `ProgramConstants.PLAYERNAME`；保存时写回 |
| `CnCNetIrcConnection` | 修复 30s 误判断线（ReadTimeout 不计错）；对齐 XNA 每 120s `PING LAG*` 保活 |

**玩家昵称**：与 XNA `GameOptionsPanel` 相同，存于用户 INI `[MultiPlayer] Handle`；修改后需退出 CnCNet 大厅再进入。

---

## 三、验证清单

### 遭遇战

1. 进入 SkirmishLobby，左下角 **开始游戏** 可点击（非灰色）。
2. 点击后 `Client/client.log` 出现 `SkirmishSpawnWriter: writing spawn.ini` 与 `Launch executable`。
3. 游戏目录生成 `spawn.ini`、`spawnmap.ini`。
4. 仅联机地图/模式会弹窗拒绝（`SkirmishLaunchValidator`）。
5. 随机地图 + 搜索框同一行对齐；搜索可过滤列表。

### CnCNet

1. 主菜单 → 在线 → CnCNet 大厅，中央列表显示 **连接日志**（Trying/JOIN/WHO 等）。
2. `client.log` 不再出现 `GameCollectionConfig.ini not found`（路径为 `Resources/` 非 `ThemeMG/`）。
3. 001 之后才有 JOIN 行；频道名应显示「创世之刻」而非 `No chat channels configured`。
4. 频道玩家列表、hosted games（若有人在广播）有数据。
5. 主菜单在线人数与 `CnCNetPlayerCountService` 一致。

### 构建部署

```powershell
.\Scripts\build-clientavalonia.ps1 -SkipValidate `
  -DeployTo "D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版"
```

---

## 四、关键文件索引

| 领域 | Avalonia | XNA 参考 |
|------|----------|----------|
| 启动行为 | `LobbyBehaviors.cs` | `GameLobbyBase.cs` L250-251 |
| 启动校验 | `SkirmishLaunchValidator.cs` | `SkirmishLobby.cs` L109-168 |
| spawn 写入 | `SkirmishSpawnWriter.cs`, `SpawnIniApplier.cs` | `GameLobbyBase.WriteSpawnIni` |
| 进程启动 | `GameLaunchService.cs` | `GameProcessLogic.cs` |
| IRC 连接 | `CnCNetIrcConnection.cs` | `Connection.cs` |
| CnCNet 会话 | `CnCNetSessionService.cs` | `CnCNetManager.cs`, `CnCNetLobby.cs` |
| Core 网络 | `ClientCore/Network/*.cs` | `Connection.cs`, `CnCNetManager.cs` |
| 身份 | `CnCNetIdentity.cs` | `Startup.cs`, `Connection.SetId` |
