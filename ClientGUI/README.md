# ClientGUI（UI 库）

## 这是什么
`ClientGUI` 是客户端的 UI 层库，基于 `Rampastring.XNAUI`（XNA/MonoGame 风格 UI 框架）实现：
- INI 驱动的窗口/控件创建与主题（skin）
- 常用控件封装（按钮、下拉框、Tab、聊天输入、消息框等）
- 工具提示、深色遮罩、窗口基类
- 选项面板与用户设置控件（对接 `ClientCore.UserINISettings`）
- IME 输入法适配（按构建目标切换实现）
- 启动/退出游戏进程的 UI 侧逻辑（调用系统进程）

项目引用：`ClientCore`、`Rampastring.XNAUI`（并在非 GL 配置下引用 `ImeSharp`）。

## 目录结构概览
- `Settings/`：设置控件与文件型设置的应用/回滚逻辑
- `IME/`：输入法处理抽象与不同平台实现
- 顶层 `.cs`：窗口基类、控件封装、INI/翻译解析器、工具提示、启动游戏逻辑等

## 核心设计点
### 1) INI 驱动的窗口/控件
- `XNAWindowBase.cs`
  - 作为窗口/面板基类，提供“从 INI 读取额外控件定义”的能力（`ParseExtraControls`）
  - 遍历子控件调用 `GetAttributes(ini)`，让控件自行从 INI 获取属性

- `XNAWindow.cs`
  - 典型子窗口实现：初始化时自动选择 INI 文件来源
    - 优先 `Resources/{Name}.ini`，否则回退到 `Base Resources/{Name}.ini`
    - 再回退到 `GenericWindow.ini`（`Resources/` 或 Base）
  - 使用 `CCIniFile`，意味着支持 INI 继承/合并
  - 读取完窗口属性后，解析 `ExtraControls` 并读取子控件属性

- `ClientGUICreator.cs`
  - 将 `XNAControl` 子类注册到 DI 容器，并允许通过“控件类型名”动态构造
  - 主要用于 INI UI 系统按类型名创建控件（以及支持 Singleton/Transient 两种生命周期）

### 2) 翻译/本地化与 INI 属性解析
- `TranslationINIParser.cs`
  - 实现 `IControlINIAttributeParser`，在解析控件属性时对 `Text/Size/Width/Height/Location/.../ToolTip/URL` 等做本地化替换
  - 典型效果：同一个窗口 INI 在不同语言下得到不同文本与布局尺寸

### 3) 表达式解析（用于 INI 布局计算）
- `Parser.cs`
  - 解析简单算术表达式（`+ - * /`、括号）
  - 提供全局常量：`RESOLUTION_WIDTH/RESOLUTION_HEIGHT`，并支持从 `ClientConfiguration` 读取额外常量
  - 支持基于控件树查找控件，从而在 INI 中使用“函数/标识符”计算位置或尺寸

## 关键文件/模块说明
### 游戏进程启动逻辑
- `GameProcessLogic.cs`
  - 负责启动主游戏进程（根据 OS/配置选择可执行文件与参数）
  - 等待 `INIProcessing.PreprocessorBackgroundTask` 完成（避免预处理尚未结束就启动游戏）
  - 可选使用 `qres.dat`（配合窗口模式/位深设置）以及单核亲和性
  - 提供事件：`GameProcessStarting/Started/Exited`

### 工具提示与交互
- `ToolTip.cs`
  - 绑定到任意控件，基于 `ClientConfiguration` 的延迟/偏移/透明度参数显示
  - 支持 `FollowCursor`、`Blocked` 等行为控制

- `IToolTipContainer.cs`
  - 为控件提供 ToolTip 文本承载接口（配合 `TranslationINIParser` 从 INI 读 `ToolTip=`）

### 选项面板与设置系统
- `XNAOptionsPanel.cs`
  - 所有选项面板基类：
    - 从 INI 解析“额外控件 + 子控件属性”
    - 自动收集实现 `IUserSetting` 的子控件，用于 `Load/Save/RefreshPanel`

- `Settings/*`
  - `SettingCheckBox*` / `SettingDropDown*`：将 UI 控件与 `UserINISettings` 的某个 section/key 绑定
  - `FileSourceDestinationInfo.cs`：处理“从源文件到目标文件”的应用/回滚策略（覆盖、仅缺失复制、保留用户修改、硬链接只读等）

### 常用 UI 控件封装
顶层大量 `XNAClient*`、`XNA*` 文件是对 XNAUI 控件的“客户端风格封装/扩展”，一般包括：
- 统一的视觉样式、字体、颜色与交互行为
- 更适配 INI 系统的属性读取/默认值

示例（不穷举）：
- `XNAMessageBox.cs`：模态提示框
- `XNAClientButton.cs`：客户端按钮
- `XNAClientDropDown.cs`：下拉框
- `XNAClientTabControl.cs`：Tab 容器
- `XNAChatTextBox.cs`：聊天输入框

### IME（输入法）适配
- `IME/IMEHandler.cs`：IME 抽象/统一接口
- `IME/SdlIMEHandler.cs` 与 `IME/WinFormsIMEHandler.cs`：按构建配置二选一（见 `ClientGUI.csproj`）
- `IME/DummyIMEHandler.cs`：无 IME 环境下的兜底实现

## 与其它项目的关系
- 读取配置/翻译/用户设置：依赖 `ClientCore` 的 `ClientConfiguration`、`UserINISettings`、`I18N`
- 渲染与控件基础：依赖 `Rampastring.XNAUI`

## 文件速查（按路径）
### 顶层（窗口/逻辑/通用）
- `ClientGUICreator.cs`：控件类型注册 + DI 工厂；INI 系统按类型名创建控件。
- `DarkeningPanel.cs`：全屏半透明遮罩面板；子控件显示/隐藏时自动淡入淡出。
- `GameProcessLogic.cs`：启动/监控游戏进程（含等待 INI 预处理、可选 qres、亲和性）。
- `HotkeyConfigurationWindow.cs`：热键配置窗口（读取 `KeyboardCommands.ini`，保存/重置绑定）。
- `ICompositeControl.cs`：复合控件接口（用于组合控件的统一处理）。
- `INIConfigException.cs`：INI 解析/配置相关异常类型。
- `INItializableWindow.cs`：可从 INI 初始化的窗口类型（与 `XNAWindow` 并行的窗口基类）。
- `IToolTipContainer.cs`：ToolTip 文本承载接口。
- `Parser.cs`：INI 表达式解析器（布局计算/常量/控件引用）。
- `ScreenResolution.cs`：分辨率模型与推荐/安全分辨率计算。
- `ToolTip.cs`：工具提示控件（延迟显示、跟随鼠标、透明度动画）。
- `TranslationGUIExtensions.cs`：把控件属性映射到翻译 key 的扩展（`INI:Controls:...`）。
- `TranslationINIParser.cs`：INI 属性解析器（对 Text/Size/Location/ToolTip/URL 等做本地化）。
- `UIDesignConstants.cs`：UI 布局常量（边距、按钮尺寸等）。
- `XNAOptionsPanel.cs`：选项面板基类（收集 `IUserSetting`，支持 Load/Save/Refresh）。
- `XNAWindowBase.cs`：窗口/面板基类（ExtraControls、子控件属性读取）。
- `XNAWindow.cs`：INI 驱动子窗口（按 `{Name}.ini`/`GenericWindow.ini` 回退链读取属性）。

### 顶层（控件封装）
这些类通常在 XNAUI 控件上增加：统一风格、INI 友好属性、音效、ToolTip 支持等。
- `XNAChatTextBox.cs`：聊天输入框控件封装。
- `XNAClientButton.cs`：按钮封装（默认按钮纹理/音效 + ToolTip）。
- `XNAClientCheckBox.cs`：复选框封装。
- `XNAClientColorDropDown.cs`：颜色下拉框封装。
- `XNAClientDropDown.cs`：下拉框封装（含 ToolTip，打开下拉时屏蔽 ToolTip）。
- `XNAClientLinkLabel.cs`：链接标签封装。
- `XNAClientPreferredItemDropDown.cs`：带“偏好项/推荐项”逻辑的下拉框封装。
- `XNAClientStateButton.cs`：状态按钮封装（按状态切换显示/行为）。
- `XNAClientTabControl.cs`：Tab 控件封装。
- `XNAClientToggleButton.cs`：切换按钮封装。
- `XNAExtraPanel.cs`：“贴图决定尺寸”的额外面板（modder 扩展点）。
- `XNALinkButton.cs`：链接按钮封装（支持 URL/UnixURL 及本地化）。
- `XNAMessageBox.cs`：通用消息框（OK/YesNo/OKCancel，带遮罩）。
- `XNAPlayerSlotIndicator.cs`：玩家槽位指示器控件。

### Settings/
- `IUserSetting.cs`：可加载/保存的设置控件接口。
- `IFileSetting.cs`：文件型设置控件接口（支持刷新/回滚）。
- `SettingCheckBoxBase.cs` / `SettingCheckBox.cs`：绑定 `UserINISettings` 的复选框设置控件。
- `SettingDropDownBase.cs` / `SettingDropDown.cs`：绑定 `UserINISettings` 的下拉设置控件。
- `FileSettingCheckBox.cs` / `FileSettingDropDown.cs`：带文件操作语义的设置控件。
- `FileSourceDestinationInfo.cs`：文件应用/回滚策略（覆盖/保留修改/硬链接只读等）。

### IME/
- `IMEHandler.cs`：IME 处理抽象。
- `SdlIMEHandler.cs`：SDL/GL 路线的 IME 实现（在 GL 配置下启用）。
- `WinFormsIMEHandler.cs`：WinForms 路线的 IME 实现（非 GL 配置下启用）。
- `DummyIMEHandler.cs`：无 IME 环境下的兜底实现。
