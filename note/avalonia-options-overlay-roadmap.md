# Options 浮层架构与工作历程

日期：2026-06-04

## 架构决定（已实现）

设置界面 **不再** 通过 `NavigateTo("OptionsWindow")` 替换整窗视口。

| 项 | 说明 |
|----|------|
| 宿主 | `MainWindow` 保持 MainMenu 分辨率（如 ThemeMG **1280×800**） |
| 设置 UI | 独立 **576×475** 浮层 `PART_OptionsOverlay`，居中叠在主菜单之上 |
| INI | 仍加载 `OptionsWindow.ini`，走 `LayoutEngine` + `OptionsWindowLayout.FinalizeLayout` + `OptionsPanelStackLayout` |
| 行为 | 独立 `BehaviorRegistry`（`_optionsBehaviors`），与主窗 `_mainBehaviors` 分离 |
| 入口 | 主菜单 `btnOptions` → `OpenOptionsOverlay()` |
| 关闭 | 保存/取消/Esc/点击遮罩 → `CloseOptionsOverlay()`，主菜单不卸载 |

常量：`ClientAvalonia/Services/OptionsOverlayConstants.cs`（576×475）

## 待迁移 / 尚未限制（记入历程，**本阶段不实现**）

以下在 XNA 客户端中为硬编码或完整实现，Avalonia 浮层当前为 **部分 INI + 代码排版**，后续里程碑再补：

1. **DisplayOptionsPanel 完整项** — 游戏内/客户端分辨率下拉、细节等级、渲染补丁、主题等（XNA `DisplayOptionsPanel.cs` 硬编码，非 MG overlay INI）
2. **AudioOptionsPanel 完整项** — 音量滑块、音乐/音效 checkbox 等（同上）
3. **FileSettingCheckBox** — 勾选时复制/链接 mix、dll 等到游戏目录（当前仅 bool 写入 RA2MG.ini）
4. **Tab 切换完整迁移** — 纹理 TabControl、禁用态 Tab（Mod 无更新器/组件页）、与 XNA `XNAClientTabControl` 行为一致（当前：代码生成 `btnTab*` + 键盘 1–6，**非**最终 UI）
5. **SettingCheckBox / SettingDropDown 全量绑定** — 所有选项页控件与 `UserINISettings` 一一对应
6. **UpdaterOptionsPanel / ComponentsPanel** — 更新器与组件列表（依赖 ClientCore / 更新镜像）

## 与旧实现的差异

| 旧 | 新 |
|----|-----|
| 整窗切换为 576×475 | 主窗尺寸不变，仅浮层 576×475 |
| 与 GenericWindow 边框表达式共用主窗 layout pass | 浮层 layout 上下文固定，FinalizeLayout 锁定坐标 |
| `CurrentWindow == OptionsWindow` | `CurrentWindow` 恒为 MainMenu，`IsOptionsOverlayOpen` 表示设置打开 |

## 验证

1. 启动后主菜单标题含 **1280×800**（或当前主题 Size）
2. 点「选项」→ 半透明遮罩 + 居中设置框，**窗口尺寸不变**
3. Esc / 取消 → 回到主菜单，无黑屏闪切
4. `--dump-tree Resources\OptionsWindow.ini OptionsWindow` 仍可用于无头排版检查

## 回归记录

| 日期 | 内容 | 结果 |
|------|------|------|
| 2026-06-04 | Options 浮层架构落地 | |
