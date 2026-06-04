# INI UI 规范：序列化逻辑、参数边界与类型

日期：2026-06-04  
状态：M0 + ClientAvalonia 中间层依据

## 1. 术语

| 术语 | 含义 |
|------|------|
| **反序列化（Load）** | INI 文本 → `UiNode` 树 + 已解析 Props + 原始 RawAttributes |
| **序列化（Save）** | 本规范 **不定义** UI 布局写回 INI；用户设置走 `UserINISettings`，与 UI INI 分离 |
| **UiNode** | 单个控件/窗口的逻辑节点，对应 INI 的一个 section |
| **ControlRegistry** | INI `TypeName`（如 `XNAClientButton`）→ Avalonia 模板与属性 schema |
| **RawAttributes** | 原始键值对，含 schema 未识别的 mod 扩展键 |

## 2. INI 文件加载顺序

与现有 `CCIniFile` / `INItializableWindow` / `XNAWindow` 行为对齐：

```
1. 定位文件（优先级从高到低）：
   Resources/{Theme}/{Name}.ini
   Base Resources/{Theme}/{Name}.ini
   （INItializableWindow 还支持 IniNameOverride）

2. [INISystem] BasedOn=foo.ini[,bar.ini]
   → 递归加载基 INI，ConsolidateIniFiles（子文件 section 覆盖基文件）

3. 每个 section 内 $BaseSection=OtherSection
   → 同文件 section 级键继承（子 section 已有键优先）

4. 窗口根 section = 窗口 Name（如 SkirmishLobby）

5. 子控件创建：
   a) [$ExtraControls] 或 [ExtraControls] 中 name:TypeName
   b) 父 section 内 $CC* = name:TypeName（可嵌套，见 GameOptionsPanel）
   c) XNAWindow.Initialize 后 C# AddChild（不在 INI 规范范围）
```

### 2.1 ExtraControls 两种 section 名

| Section | 使用场景 | 键格式 |
|---------|----------|--------|
| `[ExtraControls]` | XNAWindow（MainMenu 等） | `0=Logo:XNAExtraPanel` |
| `[$ExtraControls]` | INItializableWindow（SkirmishLobby 装饰） | `$CCbar_ul=winbar_ul:XNAPanel` |

键名规则：`ExtraControls` 用任意键；`$ExtraControls` 键须以 `$CC` 开头。

### 2.2 子控件命名边界

- 仅允许 `[A-Za-z0-9_]+`（`INItializableWindow.CreateChildControl`  enforced）
- `name:TypeName` 分割为 **恰好两段**（`Split(':')` length == 2）
- TypeName 必须在 ControlRegistry 注册，或为 **开放扩展类型**（见 §5）

## 3. 属性键命名空间

| 前缀/形式 | 处理层 | 示例 |
|-----------|--------|------|
| `$X` `$Y` `$Width` `$Height` | ExpressionEvaluator → LayoutResolver | `$X=getWidth($ParentControl)-4` |
| `$TextAnchor` `$AnchorPoint` | Label 专用（XNALabel） | `$AnchorPoint=getRight(MapPreviewBox),getY(lblMapName)` |
| `$CC` / `$CC_*` | 子控件创建，不写入 Props | `$CC01=btnLeaveGame:XNAClientButton` |
| `$BaseSection` | section 继承，不参与控件 Props | `$BaseSection=SkirmishLobby` |
| `$LeftClickAction` | 行为（有限白名单） | `$LeftClickAction=Disable` |
| 普通键 | PropertyResolver + 可选 Localization | `Text`, `IdleTexture`, `Checked` |

## 4. 参数类型（IniPropertyKind）

ClientAvalonia `PropertyResolver` 使用的强类型枚举：

| Kind | INI 示例 | 解析结果 | 本地化 |
|------|----------|----------|--------|
| `String` | `Text=Launch Game` | string | 是（Text, ToolTip, Suggestion, OptionName） |
| `Int` | `FontIndex=1` | int | 否 |
| `Bool` | `Checked=True`, `Enabled=no`, `DrawBorders=yes` | bool | 否 |
| `Size` | `Size=1280,720` | (w,h) double | Size 值不翻译 |
| `Location` | `Location=490,261` | (x,y) | 否 |
| `IntPair` | `RemapColor=192,192,192,192` | 4×byte 或 Color | 否 |
| `RgbColor` | `TextColor=255,128,0` | Color | 否 |
| `TexturePath` | `IdleTexture=MainMenu/campaign.png` | 资源 URI/路径 | 否 |
| `SoundPath` | `HoverSoundEffect=MainMenu/button.wav` | 路径 | 否 |
| `Url` | `URL=CnCNetQM.exe` | string | UnixURL 平台分支 |
| `Enum` | `DrawMode=Centered`, `$TextAnchor=CENTER` | enum 字符串 | 否 |
| `Expression` | `$Width=RESOLUTION_WIDTH-40` | int（第二遍求值） | 表达式不翻译 |
| `CommaList` | `Items=10,9,8`, `ItemLabels=a,b` | string[] | Items 逐项 Item{i} 翻译 |
| `Opaque` | 业务专用键 | 保留 string 入 RawAttributes | 视键而定 |

### 4.1 布局相关键（统一走 LayoutResolver）

| INI Key | 优先级/覆盖规则 |
|---------|------------------|
| `X`, `Y` | 基础坐标 |
| `Location` | 设置 X,Y（保留 Width/Height） |
| `Width`, `Height` | 尺寸 |
| `Size` | 设置 Width, Height |
| `DistanceFromRightBorder` | **覆盖 X**：`parentW - selfW - D` |
| `DistanceFromBottomBorder` | **覆盖 Y** |
| `FillWidth` | **覆盖 Width**：`parentW - F` |
| `FillHeight` | **覆盖 Height**：`parentH - F` |
| `DrawOrder` | → Avalonia `ZIndex = -DrawOrder` |

求解顺序与 `TranslationINIParser` + `INItializableWindow.ReadINIForControl` 一致：先 Location/Size，再表达式，再 Distance/Fill 覆盖。

### 4.2 通用视觉/交互键（XNAUI 基类 + ClientGUI）

| Key | Kind | 适用控件 |
|-----|------|----------|
| Text | String | 按钮、标签、勾选 |
| Enabled / Visible | Bool | 全部 |
| ToolTip | String | IToolTipContainer |
| Font / FontIndex / FontSize | String/Int | 文本控件 |
| IdleTexture / HoverTexture / ActiveTexture | TexturePath | 按钮、Panel |
| BackgroundTexture / SolidColorBackgroundTexture | TexturePath / IntPair | Panel |
| DrawMode | Enum | Panel（Centered/Stretched/Tiled） |
| DrawBorders | Bool | Panel |
| RemapColor | IntPair | Panel、Label |
| IdleColor / HoverColor | RgbColor | LinkLabel |
| MatchTextureSize | Bool | Button |
| HoverSoundEffect / ClickSoundEffect | SoundPath | Button、DropDown |

### 4.3 下拉框（XNADropDown / GameLobbyDropDown）

| Key | Kind |
|-----|------|
| Items | CommaList |
| ItemLabels | CommaList |
| DefaultIndex | Int |
| DataWriteMode | Enum（Boolean/String/Index/MapCode） |
| SpawnIniOption | String |
| OptionName | String（本地化） |

### 4.4 游戏选项勾选（GameSessionCheckBox / GameLobbyCheckBox）

| Key | Kind | 说明 |
|-----|------|------|
| SpawnIniOption | String | 写入 spawn.ini [Settings] |
| CustomIniPath | String | 地图 INI 片段路径 |
| Reversed | Bool | 勾选逻辑反转 |
| Checked / CheckedMP | Bool | 默认勾选 |
| EnabledSpawnIniValue / DisabledSpawnIniValue | String | spawn 写入值 |
| MapScoringMode | Enum | Irrelevant/DenyWhenChecked/DenyWhenUnchecked |
| DisallowedSideIndices | CommaList | 多人 side 限制 |
| AllowChecking | Bool | 是否允许用户切换 |

### 4.5 用户设置控件（SettingCheckBox / SettingDropDown）

| Key | Kind |
|-----|------|
| SettingSection / SettingKey | String |
| DefaultValue / Checked | Bool |
| RestartRequired | Bool |
| ParentCheckBoxName / ParentCheckBoxRequiredValue | String/Bool |

### 4.6 窗口级（GameLobbyBase section 内非控件键）

PlayerOptionLocationX/Y, PlayerOption*Margin, Player*Width — **Int**，由业务层读取，不映射 Avalonia 控件属性。

## 5. 允许自定义的参数边界

### 5.1 开放（mod/主题可安全修改）

- 任意已注册控件 section 内的 **§4.2 通用视觉/交互键**
- 布局键（§4.1），含表达式
- ExtraControls 中 **已注册 TypeName** 的子控件增删（仅官方白名单类型）
- 纹理路径、颜色、Text/ToolTip 文案（走翻译 key 或直接文本）

### 5.2 受限（可读不可随意扩展语义）

- `$CC` 子控件类型：必须在 ControlRegistry；未知类型 → 降级为 `XNAPanel` + 记录警告，**RawAttributes 全保留**
- `$LeftClickAction`：仅 `Disable` 白名单
- 表达式函数：仅 `Parser.cs` 已实现的函数集；未知函数 → 加载错误
- `$LeftClickAction`、网络/启动相关：不允许 INI 注入任意代码

### 5.3 封闭（INI 不提供，仅 C#）

- 事件处理器（LeftClick 业务逻辑）
- 网络协议、更新流程、GameProcessLogic 参数
- 动态创建的玩家槽位控件（GameLobbyBase.InitPlayerOptionDropdowns）
- DI 注册的 Singleton 窗口实例化

### 5.4 扩展键策略（ClientAvalonia）

```
若 key ∉ ControlRegistry.GetSchema(controlType):
  → 存入 UiNode.RawAttributes[key] = value
  → 若控件实现了 IIniExtensionConsumer，Initialize 时传入
否则:
  → 解析入 UiNode.Props（Avalonia 绑定用）
```

**Schema 暂不对外承诺**（与 `avalonia-ui-migration.md` 决策一致）；mod 作者仍按现有 XNA 客户端 INI 格式编写，Avalonia 层尽量兼容读取。

## 6. 表达式语法（与 Parser.cs 对齐）

```
expr   := term (('+'|'-') term)*
term   := factor (('*'|'/') factor)*
factor := int | CONSTANT | func '(' id ')' | '(' expr ')'
id     := controlName | $ParentControl | $Self
func   := getX | getY | getWidth | getHeight | getRight | getBottom | horizontalCenterOnParent
CONSTANT := RESOLUTION_WIDTH | RESOLUTION_HEIGHT | ParserConstants 中的自定义 int 常量
```

**两遍构建：**

1. 第一遍：创建所有 UiNode，解析非表达式属性与非交叉引用尺寸
2. 第二遍：对所有 `$` 键及表达式布局求值（此时树中控件均可 lookup）

## 7. 本地化

与 `TranslationINIParser` / `Translation.Instance.LookUp(control, attributeName, defaultValue)` 对齐的属性：

`Text`, `Size`, `Width`, `Height`, `Location`, `X`, `Y`, `DistanceFromRightBorder`, `DistanceFromBottomBorder`, `ToolTip`, `Suggestion`, `URL`, `UnixURL`, `OptionName`, `Items` 各分项 `Item{i}`

**翻译 key 规则：** `INI:Controls:{WindowName}:{ControlName}:{AttributeName}`（见 `TranslationGUIExtensions`）

ClientAvalonia：在 PropertyResolver 中注入 `ILocalizationService`（初期可 passthrough）。

## 8. UiNode 内存模型（序列化中间态）

```csharp
UiNode {
  string Id;                    // section 名
  string ControlType;           // 注册类型名，如 XNAClientButton
  string? AvaloniaTemplateKey; // 内部模板键，如 DxButton
  Dictionary<string, object> Props;       // 已解析，供绑定
  Dictionary<string, string> RawAttributes; // 含未识别键 + 原始字符串
  List<UiNode> Children;
  UiNode? Parent;
}
```

**Props 命名：** 布局用 `CanvasLeft`, `CanvasTop`, `Width`, `Height`, `ZIndex`；其余与 Avalonia 属性名对齐（`Text`, `IsEnabled`, `Background`, …）。

## 9. ControlRegistry 注册类型（基础 DX 组件）

与 `GameClass.cs` transient 注册 + XNAUI 23 类对齐，见 ClientAvalonia `DefaultControlRegistry.cs`。

业务类型（GameLobbyCheckBox 等）映射到 **最近似基础模板** + `ControlType` 保留原名 + 业务键进 RawAttributes。
