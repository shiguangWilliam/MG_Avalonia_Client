# ClientAvalonia M3/M4 核验清单

日期：2026-06-04

构建命令：`./Scripts/build-clientavalonia.ps1`  
MG 测试目录：`D:\MG\MG-Avalonia测试区\The Moment of Genesis 1.0.4.2内测版`

## 无头 CLI

| 命令 | 预期 |
|------|------|
| `dotnet ClientAvalonia.dll --validate-ini` | MainMenu 节点数 > 10，`rootChildren` 合理 |
| `dotnet ClientAvalonia.dll --validate-bindings` | `settingBindings >= 8`，`chkPersistentMode` 与 RA2MG.ini 一致 |
| `dotnet ClientAvalonia.dll --validate-resources` | `maps=201, gameModes=2, missions=43`（MG 测试区 cwd） |
| `dotnet ClientAvalonia.dll --validate-bindings Resources\ThemeMG\MainMenu.ini MainMenu` | `version="v.x.x.x"`，`updateStatus` 非空 |
| `dotnet ClientAvalonia.dll --dump-tree Resources\OptionsWindow.ini OptionsWindow` | `root children < 20`（无 SkirmishLobby 等 foreign 节点） |
| `dotnet ClientAvalonia.dll --dump-tree Resources\SkirmishLobby.ini SkirmishLobby` | `chkShortGame` 等为 `GameOptionsPanel` 子节点（深度 > 1） |

## M4 设置/状态绑定

- [ ] 主菜单左下角显示版本号（`lblVersion`）
- [ ] 主菜单显示更新状态文案（`lblUpdateStatus`）
- [ ] 设置 → CnCNet 页（按 **4** 或点击 tab）选项与 RA2MG.ini 一致
- [ ] 修改 checkbox → **保存/OK** → RA2MG.ini 更新
- [ ] 修改后 **取消** → 再次打开恢复原值
- [ ] 长文本 `@` 换行显示（Reshade/Vxl 等选项）

## 排版（非主界面）

- [ ] 设置 **浮层** 居中 576×475，主菜单视口尺寸不变
- [ ] 设置页签 1–6 / tab 按钮可切换面板（完整 TabControl 迁移见 `avalonia-options-overlay-roadmap.md`）
- [ ] 遭遇战 lobby：`GameOptionsPanel` 内 checkbox 两列布局，不叠在地图预览上
- [ ] checkbox 宽度随中文文本扩展，不 0×0

## M3 启动游戏

- [ ] 遭遇战 → 选地图 → **开始游戏** 启动 `gamemd.exe -SPAWN`（应先写 `spawn.ini` + `spawnmap.ini`）
- [ ] 战役浮层 → 选任务 → **开始** 启动（写战役 spawn.ini）
- [ ] 启动失败时状态栏显示原因（缺 exe、路径错误等）
- [ ] 游戏已运行时重复点击有提示

## M5 大厅/战役资源（2026-06-04）

- [ ] 遭遇战：`ddGameMode` 含「收藏地图」首项；地图列表 ~195+
- [ ] 地图预览图显示（`MapPreviewBox`）；点击预览切换收藏
- [ ] `btnPickRandomMap` 随机切换列表选中项
- [ ] 战役：GDI/Nod/ThirdSide 筛选任务列表（Allied/Soviet/Ackville）
- [ ] `--validate-resources` 通过

## 视觉（官方非 mod 控件）

- [ ] checkbox / label 使用 MG 橙色 HUD 色系
- [ ] 选项面板半透明深色底 + 细边框
- [ ] 中文 fallback 字体正常

## 已知限制

- FileSettingCheckBox 仅写入 bool，不复制 mix/dll
- 无真实更新器/CnCNet 在线人数
- Display/Audio 面板部分控件仍依赖 XNA 硬编码布局（仅 MG overlay 项可见）
- ClientCore 联编需 `-BuildDependencies`（Rampastring.Tools 子模块）
- Skirmish spawn 为简化版（1 人 + 1 AI）；lobby 选项面板未写入 spawn
- 详见 `clientavalonia-implementation-memory.md` §8

## 回归记录

| 日期 | 版本 | 结果 | 备注 |
|------|------|------|------|
| 2026-06-04 | M4+layout | | |
| 2026-06-04 | M5+resources+spawn | 构建/CLI 通过 | 待 GUI 手测 |
