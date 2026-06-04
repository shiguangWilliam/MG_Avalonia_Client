# M0 关键 UI 盘点

日期：2026-06-04  
状态：M0 交付物（主界面 + 进入游戏路径优先）

## 1. 兼容级别定义

| 级别 | 含义 | 第一阶段目标 |
|------|------|-------------|
| L0 | 能加载 INI、构建节点树、渲染不崩溃 | 全部关键界面 |
| L1 | 布局/可见性/禁用状态符合预期 | 主界面、SkirmishLobby |
| L2 | 交互闭环（设置读写、能启动游戏） | SkirmishLobby → 启动 |
| L3 | 视觉像素级一致 | 不承诺 |

## 2. 关键界面总览

「进入游戏」在本盘点中指：**主菜单 → 遭遇战大厅（SkirmishLobby）→ 启动游戏进程** 的最短闭环。CnCNet/LAN 联机大厅列为 P1 扩展。

| 界面 | 实现方式 | 主要 INI 链 | 目标级别 |
|------|----------|-------------|----------|
| MainMenu | **C# + INI 混合** | `MainMenu.ini` | L1 |
| SkirmishLobby | **INI + C# 混合** | `SkirmishLobby.ini` → `GameLobbyBase.ini` → `GenericWindow.ini` | L2 |
| GameLobbyBase（抽象） | INI 壳 + 大量 C# | 同上 | L2 |
| GenericWindow（装饰框） | 纯 INI | `GenericWindow.ini` | L1 |
| TopBar | 纯 C# | 无 | P1 |
| OptionsWindow | C# + 部分 INI | `OptionsWindow.ini` | P1 |
| CnCNetLobby / LANLobby | C# 为主 | `CnCNetLobby.ini` / `LANLobby.ini` 仅主题 | P1 |
| GameLoadingWindow / CnCNetGameLoadingLobby | 纯 C# | 无完整 INI 驱动 | P2 |

## 3. MainMenu（主界面）

### 3.1 INI 文件

- 路径：`Resources/DTA/MainMenu.ini`（主题）或 `Base Resources/DTA/MainMenu.ini`
- 窗口 section：`[MainMenu]` — `Size`, `DrawBorders`
- ExtraControls：`Logo`, `txtVersion`, `btnRankedMatch`（`[ExtraControls]` section，非 `$ExtraControls`）

### 3.2 INI 定义的控件（mod 可改）

| Section | 类型（INI 语义） | 关键属性 |
|---------|-----------------|----------|
| Logo | XNAExtraPanel | Location, BackgroundTexture |
| btnNewCampaign | XNAClientButton（section 仅纹理） | Location, IdleTexture |
| btnLoadGame | 同上 | 同上 |
| btnCnCNet | 同上 | IdleTexture, HoverTexture |
| btnRankedMatch | XNALinkButton | URL, Enabled, HoverSoundEffect |
| btnLan / btnSkirmish / btnOptions / btnExit | 按钮纹理 | Location, IdleTexture |
| btnCredits / btnStatistics / btnMapEditor | 按钮 | HoverTexture, HoverSoundEffect, Enabled |
| lblCnCNetStatus / lblCnCNetPlayerCount | XNALabel | RemapColor, DistanceFromRightBorder |
| txtVersion / lblVersion / lblUpdateStatus | 标签/链接 | RemapColor, IdleColor, HoverColor |

### 3.3 C# 硬编码（INI 无法覆盖，需 Avalonia 侧重写）

来源：`DXMainClient/DXGUI/Generic/MainMenu.cs`

- 所有主按钮在 `Initialize()` 中 **再次 new** 并绑定 `LeftClick`（与 INI section 同名但 C# 创建优先于 XNAWindow 的 INI 子控件读取）
- 背景音乐（MediaPlayer / Song）
- 更新检查（ClientUpdater、`UpdateWindow` 流）
- CnCNet 在线人数轮询
- `DarkeningPanel` + `ISwitchable` 窗口栈
- `GameProcessLogic` 退出回调
- 首次运行 MessageBox、DirectDraw 兼容性检查

### 3.4 第一阶段 Avalonia 策略

- INI 层：支持 `MainMenu.ini` 的布局与纹理属性（L1）
- 业务层：主按钮命令、音乐、更新等待 M2+ 从 DXMainClient 移植或抽象为服务

## 4. SkirmishLobby / GameLobbyBase（进入游戏核心）

### 4.1 INI 继承链

```
SkirmishLobby.ini          [INISystem] BasedOn=GameLobbyBase.ini
  └─ GameLobbyBase.ini     [INISystem] BasedOn=GenericWindow.ini
       └─ GenericWindow.ini
            └─ SkirmishLobby section（窗口尺寸表达式、装饰 ExtraControls）
```

`CCIniFile` 合并规则：`BasedOn` 递归合并 section；section 内 `$BaseSection=` 继承同文件内另一 section 的键（子 section 优先）。

### 4.2 GameLobbyBase.ini 控件清单（$CC 动态子控件）

| Section | 注册类型名 | 角色 |
|---------|-----------|------|
| btnLaunchGame | GameLaunchButton | 启动游戏 |
| btnLeaveGame | XNAClientButton | 返回主菜单 |
| MapPreviewBox | MapPreviewBox | 地图预览（自定义） |
| GameOptionsPanel | XNAPanel | 游戏选项容器 |
| PlayerOptionsPanel | XNAPanel | 玩家槽位容器 |
| lbMapList | XNAMultiColumnListBox | 地图列表 |
| ddGameMode | XNAClientDropDown | 游戏模式 |
| tbMapSearch | XNASuggestionTextBox | 地图搜索 |
| btnPickRandomMap / btnSaveLoadGameOptions | XNAClientButton | 辅助按钮 |
| lblMapName / lblMapAuthor / lblGameMode / lblMapSize | XNALabel | 地图信息 |
| cmbTSFS … cmbGameSpeedCap | GameLobbyDropDown | 生成 spawn.ini 的下拉 |
| chkBases … chkRevealShroud | GameLobbyCheckBox | 游戏选项勾选 |
| GameOptionsPanel 内 $CC_00…$CC_29 | 嵌套 $CC | 选项面板内动态子控件 |

### 4.3 GameLobbyBase.ini 窗口级参数（非控件，C# 读取）

| 键 | 用途 |
|----|------|
| PlayerOptionLocationX/Y | 玩家选项面板起始坐标 |
| PlayerOptionVerticalMargin / HorizontalMargin | 玩家行间距 |
| PlayerNameWidth / SideWidth / ColorWidth / StartWidth / TeamWidth | 玩家下拉宽度 |

### 4.4 C# 硬编码（SkirmishLobby / GameLobbyBase）

- `InitPlayerOptionDropdowns()` — 动态创建 8 玩家槽位下拉（不在 INI）
- 地图列表数据绑定、favorite maps、spawn.ini 合成
- `GameProcessLogic.StartGameProcess`
- 设置持久化 `Client/SkirmishSettings.ini`
- 网络/聊天（多人子类）

### 4.5 表达式与常量（SkirmishLobby 必支持子集）

来自 `GenericWindow.ini` / `GameLobbyBase.ini`：

- 全局常量：`RESOLUTION_WIDTH`, `RESOLUTION_HEIGHT` + `ClientDefinitions.ini` 中 ParserConstants
- 布局常量：`EMPTY_SPACE_SIDES`, `EMPTY_SPACE_BOTTOM`, `EMPTY_SPACE_TOP`, `LOBBY_PANEL_SPACING`, `CHECKBOX_SPACING`
- 函数：`getX`, `getY`, `getWidth`, `getHeight`, `getRight`, `getBottom`
- 特殊参数：`$ParentControl`, `$Self`
- 定位键：`$X`, `$Y`, `$Width`, `$Height`（表达式）；`DistanceFromRightBorder`, `FillWidth`, `FillHeight`

## 5. GenericWindow（共享装饰 INI）

所有继承 `GenericWindow.ini` 的窗口共享 14 个 `ExtraControls` 装饰 panel（bar/glow）及对应 section 布局。

**第一阶段必须支持的属性：** BackgroundTexture, DrawMode, Location, DistanceFromRightBorder/Bottom, FillWidth/Height, RemapColor, DrawBorders。

## 6. 控件类型覆盖（第一阶段最小集 vs 完整集）

### 6.1 第一阶段最小集（L0–L2）

XNAPanel, XNAExtraPanel, XNAClientButton, XNALinkButton, XNALabel, XNAClientDropDown, XNACheckBox, GameLobbyCheckBox, GameLobbyDropDown, XNASuggestionTextBox, XNAMultiColumnListBox（可先 ListBox 占位）, MapPreviewBox（占位 UserControl）, GameLaunchButton（Button 占位）

### 6.2 P1 扩展

XNATextBox, XNATabControl, XNAScrollPanel, ChatListBox, SettingCheckBox, SettingDropDown, XNAProgressBar, XNATrackbar

### 6.3 仅 C#、无 INI 驱动

TopBar, GameLoadingWindow, TunnelSelectionWindow, CnCNetLoginWindow, HotkeyConfigurationWindow, XNAMessageBox

## 7. Golden INI 回归集（建议）

| 文件 | 用途 |
|------|------|
| `Resources/DTA/MainMenu.ini` | 主界面布局 |
| `Resources/DTA/SkirmishLobby.ini` + 继承链 | 遭遇战大厅 |
| `Resources/DTA/GenericWindow.ini` | 装饰框 + 表达式 |
| 典型 mod 主题 override | 验证 mod 路径解析 |

## 8. 与 ClientAvalonia 的对应关系

- **IniUiLoader + ControlRegistry**：解析上表 INI section → UiNode 树
- **Dx* 模板**：覆盖 6.1 最小集控件类型
- **ExtensionAttributes**：GameLobby* / MapPreviewBox 等业务键保留在 `RawAttributes`，供后续业务层消费
- **C# 业务**：MainMenu / GameLobbyBase 中列出的硬编码逻辑不在 ClientAvalonia 第一阶段实现，需后续 `ClientServices` 层
