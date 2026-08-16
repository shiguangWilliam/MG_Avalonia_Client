# Tactical 地球：OpenGlControlBase 迁移方案

> 日期：2026-08-16  
> 状态：**方案（不落地代码）**，待决策后实施  
> 前置：[`tactical-globe-tech-and-anchors-2026-08-16.md`](./tactical-globe-tech-and-anchors-2026-08-16.md)  
> 目标：用 Avalonia `OpenGlControlBase` **替换** 当前 UI 线程 CPU 射线球；去掉海岸线矢量描边；贴图与经纬度严格对齐；画质靠 **预烘焙高清贴图 + GPU 采样/抗锯齿**，不再做每帧球面栅格化。

---

## 1. 决策摘要

| 项 | 决定 |
|---|---|
| 3D 底座 | **`OpenGlControlBase`**（否决 Three.js / WebView） |
| 替换对象 | `TacticalGlobeView` 的软件 `WriteableBitmap` 射线采样路径 |
| 海岸线矢量 | **移除** `ContinentOutlines` 运行时描边/填充叠层 |
| 地理对齐 | 仅保留 **等距圆柱 UV ↔ lat/lon** 同一套公式；任务锚点用同一投影 |
| 贴图策略 | **离线高清烘焙 PNG（或 KTX2）上传为 GL 纹理**；运行时不做 CPU 球面栅格化 |
| 「矢量化贴图」 | **不作为地球反照率主路径**（见 §4）；矢量仅可用于经纬网/锚点 HUD |
| 抗锯齿 | MSAA + 各向异性过滤 + mipmap；可选轻量 FXAA |

---

## 2. 为何换 OpenGL

当前路径：每姿态在 CPU 上对 ~320² 像素做射线求交 + 贴图采样 → 拖拽卡顿主因。

OpenGL 路径：

- 球 = 静态 index/vertex buffer（一次上传）  
- 贴图 = GPU `sampler2D`（一次上传，mipmap）  
- 每帧：更新 MVP / 旋转 uniform，`glDrawElements`  
- 成本与分辨率近似解耦（由 GPU 填充率决定，远低于 UI 线程软件光栅）

与后期 **城市 mesh** 同一 GL 上下文：全球球 → 剧院缩放 → 任务 glTF/简化建筑块，无需换引擎。

---

## 3. 目标架构

```
DxCampaignGlobeHost          (列表 ↔ 节点 / 选中，尽量不动)
        │
        ▼
TacticalGlobeView            (保留：Yaw/Pitch、Nodes、交互、锚点 API)
        │ 内嵌或替换为
        ▼
TacticalGlobeGlControl : OpenGlControlBase
        │
        ├─ SphereMesh (UV 球)
        ├─ AlbedoTexture (world_map 烘焙图)
        ├─ Shader: 采样 + 简易光照/大气
        ├─ Optional: 经纬网线（GL lines 或薄带）
        └─ Anchors: GL 点精灵 / 小四边形，或 Avalonia 覆盖层投影
```

**Classic 主题**：不挂此控件（现有模板路由已隔离 Tactical）。

### 3.1 包与平台

- 引用 Avalonia OpenGL 相关包（随桌面后端；实现时核对 11.x 实际 PackageId，如 `Avalonia.OpenGL` / 示例里的 `OpenGlControlBase`）  
- Windows 优先：**ANGLE / 现有 Skia GL 上下文**兼容路径；实现阶段用 ControlCatalog OpenGL 页验证  
- `PublishSingleFile`：不引入 CEF；仅原生 GL  

### 3.2 生命周期

| 回调 | 工作 |
|---|---|
| `OnOpenGlInit` | 编译 shader、建 VAO/VBO/EBO、加载贴图、设 GL 状态（深度、MSAA） |
| `OnOpenGlRender` | 清屏、绑纹理、设 uniform（yaw/pitch/相机）、画球、画锚点 |
| `OnOpenGlDeinit` | 释放 GL 对象 |
| 控件卸载 / 主题切回 Classic | 停定时器、释放资源 |

姿态仍由现有 Pointer + `DispatcherTimer` 驱动；**仅绘制进 GL**，不再 `Lock` WriteableBitmap。

---

## 4. 贴图：不要运行时栅格化球面

### 4.1 明确废弃

| 废弃 | 原因 |
|---|---|
| 每帧 `RenderSphereBitmap` 射线采样 | 卡顿源 |
| `GlobeTextureBaker` 为球盘服务的 CPU 路径 | GL 直接采纹理；Baker 可降级为「仅调试/缺图回退」或删 |
| 运行时把海岸线矢量再栅格进 albedo | 与「贴图即地理」冲突，且双份成本 |

### 4.2 推荐：离线高清烘焙（主路径）

- 资源：`Assets/Glm/world_map.png`（或升级为 2K/4K 等距圆柱）  
- 约定：**标准 equirectangular**  
  - `u = (lon + 180) / 360`  
  - `v = (90 - lat) / 180`  
- 任务点、相机对准、锚点 **全部使用同一公式**（与 shader 中 `texture(albedo, uv)` 一致）  
- 制作：GLM-Image / 程序化工具 **离线** 出图 → 人工验收 → 入库；启动器只 `glTexImage2D` + `glGenerateMipmap`

分辨率建议：

| 档 | 尺寸 | 用途 |
|---|---|---|
| 默认 | 2048×1024 | 启动器战役窗够用 |
| 高清 | 4096×2048 | 剧院缩放 L1 仍可读轮廓 |
| 再高 | 不优先 | 单文件体积与显存；城市细节走 mesh/任务板 |

### 4.3 「矢量化」能不能替代贴图？

| 方案 | 结论 |
|---|---|
| 运行时矢量大陆填球面 | **否**为 albedo 主路径：要三角化/CSG，复杂且难出全息渐变 |
| SDF / 矢量纹理 | 研究向，工程与美术工具链重，**不进本迭代** |
| 矢量经纬网 + 锚点 HUD | **可以**，作为叠加层，不替代陆地贴图 |
| **预烘焙高清光栅贴图** | **是**：全息风格已在 PNG 上定稿，GPU 采样即可 |

一句话：**陆地外观用烘焙贴图；矢量只做导航符号与可选格网。**

### 4.4 去锯齿与采样质量（Shader / GL 状态）

不靠 CPU 超采样球盘，而靠：

1. **MSAA**（4x，可配置 2x/关）— 球轮廓锯齿  
2. **Mipmap + `GL_LINEAR_MIPMAP_LINEAR`** — 缩小时闪烁  
3. **各向异性过滤**（≤8）— 斜视赤道时贴图清晰度  
4. **可选 FXAA** 全屏一次 — 补 MSAA 管不住的高对比全息描边  
5. Shader 内：轻微 **rim / atmosphere** 可用；避免重型 per-pixel 噪声

极地等距圆柱固有拉伸：靠 mipmap + 接受地理投影限制；不在 P0 做立方体贴图。

---

## 5. 移除海岸线描边与对齐契约

### 5.1 移除内容

- `TacticalGlobeView.Render` 中对 `ContinentOutlines` 的投影描边/填充  
- 依赖「矢量岸线校准贴图」的任何假设  

可保留文件 `ContinentOutlines.cs` 仅作离线烘焙工具输入（可选），**运行时零引用**。

### 5.2 对齐契约（硬约束）

```
Battle.ini (lat, lon)
    ↓ 同一 equirectangular
锚点 NDC / 屏幕位置 = SphereMesh UV 上该 texel 的投影
    ↓
fragment: color = texture(worldMap, uv(lat,lon))
```

验收：抽样 5 个已知坐标（如伦敦、巴格达），锚点中心落在贴图对应陆地区域误差 &lt; 视觉 2–3px（战役窗尺度）。

贴图制作规范写入 `Tools/GlmImage/prompts/`：必须 full-bleed、无黑边、经度缝在 ±180° 可拼。

---

## 6. 功能切片与保留面

### 6.1 P0（本迁移必须交付）

- [ ] `OpenGlControlBase` 子类画出带 `world_map` 的旋转球  
- [ ] 拖拽 / 惯性 / 可选自转（逻辑复用，绘制换 GL）  
- [ ] **删除** 海岸线矢量叠层  
- [ ] 任务锚点可见（可先 GL 四边形或沿用 Avalonia overlay 投影）  
- [ ] 列表选中 ↔ 锚点同步（`DxCampaignGlobeHost` 保留）  
- [ ] 主观流畅：中端机拖拽无明显尖刺  
- [ ] Classic 不受影响；GL 失败时降级提示或静态贴图占位  

### 6.2 P1（紧随）

- [ ] 选中战役 → yaw/pitch 插值对准  
- [ ] MSAA/各向异性设置项或随画质档  
- [ ] 贴图升到 2K 并校验体积  

### 6.3 P2（城市 mesh，另案）

- 同上下文加载任务级 mesh；全球球缩至背景  
- 不在本迁移展开  

### 6.4 明确不做（本迭代）

- Three.js / WebView  
- 运行时矢量填陆地  
- 全球瓦片金字塔  
- 物理级大气散射  

---

## 7. 模块改动清单（实施时）

| 文件 / 区域 | 动作 |
|---|---|
| 新建 `TacticalGlobeGlControl.cs` | `OpenGlControlBase`：mesh、纹理、shader、render |
| `TacticalGlobeView.cs` | 瘦身为宿主 + 输入；删除 `RenderSphereBitmap` / 海岸线绘制 |
| `DxCampaignGlobeHost.cs` | 基本不动；确认子控件类型 |
| `GlobeTextureBaker.cs` | 停止为帧路径服务；或缺图时离线/启动一次生成默认纹理 |
| `ContinentOutlines` | 运行时解耦 |
| `ClientAvalonia.csproj` | OpenGL 包引用；`AllowUnsafeBlocks` 可按需收敛 |
| `Assets/Glm/world_map.png` | 保持；后续可换更高清烘焙版 |
| 设计文档本页 + 旧卡顿报告 | 实施后勾验收 |

---

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Windows GL/ANGLE 上下文与 Skia 争用 | 跟官方 OpenGL 示例；渲染仅在 `OnOpenGlRender` |
| 部分集显 MSAA 不稳 | 回退 0x MSAA + FXAA |
| 单文件 + GL 驱动崩溃 | try/catch Init，失败显示静态球盘占位 |
| 贴图缝（±180°）接缝 | 烘焙时保证无缝；shader 用 `GL_REPEAT` on U |
| 锚点深度与球排序 | 深度测试 + 轻微 polygon offset，或屏幕空间画锚点 |

---

## 9. 工作量粗估

| 项 | 人日 |
|---|---|
| GL 球 + 贴图 + 旋转交互打通 | 2–3 |
| 去海岸线 + UV/坐标对齐验收 | 0.5 |
| 锚点 GL/覆盖层迁移 | 0.5–1 |
| MSAA/过滤/失败降级 | 0.5–1 |
| 回归 Classic/Tactical 切换 | 0.5 |
| **合计** | **约 4–6 人日** |

不含：城市 mesh、选中飞向动画、2K 贴图重烘焙美术。

---

## 10. 验收标准（迁移完成定义）

1. Tactical 战役地球为 **OpenGL 绘制**，Profiler 中拖拽时无大规模 UI 线程位图像素循环。  
2. **无** 海岸线矢量描边叠层。  
3. 任务经纬度与贴图陆地视觉对齐（§5.2 抽样）。  
4. 全息贴图清晰，球边缘锯齿可接受（MSAA 或等效）。  
5. Classic 皮肤与非战役界面无回归。  
6. GL 初始化失败有降级，不白屏崩溃。  

---

## 11. 一句话

用 **`OpenGlControlBase` 画 UV 球 + 预烘焙高清等距贴图** 替换 CPU 射线栅格化；**删海岸线矢量**；地理只认 **lat/lon ↔ equirectangular**；画质靠 **mipmap / 各向异性 / MSAA（及可选 FXAA）**，不靠运行时矢量填陆或每帧烤球。
