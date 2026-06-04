# Avalonia UI 替换可行性与迁移笔记（讨论记录）

日期：2026-05-07

## 结论（当前判断）
- **可行**：项目已经有较清晰的“核心层 ClientCore + UI 层 ClientGUI + 更新器 ClientUpdater”的分层，迁移 UI 到 Avalonia 的总体方向成立。
- **但不是零成本替换**：`ClientGUI` 目前不仅是“视图”，还包含：
  - XNA/MonoGame 风格的渲染与输入模型
  - 通过 INI 驱动的控件树创建、主题、布局（含表达式解析）
  - 一些 UI 侧业务流程（比如启动游戏进程、显示 MessageBox 等）
  因此迁移会涉及 UI 架构与配置系统适配，而非仅替换控件。

## 目标方案（你已明确的方向：保留 Core + 保留 INI modding + 替换为 Avalonia）
- **Core 保持不变**：继续复用 `ClientCore` 的 INI 读取/预处理、I18N、强类型用户设置等。
- **GUI 用 Avalonia 重写**：以桌面应用的窗口/页面体系取代 XNAUI 的“帧循环控件树”。
- **保留 INI 驱动 UI 的 modding 能力**：不把 UI 完全写死在 XAML 里，而是引入一个“INI → UI 描述模型 → Avalonia 绑定/模板”的中间层。
- **关键点**：建议避免“生成 XAML 字符串再加载”的方案；更稳妥的是将 INI 解析为一棵 UI 节点树（ViewModel/Descriptor），用 Avalonia 的 `DataTemplate`/`Styles`/自定义控件去渲染。

## 当前代码结构要点（来自阅读）
- `ClientCore/`：配置（INI）、翻译（I18N）、用户设置（UserINISettings + 强类型 setting）、INI 预处理、统计、通用工具。
- `ClientGUI/`：
  - `XNAWindow`/`XNAWindowBase`：INI 驱动窗口与 ExtraControls。
  - `TranslationINIParser`：对 INI 属性做本地化替换（Text/Size/Location/ToolTip/URL…）。
  - `Parser`：表达式解析器，用于 INI 布局计算。
  - `Settings/*`：将 UI 控件与 `UserINISettings` 绑定；含文件型设置应用/回滚策略。
  - `GameProcessLogic`：启动/监控游戏进程（等待 INI 预处理、可选 qres、亲和性等）。
- `ClientUpdater/`：更新流程、镜像、压缩、CustomComponent 下载校验等。

## Avalonia 替换的主要挑战
### 1) 渲染/输入模型差异
- XNAUI 是游戏式 UI（帧循环、Draw/Update、坐标像素定位、资源贴图）。
- Avalonia 是桌面应用 UI（布局系统、数据绑定、事件模型、样式）。
- 迁移意味着：
  - 不再依赖 `WindowManager`、`XNAControl` 控件树
  - ToolTip、动画、层级遮罩（DarkeningPanel）需用 Avalonia 的机制实现

### 2) INI 驱动 UI 的适配策略需要选型
现有 UI 很大一部分“可配置性”来自 INI：窗口/控件属性由 INI 定义，再由 Parser 计算布局。
迁移到 Avalonia 有三种思路：
1. **彻底拥抱 Avalonia**：用 XAML + MVVM，INI 仅保留为业务配置/用户设置；UI 主题改为 Avalonia Styles。
   - 优点：现代化、可维护、生态成熟
   - 缺点：失去/重写 INI 驱动 UI 的 modding 能力
2. **保留 INI 作为 UI 描述**：写一个 INI -> Avalonia 控件树的适配层（相当于重新实现 `ClientGUICreator + XNAWindow` 的创建逻辑）。
   - 优点：最大化兼容现有 mod 配置
   - 缺点：工程量大，等于再造一套 UI DSL/布局系统
3. **折中**：核心窗口用 XAML；少量 mod 扩展区域保留“INI 驱动的动态面板”（类似 WebView/自定义控件容器）。
   - 优点：兼顾现代化与一定可配置性
   - 缺点：需要定义清晰的扩展边界

### 2.1) 推荐落地方向（与当前计划对齐：INI → 描述模型 → Avalonia 渲染）
这里把“保留 INI modding”这条路写成更可执行的工程分解。

#### 2.1.1) 中间层总体结构
- **输入**：来自 Core 的 INI（可含预处理/继承/合并）+ 翻译/I18N + 运行时上下文（分辨率、DPI、是否管理员、版本号、是否联机等）。
- **输出**：一棵“UI 节点树”（可以理解为 ViewModel/Descriptor Tree），节点包含：
  - `Type`：控件类型（Window/Panel/Button/Label/TextBox/CheckBox/DropDown…）
  - `Id/Name`：控件标识
  - `Props`：强类型或字典形式的属性（Text、IsEnabled、IsVisible、Width、Height、Margin、ToolTip、Command、ItemsSource…）
  - `Bindings`：哪些属性来自绑定（User settings / runtime state / computed expression）
  - `Children`：子节点
- **渲染**：Avalonia 侧用 `ItemsControl + DataTemplate`（按节点 Type 选模板）或自定义控件容器渲染节点树。

#### 2.1.2) 建议分出的几个职责模块（概念拆分）
- **IniUiLoader**：读取某个 UI INI（窗口/页面）并生成节点树的入口。
- **IniUiSchema/ControlRegistry**：控件类型注册表（INI 里的 `Type=Button` 对应哪个节点类、支持哪些属性、默认值、兼容别名）。
- **PropertyResolver**：把 INI 字符串解析成强类型属性（数值、厚度/边距、对齐、颜色、图片资源引用等）。
- **BindingResolver**：把 INI 里写的“绑定表达式”解析为可绑定对象（对接 `UserINISettings`、运行时状态、命令）。
- **ExpressionEvaluator**：复用/迁移现有 `Parser` 的表达式能力（布局计算、条件显示等）。
- **LocalizationResolver**：复用翻译 key 的替换逻辑（可以借鉴 `TranslationINIParser` 的规则，但落地在节点 Props 上，而不是直接写控件）。
- **ResourceResolver**：图片/字体/主题资源定位（延续现有资源目录结构，避免破坏 mod 资源布局）。

#### 2.1.3) “INI 到 XAML”的更稳妥解释
从工程实现角度，不建议真的“生成 XAML 文件/字符串”。更建议：
- **INI → 节点树（ViewModel/Descriptor）**
- **XAML 只负责模板**（`DataTemplate` 根据节点类型渲染，并绑定节点的 Props）

这样能保证：
- mod 作者仍然写 INI（保持可扩展）
- 你们仍然用 XAML/Avalonia 的强项（样式、模板、绑定、布局、DPI）
- 避免运行时 XAML 解析带来的调试/安全/兼容复杂度

#### 2.1.4) 与 Core 的边界（保证 Core 不变）
- Core 继续提供：
  - INI 读取/继承/预处理（CCIniFile/IniPreprocessor 一类能力）
  - 翻译/I18N 数据源
  - 用户设置读写（UserINISettings + 强类型 settings）
- 新 UI 层新增的中间层只“消费”这些能力，并把结果变成 Avalonia 可绑定的对象。

#### 2.1.5) GameProcessLogic 的处理建议（与 UI 解耦）
- `GameProcessLogic` 本质是“启动/监控游戏进程”的应用服务，不应绑定某种 UI 框架。
- 迁移时建议把它作为服务接口暴露给 Avalonia UI（例如 UI 发起 Start，服务抛事件/进度；UI 订阅并显示）。

### 3) 资源与主题迁移
- 现有大量控件依赖贴图（按钮背景、窗体背景等）和固定像素尺寸。
- Avalonia 可以继续使用图片资源，但更推荐用矢量/样式系统。
- 翻译系统与主题系统耦合点：`TranslationINIParser` 目前会影响 Size/Location 等布局属性。

### 4) 业务逻辑与 UI 耦合点
- `GameProcessLogic`、消息框、热键配置窗口等属于“UI 驱动的业务流程”。
- 迁移建议把这类逻辑抽到更中性的层（例如新增 `ClientApp`/`ClientServices`），UI 只负责调用和展示。

## Roadmap（按你已确认约束完善：主界面/进入游戏界面 INI 兼容优先；Windows 优先但保留 Unix）

### 已确认的约束（记录）
- **兼容优先级**：先保证“主界面”和“进入游戏界面”的 INI 配置可用（可加载、可布局、可交互、可启动游戏）。
- **平台策略**：项目仍保留 Unix 平台能力（跨平台目标不丢），但 **优先完成并验收 Windows**。
- **OS 行为映射**：利用 Core 的 OS/version 枚举能力，建立 **OS → 字体/IME/窗口行为** 的映射表；先让 Windows 行为正确，再扩到 Unix。
- **INI 可配置能力**：尽可能保留原有 INI 可配置功能，但 **中间层 Schema 暂不对外开放**（仅官方 UI 使用，先不承诺长期兼容）。

### 里程碑定义（建议用 M0–M6 管控）
为了避免“做着做着变成重写一切”，每个里程碑都给出交付物与验收口径。

**构建验收约定**：各里程碑的**最终可行性验证**须使用 `./Scripts/build-clientavalonia.ps1`（输出 `CompiledAvalonia/`），在仓库标准构建链路下与 `ClientCore` 等底层模块联编验收；日常开发可用 `dotnet build ClientAvalonia` 快速迭代，但不替代里程碑签收。详见 `note/clientavalonia-dx-components.md` §构建。

#### M0：基线盘点与验收口径（最关键的前置）
交付物：
- 主界面/进入游戏界面的 **INI 清单**（涉及哪些 ini 文件、哪些 section、哪些控件类型/属性/表达式/绑定）。
- “兼容级别”定义（建议至少到 L2）：
  - L0：能加载并渲染（不崩溃）
  - L1：布局/可见性/禁用状态符合预期
  - L2：交互与业务闭环可用（设置读写、能进入游戏/启动流程正常）
  - L3：视觉像素级一致（可选，不建议一开始承诺）
- 回归用例：准备一组 **golden INI**（来自现有发布包/典型 mod 配置），后续每次迭代用它们做“能否打开/能否启动”的回归。

验收：
- 至少能列出主界面/进入游戏界面所需的控件与属性覆盖面，明确“第一期必须实现”的最小集合。

#### M1：架构落地（新 UI 并存 + 中间层骨架）
交付物：
- 新增 Avalonia 前端项目（与旧 `ClientGUI` 并存），启动后能打开一个空窗口并接入 Core 配置/翻译最小能力。
- UI 服务边界（接口）定稿：配置、更新器状态、游戏启动、对话框/通知。
- INI→节点树中间层最小骨架：`ControlRegistry` + `PropertyResolver` + `LocalizationResolver` + `ResourceResolver`（先 stub 也行）。

验收：
- 新 UI 可独立启动；能读取一个简单 INI 并生成节点树；Avalonia 侧能用 DataTemplate 渲染出最小控件（Label/Button）。

#### M2：主界面 INI 兼容（Windows 优先）
交付物：
- 覆盖主界面所需的控件类型与属性（按 M0 的清单实现），把“绝对坐标”优先落在 `Canvas` 兼容层（保证旧布局最先跑起来）。
- 复用/迁移表达式能力：让 `Expr:` 能驱动坐标/尺寸/显示隐藏等关键属性（必要时先只实现用到的表达式子集）。
- 翻译替换在节点 Props 上生效（对齐现有 key 体系）。

验收：
- Windows 上主界面能打开；主要按钮可点击；关键文本/翻译正确；布局不乱（允许细节不一致，但不能影响使用）。

#### M3：进入游戏界面 + 启动流程闭环（Windows）
交付物：
- 进入游戏界面的 INI 解析与渲染（控件覆盖按 M0 清单）。
- 对接 `GameProcessLogic`（或其抽象服务）：
  - 启动前检查/准备（INI 预处理、可选 qres 等）
  - 启动中进度/日志/错误可在 UI 展示
  - 启动后状态更新（按钮禁用、返回、错误提示）

验收：
- Windows 上从主界面能走到进入游戏界面并成功启动游戏；失败时错误信息可见且不崩溃。

#### M4：设置/绑定与 INI 可配置能力补齐（仍以主界面相关为边界）
交付物：
- `Setting:` 绑定闭环：控件值 ↔ `UserINISettings`（含保存/回滚策略）。
- `State:` 绑定闭环：更新状态、版本号、可启动状态等动态信息可驱动 UI。
- 资源引用与主题：优先保证“现有贴图资源可用”，样式系统先不追求重构。

验收：
- 关键设置项可读写且重启后生效；更新/状态变化能正确反映到 UI。

#### M5：Windows 体验与稳定性（字体/IME/窗口行为）
交付物：
- OS 映射表（Windows）：字体 fallback、文本渲染一致性、IME/焦点/快捷键行为。
- 关键窗口行为：置顶/模态对话框/消息框/遮罩（DarkeningPanel 对应能力）在 Avalonia 里可用。

验收：
- Windows 上中文输入、焦点切换、复制粘贴、常用对话框无明显问题；主流程稳定可用。

#### M6：Unix 平台保留与验收（Linux 优先，macOS 视情况）
交付物：
- Linux 运行验证与差异修复：字体选择、路径大小写、窗口管理器行为、输入法差异（可先列已知限制）。
- OS 映射表（Unix）：字体/IME/窗口行为的默认策略。

验收：
- Linux 上能启动新 UI、打开主界面、进入游戏界面并完成启动闭环（允许 UI 细节差异，但功能必须可用）。

### 备注：为什么“先 Canvas 兼容层”更稳
- 现有 INI 大量使用 `X/Y/Width/Height`，短期内用 `Canvas` 对齐语义最快。
- 后续若要更现代化布局（Grid/StackPanel），可以在 Schema 中新增更高层容器并逐步迁移官方 UI，而不破坏旧 INI。

## 风险清单
- INI 驱动 UI 的兼容性（决定了 50% 工程量）。
- 资源贴图与布局从像素绝对定位迁到布局系统后的视觉一致性。
- XNA 特有行为（帧循环、绘制顺序、DrawOrder）与 Avalonia 的差异。

## 需要额外关注的工程风险（基于你选了“保留 INI modding”路线）
- **绑定与表达式的语义稳定性**：一旦对外暴露了 INI UI Schema，后续改动会影响 mod 兼容。
- **布局兼容策略**：现有 INI 的 `X/Y/Width/Height` 更像绝对定位；Avalonia 更擅长布局容器。需要明确“兼容层”规则（例如优先 Canvas 兼容旧布局，逐步引导到 StackPanel/Grid）。
- **资源引用协议**：图片/字体路径、主题键的解析要稳定，否则 mod 资源会碎。
- **动态加载与安全**：如果允许 mod 引入外部程序集/脚本，风险更高；建议尽量限制为“数据驱动 UI + 白名单控件”。

## 已收敛的决策（当前）
- 第一阶段以 **主界面 + 进入游戏界面** 的 INI 兼容为目标。
- 平台 **Windows 优先验收**，同时保证架构不锁死，后续完成 **Linux** 功能验收。
- INI 中间层（Schema/节点树）**暂不对外开放**，先只服务官方 UI，待稳定后再决定是否公开与兼容承诺。

## 后续可选决策（不阻塞 M0–M3）
- 是否追求 L3（像素级一致）以及对应的美术/主题重构投入。
- 是否要为 mod 作者提供“受控扩展点”（白名单控件 + 受限属性集）。
