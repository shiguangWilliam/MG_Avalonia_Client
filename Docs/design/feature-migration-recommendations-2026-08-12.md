# 后续功能迁移：问题与修改建议报告

> **日期**：2026-08-12  
> **性质**：**只读评估 / 建议**，本文件对应工作**不修改代码**。  
> **前提**：Session 重构 + Global-state + MainWindow 拆分已完成（见 `refactor-acceptance-report-2026-08-12.md`）。  
> **范围**：LAN、读档房、Discord/QQ、WAF —— 相对 DXMainClient 的缺口、根因、建议改法与优先级。

---

## 0. 总览

| 主题 | 现状（Avalonia） | 相对 DX / 备注 | 建议优先级 |
|---|---|---|---|
| **LAN** | 已落地：`ILanSession` / UDP+TCP / spawn / UI 接线（对照 DX） | 需手工双端验收；细节可再对齐 | P1 验收 |
| **读档** | 本地 Load Game 已接线；CnCNet 读档密码/建房/加入闸门已落地 | LoadingLobby CTCP（OP/START）可再加深 | P1 加深协议 |
| **Discord / QQ** | 本轮不做 | — | 搁置 |
| **WAF** | 四套矩阵共 404 例：**401 绿 / 3 红**；见 `waf-test-findings-2026-08-12.md` | **暂不改生产**；先按 findings 定改方案 | P0 改方案再修 |

---

## 1. LAN（局域网）

### 1.1 问题

| 现象 | 证据 |
|---|---|
| 可打开 LAN 相关 INI 窗，但无网络 | 无 `UdpClient` / `LANBroadcast` / `HostedLANGame` 实现 |
| 开局拒绝 | `LobbyBehaviors`：*「Multiplayer in-game launch is not implemented for this lobby.」* |
| Session 预留未落地 | `Session/ILANGameSession.cs` 仅接口；`global-state-refactor.md` 同述「LAN 路径尚未完整」 |

DX 对照：`DXGUI/Multiplayer/LANLobby`、`LANGameLobby`、`LANLobbyBroadcastManager`、`LANPlayerManager` 等完整 UDP 栈。

### 1.2 修改建议

1. **实现 `LanGameSession : ILANGameSession`**  
   - 槽位 / 地图复用 Skirmish Session-aware 路径（已具备）。  
   - 增加 LAN 专属：房间广播、加入、锁房、开局信号。
2. **移植 DX `LANLobbyBroadcastManager` 语义**（UDP 发现 + 去重 + 心跳超时）。  
3. **启动**：复用 `GameLaunchService.TryLaunchLan` → 补真正的 LAN spawn 字段（目前退化为 Skirmish spawn）。  
4. **UI**：`MultiplayerLobbyBehaviors` 对非 CnCNet 分支从「仅 Navigate」改为绑定 `ILANGameSession`。  
5. **测试**：无真实网卡依赖的 UDP loopback 单测 + 一台双客户端手工验收。

### 1.3 风险

- 防火墙 / 多网卡选址与 DX 行为对齐成本高。  
- 勿与 CnCNet Tunnel 逻辑混用。

---

## 2. 读档房（本地 + 联机）

### 2.1 问题

| 类型 | 现状 | 证据 |
|---|---|---|
| **本地读档** | 主菜单 Load Game 未接线 | `MainMenuBehaviors`：*「Load Game — not wired」* |
| **联机读档房** | INI BasedOn 有映射，无 Session/协议 | `ClientEnvironment` 映射 `CnCNetGameLoadingLobby`；规则文档：读档房密码 `SHA1(spawnSG.ini GameID)` **未移植** |
| **LAN 读档房** | 同壳 | `LANGameLoadingLobby` 仅名字映射 |

DX 对照：`GameLoadingWindow`、`CnCNetGameLoadingLobby`、`LANGameLoadingLobby`、`GameLoadingLobbyBase`。

### 2.2 修改建议

**本地（P2）**

1. 移植 Saved Games 扫描（DX `SavedGame` / 目录约定）。  
2. 主菜单 `btnLoadGame` → 打开读档窗 / Overlay。  
3. 写 spawn / 启动对齐 DX 读档参数（非 `-SPAWN` 多人或单人读档路径需核对 MG）。

**联机读档（P1）**

1. 新建 `CnCNetGameLoadingSession`（或扩展 `ICnCNetGameSession` 的 Loading 态）。  
2. 移植 DX 读档房 CTCP / 密码：`SHA1(GameID from spawnSG.ini)[..10]`。  
3. START / 隧道端口与正常房共用校验（`CnCNetPortValidator` 已有，复用）。  
4. UI：LoadingLobby INI + Behaviors；房主选档、加入方同步。

### 2.3 风险

- 读档与正常房的 GAME 广播字段、列表过滤易混。  
- 存档兼容（Ares/Phobos 版本）需 MG 侧约定。

---

## 3. Discord / QQ

### 3.1 Discord — 问题

| 现象 | 证据 |
|---|---|
| 选项有勾选 | `chkDiscordIntegration` → `KnownOptionSettings` / Options bootstrap |
| **无运行时** | 无 `DiscordHandler` 类；上游说明在 `Docs/DiscordRichPresence.md`（XNA） |

DX：`DXMainClient/Domain/DiscordHandler.cs`（Discord RPC / 详细对局状态）。

### 3.2 Discord — 建议

1. 移植或用官方 Discord Game SDK / 旧 RPC 封装一层 `IDiscordPresence`。  
2. 订阅已有事件：`GameProcessStarted` / `Exited`、进入/离开房间、地图名（从 `IGameSession`）。  
3. 尊重 `UserINISettings` / `DiscordIntegration` 开关；默认关。  
4. 不在 IRC 线程直接调 SDK → 经 UI Dispatcher 或独立队列。

### 3.3 QQ — 问题与建议

- 本仓库 **无 QQ 互联 / 分享 SDK**；DX 官方客户端一般也不内置 QQ。  
- 若产品要「QQ 邀请 / 状态」：属 **MG 定制需求**，建议：  
  1. 明确渠道（QQ 互联开放平台 / 仅深链打开 QQ / 仅复制房间号）。  
  2. 做成可选 `ISocialShare` 插件，**不要**塞进 `ICnCNetSession`。  
  3. 与 FHSH / 反作弊无关，避免在注入 DLL 层做社交。

### 3.4 优先级建议

Discord = P3（体验增强）；QQ = 仅在有明确产品规格时开做。

---

## 4. WAF（入站过滤）

### 4.1 问题

WAF 已是 Avalonia **相对 DX 的增强能力**（`CnCNet/Waf/*`、Options 屏蔽名单、策略弹窗），但存在三类债：

| # | 问题 | 说明 |
|---|---|---|
| **W1 测试未进主验收** | 过滤回归刻意 `!~Waf`；历史上曾出现成批红 | 需单独修到绿并纳入 CI |
| **W2 接口未收口** | `IngressWaf` 在 `CnCNetSessionService`，不在 `ICnCNetSession` | `OptionsOverlayBehaviors` / MainWindow 仍 `CnCNetSessionService.Instance` 或 Adapter cast |
| **W3 规则/语义漂移风险** | `rules.default.json`、策略偏好、测试语料多文件并行演进 | 缺「规则版本 ↔ 测试语料」冻结说明 |

### 4.2 修改建议

**P0 — 测绿**

1. 单独跑全量 WAF 测试，按失败归类：规则期望变更 / 路径依赖 `GamePath` / 时序。  
2. 凡依赖 `AppState` / `ProgramConstants.GamePath` 的用例，统一经 `TempGameRoot.BindToProgramConstants()`。  
3. 修绿后取消 CI 对 WAF 的排除（或单独 CI job）。

**P1 — 接口化**

1. `ICnCNetSession`（或子接口 `ICnCNetIngressGuard`）暴露：  
   `ICnCNetIngressWaf? IngressWaf { get; }`  
   以及可选 `IDisposable` 生命周期。  
2. `OptionsOverlayBehaviors` / MainWindow WAF UI 全部改 `AppState.CnCNet` / 注入，删掉 `CnCNetSessionService.Instance`。  
3. `ShutdownService` 的 `Dispose` 经接口或显式 `ICnCNetSessionLifetime`。

**P2 — 产品化**

1. 规则包版本号写入日志与 FHSH 无关的独立诊断。  
2. 文档：默认策略、用户可改范围、误杀申诉路径。  
3. 与「好友列表未移植」解耦：WAF 屏蔽 ≠ 好友白名单（`CnCNetPrivateMessagePolicy` 仍注明 Friend list not ported）。

### 4.3 风险

- 过严规则会误伤正常 GAME/GO CTCP → 必须有语料回归。  
- 过松则失去增强意义。  
- 勿在重构报告验收里用「排除 WAF」掩盖红态 —— 应用本报告 P0 单独跟踪。

---

## 5. 建议实施顺序（功能迁移路线图）

```
P0  WAF 测试修绿 + 纳入 CI
P1  WAF IngressWaf 上 ICnCNetSession
P1  联机读档房（协议 + UI + 密码）
P1  LAN 会话 + UDP 广播 + LAN spawn（若产品需要）
P2  本地读档窗
P2  好友列表（PM Policy 依赖）
P3  Discord Rich Presence
P3  QQ（仅在有规格时）
```

每项建议独立 PR；**不要**与架构重构混提。

---

## 6. 与「后续需求不动代码」的边界

| 允许 | 不允许（按你的当前指令） |
|---|---|
| 本文档更新、验收清单、工时估算 | 实现 LAN / 读档 / Discord / QQ |
| 开 Issue / 拆 PR 描述 | 为「顺手」改 WAF 规则行为 |
| 架构薄修（仅当明确再授权重构） | 把功能迁移塞进重构 PR |

---

## 7. 参考索引

| 路径 | 用途 |
|---|---|
| `Session/ILANGameSession.cs` | LAN 接口预留 |
| `IniUi/Behaviors/MainMenuBehaviors.cs` | Load Game / Ranked 未接线 |
| `IniUi/Behaviors/LobbyBehaviors.cs` | LAN 开局未实现；Preset 未实现 |
| `CnCNet/CnCNetPrivateMessagePolicy.cs` | 好友列表未移植 |
| `Docs/DiscordRichPresence.md` | DX Discord 说明 |
| `CnCNet/Waf/*` + `ClientAvalonia.Tests/CnCNet/*Waf*` | WAF 实现与测试 |
| `Docs/design/refactor-acceptance-report-2026-08-12.md` | 本轮重构验收 |
