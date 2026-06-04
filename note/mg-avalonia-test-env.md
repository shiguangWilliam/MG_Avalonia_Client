# MG Avalonia 测试环境核对

日期：2026-06-04

测试目录：`D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版`

## 目录角色

完整 **MG 1.0.4.2 内测** 游戏安装 + 已部署的 `ClientAvalonia.exe` / `ClientAvalonia.dll`，可作为 Avalonia 客户端端到端验收环境（优于仓库内仅有 INI、无贴图的 `CompiledAvalonia/`）。

根目录已具备：

| 项 | 路径 | 状态 |
|----|------|------|
| Avalonia 客户端 | `ClientAvalonia.exe` / `ClientAvalonia.dll` | 有 |
| 客户端定义 | `Resources\ClientDefinitions.ini` | 有，主题 `ThemeMG/` |
| 活动主题 INI | `Resources/ThemeMG/MainMenu.ini` | 有（MG 定制） |
| DTA 基线 INI | `Resources/DTA/MainMenu.ini` | 有（DTA 默认布局） |
| 主题镜像 INI | `Resources/MainMenu.ini` | 有（与 ThemeMG 版内容一致） |
| 遭遇战大厅链 | `Resources/SkirmishLobby.ini` → `GameLobbyBase.ini` | 有 |
| 贴图资源 | `Resources/ThemeMG/MainMenu/*.png` 等 | **完整**（约 450+ PNG） |

## 主题与 INI 加载顺序（对齐 XNA）

`ClientDefinitions.ini`：

```ini
[Themes]
0=Moment of Genesis,ThemeMG/
1=Default,ThemeDefault/
```

XNA 启动后（`Startup.cs`）：

1. `ProgramConstants.RESOURCES_DIR` → `Resources/ThemeMG`
2. 窗口 INI 优先 `GetResourcePath()/MainMenu.ini` → **`Resources/ThemeMG/MainMenu.ini`**
3. 贴图搜索路径含 `GetResourcePath()`，`MainMenu/button.png` → **`Resources/ThemeMG/MainMenu/button.png`**

**ClientAvalonia**：`MainMenu.ini` 的 `Size=` 驱动布局视口与窗口尺寸（MG 为 **1280×800**）；用户 `RA2MG.ini` `ClientResolutionX/Y` 作回退并受 ClientDefinitions min/max 约束。

## 两套 MainMenu.ini 差异

| | `Resources/DTA/MainMenu.ini` | `Resources/ThemeMG/MainMenu.ini`（MG 实际使用） |
|--|-------------------------------|--------------------------------------------------|
| 分辨率 | `1280×720` | **`1280×800`** |
| 按钮 | 每按钮独立 PNG（`campaign.png` 等） | 共用 `MainMenu/button.png` + **`Text=` 中文标签** |
| 布局 | X≈490 纵向菜单 | X≈885 右侧菜单 |
| ExtraControls | Logo / txtVersion / btnRankedMatch | 空 |
| 节点数（validate） | 19 | 14 |

`ClientDefinitions.ini` 还声明 `MinimumRenderHeight=768`、`MaximumRenderHeight=800`，与 MG 主菜单 **800 高**一致；M2 固定 `1280×720` 的 `LayoutContext` 与该测试包不完全匹配。

## 主菜单贴图（ThemeMG）

`Resources/ThemeMG/MainMenu/` 含：`button.png`、`button_c.png`、`mainmenubg.png`、`campaign.png`、`skirmish.png`、`options.png` 等全套。

`Resources/ThemeMG/` 根下另有通用 UI 图（`133pxbtn.png`、`gamelobbybg.png`、`mainmenubg.png` 等），供大厅/选项等复用。

`Resources/DTA/Default Theme/MainMenu/` 为 DTA 默认英文贴图集（与 MG 主题并存，非当前活动主题）。

## 大厅 INI（M3 预备）

| 文件 | 说明 |
|------|------|
| `Resources/GameLobbyBase.ini` | 基座 |
| `Resources/SkirmishLobby.ini` | `BasedOn=GameLobbyBase.ini` |
| `Resources/MultiplayerGameLobby.ini` | 联机 |
| `Resources/CnCNetGameLobby.ini` | CnCNet |

## 在测试区验证命令

```cmd
cd /d "D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版"

:: MG 主题主菜单（应对齐此 INI）
dotnet ClientAvalonia.dll --validate-ini Resources\ThemeMG\MainMenu.ini

:: DTA 基线（仅对照）
dotnet ClientAvalonia.dll --validate-ini Resources\DTA\MainMenu.ini

:: 启动 GUI
ClientAvalonia.exe
```

## 与仓库构建产物对接

1. 用 `./Scripts/build-clientavalonia.ps1 -SkipValidate -DeployTo "D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版"` 一键构建并部署（推荐）。
2. 或手动：构建后将 `CompiledAvalonia/` 内 **全部** DLL 覆盖测试区（须含 `System.Net.Http.Formatting.dll` 等依赖）。
3. 开发调试：cwd 设为测试区，`dotnet run --project ClientAvalonia`。

## 资源数据（2026-06-04 已接入）

| 数据 | 来源 | Avalonia 加载器 |
|------|------|----------------|
| 官方地图 (~201) | `INI/MPMaps.ini` | `MapCatalogLoader` |
| 自定义地图 | `Maps/Custom/*.{map}` | `MapCatalogLoader.LoadCustomMaps` |
| 游戏模式 | `MPMaps.ini [GameModes]` | `MapCatalogLoader.LoadGameModes` |
| 战役 (~43) | `INI/Battle.ini` | `MissionCatalogLoader` |

验证：`dotnet ClientAvalonia.dll --validate-resources`

## 窗口尺寸（浮层）

| 窗口 | INI | 尺寸 |
|------|-----|------|
| MainMenu | `ThemeMG/MainMenu.ini` | 1280×800（主窗口固定） |
| CampaignSelector | `CampaignSelector.ini` | 800×600 浮层 |
| OptionsWindow | `OptionsWindow.ini` | 576×475 浮层 |

## 待办（相对测试区仍缺/简化）

1. ~~**Theme-aware ResourceResolver**~~ — 已实现
2. ~~**MG 分辨率档**~~ — MainMenu.ini `Size=` 驱动
3. ~~**游戏资源加载**~~ — MPMaps/Battle + 大厅绑定
4. ~~**spawn.ini 启动**~~ — Skirmish/Campaign spawn writer
5. **Skirmish 完整 lobby**：多 AI 槽位、选项 checkbox/dropdown 写入 spawn、起始位置选择
6. **联机大厅** CnCNet 未迁移

> 完整实现状态见 **`note/clientavalonia-implementation-memory.md`**

### 验证（MG 测试区 cwd）

```cmd
cd /d "D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版"
dotnet path\to\ClientAvalonia.dll --validate-ini
:: OK: theme=ThemeMG, btnSkirmish 145x42, texture=button.png
```
