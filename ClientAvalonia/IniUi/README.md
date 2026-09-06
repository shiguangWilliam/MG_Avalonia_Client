# IniUi 引擎

> 本文档面向修改 UI 渲染、控件类型推断、INI 解析的开发者。
> 入门请先读 [docs/ARCHITECTURE.md](../ARCHITECTURE.md) §2 的流水线总览。

## 四件套职责

| 类 | 职责 | 不应做的事 |
|---|---|---|
| `IniDocument` | 解析 INI 文件，处理 `BasedOn` 链合并、`$BaseSection` 继承 | 不知道任何 UI 概念 |
| `IniUiTreeBuilder` | 把 `IniDocument` 转成 `UiNodeTree`，对齐 DX 的 R2-R8 语义 | 不计算 layout，不做数据绑定 |
| `LayoutEngine` | 在 `UiNodeTree` 上应用布局规则（`$X`、`FillHeight`、`DistanceFromBottomBorder`） | 不改控件类型 |
| `UiViewModelFactory` | 把 `UiNodeTree` 转成 `UiNodeViewModel`，挂 Behavior | 不解析 INI |

四者**单向依赖**：`IniDocument → IniUiTreeBuilder → LayoutEngine → UiViewModelFactory`。任何反向调用都是 bug。

## IniUiTreeBuilder 的 DX 对齐语义（R2-R8）

DX 客户端用代码硬编码创建控件，INI 只填属性。Avalonia 必须从 INI 反推出控件树，因此要复刻 DX 的"隐式控件创建规则"。这些规则记为 R2-R8：

| 规则 | DX 来源 | Avalonia 实现 |
|---|---|---|
| **R2** `[ExtraControls]` section | `XNAWindowBase.ReadChildControlAttributes` | `ParseExtraControlsSection(ini, root, "ExtraControls", ...)` |
| **R3** `[$ExtraControls]` section | 同上，新版 INI 约定 | `ParseExtraControlsSection(ini, root, "$ExtraControls", ...)` |
| **R4** `[$CCnn=name:Type]` 在普通 section 内 | `INItializableWindow.ReadINIForControl` | `ParseChildControlsFromSection` 递归 |
| **R5** `[$BaseSection=Name]` | `XNAWindowBase` 属性继承 | `ParseBaseSectionChildren` |
| **R6** `[ChildName]` 独立 section | DX 把没被任何 `$CC` 引用的 section 也当作 control | `AdoptOrphanControlSections`（黑名单过滤 meta） |
| **R7** 没有 `$CC` 引用的独立 section | 同 R6，但 section 名不带 `btn`/`lb` 等前缀 | 同上，类型走 `InferControlType` |
| **R8** Panel 内含 `$CC` children | DX 递归 `ReadINIForControl` | `AdoptOrphanControlSections` 内立即调 `ParseChildControlsFromSection` |

### 修改 R 规则时的注意事项

1. **任何"过滤规则"都要先看 DX 源码**。`IniUiTreeBuilder.ShouldSkipOrphanSection` 的每一行注释都标了对应的 DX 行为。删一条过滤可能让某个 mod 的 UI 起死回生，也可能让另一个 mod 多出一堆乱码控件——务必跑 `ThreeModCompatibilityTests`。

2. **不要按 overlay 限制收养**。曾经有过 `overlaySections.Count > 0 && !overlaySections.Contains(name)` 的限制，意图是"只让 overlay 显式声明的控件进树"。这违反 DX 的 `BasedOn` 累加合并语义，会让 QEC 的 `SkirmishLobby → MultiplayerGameLobby → GenericWindow` 三层链丢失 `btnLaunchGame`。详见 commit `ee5ea22` 的修复说明。

3. **类型推断顺序**（`InferControlType` + `TryInferKnownControlType`）：
   - 精确名匹配（`btnlaunchgame` → `GameLaunchButton`）
   - 前缀+后缀匹配（`lbchatmessages_host` → `ChatListBox`）
   - 通用前缀（`lbl*` → `XNALabel`）
   - 属性启发式（有 `Text` → `XNALabel`）
   - 兜底 `XNAPanel`

   新增类型映射时，优先在前三档加，**不要轻易动兜底**。

## IniDocument 的 BasedOn 合并

`BasedOn` 是 DX 的累加式继承：子文件未声明的 section 也会从父文件全部继承。实现见 `IniDocument.ApplyBasedOnChain`：

```
SkirmishLobby.ini (BasedOn=MultiplayerGameLobby.ini)
       │
       ▼ Load(MultiplayerGameLobby.ini)
MultiplayerGameLobby.ini (BasedOn=GenericWindow.ini)
       │
       ▼ Load(GenericWindow.ini)
GenericWindow.ini (无 BasedOn)
       │
       ▼ Consolidate(base=GenericWindow, overlay=MultiplayerGameLobby)
合并后的 MultiplayerGameLobby 文档
       │
       ▼ Consolidate(base=合并后的 MultiplayerGameLobby, overlay=SkirmishLobby)
最终文档（60 个 section 全部到位）
```

`Consolidate` 的语义：
- overlay 中**已有**的 section：用 overlay 的 key 覆盖 / 追加到 base
- overlay 中**没有**的 section：从 base 直接保留

测试见 `ClientAvalonia.Tests/IniUi/QecBasedOnChainTests.cs`。

## ResourceResolver：资产搜索路径

`ClientEnvironment.GetAssetSearchPaths()` 返回的搜索根顺序，对齐 DX 的 `ProgramConstants.GetResourcePath()`：

```
Resources/<Theme>/             ← 主题覆盖（最高优先级）
Resources/
Resources/Base/                ← XNA base resource dir
Resources/DTA/                 ← 历史 DTA 资源包（pre-2.12）
Resources/DTA/Default Theme/
<GameRoot>/                    ← 地图预览、side icon 等游戏根资产
```

**不要删除 `<GameRoot>/`**。地图预览（`Maps/Fan-made/xxx.png`）、side icon（`sovieticon.png`）等资产是相对**游戏根**而不是 `Resources/`。详见 `ee5ea22` 的修复说明（曾误删导致 MG/LNOD/QEC 三 mod 地图预览全部失效）。

## 添加新控件的步骤

1. 在 `ControlRegistry` 注册控件类型定义（指定 `IniTypeName` + `TemplateKey`）。
2. 在 `Themes/DxControlStyles.axaml` 添加 `DataTemplate`（`TemplateKey` 对应）。
3. 若控件有特殊属性解析：扩展 `PropertyResolver`。
4. 若控件类型靠 section 名推断：在 `TryInferKnownControlType` 加精确名或前缀规则。
5. 写测试：在 `IniUiTreeBuilderSemanticsTests` 加用例。
6. 跑 `ThreeModCompatibilityTests` 确认 MG/LNOD/QEC 不回归。

## 添加新窗口的步骤

1. modder 在 `Resources/<WindowName>.ini` 写 INI。
2. 在 `IniBehaviorCatalog.RegisterForWindow` 注册窗口的按钮回调（`<WindowName>Behaviors.cs`）。
3. 若窗口有特殊数据绑定（如 lobby）：在 `IniUi/Binding/` 添加 applier。
4. 写测试：在 `ClientAvalonia.Tests/IniUi/` 加 `<WindowName>LoadTests.cs`。

## 调试技巧

```bash
# Dump 完整 UiNodeTree（含 CanvasLeft/Top、texture 是否加载）
dotnet run --project ClientAvalonia -- --dump-tree Resources/SkirmishLobby.ini SkirmishLobby

# 校验 INI 加载无误
dotnet run --project ClientAvalonia -- --validate-ini Resources/MainMenu.ini

# 校验游戏资源（地图、mode、mission）能加载
dotnet run --project ClientAvalonia -- --validate-resources

# 校验设置绑定（OptionsWindow）
dotnet run --project ClientAvalonia -- --validate-bindings Resources/OptionsWindow.ini OptionsWindow
```

## 代码驱动区域边界声明（Issue #22）

以下 UI 区域**由 C# 代码生成而非纯 INI 定制**，这是有意取舍而非疏漏。INI 只能覆盖其中
标注的"可调项"：

| 区域 | 驱动代码 | 为什么代码驱动 | INI 可调项 |
|---|---|---|---|
| 玩家槽位行（ddPlayerSide/Color/Team/Start N） | `LobbyPlayerBindingApplier` | 行数随 `LobbyPlayerSlot.MaxSlots` 与地图容量动态变化，下拉项来自运行时目录（阵营/颜色/AI 名），INI 静态声明无法表达 | `PlayerOptionLocationX/Y`、`PlayerOptionVerticalMargin`、各列宽（`PlayerNameWidth` 等，见 `ReadLayout`） |
| Options 安全页控件 | `OptionsSecurityControlsBootstrap` | WAF/通知开关是客户端内部功能面，与 mod 数据无关 | 无（低频管理页） |
| 顶部栏与私信面板 | `ChannelLobbyLayout` + `CnCNetGameLobbyUiHelper` | CnCNet 协议 UI，控件集合随功能开关变化 | 面板几何常量 |
| Tactical 模板白名单 | `DxNodeTemplateSelector` | Tactical 皮肤按控件 ID 精确映射玻璃拟态模板，白名单是主题内部契约 | 换主题即整组切换 |
| Options 页脚按钮 | `OptionsWindowLayout` / `OptionsFooterChrome` | MG OptionsWindow.ini 未声明页脚；DX 在 C# 创建 | `FooterSaveLeft`、`FooterCancelLeft`、`FooterBottomOffset`、`FooterButtonWidth/Height`、`FooterZIndex`（Issue #5） |
| Campaign 阵营 tab 文案 | `CampaignSideTabCatalog`（Issue #22） | tab 集合派生自 `GameOptions.ini` 的 `Sides=`，与遭遇战下拉同源 | 改 `Sides=` 即生效 |

窗口外壳移除（`WindowTreePostProcessor`）支持显式声明：节点 `IsShell=true/false` 优先于
像素启发式；窗口 section 可用 `ShellMaxWidthLobby` / `ShellMaxWidthWindow` 覆盖启发式
阈值（Issue #18）。每次启发式移除都会记录到 `client.log`。

