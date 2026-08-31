# 重构验收报告：Global-state 收口 + MainWindow 拆分

> **日期**：2026-08-12  
> **范围**：`ClientAvalonia` + `ClientAvalonia.Tests`  
> **前置**：Phase 1–6 Session/Player 统一已完成（见 `phase6-cleanup-report.md`）  
> **验收测试**：过滤 WAF / LiveIrc / MgAndLnod 后 **808 通过 / 0 失败 / 0 跳过**

---

## 0. 一句话结论

本轮把两项搁置已久的架构债落地并验收通过：

1. **Global-state L1 收口**：生产读路径经 `AppState` → `EnvironmentServices` → 四域接口；可变全局直读基本清零。  
2. **MainWindow 拆分**：`MainWindow.axaml.cs` **2480 → 847 行**，职责拆入 7 个 Controllers。

Session 统一（Phase 1–6）+ 本轮 Global-state + MainWindow = **当前规划内的架构重构已完成并通过过滤回归**。

---

## 1. 做了什么

### 1.1 Global-state（Phase A）

| 项 | 结果 |
|---|---|
| 新增 `GlobalState/AppState.cs` | `Environment` / `Configuration` / `CnCNet` / `Resources` / `Updater` / `Colors` 统一入口 |
| 扩展 `IGameConfiguration` | 高频成员 + `Legacy` escape hatch |
| 批量迁移 | **71** 个生产文件改走 `AppState.*` |
| `AppState` 未注册时 | `TryResolve` 回退生产 Adapter（单测无需处处 Register） |
| `TempGameRoot` | Mock 与 `ProgramConstants.PLAYERNAME` / `GAME_VERSION` 动态同步 |

**收口后度量（生产代码，排除 Adapter / Bootstrap）**：

| 静态入口 | 剩余 | 说明 |
|---|---|---|
| `ProgramConstants.{GamePath,PLAYERNAME,GAME_VERSION,…}` 可变读 | **0 读**；**写**保留在 `PlayerNameSettings` / IRC nick（只写源） | 符合「接口只读、写仍落 ProgramConstants」 |
| `ClientConfiguration.Instance` | **0**（接口注释提及除外） | 读走 `AppState.Configuration` / `.Legacy` |
| `CnCNetSessionService.Instance` | **1**（`ShutdownService.Dispose`）+ WAF Options UI 若干 | Dispose / IngressWaf 尚未上 `ICnCNetSession` |
| `AppState.*` 调用 | **~181** | 新标准路径 |

**有意保留的 static**：

- 真正常量：`SPAWNER_SETTINGS`、`SPAWNMAP_INI`、`CNCNET_PROTOCOL_REVISION`、`BASE_RESOURCE_PATH` 等  
- 进程标志：`IsInGame` / `IsLaunchingGame`  
- Bootstrap 数据源：`PreStartup` / `Startup` / `ClientCoreBootstrap` / `*Adapter`  
- 玩家名**写入**：仍写 `ProgramConstants.PLAYERNAME`（`IGameEnvironment.PlayerName` 只读）

### 1.2 MainWindow 拆分（Phase B）

| 文件 | 行数 | 职责 |
|---|---|---|
| `MainWindow.axaml.cs` | **847**（原 2480） | 壳：生命周期、导航栈、PART_*、设置、顶栏、私信、WAF 弹窗 |
| `Controllers/MainWindowContext.cs` | 203 | 共享依赖 + 回调（无 PART_*） |
| `Controllers/OverlayHostController.cs` | 344 | Floating / 建房 / Tunnel / Settings overlay |
| `Controllers/CnCNetLobbyController.cs` | 209 | 列表广播、加入、大厅 StateChanged |
| `Controllers/CnCNetGameRoomController.cs` | 378 | 房间玩家 UI、GO、GameStarting |
| `Controllers/LobbyMapController.cs` | 526 | 地图列表 / 收藏 / start markers / ApplyLobbyData |
| `Controllers/CampaignOverlayController.cs` | 48 | 战役 overlay |
| `Controllers/GameLaunchController.cs` | 205 | Skirmish / Campaign / CnCNet 启动 |

---

## 2. 验收

```
dotnet test ClientAvalonia.Tests/ClientAvalonia.Tests.csproj
  -p:DisableGitVersionTask=true -p:GitVersion_MsBuildTask_Disabled=true
  --filter "FullyQualifiedName!~Waf&FullyQualifiedName!~LiveIrc&FullyQualifiedName!~CnCNetMgAndLnod"

已通过! - 失败: 0，通过: 808，已跳过: 0
```

| 检查项 | 结果 |
|---|---|
| `ClientAvalonia` 编译 | ✅ |
| Session / Binding / Phase 相关 | ✅（含于 808） |
| MainWindow 行数 ≤ 1200 | ✅ 847 |
| Controllers ≥ 4 | ✅ 7 |
| 行为等价（无功能迁移） | ✅ 本轮仅重构 |

**未纳入本验收过滤的已知红项**（非本轮引入）：WAF 套件、Live IRC、`CnCNetMgAndLnodJoinIntegrationTests` —— 见后续功能报告。

---

## 3. 重构完成度总表

| 轨道 | 状态 |
|---|---|
| Phase 1–6 Session / Player 统一 + 删 `LobbyPlayerState` | ✅ 完成 |
| Global-state L1 生产读路径收口 | ✅ 完成 |
| MainWindow → Controllers | ✅ 完成（壳仍保留导航/PART/WAF UI） |
| LAN / 读档 / Discord / QQ 功能迁移 | ❌ **非本轮**（见功能报告） |
| WAF 测试修绿 / IngressWaf 上接口 | ❌ **非本轮**（见功能报告） |
| L2 DI 容器 | ❌ 有意不做（设计文档建议跳过） |

---

## 4. 残留技术债（重构范畴内、可后续薄修）

1. `ICnCNetSession` 尚未暴露 `IngressWaf` / `Dispose` → Options/Shutdown 仍碰 `CnCNetSessionService.Instance`  
2. MainWindow 壳仍 ~847 行：导航栈、顶栏、私信、WAF 弹窗可再拆  
3. `AppState` 未注册时回退 Adapter：方便测试，但掩盖「忘记 Register」；可逐步改为测试夹具强制 Register  
4. 注释中仍有历史 `LobbyPlayerState` / `ClientConfiguration.Instance` 字样，可择机 scrub  

---

## 5. 总评

**规划内架构重构：已完成并验收通过。**  
功能缺口（LAN、读档房、Discord/QQ、WAF）不属于本轮重构，单独见《后续功能迁移问题与建议报告》。
