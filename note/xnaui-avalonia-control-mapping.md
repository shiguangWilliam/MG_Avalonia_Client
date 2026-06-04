# Rampastring.XNAUI 控件盘点与 Avalonia 对照

日期：2026-05-09

## 1. 盘点范围与结论

本笔记只统计 `Rampastring.XNAUI/XNAControls` 中公开暴露的控件与控件相关类型，因为这部分才是客户端 UI 的基础能力来源。

结论先给出：

- 如果按 `public` 的视觉控件类统计，Rampastring.XNAUI 当前共有 **23 种控件类**。
- 如果加上用于承载子项/菜单项的数据模型，共有 **3 种公开项模型**。
- 如果加上公开事件参数和 INI 解析接口，还有 **4 种公开支撑类型**。
- 从 Avalonia 迁移角度看，这 23 种控件没有“无法替代”的类型；它们都可以落到：
  - Avalonia 原生控件
  - Avalonia 原生控件 + 自定义模板/样式
  - Avalonia 原生控件组合
  - 非视觉服务对象

本次盘点的公开视觉控件类如下：

1. XNAControl
2. XNAPanel
3. XNAButton
4. XNACheckBox
5. XNALabel
6. XNALinkLabel
7. XNATextBox
8. XNAPasswordBox
9. XNASuggestionTextBox
10. XNATextBlock
11. XNATextRenderer
12. XNAProgressBar
13. XNAScrollBar
14. XNAHorizontalScrollBar
15. XNAScrollPanel
16. XNADropDown
17. XNAListBox
18. XNAMultiColumnListBox
19. XNATabControl
20. XNATrackbar
21. XNAIndicator<T>
22. XNAContextMenu
23. XNATimerControl

公开的非视觉项模型如下：

1. XNADropDownItem
2. XNAListBoxItem
3. XNAContextMenuItem

公开的支撑类型如下：

1. ControlEventArgs
2. MouseEventArgs
3. ContextMenuItemSelectedEventArgs
4. IControlINIAttributeParser

## 2. 为什么这份映射足以覆盖整个客户端

虽然业务项目里还有很多 `XNAClientButton`、`XNAClientDropDown`、`GameListBox`、`TunnelListBox`、`XNAWindow`、`XNAPlayerSlotIndicator` 之类的类，但它们本质上都只是对基础控件的扩展或组合。

例如：

- `ClientGUI/XNAClientButton` 继承 `XNAButton`
- `ClientGUI/XNAClientDropDown` 继承 `XNADropDown`
- `ClientGUI/XNAChatTextBox` 继承 `XNASuggestionTextBox`
- `ClientGUI/XNAPlayerSlotIndicator` 继承 `XNAIndicator<T>`
- `DXMainClient/DXGUI/Multiplayer/GameListBox` 继承 `XNAListBox`
- `DXMainClient/DXGUI/Multiplayer/CnCNet/TunnelListBox` 继承 `XNAMultiColumnListBox`
- `ClientGUI/XNAWindowBase` 和 `ClientGUI/XNAWindow` 则是建立在 `XNAPanel` 之上的窗口语义封装

因此，只要基础框架控件能在 Avalonia 里找到稳定映射，业务层派生控件也就都能跟着落位。

## 3. 迁移时的总原则

在 XNAUI 里，控件不是传统桌面 UI 控件，而是基于 `DrawableGameComponent` 的游戏式控件树。迁移到 Avalonia 时，不要只看“名字像不像”，还要处理四类根本差异：

### 3.1 布局模型

- XNAUI 以绝对像素坐标为主，适合先映射到 Avalonia 的 `Canvas`
- `DistanceFromRightBorder`、`DistanceFromBottomBorder`、`FillWidth`、`FillHeight` 这类语义，建议在 INI 解析阶段预计算成 Avalonia 可直接消费的定位结果
- 第一阶段不必强求直接改成 `Grid` 或 `StackPanel`，优先做兼容层

### 3.2 输入模型

- `XNAControl` 内建了 preview/tunnel 与 bubble 两阶段鼠标事件
- Avalonia 有 routed events，可以承接这套语义
- 焦点、选中态、拖拽、滚轮输入都应收敛到 Avalonia 的输入系统，而不是继续模拟 XNA 的逐帧轮询

### 3.3 渲染模型

- XNAUI 大量使用纹理、alpha 过渡、自绘边框、独立 render target
- Avalonia 对应方案通常是 `ControlTemplate`、`Styles`、`Transitions`、`ClipToBounds`
- 只有少量需要逐部分排版的复杂文本，才需要保留自绘逻辑

### 3.4 逻辑与视觉分离

- `XNATimerControl` 这类类型本质不是视觉控件，在 Avalonia 中应直接降级为逻辑服务
- `XNAIndicator<T>`、`XNATextRenderer` 这类“半视觉、半逻辑”的类型，更适合做成 `UserControl` 或自定义 presenter

## 4. 控件对照表

### 4.1 基础骨架与容器

| XNAUI 控件 | 角色 | XNAUI 中的核心逻辑 | Avalonia 对应 | 迁移建议 |
|---|---|---|---|---|
| `XNAControl` | 全部控件基类 | 基于 `DrawableGameComponent`；管理子控件、鼠标预览/冒泡、焦点、选中、绝对坐标、Draw/Update 生命周期 | `Control` 或 `TemplatedControl` + 附加行为 | 这不是一比一替换件，而是新 UI 框架基类；需要把 XNA 生命周期拆成 Avalonia 的布局、事件、渲染、样式体系 |
| `XNAPanel` | 基础容器 | 背景纹理、边框、平铺/拉伸/居中绘制、Padding、透明度变化 | `Border` + `Panel`，通常是 `Canvas`/`Grid`/`Panel` 外包一层 `Border` | 若目标是兼容原 INI，第一阶段建议默认映射到 `Border + Canvas` |
| `XNAScrollPanel` | 可滚动容器 | 内含内容面板、水平/垂直滚动条、视口、内容偏移、键盘滚动、overscroll | `ScrollViewer` | 行为最接近 `ScrollViewer`；若要保留原始滚动条纹理与 corner panel，再额外重写模板 |
| `XNATabControl` | 标签容器 | 维护 tab 列表、tab 纹理、可选/不可选状态、选中索引 | `TabControl` | 语义直接对应；差异主要在 tab 头部纹理和禁用样式 |
| `XNAContextMenu` | 弹出式菜单 | 动态计算菜单项可见性/可选性、hint text、逐项高度、打开位置 | `ContextMenu` | 功能上可直接映射；如果要保留 hint text、变高菜单项、图标纹理，需自定义 `MenuItem` 模板 |
| `XNATimerControl` | 非视觉定时器 | 依赖 `Update` 驱动计时，支持 `Start/Pause/Resume/AutoReset` | `DispatcherTimer` 或 ViewModel 计时服务 | 不应再作为视觉控件迁移；直接改成逻辑服务最合适 |

### 4.2 按钮、勾选与状态显示

| XNAUI 控件 | 角色 | XNAUI 中的核心逻辑 | Avalonia 对应 | 迁移建议 |
|---|---|---|---|---|
| `XNAButton` | 基础按钮 | Idle/Hover 纹理、alpha 动画、文本居中、快捷键、悬停/点击音效 | `Button` | 原生按钮足够承接交互；视觉层用模板恢复双纹理和 hover/pressed 效果 |
| `XNACheckBox` | 勾选框 | Checked/Unchecked/Disabled 纹理、文字布局、勾选音效、渐变 alpha | `CheckBox` | 语义直接对应；纹理式外观用模板和伪类状态实现 |
| `XNAProgressBar` | 进度条 | 边框、已填充/未填充颜色、平滑前进/回退过渡 | `ProgressBar` | 可直接映射；平滑过渡可用动画或绑定层做缓动 |
| `XNATrackbar` | 滑块 | 最小/最大值、鼠标拖动、按钮纹理、点击音效 | `Slider` | 语义直接对应；按钮贴图和轨道外观用模板恢复 |
| `XNAIndicator<T>` | 状态指示器 | 枚举状态切图、图标与文本并排、hover 高亮、切图淡入 | `UserControl`、`ContentControl` 或 `TemplatedControl` | 这是“状态图标 + 文本”的组合控件，最适合做成带状态模板的自定义控件 |

### 4.3 文本展示与输入

| XNAUI 控件 | 角色 | XNAUI 中的核心逻辑 | Avalonia 对应 | 迁移建议 |
|---|---|---|---|---|
| `XNALabel` | 静态文本 | 文本颜色、字体索引、锚点、文本锚定、阴影 | `TextBlock` | 直接映射；AnchorPoint/TextAnchor 可在描述层换算为 Canvas 定位或对齐属性 |
| `XNALinkLabel` | 链接文本 | 基于 `XNALabel`，支持悬停变色和下划线 | `TextBlock` + 点击行为，或 `Hyperlink` 风格控件 | Avalonia 没必要保留独立渲染逻辑，重点是悬停样式和命令行为 |
| `XNATextBox` | 单行文本输入 | 光标、选择区、复制粘贴、最大长度、激活边框色、Enter 事件、拖拽选区 | `TextBox` | 这是最标准的一类映射，主要工作在视觉样式和少数快捷键行为对齐 |
| `XNAPasswordBox` | 掩码输入 | 继承 `XNATextBox`，区别只在于显示字符被掩码替换 | 掩码输入框，通常是 `TextBox` 包装或专用密码输入控件 | 迁移时没必要保留单独绘制逻辑，保留“真实值”和“显示值”分离即可 |
| `XNASuggestionTextBox` | 提示文本输入 | 继承 `XNATextBox`，提供 suggestion/watermark 语义 | `TextBox` + `Watermark` | 语义直接对应 |
| `XNATextBlock` | 多行文本块 | 基于 `XNAPanel`，负责换行文本和文本边距 | `TextBlock` 放进 `Border`/`Panel` | 如果不需要容器边框，可直接退化成 `TextBlock`；需要背景时再套 `Border` |
| `XNATextRenderer` | 富文本/分段文本渲染器 | 由 `XNATextPart` 组成，支持分段字体、颜色、缩放、下划线、自动换行 | `TextBlock` + `Inline`/`Run`，或自定义富文本 presenter | 这是 XNAUI 里最像“文本排版器”的控件；如果业务大量依赖分段样式，建议单独做自定义适配层 |

### 4.4 列表、下拉与滚动条

| XNAUI 控件 | 角色 | XNAUI 中的核心逻辑 | Avalonia 对应 | 迁移建议 |
|---|---|---|---|---|
| `XNAScrollBar` | 垂直滚动条 | 内建上下按钮、步长、显示像素范围、滚轮联动 | `ScrollBar` | 原生 `ScrollBar` 即可；视觉和按钮纹理可模板化 |
| `XNAHorizontalScrollBar` | 水平滚动条 | 与 `XNAScrollBar` 同构，只是方向改为水平 | `ScrollBar` | 没必要保留为独立 Avalonia 类型，直接用同一个 `ScrollBar` 模板切方向 |
| `XNADropDown` | 下拉选择 | 选中项、展开方向、重新选择事件、顶部索引、动态展开宽度 | `ComboBox` | 核心语义就是 `ComboBox`；如果要保留“展开时宽度可大于闭合宽度”，需要自定义弹出层宽度策略 |
| `XNAListBox` | 单列列表 | 选中/悬停索引、滚动条、可多行项、键盘滚动、项目纹理缓存、独立 render target | `ListBox` | 功能上是标准 `ListBox`；若保留多行项高度和贴图渲染，可用自定义 `ItemTemplate` |
| `XNAMultiColumnListBox` | 多列列表 | 多个 `XNAListBox` 联动，列头、跨列选中同步、列宽调整 | `DataGrid` 或 `Grid + ItemsRepeater/ListBox` | 若需要表格体验，优先 `DataGrid`；若需要完全仿旧 UI，可自己组合多列 ItemsControl |

## 5. 非视觉项模型的 Avalonia 对应

这三类不是视觉控件，不需要在 Avalonia 侧一比一创建“新控件类”，保留为 ViewModel 或数据项即可。

| XNAUI 类型 | 当前职责 | Avalonia 对应 |
|---|---|---|
| `XNADropDownItem` | 下拉选项文本、颜色、纹理、Tag、可选性 | `ComboBoxItem` 对应的数据对象，或 ViewModel |
| `XNAListBoxItem` | 列表项文本、颜色、背景、纹理、是否 header、可见性 | `ListBox`/`DataGrid` 的项 ViewModel |
| `XNAContextMenuItem` | 菜单项文本、hint、回调、显隐和可选规则 | `MenuItem` 的数据对象，或命令模型 |

## 6. 哪些控件是“直接替代”，哪些需要“组合替代”

### 6.1 可以直接落到 Avalonia 原生控件的

- XNAButton -> Button
- XNACheckBox -> CheckBox
- XNALabel -> TextBlock
- XNATextBox -> TextBox
- XNASuggestionTextBox -> TextBox + Watermark
- XNAProgressBar -> ProgressBar
- XNAScrollBar / XNAHorizontalScrollBar -> ScrollBar
- XNADropDown -> ComboBox
- XNAListBox -> ListBox
- XNATabControl -> TabControl
- XNATrackbar -> Slider

### 6.2 需要 Avalonia 原生控件组合的

- XNAPanel -> Border + Canvas/Panel
- XNAScrollPanel -> ScrollViewer + 自定义样式滚动条
- XNAMultiColumnListBox -> DataGrid，或多列 ItemsControl 组合
- XNALinkLabel -> TextBlock + Pointer 交互 + Command
- XNATextBlock -> Border + TextBlock
- XNAContextMenu -> ContextMenu + MenuItem 自定义模板
- XNAIndicator<T> -> Image + TextBlock 组合控件

### 6.3 需要自定义 presenter 或服务化处理的

- XNAControl -> 新的 Avalonia 控件基类/适配层
- XNATextRenderer -> 富文本 presenter
- XNATimerControl -> DispatcherTimer / ViewModel service
- XNAPasswordBox -> 掩码输入适配层

## 7. 迁移难点排名

如果按实现难度排序，真正需要重点设计的不是普通按钮和输入框，而是下面这些：

1. `XNAControl`
原因：它承载了 XNAUI 的事件路由、焦点、子树、生命周期、绘制顺序等基础约束。Avalonia 迁移成败，首先取决于这里的适配策略。

2. `XNAScrollPanel`
原因：它不是简单滚动条，而是带内容面板、双轴滚动、视口、裁剪、键盘滚动、overscroll 的复合容器。

3. `XNAMultiColumnListBox`
原因：它本质上是“多个列表的联动控件”，如果简单映射成单个 `ListBox` 会丢掉列结构。多数情况下应直接落到 `DataGrid`。

4. `XNATextRenderer`
原因：它不是单纯文本控件，而是带分段样式、手工换行、下划线和尺寸预计算的排版器。

5. `XNAIndicator<T>`
原因：它混合了状态机、图像池、文本布局与 hover 视觉反馈，适合做成自定义组合控件，而不是套现成单控件。

## 8. 最终判断

这次盘点的核心结论是：**Rampastring.XNAUI 没有任何一种控件会阻止迁移到 Avalonia。**

真正的工作量不在“有没有对应控件”，而在以下三件事：

- 是否先用 `Canvas` 完成绝对定位兼容层
- 是否把 `XNAControl` 的输入/焦点/子树语义抽成 Avalonia 基础适配层
- 是否把少量复合控件（`XNAScrollPanel`、`XNAMultiColumnListBox`、`XNATextRenderer`、`XNAIndicator<T>`）单独视作“迁移专项”处理

如果这三件事做对，那么 XNAUI 到 Avalonia 的控件映射是完整且可执行的。