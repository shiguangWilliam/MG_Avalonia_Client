# INI → Avalonia UI 渲染设计草案（v0.1）

日期：2026-05-07
状态：草案，待你审阅修改

---

## 0. 核心问题：XNA vs Avalonia 定位模型差异

这是迁移中最根本的技术差异，必须在设计第一层就解决。

### 0.1 现有 XNA/MonoGame 定位模型

XNAUI 的控件定位本质是**游戏引擎式绝对像素定位**：

```
每个 XNAControl 拥有独立的像素属性：
  X, Y          — 相对父控件的左上角偏移（整数像素）
  Width, Height — 固定像素尺寸

辅助约束（自定义概念，不是标准布局）：
  DistanceFromRightBorder   — 距离父控件右边缘的像素偏移（覆盖 X）
  DistanceFromBottomBorder  — 距离父控件底边缘的像素偏移（覆盖 Y）
  FillWidth / FillHeight    — 拉伸到父控件的 Width/Height 加减偏移值

绘制顺序：
  DrawOrder (int) — 越小越先绘制（在背后），类似 Z-order 但方向相反

表达式定位：
  $X / $Y / $Width / $Height — 通过 Parser 计算得到整数值
  表达式可引用其他控件：getX(btnOK)、getWidth($ParentControl)
```

**关键特征**：
- **坐标系**：整数像素，无 DPI 缩放概念。控件尺寸写死在像素值里。
- **父约束**：子控件 X/Y = 相对父控件 client area 的绝对像素值。
- **"右对齐"** 并非原生概念，靠 `DistanceFromRightBorder` 间接实现。
- **"填充"** 也不是自动的，靠 `FillWidth`/`FillHeight` 手动计算。
- **布局是一次性计算**：Initialize 时解析并计算一次，窗口 resize 时需手动重新计算。

### 0.2 Avalonia 定位模型

Avalonia 是 **WPF 式布局系统**：

```
布局容器（Panel 子类）：
  Canvas     — 绝对定位（Canvas.Left / Canvas.Top），最接近 XNA 模型
  Grid       — 行列网格定位
  StackPanel — 垂直/水平堆叠
  DockPanel  — 停靠定位

控件通用属性：
  Margin     — 外边距（Thickness: left, top, right, bottom）
  HorizontalAlignment — Left / Center / Right / Stretch
  VerticalAlignment   — Top / Center / Bottom / Stretch
  Width / Height      — 可设固定值，也可不设（由容器分配）

Z-order：
  ZIndex (int) — 越大越在前（与 DrawOrder 方向相反！）

坐标系：
  设备无关像素（device-independent pixels），自动 DPI 缩放。
  渲染分辨率 1280x720 在 Avalonia 中可能需要额外处理。
```

### 0.3 差异对比表

| 概念 | XNA/MonoGame | Avalonia 等效 | 差异程度 |
|------|-------------|--------------|---------|
| 绝对定位 | X, Y (int pixel) | Canvas.Left, Canvas.Top | **低** — 直接映射 |
| 尺寸 | Width, Height (int) | Width, Height | **低** |
| 右对齐 | DistanceFromRightBorder=X | HorizontalAlignment=Right + Margin.Right=X | **中** — 概念不同 |
| 底对齐 | DistanceFromBottomBorder=Y | VerticalAlignment=Bottom + Margin.Bottom=Y | **中** — 概念不同 |
| 宽度填充 | FillWidth=X | HorizontalAlignment=Stretch + Margin | **中** — 需换算 |
| 高度填充 | FillHeight=Y | VerticalAlignment=Stretch + Margin | **中** — 需换算 |
| Z序 | DrawOrder (越小越后) | ZIndex (越大越前) | **高** — 方向相反 |
| 布局触发 | 初始化时一次性计算 | 自动 re-layout | **高** — 行为差异 |
| 表达式引用 | getX(controlName) | 绑定到其他控件属性 | **高** — 机制完全不同 |
| DPI | 无（物理像素） | 有（设备无关像素） | **中** — 需统一处理 |
| 父容器 | Parent.Width / Parent.Height | 容器自动传递 AvailableSize | **中** — 概念差异 |

### 0.4 选型结论（Canvas 兼容层优先）

**第一阶段用 Canvas 作为桥梁**（与 `avalonia-ui-migration.md` M2 里程碑一致）：

- INI 解析后生成的每个控件节点，最终放在 `Canvas` 容器内
- `X/Y` → `Canvas.Left / Canvas.Top` **直接映射**
- `Width/Height` → 控件的 `Width / Height` **直接映射**
- `DistanceFromRightBorder` / `DistanceFromBottomBorder` → **在 INI→Descriptor 阶段计算为绝对 X/Y**（利用父控件尺寸提前求解）
- `FillWidth` / `FillHeight` → **同样在树构建阶段预计算为固定值**
- `DrawOrder` → **取反后映射到 `ZIndex`**（`ZIndex = -DrawOrder` 或 `MAX_DRAW_ORDER - DrawOrder`）

**后续阶段再引入 Grid/StackPanel 等原生布局**（此时旧 INI 已被 Canvas 兼容层兜底）。

---

## 1. 总体数据流

```
INI 文件 (.ini)
    │
    ▼
┌─────────────────────────────────────────────┐
│  ClientCore (不变)                            │
│  - CCIniFile 读取                             │
│  - INI 继承/预处理（BasedOn）                  │
│  - 翻译数据源（I18N）                          │
│  - 用户设置（UserINISettings）                 │
└─────────────────────────────────────────────┘
    │ 提供解析好的 IniSection / KeyValue
    ▼
┌─────────────────────────────────────────────┐
│  IniUiLoader（新中间层）                       │
│  ┌──────────────┐  ┌──────────────────────┐  │
│  │ Schema/       │  │ PropertyResolver     │  │
│  │ ControlRegistry│  │ (类型转换 + 翻译替换) │  │
│  └──────────────┘  └──────────────────────┘  │
│  ┌──────────────┐  ┌──────────────────────┐  │
│  │ Expression    │  │ BindingResolver      │  │
│  │ Evaluator     │  │ (Setting:/State:绑定) │  │
│  └──────────────┘  └──────────────────────┘  │
│                                              │
│  输出：UiNodeTree（ViewModel/Descriptor 树）   │
└─────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────┐
│  Avalonia 渲染层                              │
│  - DataTemplate 按 NodeType 选模板            │
│  - 绑定到 UiNode.Props                        │
│  - Canvas 兼容层 / Grid 原生布局               │
└─────────────────────────────────────────────┘
```

---

## 2. UiNodeTree 节点模型

```csharp
// 所有节点的基类/接口
public interface IUiNode
{
    string Id { get; }                    // 对应 INI Section 名，例如 "btnOK"
    string ControlType { get; }           // 例如 "XNAPanel", "XNAClientButton"
    Dictionary<string, object> Props { get; }  // 已解析的属性键值对
    List<string> Bindings { get; }        // 需要运行时绑定的属性名列表
    List<IUiNode> Children { get; }
    IUiNode Parent { get; }
}
```

### 2.1 Props 字典的设计原则

Props 中存放的是**已转为 Avalonia 可直接使用的类型和语义**的值。例如：

| INI 原始值 | Props 中的值 | 说明 |
|-----------|-------------|------|
| `X=100` | `Props["CanvasLeft"] = 100.0` | 已转为 double，key 改为 Avalonia 属性名 |
| `Y=200` | `Props["CanvasTop"] = 200.0` | 同上 |
| `Width=300` | `Props["Width"] = 300.0` | 同上 |
| `DistanceFromRightBorder=25` | `Props["CanvasLeft"] = parentW - 25 - w` | 已求解为绝对值 |
| `FillWidth=20` | `Props["Width"] = parentW - 20` | 已求解为绝对值 |
| `Text=Enter Game` | `Props["Text"] = "Enter Game"` | 翻译已完成 |
| `Enabled=no` | `Props["IsEnabled"] = false` | 类型已转换 |
| `DrawOrder=5` | `Props["ZIndex"] = -5` | DrawOrder 已取反 |
| `RemapColor=192,192,192,192` | `Props["OpacityMask"] = Color(...)` | 颜色已解析 |
| `BackgroundTexture=foo.png` | `Props["Background"] = IBitmap("foo.png")` | 资源已加载 |

**关键原则**：Props 中不放任何原始 INI 字符串，全量转为最终值。这样 Avalonia 侧的 DataTemplate 只需要做简单的 `{Binding Props[X]}`。

---

## 3. Canvas 兼容层的定位映射规则

### 3.1 输入属性与求解流程

```
对于每个 UiNode（在处理 Children 之前先处理自己）：

输入（来自 INI section）：
  X, Y, Width, Height             — 绝对定位（可能含表达式）
  DistanceFromRightBorder         — 右距离（可能含表达式）
  DistanceFromBottomBorder        — 底距离（可能含表达式）
  FillWidth, FillHeight           — 填充（可能含表达式）
  Location                        — 快捷设置 X,Y 的复合属性
  Size                            — 快捷设置 Width,Height 的复合属性

求解顺序：
  1. 解析 Location → X, Y（如果存在）
  2. 解析 Size → Width, Height（如果存在）
  3. 计算表达式（$X/$Y/$Width/$Height 需要 Parser）
  4. 应用 DistanceFromRightBorder → 覆盖 X
  5. 应用 DistanceFromBottomBorder → 覆盖 Y
  6. 应用 FillWidth → 覆盖 Width
  7. 应用 FillHeight → 覆盖 Height

最终输出 Props：
  "CanvasLeft"   = computed X (double)
  "CanvasTop"    = computed Y (double)
  "Width"        = computed Width (double)
  "Height"       = computed Height (double)
  "ZIndex"       = -DrawOrder (int)
```

### 3.2 DistanceFromRightBorder 的求解

```
已知：parentW = Parent.Props["Width"]
      selfW  = self.Props["Width"]
      D      = DistanceFromRightBorder 值

求解：CanvasLeft = parentW - D - selfW
```

**关键**：依赖父控件的 Width 必须先被确定。因此在遍历子节点时，父节点的 Width 必须已经求解完毕。这对 INI 解析顺序有要求。

### 3.3 FillWidth 的求解

```
已知：parentW = Parent.Props["Width"]
      F      = FillWidth 值（它是"在父宽度基础上缩进多少"）

求解：Width = parentW - F
```

注意：原 XNA 的 FillWidth 语义是 `this.Width = parent.Width - fillWidth`，即 "在父宽度基础上左右各缩进 fillWidth 像素"。需要确认这个语义并据此计算。

### 3.4 DrawOrder → ZIndex 映射

```
原 XNA: DrawOrder 越小越在底层（先绘制）
Avalonia: ZIndex 越大越在上层

映射：ZIndex = MAX_DRAW_ORDER_BASE - DrawOrder
     其中 MAX_DRAW_ORDER_BASE 设为一个足够大的基数（如 10000）

或者简单：
     ZIndex = -DrawOrder
     （假设没有两个控件 DrawOrder 相同）
```

---

## 4. 表达式系统迁移

### 4.1 现有表达式能力（Parser.cs）

```
函数（返回 int）：
  getX(name)                 — 获取控件 X
  getY(name)                 — 获取控件 Y
  getWidth(name)             — 获取控件 Width
  getHeight(name)            — 获取控件 Height
  getRight(name)             — X + Width
  getBottom(name)            — Y + Height
  horizontalCenterOnParent() — 居中并返回新的 X

常量：
  RESOLUTION_WIDTH           — 渲染宽度
  RESOLUTION_HEIGHT          — 渲染高度
  自定义常量（来自 parser constants INI section）

运算符：
  +, -, *, /, 括号
```

### 4.2 迁移策略

**方案：在树构建阶段（PropertyResolver）内求解表达式，输出数值到 Props**

```
输入 INI:  $X=getWidth($ParentControl) - getWidth($Self) - 4

处理流程：
  1. Parser 识别这是 expression key（以 $ 开头或值中含 get* 函数）
  2. 把 $ParentControl 替换为 parentNode.Id
  3. 把 $Self 替换为 currentNode.Id
  4. 在已构建的部分节点树中查找 getX(name) 等函数的值
  5. 执行数学运算
  6. 输出最终的数值到 Props["CanvasLeft"]

注意：表达式求解时，被引用的控件（getX(name) 中的 name）必须在树中已存在且已求解过位置。
这意味着 INI section 的解析顺序有拓扑依赖。
```

**简化实现（第一阶段）**：由于 M0 范围是主界面+进入游戏界面，可以：
1. 识别这两个界面用了哪些表达式
2. 只实现这些表达式的求解
3. 把表达式引擎的完整迁移推迟到 M4

### 4.3 Parser 表达式样例映射

以 `GenericWindow.ini` 中的 `SkirmishLobby` 为例：

```ini
[winbar_ur]
$X=getWidth($ParentControl) - 4
```

求解过程（已知 ParentControl = SkirmishLobby，其 Width 已算出为 RESOLUTION_WIDTH - 40）：
```
parentW = Props(SkirmishLobby).Width    // 比如 1240
$Self = "winbar_ur"                      // 当前节点 ID
selfW = Props(winbar_ur).Width           // 比如 24
CanvasLeft = parentW - 4                 // 1236
```

最终 Props(winbar_ur)["CanvasLeft"] = 1236。

---

## 5. 属性映射总表（第一期需要覆盖）

### 5.1 通用控件属性映射

| INI Key | 值类型 | 转换后 Props Key | 转换逻辑 |
|---------|-------|-----------------|---------|
| `X` | int/expr | `CanvasLeft` | 表达式求解 → double |
| `Y` | int/expr | `CanvasTop` | 同上 |
| `Width` | int/expr | `Width` | 同上 |
| `Height` | int/expr | `Height` | 同上 |
| `Location` | "X,Y" | `CanvasLeft`, `CanvasTop` | 拆分为两个值 |
| `Size` | "W,H" | `Width`, `Height` | 拆分为两个值 |
| `DistanceFromRightBorder` | int/expr | `CanvasLeft` | 求解为绝对值 |
| `DistanceFromBottomBorder` | int/expr | `CanvasTop` | 求解为绝对值 |
| `FillWidth` | int/expr | `Width` | 求解为绝对值 |
| `FillHeight` | int/expr | `Height` | 求解为绝对值 |
| `DrawOrder` | int | `ZIndex` | 取反或 base - value |
| `Text` | string | `Text` | 翻译后直接赋值 |
| `Enabled` | "yes"/"no" | `IsEnabled` | bool 转换 |
| `Visible` | "yes"/"no" | `IsVisible` | bool 转换 |
| `ToolTip` | string | `ToolTipText` | 翻译后赋值 |
| `Font` | string | `FontFamily` | 字体名映射 |
| `FontSize` | int | `FontSize` | double 转换 |
| `TextColor` | "R,G,B" | `Foreground` | 转为 IBrush |
| `BackgroundTexture` | path | `Background` | 转为 IBitmap/ImageBrush |
| `DrawMode` | enum | `Stretch` | Centered/Stretched/Tiled → Stretch 枚举 |
| `DrawBorders` | "true"/"false" | (忽略) | Canvas 不做边框 |
| `RemapColor` | "R,G,B,A" | (暂略) | 后续再映射到 opacity mask |

### 5.2 控件类型映射

| INI TypeName (旧的) | Avalonia 对应 | 说明 |
|---------------------|--------------|------|
| `XNAPanel` | `Border` 或自定义 `Panel` | 带背景图的容器 |
| `XNAClientButton` | `Button` | 主要按钮 |
| `XNALinkButton` | `Button`（Link 样式） | 链接按钮 |
| `XNALabel` | `TextBlock` | 文本标签 |
| `XNATextBox` | `TextBox` | 文本输入 |
| `XNAClientCheckBox` | `CheckBox` | 复选框 |
| `XNAClientDropDown` | `ComboBox` | 下拉框 |
| `XNAClientColorDropDown` | `ComboBox`（特殊模板） | 颜色下拉 |
| `XNAClientToggleButton` | `ToggleButton` | 切换按钮 |
| `XNAClientTabControl` | `TabControl` | 标签页 |
| `XNAExtraPanel` | `Border` | 装饰面板 |
| `XNAPlayerSlotIndicator` | 自定义控件 | 玩家槽位指示器 |

---

## 6. INI 解析与树构建流程

```
输入：窗口名 "SkirmishLobby"

步骤 1：加载 INI 链
  SkirmishLobby.ini → BasedOn → GameLobbyBase.ini → BasedOn → GenericWindow.ini
  合并所有 section（后加载的覆盖先加载的，这是现有 CCIniFile 的行为）

步骤 2：解析窗口自身
  - 读 [SkirmishLobby] section
  - 解析 $Width, $Height, DrawBorders 等自身属性
  - 创建根 UiNode(
      Id = "SkirmishLobby",
      ControlType = "Window",
      Props = { Width, Height, ZIndex, ... }
    )

步骤 3：解析 [$ExtraControls]
  - $CCbar_ul=winbar_ul:XNAPanel
  - 为每个 $CC 条目创建 UiNode 并加入 Children

步骤 4：递归解析所有子控件
  - 读 [winbar_ul] section
  - 解析 $X, $Y, $Width, $Height, BackgroundTexture 等
  - 表达式求解（此时父节点尺寸已确定）

步骤 5：求解 DistanceFromRightBorder / Bottom / FillWidth / FillHeight
  - 用父节点 Props 中的 Width/Height 代入公式

步骤 6：应用翻译
  - 对 Text, ToolTip 等进行 I18N 替换

输出：完整的 UiNode 树
```

### 6.1 拓扑排序问题

由于表达式可以引用任意控件（例如 `getX(btnOK)`），而 btnOK 可能在当前控件之后才出现在 INI 中，所以存在依赖顺序问题。

**处理方案**：两遍处理
- **第一遍**：解析所有 section，创建所有 UiNode，计算不依赖其他控件的属性（Width, Height, Text 等）
- **第二遍**：求解表达式，此时所有节点都已在树中，可以自由引用

---

## 7. Avalonia 渲染侧设计

### 7.1 DataTemplate 机制

```xml
<!-- 在 Avalonia Window/UserControl 的资源中定义 -->
<UserControl.Resources>
    <!-- 按 ControlType 选模板 -->
    <DataTemplate DataType="{x:Type local:UiNode}">
        <ContentControl Content="{Binding}">
            <ContentControl.ContentTemplate>
                <local:UiNodeTemplateSelector Content="{Binding}" />
            </ContentControl.ContentTemplate>
        </ContentControl>
    </DataTemplate>
</UserControl.Resources>
```

核心思路：一个自定义的 `ItemsControl` 或 `Panel`，遍历 `Children`，为每个 `UiNode` 用 DataTemplate 生成对应 Avalonia 控件。

### 7.2 Canvas 容器模板

```xml
<!-- 窗口根容器 -->
<Canvas Name="RootCanvas"
        Width="{Binding Props[Width]}"
        Height="{Binding Props[Height]}">
    <!-- 子控件通过 ItemsControl 绑定到 UiNode.Children -->
    <ItemsControl ItemsSource="{Binding Children}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <Canvas />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <!-- 每个子节点渲染为一个 Avalonia 控件 -->
                <ContentControl Canvas.Left="{Binding Props[CanvasLeft]}"
                                Canvas.Top="{Binding Props[CanvasTop]}"
                                Width="{Binding Props[Width]}"
                                Height="{Binding Props[Height]}"
                                ZIndex="{Binding Props[ZIndex]}">
                    <!-- 按 ControlType 选择内部控件 -->
                    ...
                </ContentControl>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Canvas>
```

### 7.3 窗口 resize 时的重新布局

这是 Canvas 兼容层的一个**重要局限**：

- 现有 XNA 系统在窗口 resize 时需要手动重新计算布局
- Avalonia Canvas 在父容器 resize 时，子控件的位置**不会自动更新**（Canvas.Left/Top 是固定值）
- 这意味着如果 INI 使用了 `RESOLUTION_WIDTH` 常量或 `FillWidth` 等相对语义，窗口 resize 后位置**会是错的**

**处理策略**：
1. **第一阶段**：窗口固定大小，不允许 resize（与当前行为一致）
2. **第三阶段**：如果需要窗口 resize，则需要监听 SizeChanged 并重新求解表达式、更新所有 Props

---

## 8. INI 继承链的处理

现有系统的 INI 继承通过 `BasedOn` 实现（如 `SkirmishLobby.ini` → `GameLobbyBase.ini` → `GenericWindow.ini`）。

```
在 IniUiLoader 中：

LoadIniChain(windowName):
  chain = []
  current = windowName
  while current != null:
    ini = CCIniFile.Load(current + ".ini")
    chain.Add(ini)
    current = ini.GetValue("INISystem", "BasedOn", null)
  反转 chain 使基类在前
  合并所有 section（后者覆盖前者）
  返回合并后的 IniFile
```

这项能力已存在于 Core 的 `CCIniFile` 中，直接复用即可。

---

## 9. 与 ClientCore 的边界

```
ClientCore 提供（不变）：
  ✅ CCIniFile                   — INI 读取、section 遍历、值存取
  ✅ INI 继承合并               — BasedOn 链
  ✅ Translation / I18N          — 文本翻译查询
  ✅ UserINISettings             — 用户设置读写
  ✅ ClientConfiguration         — 全局配置常量

新 UI 层只消费这些能力：
  ✅ 从 CCIniFile 读取 section/keys
  ✅ 调用 Translation.Instance.LookUp() 做文本翻译
  ✅ 调用 UserINISettings 做设置绑定
  ❌ 不依赖 XNAControl 体系
  ❌ 不依赖 WindowManager
  ❌ 不依赖 Rampastring.XNAUI 任何类型
```

---

## 10. 未覆盖的能力（已知限制）

以下能力在第一阶段**不实现**，列为已知限制：

| 能力 | 原因 | 后续计划 |
|------|------|---------|
| `horizontalCenterOnParent()` | 需要动态布局重算 | M2 后期或 M4 实现 |
| `RemapColor` | 颜色重映射涉及像素操作，Avalonia 无直接等价 | 后续用 OpacityMask 或 Effect 近似 |
| `DrawMode=Tiled` | 平铺绘制在 Avalonia 需自定义 Brush | M2 后期 |
| 动态控件增删（运行时修改 INI） | 当前行为极少使用 | M4+ |
| DrawBorders（控件边框线） | XNA 自绘边框，Avalonia 用 Border 控件 | 改为 Avalonia Border 的 BorderBrush |
| 贴图缓存/TextureManager | XNA 独有的纹理管理 | 资源加载统一走 Avalonia IAssetLoader |

---

## 11. 需要你确认/修改的关键决策

1. **Canvas 兼容层策略** — 是否同意第一阶段全量走 Canvas 绝对定位？（我建议是，因为最快）
2. **DrawOrder → ZIndex 取反映射** — 方向反了，是否接受这个语义转换？
3. **FillWidth 的准确语义** — 我理解是 `parentW - fillValue`，请你确认
4. **表达式求解时机** — 我建议两遍处理，第一遍建树，第二遍求解。是否 OK？
5. **窗口 resize** — 第一阶段是否接受「窗口固定大小、不可 resize」？
6. **属性映射表** — 上述表 5.1 中是否有遗漏的关键属性需要第一期就支持？
7. **控件类型覆盖范围** — 表 5.2 是否覆盖了主界面 + 进入游戏界面所需的所有控件类型？
8. **Props 字典中 key 的命名风格** — 我用的是 Avalonia 属性名（如 `CanvasLeft`），是否统一这个风格？

---

## 参考：主界面关键 INI 文件清单（从现有项目提取）

| 界面 | INI 文件 | 关键控件 |
|------|---------|---------|
| 通用窗口 | `GenericWindow.ini` | Extra panels (bar, glow), Background |
| 主大厅 | `CnCNetLobby.ini` → `LANLobby.ini` → `GenericWindow.ini` | 聊天列表、玩家列表、按钮 |
| LAN 大厅 | `LANLobby.ini` → `GenericWindow.ini` | 游戏列表、聊天输入、按钮 |
| 游戏大厅基类 | `GameLobbyBase.ini` → `GenericWindow.ini` | 地图选择、选项 CheckBox |
| 遭遇战大厅 | `SkirmishLobby.ini` → `GameLobbyBase.ini` | 同上+遭遇战特有选项 |
| 多人游戏大厅 | `MultiplayerGameLobby.ini` → `GameLobbyBase.ini` | 同上+多人特有选项 |

---

*草案完毕。请逐项过目，把不合理的地方标注出来，我根据你的反馈修改。*