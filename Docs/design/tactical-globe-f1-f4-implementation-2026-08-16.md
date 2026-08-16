# Tactical 地球 F1–F4 实施技术文档

> 日期：2026-08-16  
> 状态：**实施中**（与代码同步演进；完成后勾验收）  
> 需求来源：[`tactical-globe-features-rotation-borders-anchors-city-2026-08-16.md`](./tactical-globe-features-rotation-borders-anchors-city-2026-08-16.md)  
> 底座：OpenGL 球已迁移完成（[`tactical-globe-opengl-migration-2026-08-16.md`](./tactical-globe-opengl-migration-2026-08-16.md) §12–13）

本文把需求文档的四项能力（F1 旋转对准 / F2 国境高亮 / F3 锚点 / F4 城市全息）落成可直接编码的规格：每个功能给出精确的数学契约、数据格式、类/文件改动、状态机与验收。

---

## 0. 公共契约（所有功能共享）

### 0.1 地理 ↔ 几何映射（单一事实源）

```
Dir(lat, lon) — 球面单位向量（GL 顶点与 overlay 锚点共用同一公式）：
  x = cos(lat)·sin(lon)      z = cos(lat)·cos(lon)      y = sin(lat)
姿态：camera z 轴正向 F=3.4；模型 = Rx(pitch)·Ry(yaw)
投影：screen = center ± dir · radius · F/(F−z)
```

由该公式可直接推导 F1 目标姿态（见 §1.2）：`targetYaw = −lon`，`targetPitch = clamp(lat, −40°, 40°)`。该推导的正确性在单测中固化为回归（§5.1）。

### 0.2 设置项

| 设置（`Settings.ini [Visual]`） | 默认 | 用途 |
|---|---|---|
| `GlobeAutoRotateEnabled` | true | F1 动画期间强制暂停，结束按设置恢复 |
| `GlobeStyle=Vector\|Art` | Vector | 已有 |
| `GlobeFocusEnabled`（新增） | true | F1 选中对准总开关 |
| `GlobeBorderHighlightEnabled`（新增） | true | F2 高亮总开关 |
| `GlobeCityHoloEnabled`（新增） | true | F4 自动进入城市层总开关 |

### 0.3 Battle.ini 字段扩展

```ini
[SECTION]
GlobeLatitude=41.9         ; 既有，纬度 −90..90
GlobeLongitude=12.5        ; 既有，经度 −180..180
GlobeCountry=IT            ; 新增，ISO 3166-1 alpha-2（大小写不敏感），驱动 F2
GlobeCityArt=Resources/DTA/Globe/Cities/mission_x_holo.png   ; 新增，F4A 自定义投影板（可选）
```

缺 `GlobeCountry`：F2 对该任务静默跳过。缺 `GlobeCityArt`：F4A 用程序化全息板兜底。`GlobeCityArt` 相对 `Resources/DTA/` 解析，缺失文件时日志告警并回退程序化板，不崩溃。

### 0.4 分层架构（实现后目标）

```
DxCampaignGlobeHost ── 列表选中/过滤 ──► TacticalGlobeView ── pose/动画状态机 ──► TacticalGlobeGlControl
        │                                   │        │
        │                                   │        ├─ OverlayLayer (Avalonia): F3 锚点、F4A 投影板
        │                                   │        └─ GL 线层: F2 国境高亮
        │                                   └─ GlobeBorderLibrary (country_borders.bin 解析)
        └─ MissionEntry.GlobeCountry / GlobeCityArt (Battle.ini)
```

---

## 1. F1 — 选中旋转对准动画

### 1.1 状态机

```
Idle ──选中变化/锚点点击──► Focusing
Idle/Focusing ──PointerPressed──► Idle (动画取消, 惯性清零)
Focusing ──达时/误差<ε──► Idle (若 AutoRotate 恢复自转)
Focusing ──再次选中变化──► Focusing (重定向, 不经过 Idle)
```

动画期间：忽略 `AutoRotateEnabled`；`PointerPressed` 打断并把残留惯性归零。

### 1.2 目标姿态推导（硬契约）

要使 `(lat, lon)` 投影到屏幕中心：`Dir(lat,lon)` 经 Rx(pitch)·Ry(yaw) 后须为 `(0,0,1)`。逐分量求解得唯一解：

- `targetYaw = −lon`（度）
- `targetPitch = clamp(lat, −40°, +40°)`（避开极地，与拖拽钳制一致）

> 钳制时高纬度点不落正中而是留在安全区上/下方——符合「视口中心偏上留抬头」的体验意图。

### 1.3 动画参数

| 参数 | 值 | 说明 |
|---|---|---|
| 时长 | 0.8s | 0.6–1.2s 折中 |
| easing | ease-out cubic：`1−(1−t)³` | 起步快收尾稳 |
| yaw 路径 | 最短弧：`Δ = ((target−yaw+540) mod 360) − 180` | 跨 ±180 不绕远 |
| pitch 路径 | 线性插值（同 easing） | |
| 驱动 | 复用 33ms `DispatcherTimer`（不新增定时器） | |
| 完成判据 | 误差 < 0.05° 或时长耗尽，取先到 | |
| 重定向 | 新目标时以当前姿态+进度重启动画 | |

### 1.4 API

```csharp
// TacticalGlobeView
public void FocusNode(int index);        // 升级为完整动画（保持既有签名）
public bool IsFocusing { get; }          // F4 判断「对准完成」
public event EventHandler? FocusCompleted; // F4A 淡入触发点
```

- `DxCampaignGlobeHost`：`SelectedIndex` 变化 → 若 `GlobeFocusEnabled` 且节点坐标有效（`HasGlobePosition`）→ `FocusNode`；哈希回退坐标同样有效（哈希点也是合法目标）。
- `TacticalGlobeView.NodeClicked`（锚点点击）→ 宿主回写列表 → 同一链路触发 F1。

### 1.5 验收

- [x] 切列表项 → 球平滑转向目标区域（单测断言最终姿态）
- [x] 最短弧（单测：yaw 从 170°→−170° 实际 Δ=+20°）
- [x] 拖拽打断、打断后自转遵守设置
- [x] 无坐标任务不旋转不报错（哈希回退即有效目标）

---

## 2. F2 — 国境线高亮

### 2.1 数据管线（离线）

数据源：Natural Earth 1:10m `ne_10m_admin_0_countries.geojson`（含每国 polygon + `ISO_A2` 属性）。工具 `Tools/GeoConvert` 扩展第三输入：

```
GeoConvert <land.geojson> <border.geojson> <countries.geojson> <out.bin>
```

新二进制 `country_borders.bin`（magic `'GBCB'` v1，与 world_geo.bin 同一量化：`q = round((deg+off)/range·65535)`，步长 0.0055°）：

```
u32 magic 'GBCB' | u32 version=1 | u32 countryCount
per country:
  char[2] isoA2 (ASCII 大写)
  u32 ringCount; per ring: u32 vertexCount + (u16 qLon, u16 qLat)*n   // 外环,含洞不区分(仅描边)
```

量化精度 0.0055° ≈ 610m，对描边用途足够（1:10m 数据本身粒度更粗）。体积预估 2–4MB（与 world_geo.bin 同量级），嵌入 `Assets/Geo/country_borders.bin`（`AvaloniaResource`）。

### 2.2 运行时

**解析**：`Controls/GlobeBorderLibrary.cs`（新文件）

- 启动后台（挂 `PreloadTacticalAssets`）解析为 `Dictionary<string, ushort[][]>`（ISO_A2 大写 → 折线环数组）
- 解析失败/缺文件：F2 功能整体禁用（`IsAvailable=false`），日志一条，不抛
- 线程安全：静态缓存 + `lock` 双检锁

**GL 层**：`TacticalGlobeGlControl` 追加「边界线 pass」：

- 高亮国切换时上传该国折线 VBO（`GL_LINE_STRIP` 每环一 draw，或展平为单 VBO + draw-range）— 选展平+per-ring draw，避免多 VBO 管理
- 顶点即 `Dir(lat,lon)` 球面点（半径 1.002 轻微抬升避免 z-fighting）
- Shader：复用球 MVP；片元 `#2EE6C5` emissive，`uBorderAlpha` 淡入淡出（200ms）
- 深度测试开启 → 背面边界自然被球遮挡

**绑定**：`GlobeNode` 增加 `CountryCode`；`DxCampaignGlobeHost.RefreshNodes` 从 `CatalogListItemViewModel.GlobeCountry` 传入。`TacticalGlobeView.SelectedNodeIndex` 变化 → `SetHighlightedCountry(code)`。

### 2.3 INI 语义

- `GlobeCountry=IT`：大小写不敏感，解析时归一大写
- 非 ISO 合法值（长度≠2 / 非字母）：日志告警 + 跳过 F2（F1/F3 不受影响）
- 同一国家多任务：共享同一高亮，切换任务零成本

### 2.4 验收

- [ ] 选中 `GlobeCountry=US` 任务 → 仅美国边界高亮
- [ ] 切换国家 200ms 交叉淡化无闪烁
- [ ] 边界与矢量烘焙贴图大陆套合（同源量化数据，理论零偏移）
- [ ] 无字段/坏字段任务无崩溃、无错误高亮（单测）

---

## 2A. F2 数据生产步骤（实施时执行）

1. 下载 `ne_10m_admin_0_countries.geojson`（~25MB）到 `Tools/GeoConvert/data/`
2. `dotnet run -- <land> <border> <countries> country_borders.bin`
3. 拷贝到 `ClientAvalonia/Assets/Geo/country_borders.bin`
4. `Tools/GeoVerify`：按国采样断言（美国含 49°N 直线边界、意大利靴尖、日本列岛）

---

## 3. F3 — 锚点视觉强化

### 3.1 视觉规格

| 状态 | 形状 | 尺寸 | 颜色 | 备注 |
|---|---|---|---|---|
| 默认 | 菱形 + 1px 外环 | 5px（半对角线） | `DxAccentPrimaryBrush` | 深度缩尺 `0.75+0.25·z` |
| 悬停 | 菱形 + 外环 | ×1.5 | 同默认 | tooltip 显示 `Label` |
| 选中 | 菱形反色 + 角括号 + 短标签 | ×1.3 | `DxAccentInverseBrush` | 括号呼吸缩放 ±8% |
| 锁定 | 空心菱形 | 5px | `DxLineBrush`（muted） | `Locked` 语义既有 |
| 背面 | 同状态 | ×0.6 | 透明度 0.30 | 既有行为保留 |

- 菱形 = 方形旋转 45°（`RotateTransform` 或 StreamGeometry 顶点直算）
- 外环：`EllipseGeometry` 半径 = 菱形半对角线 + 2px
- 呼吸：选中态括号 `len = 3.2·(1+0.08·sin(2π·t/1.6s))`，`t` 取自渲染时钟（`Environment.TickCount64`）
- 标签：选中态 `FormattedText`，系统字体，11px，随锚点偏移 `b+6` px；超宽截断至 12 字符 + `…`

### 3.2 行为

- 锚点集合 = `Nodes`（宿主已按列表过滤生成，含哈希回退）
- `HitTest` 半径 12px 不变；仅正面锚点可点
- 点击 → `NodeClicked` → 宿主回写列表选中 → F1 触发（同一链路）
- 悬停改变光标为 `PointerCursor`（hand）

### 3.3 验收

- [ ] 当前列表所有任务锚点常驻可见、不被贴图淹没
- [ ] 悬停放大 + tooltip；点击与列表选中一致并触发 F1
- [ ] 锁定任务空心灰显示；背面降透明
- [ ] 自转/拖拽时锚点与大陆套合（共享 Dir，理论零偏移）

---

## 4. F4A — 城市全息投影板

### 4.1 状态机（并入 TacticalGlobeView）

```
Idle (L0)
  ── FocusCompleted ──► CityHolo (L2, 仅当 GlobeCityHoloEnabled 且节点非锁定)
CityHolo:
  ── 拖拽/换任务(未达标)/SelectedNodeIndex=-1 ──► 退出 → Idle
```

- 进入：`FocusCompleted` 后 300ms 延迟防误触
- 退出：`PointerPressed`、`SelectedNodeIndex` 变为 −1 或换到无坐标节点
- CityHolo 期间：自转暂停（同 Focusing 优先级）

### 4.2 表现（overlay 层，非 GL）

**程序化默认板**（无 `GlobeCityArt` 时）：

- 位置：锚点屏幕坐标 `(sp.X, sp.Y)`，向上展开
- 构成：
  - 底部锚点 → 板底引线（2px accent 竖线，长度 = 板高 + 12px）
  - 板体：`300×170` 圆角 6 玻璃拟态（背景 `#0A1420E6`，边 1px accent，BoxShadow 轻投影）
  - 内部程序化「城市街区」：5×3 网格建筑剪影（`StreamGeometry` 矩形群，高度伪随机（节点哈希），填充 accent 透明度 0.25–0.55，顶部 1px 高光）+ 底部 1px 地平线 + 2 条水平扫描线（accent 0.15）
  - 标题栏：`Label` 截断 18 字符，`FontWeight SemiBold`，11px
  - 副标题：`(lat, lon)` 格式 `N41.9 E12.5`，`DxTextMutedBrush` 10px
- 淡入：300ms，alpha 0→1（与 F2 节奏一致）；淡出 200ms

**自定义图**（`GlobeCityArt` 存在）：`Bitmap` 加载替换建筑剪影区，其余框架一致；`InterpolationQuality` 高。

边界处理：板不得超出 globe 视口 — 左右钳制 `clamp(sp.X, 155, W−155)`；顶部不足时翻转到锚点下方。

### 4.3 数据

- `MissionEntry.GlobeCityArt`（string?）→ `CatalogListItemViewModel.GlobeCityArt` → `GlobeNode.CityArtPath`
- 加载：`SafePath.GetFile(GamePath, "Resources/DTA", path)`；失败日志 + 程序化板回退
- 缓存：`Dictionary<string, IImage>`，容量 8，LRU 逐出，避免多任务切换累积

### 4.4 验收

- [ ] 对准完成后淡入全息板，视觉可识别为「城市全息」而非全球贴图放大
- [ ] 板锚定任务坐标（引线指向锚点）
- 钳制与翻转逻辑边界正确（单测：sp 近左缘/顶部）
- [ ] 拖拽/换任务退出无泄漏、无残留位图
- [ ] Classic / 无资源任务安全降级（仅 F1+F3）
- [ ] 发布单文件运行（`AvaloniaResource` 与文件系统混合路径可用）

---

## 5. 测试计划

### 5.1 新增单测（`ClientAvalonia.Tests/Controls/`）

| 测试类 | 覆盖 |
|---|---|
| `GlobeFocusMathTests` | 目标姿态推导（伦敦/巴格达/悉尼/极地钳制）；最短弧 Δ；ease-out 单调性 |
| `GlobeBorderLibraryTests` | GBCB 解析（合法/坏 magic/截断流）；ISO 查询；解析器容错 |
| `GlobeNodeMappingTests` | Battle.ini 字段 → GlobeNode 映射（含 GlobeCountry 归一、非法值跳过） |
| `GlobeCityHoloLayoutTests` | 板边界钳制/翻转决策函数（纯函数抽取） |

### 5.2 回归

- 既有 `GlobeTextureBakerTests` 必须保持通过
- 全量测试套件（排除已知 17 个 WAF 失败）零新增失败

---

## 6. 实施顺序（本文档驱动）

```
1. F1 FocusNode 完整动画 + 打断/自转策略          ← 纯 C#，先落地
2. F3 锚点视觉强化（overlay 绘制）                 ← 与 F1 同文件，顺带
3. F2 数据管线 + GlobeBorderLibrary + GL 线层      ← 数据先行
4. F4A 投影板 + 状态机                            ← 依赖 F1 事件
5. Battle.ini 示例字段 + 设置项
6. 测试 + 构建回归
```

---

## 7. 风险与缓解

| 集中风险 | 缓解 |
|---|---|
| GeoConvert 第三输入引入解析回归 | 版本化 magic（GBCB v1）+ 单测容错路径 |
| 每帧 GL 线层叠加的性能 | 线 VBO 仅在国切换时上传；per-ring draw 调用少（<10 环典型） |
| 投影板遮挡战役窗左右栏 | 板宽 300px ≤ 左栏宽，且左右钳制 |
| 城市图 LRU 泄漏 | 容量 8 + 切窗 Dispose |
| Focus 与自转/惯性竞态 | 状态机单一入口 `BeginFocus`，PointerPressed 显式取消 |

---

## 8. 实施记录

（随实施填充）

### 8.1 F1 + F3（2026-08-16）

- `TacticalGlobeView.cs`：`FocusNode` 重写为完整动画 — 目标姿态 `(yaw=−lon, pitch=clamp(lat,±40°))`、最短弧、ease-out cubic、0.8s、完成判据 0.05°；`PointerPressed` 取消动画并清惯性；`FocusCompleted` 事件；`IsFocusing` 属性；动画期间跳过自转/惯性分支。单测 `GlobeFocusMathTests` 固化推导与最短弧。
- F3 锚点：菱形 + 外环 + 深度缩放（`0.75+0.25·z`）、锁定空心、选中反色角括号呼吸（TickCount64 驱动）+ 标签截断 12 字符、悬停放大 1.5× + tooltip 文本、光标 Hand。

### 8.2 F2 国境高亮（2026-08-16）

- 数据：`Tools/GeoConvert` 扩展第 4/5 参（`ne_10m_admin_0_countries.geojson` → `country_borders.bin`，'GBCB' v1，252 国 / 4293 环 / 2.1MB）。**关键坑**：Natural Earth 对法国等海外省合体国家把 `ISO_A2`/`ISO_A3` 置 `-99`，回退链必须是 `ISO_A2 → ISO_A2_EH → ISO_A3 → ISO_A3_EH → ADM0_A3`（否则法国丢码）。
- 运行时：`Controls/GlobeBorderLibrary.cs`（解析 + 短读防护 + 双检锁缓存）；`TacticalGlobeGlControl` 新增独立线 shader（无 albedo 采样，预乘 alpha 淡入 220ms/淡出 260ms）、`glBlendFunc`/`glDisableVertexAttribArray` 经 `GetProcAddress` 手工绑定（`GlInterface` 未封装）；顶点为 `Dir()` 球面点 ×1.002 抬升，深度测试天然遮挡背面。
- 绑定：`Battle.ini` `GlobeCountry` → `MissionCatalogLoader.ReadCountryCode`（2/3 字母归一大写，非法日志+跳过）→ `CatalogListItemViewModel.GlobeCountry` → `GlobeNode.CountryCode` → `SelectedNodeIndex` 变化时 `SetHighlightedCountry`。
- 验证：`Tools/GeoVerifyBorders`（射线法 point-in-ring）16/16 抽样通过（含美加 49°N、科西嘉属法、首尔非日等负样本）。

### 8.3 F4A 城市全息板（2026-08-16）

- 状态机：`FocusCompleted` → 300ms 防误触延迟 → 300ms 淡入；拖拽/换选/清除选中即时退出。锁定的任务不进入。
- 板：`GlobeMath.ClampHoloBoard` 纯函数（左右钳制 + 头顶空间不足翻转到锚点下方，顶部同步钳制）+ overlay 绘制（引线、玻璃体、标题、N/S E/W 坐标、按标签哈希的确定性程序化天际线、地平线+扫描线）。单测覆盖 6 组边界（含锚点出视口、视口小于板的退化情形）。
- 说明：`GlobeCityArt` 自定义图路径未在本批实现（当前为程序化板兜底）；字段契约保留在 §0.3，美术就绪后接 `IImage` 缓存（LRU 8）即可。

### 8.4 设置与文档

- `UserINISettings`：新增 `GlobeFocusEnabled` / `GlobeBorderHighlightEnabled` / `GlobeCityHoloEnabled`（[Visual]，默认 true）。
- `Battle.ini` 头部注释补充 `GlobeCountry` 说明。
- `DxThemeManager.PreloadTacticalAssets` 挂 `GlobeBorderLibrary.WarmUp()`。

### 8.5 验证汇总

- 主工程 + 测试工程 Debug 构建 0 错误。
- Globe 专项 47/48 通过（1 个既有 Skippable 因测试宿主无 AssetLoader 跳过）。
- 全量回归 1804 通过 / 19 失败——失败全部为既有 CnCNet WAF/Ingress 基线（stash 前后同过滤集均 17 失败，另 2 处 Integration 为套件并发偶发，单独/组合复跑均通过），**0 新增失败**。
- 待人工验收：Tactical 战役窗内 F1 旋转手感、F2 高亮与贴图套合、F4A 板排版、Classic 回归、发布单文件运行。

### 8.6 崩溃修复：渲染期 invalidate（2026-08-16）

- **现象**：进入 Tactical 单人战役窗即崩溃（`ClientCrashLog2026_08_16_18_19`）。堆栈：`InvalidOperationException: Visual was invalidated during the render pass` at `TacticalGlobeView.RenderOverlay` → `Visual.InvalidateVisual()`。
- **根因**：Avalonia 11 合成器禁止在渲染 pass 内调用 `Visual.InvalidateVisual()`。初版实现在 `RenderOverlay` 末尾加了「动画期间持续 invalidate」块，而进入战役窗时默认有选中任务，`HasPulsingSelection()` 恒真 → 首帧即触发。
- **修复**：删除渲染回调内的 invalidate；连续动画（选中脉冲括号、F4A 全息淡入淡出）改由既有的 33ms `DispatcherTimer` 驱动——定时器在渲染 pass 之外运行，`InvalidateVisual` 安全。定时器新增 `HasAnimatedOverlay()` 分支：无惯性、无自转但有动画 overlay 时仍每帧重绘。GL 侧 `TacticalGlobeGlControl` 用的是 `OpenGlControlBase.RequestNextFrameRendering()`（入组合器更新队列，渲染期安全），无需改动。
- **验证**：Debug 构建 0 错误；Globe 专项 47/48（同前）；已重新发布单文件并部署至测试区。

### 8.7 国境线整体隐去（2026-08-16）

- **背景**：当前版本的国境线数据（GBCB blob + world_geo v2 折线）绘制结果有误，本版本要求把国境线全部隐去，包括贴图烘焙里的常驻国境线。
- **方案**：`GlobeBorderLibrary.CountryBordersEnabled`（`internal const bool = false`）作为单一总开关，管线（blob、shader、INI 字段、状态机）全部保留，数据修复后翻转一个常量即可整体恢复：
  - `GlobeVectorBaker` Pass 5（贴图烘焙国境线笔画）整段门控；
  - `TacticalGlobeGlControl.SetHighlightedCountry` 把任何 code 归一为 null（F2 高亮层完全空跑，不取数据不上传顶点）；
  - `DxThemeManager.PreloadTacticalAssets` 不再预热国界 blob（省 2.1MB 解析）。
- **副作用**：`const false` 折叠产生 2 条 CS0162 不可达代码警告（`GlobeVectorBaker.cs` / `DxThemeManager.cs`），属预期，恢复开关即消失。`Tools/GeoPreview`（离线预览工具）不改，便于继续排查数据问题。
- **验证**：Debug 构建 0 错误；Globe 专项 47/48；已重新发布并部署至测试区。
