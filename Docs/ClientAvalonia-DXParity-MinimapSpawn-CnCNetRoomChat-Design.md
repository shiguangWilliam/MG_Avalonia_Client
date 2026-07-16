# ClientAvalonia 与 DX 一致性设计

## 主题
- DXMain 小地图出生点绘制与交互一致性。
- CnCNet 多人游戏房间聊天一致性（不仅是频道大厅聊天）。

## 目标
明确 ClientAvalonia 侧尚缺的功能，并给出可执行的实现检查清单。本设计文档不涉及运行时代码改动。

## 基线（DXMain）

### 1) 小地图出生点绘制与交互
参考实现：
- `DXMainClient/DXGUI/Multiplayer/GameLobby/MapPreviewBox.cs`
- `DXMainClient/DXGUI/Multiplayer/GameLobby/PlayerLocationIndicator.cs`

DXMain 具备能力：
- 绘制地图预览并计算缩放后的出生点锚点坐标。
- 为每个出生点绘制指示器（动态环 + 出生点图标纹理）。
- 在指示器附近绘制悬停/背景玩家名标签。
- 左键行为：
- 非上下文菜单模式下，本地玩家可快速选择出生点。
- 上下文菜单模式下，房主可将出生点分配给玩家/AI。
- 右键行为：
- 房主可清除该出生点分配，或在非房主路径清除自身已选出生点。
- 根据玩家/AI 槽位状态更新指示器文本与占用状态。
- 遵守地图约束（`EnforceMaxPlayers`、无效/占用出生点处理）。

### 2) CnCNet 游戏房间聊天
参考实现：
- `DXMainClient/DXGUI/Multiplayer/GameLobby/MultiplayerGameLobby.cs`
- `DXMainClient/DXGUI/Multiplayer/GameLobby/CnCNetGameLobby.cs`

DXMain 具备能力：
- 游戏房间内聊天输入框（`tbChatInput`）发送到房间 IRC 频道。
- 房间消息流（`Channel_MessageAdded`）追加到房间聊天列表。
- 房间输入支持斜杠命令（`/command`），包含房主权限校验。
- 系统提示（`AddNotice`）与普通聊天共享同一消息流。
- 房间发言支持聊天颜色。

## ClientAvalonia 当前状态

### 1) 小地图出生点绘制
现有实现：
- `ClientAvalonia/Themes/DxControlStyles.axaml`（`MapPreviewBox` 模板）
- `ClientAvalonia/IniUi/Binding/GameDataBindingApplier.cs`
- `ClientAvalonia/IniUi/Behaviors/LobbyBehaviors.cs`

状态：
- `MapPreviewBox` 仅渲染预览图。
- 点击行为绑定为“收藏地图开关”。
- 无出生点指示器渲染层。
- 无房主出生点分配上下文菜单。
- 无本地点击选择出生点行为。
- 无悬停标签与占用/不可用标记展示。

### 2) CnCNet 聊天行为
现有实现：
- `ClientAvalonia/CnCNet/CnCNetSession.cs`
- `ClientAvalonia/CnCNet/CnCNetGameRoomSession.cs`
- `ClientAvalonia/Views/MainWindow.axaml.cs`
- `ClientAvalonia/IniUi/Binding/GameDataBindingApplier.cs`

状态：
- 频道大厅聊天已实现（收发），路径为 `CnCNetSession.SendChatMessage` 与 `OnChatMessageReceived`。
- 当前发送路径仅使用当前游戏的大厅聊天频道（不是活动游戏房间频道）。
- 大厅中按 Enter 会触发 `TrySendChannelChat`，仍走大厅聊天发送路径。
- 房间会话已实现 CTCP/游戏状态子集（PO/GO/START/R/OR/TNLPNG/GSETTINGS/FHSH 等），但无独立房间聊天消息流。
- 尚无与 DX `MultiplayerGameLobby` 对齐的房间级斜杠命令解析器。
- 部分房间控制仍为占位：隧道选择 UI 待实现、房间设置 UI 待实现。

## 已确认缺口（DX 一致性差距）

### A. 小地图出生点一致性缺口
- A1. 出生点指示器视觉层（动态环、出生点图标、占用颜色）。
- A2. 房主在指示器上的分配交互（上下文菜单分配/清除）。
- A3. 加入方/本地玩家从小地图点击选择出生点。
- A4. 出生点悬停/选中提示标签。
- A5. 指示器与槽位模型同步（人类 + AI，队伍/名称渲染）。
- A6. 与 DX 等价的占用/不可用校验行为。

### B. CnCNet 房间聊天一致性缺口
- B1. 独立的房间聊天流模型（与大厅聊天流分离）。
- B2. 在 `CnCNetGameLobby` 中将发送目标路由到活动房间频道。
- B3. 将房间频道 PRIVMSG 接收并展示到房间聊天列表。
- B4. 房间提示消息与用户消息的统一时序模型。
- B5. 房间斜杠命令与 host-only 权限校验一致性。
- B6. 房间侧完整 UI 操作仍待实现：隧道选择器、房间设置面板。

## 设计方案

### 1) 小地图出生点架构（Avalonia）

#### 1.1 视图模型扩展
- 在大厅 UI 状态下增加 `MapPreviewOverlayState`：
- `List<StartLocationMarkerVm> Markers`
- `bool CanAssign`（房主）
- `bool CanSelectLocal`
- `int? HoveredMarkerIndex`
- `int? SelectedMarkerIndex`
- `bool EnableContextMenu`

`StartLocationMarkerVm` 字段：
- `int Index`（1 基出生点索引）
- `double X`、`double Y`（预览归一化坐标或控件像素坐标）
- `bool IsVisible`
- `bool IsOccupied`
- `bool IsSelectable`
- `IReadOnlyList<PlayerTagVm> Occupants`
- `MarkerVisualState VisualState`（`Empty`、`Occupied`、`Hovered`、`Disabled`）

#### 1.2 数据来源与映射
- 复用现有地图目录模型中的出生点数据。
- 按 DX 的预览缩放逻辑计算指示器坐标。
- 从 `LobbyPlayerState` 绑定占用状态（人类 + AI）与队伍/名称/颜色。

#### 1.3 交互模型
- 房主模式：
- 左键指示器 -> 打开分配菜单（玩家/AI 列表）。
- 右键指示器 -> 清除分配。
- 加入方模式：
- 左键指示器 -> 设置本地出生点。
- 右键指示器 -> 清除本地出生点（若已选择）。

#### 1.4 渲染方案
- 在 `MapPreviewBox` 模板上方增加叠加 `Canvas`。
- 指示器控件包含：
- 环形图标（可选动画：定时器或状态驱动的透明度/旋转）
- 出生点编号/图形
- 悬停占用徽标/文本气泡
- 将“收藏地图切换”改为独立显式按钮，避免与出生点点击冲突。

### 2) CnCNet 房间聊天架构（Avalonia）

#### 2.1 聊天域拆分
引入两条消息流：
- `LobbyChannelChat`（现有）。
- `GameRoomChat`（新增，仅在 `CnCNetGameLobby` 活跃）。

新增模型：
- `CnCNetChatScope` 枚举：`LobbyChannel`、`GameRoom`。
- `CnCNetChatMessage` 字段：
- `Scope`、`Channel`、`Sender`、`MessageType`（`User`、`Notice`、`System`）、`Timestamp`、`DisplayText`、`ColorId?`。

#### 2.2 发送路由
- 当活动房间存在且当前处于 `CnCNetGameLobby` 时，发送到房间频道。
- 否则发送到当前大厅频道。
- 通过 IRC 连接层保留颜色前缀行为。

#### 2.3 接收路由
- 同时订阅/处理两类频道 PRIVMSG：
- 当前大厅频道 -> 大厅消息流。
- 活动房间频道 -> 房间消息流。
- 房间内 CTCP/状态提示统一写入房间流，`MessageType=Notice`。

#### 2.4 斜杠命令层
- 新增与 DX 风格兼容的房间命令解析器：
- `/roll`、`/tunnelinfo`、`/changetunnel`、`/downloadmap`，以及适用的基础命令。
- 命令注册表包含 `HostOnly` 元数据。
- 未知命令在房间流输出帮助列表。

#### 2.5 UI 绑定
- `lbChatMessages` 按当前窗口/上下文绑定对应消息流。
- `tbChatInput` 的占位文案与可用状态随聊天域变化。
- 支持自动滚动与有界消息保留（策略与大厅一致）。

#### 2.6 房间待实现操作
- 将当前占位动作替换为真实流程：
- 隧道选择对话框 + 应用 + CHTNL 广播路径。
- 房间设置对话框 + 校验 + GSETTINGS 广播路径。

## 风险说明
- 若大厅与房间复用同一控件，输入路由可能混淆。
- 缩放后预览图上的指示器命中区域需与渲染坐标严格一致。
- 命令一致性建议分阶段推进，部分 DX 命令依赖地图分享子系统。
- CTCP/状态事件与聊天事件应解耦，避免重复入流。

## 检查清单

### Phase 0 - 对齐
- [ ] 固化 DX 参考行为与验收样例。
- [ ] 明确第一阶段必须覆盖的命令范围。

### Phase 1 - 小地图出生点指示器
- [x] 增加指示器 VM/状态结构。
- [x] 建立与 DX 对齐的 preview->marker 坐标换算。
- [x] 渲染指示器叠加层与占用视觉。
- [x] 接入本地点选出生点。
- [x] 接入房主分配/清除交互。（当前为启发式自动分配；完整上下文菜单仍为 polish）
- [x] 从玩家 + AI 槽位同步指示器状态。
- [x] 增加悬停标签与边界安全布局。（占用名 tooltip / 旁注）

### Phase 2 - 房间聊天核心
- [x] 增加聊天域模型（`LobbyChannel` vs `GameRoom`）。
- [x] 按窗口/上下文路由发送逻辑。
- [x] 按频道目标路由接收逻辑。
- [x] 在 `CnCNetGameLobby` 绑定房间聊天列表。
- [x] 将房间提示消息并入房间时间线。

### Phase 3 - 斜杠命令
- [x] 增加房间命令解析器与注册表。
- [x] 实现 host-only 权限校验与反馈。
- [x] 增加未知命令帮助输出。
- [x] 按优先级实现选定 DX 房间命令。（`/roll` `/hidemaps` `/showmaps` `/tunnelinfo` `/changetunnel`；`/downloadmap` 等仍属后续 polish）

### Phase 4 - 房间待实现操作
- [x] 实现隧道选择 UI 与端到端应用流程。
- [x] 实现房间设置 UI 与广播/应用流程。

### Phase 5 - 验证
- [ ] 房主/加入方手工测试：房间聊天双向收发。
- [ ] 房主/加入方手工测试：出生点分配/选择/清除同步。
- [ ] 重连/重进测试：房间聊天流连续性。
- [ ] 启动链路测试：START/PO/GO/Ready 流程无回归。

## 建议验收标准
- `CnCNetGameLobby` 中的房间聊天与频道大厅聊天彼此独立。
- Enter 键可根据当前上下文发送到正确目标频道。
- 出生点点击交互可更新槽位状态，并在需要时同步到远端。
- 非房主执行 host-only 命令时有明确拦截反馈。
- 现有频道大厅聊天与游戏启动流程保持不变。
