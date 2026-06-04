# ClientAvalonia — INI 驱动 DX 基础组件

日期：2026-06-04

## 架构（M2）

```
INI (IniDocument / CCIniFile 对齐)
  → IniAstBuilder → IniFileAst
  → IniUiTreeBuilder（$CC / ExtraControls / 孤儿 section）
  → MeasurePass（纹理/文本尺寸）
  → LayoutResolver + ExpressionEvaluator（1280×720 一次求值）
  → UiNode 树
  → UiViewModelFactory + BehaviorRegistry
  → UiNodeViewModel（预编译 DataTemplate + 点击桩）
```

| 路径 | 职责 |
|------|------|
| `IniUi/Ast/` | `IniFileAst`, `IniAstBuilder` |
| `IniUi/Layout/` | `LayoutContext`, `LayoutEngine`, `DefaultParserConstants` |
| `IniUi/Loading/` | `ClientEnvironment`, `IniUiTreeBuilder`, `MeasurePass`, `ResourceResolver`, … |
| `IniUi/Behaviors/` | `BehaviorRegistry`, `MainMenuBehaviors` |
| `Rendering/` | `UiNodeViewModel`, `UiViewModelFactory` |
| `Controls/` | `DxTextureButton`, `IIniExtensionConsumer` |
| `Themes/DxControlStyles.axaml` | 全部基础 DX 组件 DataTemplate |
| `Views/` | `MainWindow`, `DxNodeCanvasView`, `DxNodeTemplateSelector` |

## 已注册基础模板（TemplateKey）

| TemplateKey | INI 类型（部分） | Avalonia 控件 |
|-------------|-----------------|---------------|
| DxPanel | XNAPanel, XNAExtraPanel, MapPreviewBox | Border + 子 Canvas |
| DxButton | XNAButton, XNAClientButton, GameLaunchButton | DxTextureButton（Idle/Hover 图） |
| DxLinkButton | XNALinkButton | DxTextureButton |
| DxLabel | XNALabel | TextBlock + RemapColor |
| DxLinkLabel | XNALinkLabel | 可点击 TextBlock |
| DxCheckBox | XNACheckBox, GameLobbyCheckBox, SettingCheckBox | CheckBox |
| DxComboBox | XNADropDown, GameLobbyDropDown | ComboBox |
| DxTextBox | XNATextBox, XNASuggestionTextBox | TextBox |
| DxListBox | XNAListBox, XNAMultiColumnListBox, ChatListBox | ListBox |
| DxProgressBar | XNAProgressBar | ProgressBar |
| DxSlider | XNATrackbar | Slider |
| DxScrollViewer | XNAScrollPanel | ScrollViewer |
| DxTabControl | XNATabControl | TabControl |
| DxIndicator | XNAIndicator | StackPanel |
| DxControlHost | XNAControl, 未知降级 | Border |

## 动态填充能力

1. **`$CC` / `ExtraControls`**：运行时按 `name:TypeName` 创建子节点（与 XNA `INItializableWindow` 一致）。
2. **孤儿 section 收养**（`AdoptOrphanControlSections`）：INI 中存在 section 但未通过 `$CC` 链接时（如 `MainMenu.ini` 的 `btnSkirmish`），按内容推断类型并挂到根节点。
3. **`RawAttributes`**：schema 未声明的键原样保留，供 `IIniExtensionConsumer` 业务控件读取。
4. **未知 TypeName**：降级为 `XNAPanel` 模板，不丢失 section 属性。

## 构建

### 日常开发（快速迭代）

M2 阶段 ClientAvalonia 可暂时无 `ClientCore` 引用（内置 `IniDocument`），允许单独编译：

```powershell
dotnet build ClientAvalonia/ClientAvalonia.csproj -c Debug
dotnet run --project ClientAvalonia -- --validate-ini
```

### 最终可行性验证（里程碑验收必用）

**各阶段（含 M2）做最终可行性验证时，必须使用仓库 `Scripts/` 下的专用脚本**，不要仅用上述孤立 `dotnet build` 作为验收依据：

```powershell
./Scripts/build-clientavalonia.ps1          # Release → CompiledAvalonia/
./Scripts/build-clientavalonia.ps1 -IsDebug # Debug
./Scripts/build-clientavalonia.ps1 -BuildDependencies  # 同时编 ClientCore（需 submodule）
```

脚本行为（`Scripts/build-clientavalonia.ps1`）：

1. `dotnet publish` ClientAvalonia（net8.0）→ `CompiledAvalonia/`
2. 从 `DXMainClient/Resources/` 复制 DTA、ClientDefinitions.ini、SUN.ini、Maps/INI/MIX
3. 无头校验 `--validate-ini`（MainMenu.ini 加载与布局）
4. 兼容 **Windows PowerShell 5.1**（`#Requires -Version 5.1`；MSBuild 属性用 `Debug%3BRelease` 避免分号被拆参）
5. `-BuildDependencies`：额外编 `ClientCore`（需 `Rampastring.Tools` submodule；ClientAvalonia 接入 ProjectReference 后验收时打开）

运行：

```powershell
cd CompiledAvalonia
dotnet ClientAvalonia.dll
```

ClientAvalonia 当前使用内置 `IniDocument`（语义对齐 `CCIniFile`），在 `Rampastring.Tools` submodule 就绪后可改回 `ProjectReference ClientCore` 并复用 `CCIniFile` + `Translation`。

若 `DXMainClient/Resources/DTA/MainMenu.ini` 可解析，启动后将预览主菜单 INI 布局（1280×720，不可缩放）。

无头校验：`dotnet run --project ClientAvalonia -- --validate-ini`

## M2 完成项

- 固定 1280×720 `LayoutContext`，加载时一次布局求值
- AST → Tree → Measure → Layout 管线（`LayoutEngine`）
- `ResourceResolver` + 纹理缺失时的按钮默认尺寸
- `BehaviorRegistry` + MainMenu 按钮/链接标签点击桩（状态栏反馈）
- 主菜单背景 `MainMenu/mainmenubg.png` 约定（C# 侧与 XNA 一致）
- `Relayout(tree, ctx)` 接口预留（M5 分辨率档）

## 规范依据

- `note/m0-key-ui-inventory.md`
- `note/ini-ui-specification.md`
