# 全局可变状态重构设计文档

> **状态**：设计稿 v3（§12 已拍板，L1 已落地）。本文件提供域建模、接口 / 抽象类定义、迁移路径与继承层次评估；实施细节见 `architecture-evaluation-l1.md`。
> **修订记录**：
>
> - **v3（2026-07-19）**—— 四大域建模；资源全接口化；`IGameSession` 树；`CnCNetGameRoomSession` 直接实现 `ICnCNetGameSession`；`Resolve` 未注册抛异常；去掉 L0；§12 拍板后落地。
> - **v2（2026-07-19）**—— 移除短期设计；中文注释；继承层次评估；同步后补功能暴露的全局状态问题。

## 目录

1. [现状盘点](#1-现状盘点)
2. [设计目标](#2-设计目标)
3. [重构策略](#3-重构策略)
4. [域驱动建模总览](#4-域驱动建模总览)
5. [接口设计](#5-接口设计)
6. [继承层次评估](#6-继承层次评估)
7. [EnvironmentServices 服务定位器](#7-environmentservices-服务定位器)
8. [迁移路径](#8-迁移路径)
9. [后补功能改造清单](#9-后补功能改造清单)
10. [不推荐做的事](#10-不推荐做的事)
11. [验收标准](#11-验收标准)
12. [待确认问题](#12-待确认问题)
13. [关键设计决策汇总](#13-关键设计决策汇总)

---

## 1. 现状盘点

全局可变状态散布在以下 7 个静态入口：


| 入口                              | 类型           | 主要可变字段                                                                                                               | 用途           | 串扰风险   |
| ------------------------------- | ------------ | -------------------------------------------------------------------------------------------------------------------- | ------------ | ------ |
| `ProgramConstants`              | static class | `LocalGame`, `PLAYERNAME`, `GamePath`, `GAME_VERSION`, `HostedGameRoot`, `RESOURCES_DIR`, `AI_PLAYER_NAMES`, `TEAMS` | 全局游戏常量       | **极高** |
| `ClientConfiguration.Instance`  | singleton    | `LocalGame`, `SettingsIniName`, `ModMode`, ...                                                                       | INI 配置访问     | 高      |
| `CnCNetSession.Instance`        | singleton    | `LocalNick`, `Connection`, `ActiveGameRoom`, `GameRoom`, `TunnelSorter`                                              | IRC 连接 + 房间  | 高      |
| `CnCNetSessionService.Instance` | singleton    | 包 `CnCNetSession.Instance`，加线程调度                                                                                     | UI 入口 facade | 中（已封装） |
| `Updater`                       | static class | `CustomComponents`, `GameVersion`, `OnLocalFileVersionsChecked`                                                      | 版本检查 / 自更新   | 中      |
| `GameResourceCatalog.Instance`  | singleton    | `Maps`, `GameModes`, `Missions`（sealed，不可继承）                                                                         | 地图 / mode 缓存 | 中      |
| `Logger`                        | static class | log 文件路径                                                                                                             | 日志           | 低（只写）  |


**实际观察到的串扰**：测试套件中的 `LnodWorkspace_SynthesizesCncnetLnodChannels` 偶发失败，根因就是 `ProgramConstants.LocalGame` 在多个测试间残留 `"mg"` 值。

### 1.1 后补功能引入的新问题

最近落地的三个功能都没有新建静态可变状态，但它们的接入点暴露了既有单例的耦合面，**应当在 L1 阶段一并改造**：


| 后补功能                   | 暴露的耦合点                                                                           | 文件 / 行                                  | 待迁移到接口                                      |
| ---------------------- | -------------------------------------------------------------------------------- | --------------------------------------- | ------------------------------------------- |
| **Auto-Refresh**       | `LobbyPlayerBindingApplier.WireSlot` 闭包内调 `CnCNetSession.Instance.GameRoom`      | `LobbyPlayerBindingApplier.cs:414, 439` | `ICnCNetGameSession`（经 `ActiveGameRoom`）     |
| **Auto-Refresh**       | `OnLobbySlotsMutated` 调 `CnCNetSessionService.Instance`                          | `MainWindow.axaml.cs:1182` 区域           | `ICnCNetGameSession` 注入                     |
| **Auto AI Slots**      | `DefaultAiSlotPolicy.AutoFillToMapCapacity` 读 `ProgramConstants.PLAYERNAME`      | `DefaultAiSlotPolicy.cs:33`             | `IGameEnvironment.PlayerName`               |
| **Auto AI Slots**      | `LobbyPlayerState` 读 `ProgramConstants.PLAYERNAME` / `AI_PLAYER_NAMES` / `TEAMS` | `LobbyPlayerState.cs:34,36,47,48,65`    | `IGameEnvironment` + Session.Slots          |
| **Auto AI Slots**      | `DefaultAiSlotPolicy` 读 `MultiplayerColorCatalog.Load()` 静态缓存                    | `DefaultAiSlotPolicy.cs:42`             | `IMultiplayerColorCatalog`                  |
| **Low-Latency Tunnel** | `CnCNetSession.TunnelSorter` 作为单例的可变字段                                           | `CnCNetSession.cs:67`                   | 由 `ICnCNetSession.TunnelSorter` 暴露，本身无需独立接口 |
| **Low-Latency Tunnel** | `IcmpTunnelPinger` / `TunnelMaintenanceLoop` / `TunnelSorter` 写 `Logger.Log`     | 多处                                      | 不抽象（见 §10）                                  |


这三个功能本身**没有引入新的 `static` 可变字段**，这是后补设计正确性的体现：Action 基类天然是实例类型，Sorter / Prewarmer / Maintenance 都是普通 sealed class，状态通过构造注入。**唯一的隐患是接入点（MainWindow / BindingApplier / DefaultAiSlotPolicy）通过单例获取依赖**，迁移到 L1 接口后即可消除。

另有结构性问题（v3 新增识别）：

| 问题 | 现状 | v3 目标 |
| ---- | ---- | ------ |
| `LobbyPlayerState` 混 Skirmish + Multiplayer | `LobbyPlayerMode` 枚举切换两套逻辑 | 拆为 `ISkirmishSession` / `ICnCNetGameSession`，分支消失 |
| `LobbyActionContext` 持 `Player` + `CnCNet` 两字段 | Action 需自行判断模式 | 持单个 `ISkirmishSession`，自动适配子类 |
| `ChangeMapAction` 接 `MapEntry` 具体类 | 无法 mock / 无法接在线下载的 map | 接 `IMapResource` |
| `GameResourceCatalog` sealed + 返回具体 DTO | 无法 mock；无元数据契约 | `IResourceCatalog` 返回 `IMapResource` 等接口 |
| `CnCNetSession` 同时管网络与房间会话 | 职责混淆 | 网络层保留；游戏会话走 `ActiveGameRoom : ICnCNetGameSession` |

---

## 2. 设计目标

1. **测试隔离**：单元测试能在不污染全局的情况下设置 `LocalGame = "lnod"` 等局部状态。
2. **依赖清晰**：所有可变全局依赖必须显式注入（构造函数 / 方法参数），不能用 `Instance` 隐式获取。
3. **不破坏现有调用点**：300+ 处使用 `ProgramConstants.LocalGame` 的代码不可能一次性改完，必须有兼容桥接。
4. **保留 singleton 性能优势**：生产路径上仍走单例（避免每次都走接口调度的虚函数开销）。
5. **跨线程安全**：多线程访问的入口（如 `CnCNetSession`）保留锁。
6. **资源可演进**（v3 新增）：Map / Mission / GameMode 具备完整元数据契约，为在线更新、增量包、mod 自定义扩展铺路。
7. **会话语义清晰**（v3 新增）："玩家语义上的一场游戏"与"IRC 网络层"解耦，Skirmish / CnCNet / LAN / Mission 用接口继承表达关系。

---

## 3. 重构策略


| 层                | 范围                                                             | 成本    | 收益                   |
| ---------------- | -------------------------------------------------------------- | ----- | -------------------- |
| **L1** 接口抽象      | 抽 Environment / Resource / Session / Network 四域接口，UI 层依赖接口     | ~3 周  | 测试可注入 mock；后补功能接入点解耦；资源/会话可演进 |
| **L2** DI 容器（可选） | 引入 `Microsoft.Extensions.DependencyInjection`，singleton 通过容器管理 | +2 周  | 完整解耦，但收益边际           |


**建议先做 L1，跳过 L2**。L2 的 DI 容器对 desktop 客户端收益有限（启动一次性注入、运行时无热重载需求），且会大幅改动 `Program.cs` / `App.axaml.cs`。

> **v3 明确**：旧版本的"L0 短期缓解"（`[Collection]` 标注 + `TempGameRoot.Dispose` 补 reset）已经被采纳实施，**不再列入本设计**。

---

## 4. 域驱动建模总览

### 4.1 四大域

| 域 | 职责 | 核心接口 | 不做什么 |
| -- | ---- | -------- | -------- |
| **Environment** | 运行时环境（game root、玩家名、AI/队伍名） | `IGameEnvironment` | 不持会话状态、不持资源列表 |
| **Resource** | 静态/半静态游戏资源（地图、任务、模式、颜色目录）及元数据 | `IResource` / `IResourceCatalog` / `IResourceManifest` | 不持玩家槽位、不持网络连接 |
| **Session** | 玩家语义上的一场游戏（地图选择、槽位、选项、状态机） | `IGameSession` 树 | 不持 IRC 连接、不扫描磁盘 |
| **Network** | CnCNet IRC / Tunnel / 广播 / 心跳 | `ICnCNetSession` | 不实现 `IGameSession`；通过 `ActiveGameRoom` 暴露当前游戏会话 |

### 4.2 域边界 ASCII 图

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          ClientAvalonia 进程                              │
│                                                                          │
│  ┌─────────────────────┐     ┌──────────────────────────────────────┐   │
│  │   Environment 域     │     │          Resource 域                  │   │
│  │                     │     │                                      │   │
│  │  IGameEnvironment   │     │  IResource                           │   │
│  │  IGameConfiguration │     │    ├─ IMapResource                   │   │
│  │  IUpdater           │     │    ├─ IMissionResource               │   │
│  │                     │     │    └─ IGameModeResource              │   │
│  │  (启动期固定 /        │     │  IResourceCatalog                   │   │
│  │   INI 配置 / 自更新)  │     │  IResourceManifest                  │   │
│  └──────────┬──────────┘     │  IMultiplayerColorCatalog            │   │
│             │                └──────────────────┬───────────────────┘   │
│             │ 注入路径 / 玩家名 / 颜色目录          │ 注入 Map/Mission      │
│             ▼                                  ▼                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                        Session 域                                 │   │
│  │                                                                  │   │
│  │              IGameSession                                        │   │
│  │               ├─ ISkirmishSession                                │   │
│  │               │    ├─ ICnCNetGameSession  ←── ActiveGameRoom      │   │
│  │               │    └─ ILANGameSession                            │   │
│  │               └─ IMissionSession                                 │   │
│  │                                                                  │   │
│  │  IPlayerSlot / IGameOptionsState                                 │   │
│  └──────────────────────────────▲───────────────────────────────────┘   │
│                                 │                                       │
│                                 │ exposes ActiveGameRoom                │
│  ┌──────────────────────────────┴───────────────────────────────────┐   │
│  │                        Network 域                                 │   │
│  │                                                                  │   │
│  │  ICnCNetSession  (= CnCNetSession 适配)                           │   │
│  │    ├─ IRC 连接 / 心跳 / 重连                                      │   │
│  │    ├─ 频道管理 / 玩家计数                                         │   │
│  │    ├─ Tunnel list + TunnelSorter                                 │   │
│  │    ├─ Hosted game 广播                                           │   │
│  │    └─ ActiveGameRoom : ICnCNetGameSession?   ← 当前活动游戏会话    │   │
│  │                                                                  │   │
│  │  ★ CnCNetSession 不实现 IGameSession                              │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

### 4.3 域间依赖规则

```
Environment  ←── 被所有域读取（只读视图）
Resource     ←── 被 Session 引用（Map / Mission）
Session      ←── 被 UI / Action 操作；被 Network 容器持有
Network      ←── 持有 ActiveGameRoom，但不被 Session 反向依赖网络细节
```

禁止：

- Resource 依赖 Session / Network
- Session 直接读 `ProgramConstants` / `CnCNetSession.Instance`
- Network 实现 `IGameSession`（网络层 ≠ 游戏会话）

---

## 5. 接口设计

### 5.1 环境层

#### 5.1.1 `IGameEnvironment`（替代 `ProgramConstants` 的读访问）

```csharp
// 新文件：ClientAvalonia/Environment/IGameEnvironment.cs
namespace ClientAvalonia.Environment;

/// <summary>
/// 游戏运行环境的只读视图。
///
/// 作用：替代散落在 300+ 处的 ProgramConstants.XXX 静态读取。所有需要
/// 知道"当前是哪个游戏 / 游戏根目录在哪 / 玩家名是什么"的代码都应通过
/// 此接口获取，而不是直接读 static 字段。这样：
///   1. 单元测试可以注入 MockGameEnvironment，不用污染全局
///   2. 多 mod 启动器（multi-mod launcher）未来可以在运行时切换 environment
///      而不是改 ProgramConstants 静态字段
///   3. 接口的只读性质避免了"在 lobby 里被无意改写 LocalGame"之类的 bug
/// </summary>
public interface IGameEnvironment
{
    /// <summary>
    /// 当前游戏标识符，例如 "mg" / "yr" / "lnod" / "qec"。
    /// 对应 ProgramConstants.LocalGame。
    /// 决定 Resources 子目录、mod 命名空间、IRC 频道路由。
    /// </summary>
    string LocalGame { get; }

    /// <summary>
    /// 已解析的游戏根目录绝对路径（启动时由 registry / working dir 决定）。
    /// 对应 ProgramConstants.GamePath。
    /// 所有相对路径（INI、Maps、Resources）都以此为根。
    /// </summary>
    string GamePath { get; }

    /// <summary>
    /// Resources 目录 = GamePath/Resources。
    /// 对应 ProgramConstants.GetResourcePath()。
    /// 大多数 mod 资源、INI 都在此目录下。
    /// </summary>
    string ResourcesPath { get; }

    /// <summary>
    /// Base 资源目录 = ResourcesPath/Base。
    /// 对应 ProgramConstants.GetBaseResourcePath()。
    /// 跨 mod 共享的兜底资源（GameOptions.ini、MPMaps.ini 模板等）放这里。
    /// </summary>
    string BaseResourcesPath { get; }

    /// <summary>
    /// 当前玩家显示名（来自 Settings.ini 或 IRC nick）。
    /// 对应 ProgramConstants.PLAYERNAME。
    /// 用于 lobby 槽位、保存设置、IRC nick 注册。
    /// DefaultAiSlotPolicy 填充 Slot[0] 时从此字段取名。
    /// </summary>
    string PlayerName { get; }

    /// <summary>
    /// 解析后的游戏版本号，如 "1.0.4.2"。ModMode 下可能是 "N/A"。
    /// 对应 ProgramConstants.GAME_VERSION。
    /// 用于联机兼容性检查、自更新流程。
    /// </summary>
    string GameVersion { get; }

    /// <summary>
    /// AI 玩家名称列表（如 ["Easy AI", "Medium AI", "Hard AI"]）。
    /// 对应 ProgramConstants.AI_PLAYER_NAMES。
    /// Lobby 的 AI 槽位下拉、Skirmish 默认填充都引用此列表。
    /// </summary>
    IReadOnlyList<string> AiPlayerNames { get; }

    /// <summary>
    /// 队伍名称列表（如 ["A", "B", "C", "D"]）。
    /// 对应 ProgramConstants.TEAMS。
    /// Lobby 的队伍下拉引用此列表。
    /// </summary>
    IReadOnlyList<string> TeamNames { get; }
}
```

#### 5.1.2 `GameEnvironmentBase`（抽象基类）

见 [§6.1](#61-环境树环境树用抽象基类)。核心：`ResourcesPath` / `BaseResourcesPath` 为派生属性。

---

### 5.2 资源层（★ v3 全新）

资源层的目标不是"保留现有字段的半截方案"，而是：**Map / Mission / GameMode 全部抽接口，并含完整元数据**（hash / version / origin / size），为在线更新、增量包、mod 自定义扩展铺路。旧 DTO（`MapEntry` 等）保留为默认实现，允许渐进迁移。

#### 5.2.1 `IResource` 基接口（元数据契约）

```csharp
// 新文件：ClientAvalonia/Domain/Resources/IResource.cs

/// <summary>
/// 所有游戏资源的公共元数据契约。
///
/// 作用：统一 Map / Mission / GameMode（及未来 mod 扩展资源）的身份标识、
/// 显示名、文件路径、内容 hash、版本与来源。在线更新 / 增量包 / 完整性校验
/// 都依赖此契约，而不是各 DTO 各自发明一套字段。
/// </summary>
public interface IResource
{
    /// <summary>
    /// 逻辑标识。Map 通常为 Sha1；Mission 为 SectionName；GameMode 为 Name。
    /// 用于 catalog 索引、增量 diff 的主键匹配。
    /// </summary>
    string LogicalId { get; }

    /// <summary>
    /// UI 显示名（已本地化）。对应 MapEntry.DisplayName / MissionEntry.DisplayName /
    /// GameModeEntry.DisplayName。
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 未本地化名（用于 hash、匹配、跨语言协议）。对应 MapEntry.UntranslatedName /
    /// GameModeEntry.UntranslatedUIName。Mission 可回退到 SectionName。
    /// </summary>
    string UntranslatedName { get; }

    /// <summary>
    /// 绝对路径或相对 GamePath 的路径。对应 MapEntry.CompleteFilePath /
    /// MapEntry.BaseFilePath；Mission 对应 Scenario 文件路径。
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 内容 hash（在线更新校验用）。对应 MapEntry.Sha1。
    /// Mission / GameMode 若无现成 hash，加载期计算或留空字符串。
    /// </summary>
    string Sha1 { get; }

    /// <summary>
    /// 文件大小（字节）。现有 DTO 无此字段；加载期从 FileInfo 填充。
    /// 增量包选型与进度条依赖此值。
    /// </summary>
    long SizeBytes { get; }

    /// <summary>
    /// 资源来源。对应 MapEntry.IsOfficial / IsCustom 的语义扩展。
    /// Official = 官方包；Custom = 用户自定义；ModExtension = mod 扩展；
    /// Downloaded = 在线下载缓存。
    /// </summary>
    ResourceOrigin Origin { get; }

    /// <summary>
    /// 资源版本（在线更新增量包用）。现有 DTO 无此字段；默认 (0,0,0,0)。
    /// </summary>
    VersionInfo Version { get; }

    /// <summary>
    /// official 资源不允许用户修改。通常 Origin == Official 时为 true。
    /// </summary>
    bool IsReadOnly { get; }
}

/// <summary>资源来源枚举。</summary>
public enum ResourceOrigin
{
    /// <summary>官方发行包内资源。</summary>
    Official,

    /// <summary>用户自定义（如 Custom Maps 目录）。</summary>
    Custom,

    /// <summary>Mod 扩展包提供。</summary>
    ModExtension,

    /// <summary>在线下载 / 增量更新缓存。</summary>
    Downloaded,
}

/// <summary>资源版本四元组。</summary>
public sealed record VersionInfo(int Major, int Minor, int Build, int Revision);
```

#### 5.2.2 `IMapResource` / `IMissionResource` / `IGameModeResource`

```csharp
/// <summary>
/// 多人 / 遭遇战地图资源。
///
/// 作用：替代直接依赖 MapEntry 具体类。字段从 ClientAvalonia/Domain/MapEntry.cs
/// 反推；ChangeMapAction / MapListBindingApplier / spawn 写入均应依赖此接口。
/// 默认实现：MapEntry : IMapResource（渐进迁移，旧代码不破坏）。
/// </summary>
public interface IMapResource : IResource
{
    /// <summary>所属游戏模式名列表。对应 MapEntry.GameModes。</summary>
    IReadOnlyList<string> GameModes { get; }

    /// <summary>最少玩家数。对应 MapEntry.MinPlayers。</summary>
    int MinPlayers { get; }

    /// <summary>最多玩家数。对应 MapEntry.MaxPlayers。DefaultAiSlotPolicy 据此填充。</summary>
    int MaxPlayers { get; }

    /// <summary>是否强制 MaxPlayers 上限。对应 MapEntry.EnforceMaxPlayers。</summary>
    bool EnforceMaxPlayers { get; }

    /// <summary>是否仅多人可用。对应 MapEntry.MultiplayerOnly。</summary>
    bool MultiplayerOnly { get; }

    /// <summary>是否自定义地图。对应 MapEntry.IsCustom（与 Origin 互补，保留兼容语义）。</summary>
    bool IsCustom { get; }

    /// <summary>预览图相对路径。对应 MapEntry.PreviewRelativePath。</summary>
    string PreviewRelativePath { get; }

    /// <summary>附加 INI 名。对应 MapEntry.ExtraIniName。</summary>
    string ExtraIniName { get; }

    /// <summary>
    /// 起点 waypoint 原始 token。对应 MapEntry.Waypoints
    ///（MPMaps.ini Waypoint0..7 或自定义 [Waypoints]）。
    /// </summary>
    IReadOnlyList<string> Waypoints { get; }

    /// <summary>TDRA 地图原点 X。对应 MapEntry.MapX。</summary>
    int MapX { get; }

    /// <summary>TDRA 地图原点 Y。对应 MapEntry.MapY。</summary>
    int MapY { get; }

    /// <summary>TDRA 地图宽度。对应 MapEntry.MapWidth。</summary>
    int MapWidth { get; }

    /// <summary>TDRA 地图高度。对应 MapEntry.MapHeight。</summary>
    int MapHeight { get; }

    /// <summary>Isometric ActualSize（4 CSV ints）。对应 MapEntry.ActualSize。</summary>
    IReadOnlyList<string> ActualSize { get; }

    /// <summary>Isometric LocalSize（4 CSV ints）。对应 MapEntry.LocalSize。</summary>
    IReadOnlyList<string> LocalSize { get; }
}

/// <summary>
/// 战役 / 任务资源。
///
/// 作用：替代直接依赖 MissionEntry。字段从 ClientAvalonia/Domain/MissionEntry.cs
/// 反推。ModMetadata 预留 mod 自定义扩展点（战役脚本参数、解锁条件等）。
/// 默认实现：MissionEntry : IMissionResource。
/// </summary>
public interface IMissionResource : IResource
{
    /// <summary>INI section 名（逻辑主键）。对应 MissionEntry.SectionName。</summary>
    string SectionName { get; }

    /// <summary>任务描述文本。对应 MissionEntry.Description。</summary>
    string Description { get; }

    /// <summary>场景地图文件名。对应 MissionEntry.Scenario。空 = UI 分组标题行。</summary>
    string Scenario { get; }

    /// <summary>阵营显示名。对应 MissionEntry.SideName。</summary>
    string SideName { get; }

    /// <summary>阵营索引。对应 MissionEntry.Side。</summary>
    int Side { get; }

    /// <summary>所属战役 ID。对应 MissionEntry.CampaignId（-1 = 无）。</summary>
    int CampaignId { get; }

    /// <summary>是否启用。对应 MissionEntry.Enabled。</summary>
    bool Enabled { get; }

    /// <summary>是否需要资料片。对应 MissionEntry.RequiredAddon。</summary>
    bool RequiredAddon { get; }

    /// <summary>是否允许盟友建筑。对应 MissionEntry.BuildOffAlly。</summary>
    bool BuildOffAlly { get; }

    /// <summary>玩家是否始终普通难度。对应 MissionEntry.PlayerAlwaysOnNormalDifficulty。</summary>
    bool PlayerAlwaysOnNormalDifficulty { get; }

    /// <summary>是否为 UI 分组标题行（Scenario 为空）。对应 MissionEntry.IsHeader。</summary>
    bool IsHeader { get; }

    /// <summary>
    /// Mod 扩展元数据（战役 mod 自定义键值）。现有 MissionEntry 无此字段；
    /// 默认空字典。未来 mod 可通过此字典传递解锁条件、脚本参数等。
    /// </summary>
    IReadOnlyDictionary<string, object> ModMetadata { get; }
}

/// <summary>
/// 游戏模式资源。
///
/// 作用：替代直接依赖 GameModeEntry。字段从 ClientAvalonia/Domain/GameModeEntry.cs
/// 反推。默认实现：GameModeEntry : IGameModeResource。
/// </summary>
public interface IGameModeResource : IResource
{
    /// <summary>模式内部名（逻辑主键）。对应 GameModeEntry.Name。</summary>
    string Name { get; }

    /// <summary>未本地化 UI 名。对应 GameModeEntry.UntranslatedUIName。</summary>
    string UntranslatedUIName { get; }

    /// <summary>地图代码 INI 名。对应 GameModeEntry.MapCodeIniName。</summary>
    string MapCodeIniName { get; }

    /// <summary>是否仅多人。对应 GameModeEntry.MultiplayerOnly。</summary>
    bool MultiplayerOnly { get; }
}
```

#### 5.2.3 `IResourceManifest`（逻辑服务）

```csharp
/// <summary>
/// 资源清单与在线更新逻辑服务。独立于 IResource 本身。
///
/// 作用：hash 校验、增量 diff、在线更新检查与应用。当前代码尚无对应实现；
/// 本接口为未来"增量包 / 在线更新"预留契约，L1 阶段可先提供 NoOp 适配器
///（VerifyHash 恒 true、CheckForUpdates 返回空），避免阻塞主路径。
/// </summary>
public interface IResourceManifest
{
    /// <summary>校验资源内容 hash 是否与 Sha1 字段一致。</summary>
    bool VerifyHash(IResource resource);

    /// <summary>
    /// 计算 baseline → current 的增量集合（新增 / 变更 / 删除由调用方解释）。
    /// 用于增量包生成与客户端差量更新。
    /// </summary>
    IReadOnlyList<IResource> ComputeDiff(
        IReadOnlyList<IResource> baseline,
        IReadOnlyList<IResource> current);

    /// <summary>检查是否有可用更新（异步）。</summary>
    Task<UpdateResult> CheckForUpdatesAsync(CancellationToken ct);

    /// <summary>对单个资源应用增量更新（异步）。</summary>
    Task<UpdateResult> ApplyIncrementalUpdateAsync(
        IResource target,
        CancellationToken ct);
}

/// <summary>更新操作结果。</summary>
public sealed record UpdateResult(bool Success, IReadOnlyList<IResource> Updated);
```

#### 5.2.4 `IMultiplayerColorCatalog`（静态资源目录）

颜色**目录**独立于颜色**分配状态**：

- 目录：`IMultiplayerColorCatalog`（有哪些颜色可选）
- 分配状态：`IPlayerSlot.ColorIndex`（Session 内承载）

```csharp
// 新文件：ClientAvalonia/Domain/IMultiplayerColorCatalog.cs

/// <summary>
/// 多人游戏颜色目录接口。
///
/// 作用：后补的 DefaultAiSlotPolicy.AutoFillToMapCapacity 调用
/// MultiplayerColorCatalog.Load() 来确定 ColorIndex 上限。当前 Load() 是
/// static method + 静态缓存，跨测试会污染。抽接口后，测试可注入
/// 一个稳定的 8 色目录，避免缓存串扰。
///
/// 注意：本接口只提供"颜色有哪些"；具体槽位选了哪个颜色由
/// IPlayerSlot.ColorIndex 在 Session 内承载，不要把分配状态塞进目录。
/// </summary>
public interface IMultiplayerColorCatalog
{
    /// <summary>所有多人游戏颜色（按 GameOptions.ini [MPColors] 顺序）。</summary>
    IReadOnlyList<MultiplayerColorEntry> Load();
}
```

#### 5.2.5 `IResourceCatalog`（替代 `GameResourceCatalog.Instance`）

```csharp
// 新文件：ClientAvalonia/Domain/IResourceCatalog.cs

/// <summary>
/// 地图 / 游戏模式 / 任务资源目录接口。
///
/// 作用：GameResourceCatalog 是 sealed 类，无法继承做 mock。本接口
/// 把它解密封装，返回 IMapResource / IMissionResource / IGameModeResource
/// 而非具体 DTO，让 LobbyAction / ChangeMapAction / MapListBindingApplier
/// 可以注入测试用 catalog，并为在线下载资源预留扩展点。
/// </summary>
public interface IResourceCatalog
{
    /// <summary>所有已加载的地图（来自 MPMaps.ini + 自定义 map 目录扫描）。</summary>
    IReadOnlyList<IMapResource> Maps { get; }

    /// <summary>所有游戏模式（Standard, Custom, ...）。</summary>
    IReadOnlyList<IGameModeResource> GameModes { get; }

    /// <summary>所有任务（Campaign / Mission）。</summary>
    IReadOnlyList<IMissionResource> Missions { get; }

    /// <summary>资源加载完成事件。UI 用于触发首次列表渲染。</summary>
    event Action? Loaded;

    /// <summary>确保资源已加载（幂等）。首次调用触发磁盘扫描。</summary>
    void EnsureLoaded();

    /// <summary>根据 dropdown filter index（0=favorites, 1+=mode）取对应 GameMode。</summary>
    IGameModeResource? GetGameModeForFilterIndex(int filterIndex);

    /// <summary>根据 filter index 取该模式下的地图列表。</summary>
    IReadOnlyList<IMapResource> GetMapsForFilterIndex(int filterIndex);

    /// <summary>在给定地图列表中随机选一个（按玩家数过滤）。</summary>
    int PickRandomMapIndex(IReadOnlyList<IMapResource> visible, int playerCount = 2);

    /// <summary>切换地图的"收藏"状态并持久化。</summary>
    bool ToggleFavoriteMap(IMapResource map, IGameModeResource? gameMode);

    /// <summary>取所有收藏的地图。</summary>
    IReadOnlyList<IMapResource> GetFavoriteMaps();
}
```

---

### 5.3 会话层（★ v3 全新）

会话层表达**玩家语义上的一场游戏**：选了哪张图、几个槽位、游戏选项、当前状态（Lobby / InGame …）。它**不是** IRC 连接。

#### 5.3.1 `IGameSession` 基接口

```csharp
/// <summary>
/// 玩家语义上的一场游戏（基接口）。
///
/// 作用：统一 Skirmish / CnCNet 房间 / LAN / Mission 的共同面——地图、
/// 玩家槽位、游戏选项、状态机。UI Applier / Action 应依赖此接口（或其
/// 子接口），而不是 LobbyPlayerState + LobbyPlayerMode 枚举切换。
///
/// 对应现状：LobbyPlayerState（槽位）+ LobbySessionState（选图）+
/// CnCNetGameOptionsState（选项）的交集。
/// </summary>
public interface IGameSession
{
    /// <summary>当前选中地图。对应 LobbySessionState 选图 + ChangeMapAction 目标。</summary>
    IMapResource? Map { get; set; }

    /// <summary>
    /// 玩家槽位（最多 LobbyPlayerSlot.MaxSlots = 8）。
    /// 对应 LobbyPlayerState.Slots。
    /// </summary>
    IReadOnlyList<IPlayerSlot> PlayerSlots { get; }

    /// <summary>游戏选项状态（checkbox / dropdown / 协议参数）。</summary>
    IGameOptionsState Options { get; }

    /// <summary>会话生命周期状态。</summary>
    GameSessionState State { get; }

    /// <summary>状态或槽位变化时触发（UI 刷新）。</summary>
    event Action? StateChanged;
}

/// <summary>游戏会话生命周期。</summary>
public enum GameSessionState
{
    /// <summary>大厅配置中。</summary>
    Lobby,

    /// <summary>正在启动（写 spawn / 等 Syringe）。</summary>
    Launching,

    /// <summary>游戏进程运行中。</summary>
    InGame,

    /// <summary>已结束 / 已离开。</summary>
    Finished,
}
```

#### 5.3.2 `IPlayerSlot`

```csharp
/// <summary>
/// 单个玩家 / AI 槽位。
///
/// 作用：替代直接操作 LobbyPlayerSlot 具体类。字段从
/// ClientAvalonia/Domain/LobbyPlayerSlot.cs 反推。
/// ColorIndex 是分配状态；颜色目录在 IMultiplayerColorCatalog。
/// 默认实现：LobbyPlayerSlot : IPlayerSlot。
/// </summary>
public interface IPlayerSlot
{
    /// <summary>显示名（人名或 AI 名）。对应 LobbyPlayerSlot.Name。</summary>
    string Name { get; set; }

    /// <summary>阵营索引。对应 LobbyPlayerSlot.SideIndex。</summary>
    int SideIndex { get; set; }

    /// <summary>
    /// 颜色索引（分配状态）。对应 LobbyPlayerSlot.ColorIndex。
    /// 目录上限由 IMultiplayerColorCatalog.Load().Count 决定。
    /// </summary>
    int ColorIndex { get; set; }

    /// <summary>队伍索引。对应 LobbyPlayerSlot.TeamIndex。</summary>
    int TeamIndex { get; set; }

    /// <summary>起点索引。对应 LobbyPlayerSlot.StartIndex。</summary>
    int StartIndex { get; set; }

    /// <summary>是否 AI。对应 LobbyPlayerSlot.IsAi。</summary>
    bool IsAi { get; set; }

    /// <summary>AI 难度等级。对应 LobbyPlayerSlot.AiLevel。</summary>
    int AiLevel { get; set; }

    /// <summary>是否本机人类玩家。对应 LobbyPlayerSlot.IsHumanLocal。</summary>
    bool IsHumanLocal { get; set; }

    /// <summary>槽位是否被占用（Name 非空）。对应 LobbyPlayerSlot.IsOccupied。</summary>
    bool IsOccupied { get; }
}
```

#### 5.3.3 `IGameOptionsState`

```csharp
/// <summary>
/// 游戏选项状态（大厅 checkbox / dropdown + 协议参数）。
///
/// 作用：统一 Skirmish 本地选项与 CnCNet GO CTCP 载荷的共同面。
/// 字段参考 CnCNetGameOptionsState；Skirmish 可不使用协议相关字段。
/// </summary>
public interface IGameOptionsState
{
    /// <summary>Checkbox 值列表。对应 CnCNetGameOptionsState.CheckBoxValues。</summary>
    IReadOnlyList<bool> CheckBoxValues { get; }

    /// <summary>Dropdown 选中索引。对应 CnCNetGameOptionsState.DropDownIndices。</summary>
    IReadOnlyList<int> DropDownIndices { get; }

    /// <summary>当前地图是否官方。对应 CnCNetGameOptionsState.MapOfficial。</summary>
    bool MapOfficial { get; }

    /// <summary>当前地图 Sha1。对应 CnCNetGameOptionsState.MapSha1。</summary>
    string MapSha1 { get; }

    /// <summary>当前游戏模式名。对应 CnCNetGameOptionsState.GameModeName。</summary>
    string GameModeName { get; }

    /// <summary>FrameSendRate。对应 CnCNetGameOptionsState.FrameSendRate。</summary>
    int FrameSendRate { get; }

    /// <summary>MaxAhead。对应 CnCNetGameOptionsState.MaxAhead。</summary>
    int MaxAhead { get; }

    /// <summary>协议版本。对应 CnCNetGameOptionsState.ProtocolVersion。</summary>
    int ProtocolVersion { get; }

    /// <summary>随机种子。对应 CnCNetGameOptionsState.RandomSeed。</summary>
    int RandomSeed { get; }

    /// <summary>是否移除起点。对应 CnCNetGameOptionsState.RemoveStartingLocations。</summary>
    bool RemoveStartingLocations { get; }

    /// <summary>地图未本地化名。对应 CnCNetGameOptionsState.MapUntranslatedName。</summary>
    string MapUntranslatedName { get; }
}
```

#### 5.3.4 Session 子接口

```csharp
/// <summary>
/// 遭遇战会话（单人本地）。
///
/// 作用：无网络元数据的本地对战。ICnCNetGameSession / ILANGameSession
/// 均继承此接口——"联网遭遇战 = 遭遇战 + 网络元数据"。
/// DefaultAiSlotPolicy / ChangeMapAction 应接收 ISkirmishSession。
/// </summary>
public interface ISkirmishSession : IGameSession
{
    // 单人本地，无额外网络元数据。
    // 共享逻辑（若有）用扩展方法，不用抽象基类（见 §6.3）。
}

/// <summary>
/// CnCNet 多人游戏会话 = 联网遭遇战 + Tunnel + Host 元数据。
///
/// 作用：表达"当前进入的 CnCNet 游戏房间"。字段从
/// CnCNetActiveGameRoom.cs 反推。实现类建议为 CnCNetGameRoomSession
///（或适配器包装 ActiveGameRoom + GameRoomSession）。
/// </summary>
public interface ICnCNetGameSession : ISkirmishSession
{
    /// <summary>当前选用的 NAT Tunnel。对应 CnCNetActiveGameRoom.Tunnel。</summary>
    CnCNetTunnel Tunnel { get; set; }

    /// <summary>房主名。对应 CnCNetActiveGameRoom.HostName。</summary>
    string HostName { get; }

    /// <summary>本机是否房主。对应 CnCNetActiveGameRoom.IsHost。</summary>
    bool IsHost { get; }

    /// <summary>IRC 游戏频道名。对应 CnCNetActiveGameRoom.ChannelName。</summary>
    string ChannelName { get; }

    /// <summary>IRC 频道密钥（明文或 SHA1 默认 key）。对应 CnCNetActiveGameRoom.Password。</summary>
    string? Password { get; set; }

    /// <summary>最大玩家数。对应 CnCNetActiveGameRoom.MaxPlayers。</summary>
    int MaxPlayers { get; set; }

    /// <summary>技能等级。对应 CnCNetActiveGameRoom.SkillLevel。</summary>
    int SkillLevel { get; set; }
}

/// <summary>
/// 局域网游戏会话 = 局域网遭遇战 + Host，无 Tunnel。
///
/// 作用：与 ICnCNetGameSession 平级，共享 ISkirmishSession，但不依赖
/// CnCNet Tunnel / IRC。当前 Avalonia LAN 路径尚未完整，接口先预留。
/// </summary>
public interface ILANGameSession : ISkirmishSession
{
    /// <summary>房主名。</summary>
    string HostName { get; }

    /// <summary>本机是否房主。</summary>
    bool IsHost { get; }
}

/// <summary>
/// 战役会话。平级于 ISkirmishSession，不继承遭遇战。
///
/// 作用：战役只有 Mission + 有限玩家配置，语义上不是"遭遇战"。
/// ModMetadata 预留战役 mod 自定义扩展点。
/// </summary>
public interface IMissionSession : IGameSession
{
    /// <summary>当前任务。对应 MissionEntry 选中项。</summary>
    IMissionResource Mission { get; }

    /// <summary>战役 mod 扩展元数据（解锁条件、脚本参数等）。</summary>
    IReadOnlyDictionary<string, object> ModMetadata { get; }
}
```

继承关系 ASCII：

```
                    IGameSession
                   /            \
        ISkirmishSession     IMissionSession
         /            \
ICnCNetGameSession   ILANGameSession
```

#### 5.3.5 与网络层的关系（★ 必须明确）

```
CnCNetSession (网络层，实现 ICnCNetSession，不实现 IGameSession)
├── IRC 连接、心跳、重连
├── 频道管理、玩家计数
├── Tunnel list 维护（已落地 TunnelSorter）
├── Hosted game 广播
└── ActiveGameRoom: ICnCNetGameSession?    ← 当前活动游戏会话
        └── 实现类：CnCNetGameRoomSession（或适配器）
            实现 ICnCNetGameSession（即 IGameSession 体系）
```

实现关系：

| 类型 | 实现什么 | 不实现什么 |
| ---- | -------- | ---------- |
| `CnCNetSession` | `ICnCNetSession`（网络层） | **不**实现 `IGameSession` |
| `CnCNetGameRoomSession`（或适配器） | `ICnCNetGameSession` | 不负责 IRC 连接本身 |
| `SkirmishSession`（新） | `ISkirmishSession` | 无网络 |
| `MissionSession`（新） | `IMissionSession` | 无网络 |

`ICnCNetSession.ActiveGameRoom` 的类型从现状的 `CnCNetActiveGameRoom?` 迁移为 `ICnCNetGameSession?`（迁移期可用适配器包装现有 `ActiveGameRoom` + `GameRoom`）。

---

### 5.4 配置 / 网络 / 更新层

#### 5.4.1 `IGameConfiguration`（替代 `ClientConfiguration.Instance`）

```csharp
// 新文件：ClientAvalonia/Configuration/IGameConfiguration.cs

/// <summary>
/// ClientConfiguration.ini 的强类型只读视图。
///
/// 作用：把 ClientConfiguration.Instance 单例封装为接口，让 OptionsWindow /
/// AudioOptionsApplier 等读取配置的代码可注入 mock。和 IGameEnvironment
/// 的区别：Environment 是"运行时环境"（game root、player name 等启动期固定），
/// IGameConfiguration 是"INI 文件配置"（audio volume、renderer 设置等用户可改）。
/// </summary>
public interface IGameConfiguration
{
    /// <summary>对应 ClientConfiguration.LocalGame。决定 mod 的逻辑分支。</summary>
    string LocalGame { get; }

    /// <summary>设置文件名（如 "SUN.ini" / "RA2MD.ini"）。决定 Settings 文件位置。</summary>
    string SettingsIniName { get; }

    /// <summary>游戏长名（如 "Mental Omega"）。UI 标题栏显示。</summary>
    string LongGameName { get; }

    /// <summary>IRC 中显示的游戏长名。可能含 ASCII 安全变体。</summary>
    string LongGameNameIRC { get; }

    /// <summary>是否为开发者 ModMode（允许 / Editor 入口、跳过版本检查）。</summary>
    bool ModMode { get; }

    /// <summary>是否在 GamePath 下创建 SavedGames 目录。</summary>
    bool CreateSavedGamesDirectory { get; }

    /// <summary>CnCNet 实时状态标识符（如 "cncnet-5"）。IRC 频道命名用。</summary>
    string CnCNetLiveStatusIdentifier { get; }

    /// <summary>取自定义本地化字符串（ClientDefinitions.ini [CustomStrings]）。</summary>
    string GetCustomLocalizedString(string key);

    /// <summary>取泛型设置值（Options INI 中的 checkbox / dropdown）。</summary>
    T GetSettingValue<T>(string key);
}
```

#### 5.4.2 `ICnCNetSession`（网络层，替代 `CnCNetSessionService.Instance`）

```csharp
// 新文件：ClientAvalonia/CnCNet/ICnCNetSession.cs

/// <summary>
/// CnCNet 网络会话接口（Network 域）。
///
/// 作用：把 CnCNetSessionService.Instance 单例封装为接口，让 MainWindow /
/// LobbyBehaviors / GameCreationOverlay 等所有调 IRC 的代码可注入 mock。
///
/// ★ 本接口是网络层，不实现 IGameSession。当前活动的游戏房间通过
/// ActiveGameRoom : ICnCNetGameSession? 暴露。
///
/// 后补的 Auto-Refresh 与 Low-Latency Tunnel（TunnelSorter）目前都直接读
/// CnCNetSessionService.Instance，迁移后通过此接口注入即可单测。
/// </summary>
public interface ICnCNetSession
{
    /// <summary>IRC 连接状态（Disconnected / Connecting / Connected）。</summary>
    CnCNetConnectionState ConnectionState { get; }

    /// <summary>当前 IRC 昵称（= PlayerName 或登录名）。</summary>
    string LocalNick { get; }

    /// <summary>
    /// 当前活动的游戏会话（host 或 joiner）。null 表示未在房间。
    /// v3：类型为 ICnCNetGameSession?（替代原 CnCNetActiveGameRoom?）。
    /// </summary>
    ICnCNetGameSession? ActiveGameRoom { get; }

    /// <summary>
    /// 房间内 lobby 逻辑对象（CTCP / 玩家列表等）。迁移期可保留具体类型；
    /// 长期可并入 ICnCNetGameSession 实现。
    /// </summary>
    CnCNetGameRoomSession? GameRoom { get; }

    /// <summary>所有已知 tunnel 服务器列表。原顺序保留（按 IRC 广播）。</summary>
    IReadOnlyList<CnCNetTunnel> Tunnels { get; }

    /// <summary>
    /// Tunnel 延迟小顶堆（Low-Latency Tunnel v2，已落地）。
    /// 暴露在此接口上，让 UI / 测试可以订阅 BestTunnelChanged 事件。
    /// TunnelMaintenanceLoop 通过此属性 + ActiveGameRoom.Tunnel 装配。
    /// </summary>
    TunnelSorter TunnelSorter { get; }

    /// <summary>在线玩家总数（CnCNet 主频道广播）。-1 = 未知。</summary>
    int OnlinePlayerCount { get; }

    /// <summary>会话状态变化时触发（UI 用于刷新所有 CnCNet 相关面板）。</summary>
    event Action? StateChanged;

    /// <summary>成功加入游戏房间时触发。</summary>
    event Action<ICnCNetGameSession>? GameRoomJoined;

    /// <summary>加入游戏房间失败时触发（参数为失败原因）。</summary>
    event Action<string>? GameRoomJoinFailed;

    /// <summary>游戏即将启动时触发（参数含启动配置）。</summary>
    event Action<CnCNetStartGameInfo>? GameStarting;

    /// <summary>房主离开房间时触发（joiner 检测到 host 消失）。</summary>
    event Action? GameRoomHostAbandoned;

    /// <summary>建立 CnCNet 连接（幂等，已连接则 no-op）。</summary>
    void ConnectIfNeeded();

    /// <summary>主动断开（用户退出或关窗）。</summary>
    void Disconnect();

    /// <summary>尝试加入指定主机的游戏房间。返回是否成功 + 失败原因。</summary>
    bool TryJoinGame(CnCNetHostedGameSummary game, string? password, out string message);

    /// <summary>尝试启动已加入的游戏（host 已 START 后 joiner 调用）。</summary>
    bool TryLaunchHostedGame(out string message);

    /// <summary>发送聊天消息到当前频道。</summary>
    void SendChatMessage(string message);

    // ... 其他 UI 调用点按需追加
}
```

#### 5.4.3 `IUpdater`（替代 `Updater` static）

```csharp
// 新文件：ClientAvalonia/Updater/IUpdater.cs

/// <summary>
/// 版本检查与自更新接口。
///
/// 作用：Updater 是 static class，无法 mock。OptionsWindow 的"检查更新"
/// 按钮和启动期版本检查都直接调 Updater，迁移到接口后可注入 fake
/// 模拟"有更新 / 无更新 / 检查失败"三种场景。
/// </summary>
public interface IUpdater
{
    /// <summary>当前游戏版本号。</summary>
    string GameVersion { get; }

    /// <summary>自定义更新组件清单（call-in 自更新）。</summary>
    IReadOnlyList<CustomComponent> CustomComponents { get; }

    /// <summary>本地文件版本检查完成事件。</summary>
    event Action? OnLocalFileVersionsChecked;

    /// <summary>初始化 Updater（启动期由 PreStartup 调用）。</summary>
    void Initialize(string gamePath, string baseResourcePath, string settingsIniName,
                    string localGame, string callingExecutable);

    /// <summary>触发本地文件版本检查（异步，结果通过事件回报）。</summary>
    void CheckLocalFileVersions();
}
```

---

## 6. 继承层次评估

### 6.0 三棵树总览

v3 将继承决策拆为**三棵独立的树**，策略不同：

```
┌─ 环境树 ─┐  抽象基类（有派生属性逻辑）
┌─ 资源树 ─┐  基接口 + 默认实现（C# 单继承限制）
┌─ 会话树 ─┐  纯接口继承（无共享逻辑，mod / mock 友好）
```

### 6.1 环境树：用抽象基类

```
                  ┌─────────────────────────────────┐
                  │  IGameEnvironment  (interface)  │
                  └─────────────────────────────────┘
                                  ▲
                                  │
                  ┌───────────────┴────────────────┐
                  │  GameEnvironmentBase (abstract) │  ← ResourcesPath / BaseResourcesPath
                  └────────────────────────────────┘
                                  ▲
                                  │
            ┌─────────────────────┼─────────────────────────┐
            │                     │                         │
   ┌────────┴──────────┐  ┌───────┴────────┐  ┌─────────────┴──────────┐
   │ ProgramConstants   │  │ MockGame       │  │ MultiModGame           │
   │ GameEnvironment    │  │ Environment    │  │ Environment (future)   │
   │ (生产实现，内部读   │  │ (测试，可变属性)│  │ (多 mod 启动器预留)    │
   │  ProgramConstants) │  │                │  │                        │
   └────────────────────┘  └────────────────┘  └────────────────────────┘
```

**理由**：`ResourcesPath` / `BaseResourcesPath` 都是 `GamePath` 的派生值，基类提供默认实现避免每个子类重复 `Path.Combine`。

```csharp
// 新文件：ClientAvalonia/Environment/GameEnvironmentBase.cs

/// <summary>
/// IGameEnvironment 的默认基类。
///
/// 作用：把"派生路径"逻辑（ResourcesPath / BaseResourcesPath）抽到基类，
/// 让子类只需要提供核心抽象属性，其余路径自动派生。
/// </summary>
public abstract class GameEnvironmentBase : IGameEnvironment
{
    public abstract string LocalGame { get; }
    public abstract string GamePath { get; }
    public abstract string PlayerName { get; }
    public abstract string GameVersion { get; }

    public virtual IReadOnlyList<string> AiPlayerNames { get; } = Array.Empty<string>();
    public virtual IReadOnlyList<string> TeamNames { get; } = new[] { "A", "B", "C", "D" };

    public virtual string ResourcesPath => Path.Combine(GamePath, "Resources");
    public virtual string BaseResourcesPath => Path.Combine(ResourcesPath, "Base");
}
```

生产 / 测试子类：

```csharp
internal sealed class ProgramConstantsGameEnvironment : GameEnvironmentBase
{
    public override string LocalGame => ProgramConstants.LocalGame;
    public override string GamePath => ProgramConstants.GamePath;
    public override string PlayerName => ProgramConstants.PLAYERNAME;
    public override string GameVersion => ProgramConstants.GAME_VERSION;
    public override IReadOnlyList<string> AiPlayerNames => ProgramConstants.AI_PLAYER_NAMES;
    public override IReadOnlyList<string> TeamNames => ProgramConstants.TEAMS;
}

internal sealed class MockGameEnvironment : GameEnvironmentBase
{
    public override string LocalGame { get; set; } = "mg";
    public override string GamePath { get; set; } = @"C:\fake\mg";
    public override string PlayerName { get; set; } = "TestPlayer";
    public override string GameVersion { get; set; } = "1.0.0";
    public override IReadOnlyList<string> AiPlayerNames { get; set; } =
        new[] { "Easy AI", "Medium AI", "Hard AI" };
    public override IReadOnlyList<string> TeamNames { get; set; } =
        new[] { "A", "B", "C", "D" };
}
```

> C# 11+ 允许 override 时把只读属性升级为可写属性（如 `MockGameEnvironment.LocalGame { get; set; }`），这是合法的。

### 6.2 资源树：基接口 + 默认实现

```
                    IResource
                   /    |    \
        IMapResource  IMissionResource  IGameModeResource
              ▲              ▲                ▲
              │              │                │
         MapEntry       MissionEntry     GameModeEntry
      (默认实现)        (默认实现)        (默认实现)
              │
              │ 未来可并行
              ▼
      DownloadedMapResource / ModMapResource / ...
```

**理由**：

1. C# **单继承**限制——`MapEntry` / `MissionEntry` 已是 sealed（或即将保持独立继承链），不能再塞进共同抽象基类。
2. Map / Mission / GameMode 字段差异大，强行抽抽象基类只会变成"一堆 abstract 属性转发"。
3. 基接口 `IResource` 提供元数据契约；默认实现保留旧 DTO，**渐进迁移、旧代码不破坏**。

### 6.3 会话树：纯接口继承（★ 不用抽象基类）

```
                    IGameSession
                   /            \
        ISkirmishSession     IMissionSession
         /            \
ICnCNetGameSession   ILANGameSession
```

**关键决策：会话树不做抽象基类。** 理由：

1. **Mod 多继承需求**：未来 mod 可能同时需要 "战役 + 局域网" 等组合；接口允许多实现，抽象基类受单继承限制。
2. **Mock 友好**：测试只需实现接口（或用 NSubstitute），不必继承基类再 override。
3. **没有共享逻辑**：Session 之间的共性只是字段契约，没有像 `ResourcesPath` 那样的派生计算；共享行为用扩展方法即可。

后补 `UiAction` 体系的继承对比（参照，效果良好，但与 Session 树决策不同）：

```
UiAction<TContext> (abstract, 顶层)          ← Action 有共享 Execute 管线，值得抽基类
    └─ LobbyAction : UiAction<LobbyActionContext>
          ├─ SetPlayerColorAction
          ├─ ChangeMapAction
          └─ ...
```

Action 有共享管线逻辑 → 抽象基类合理。Session 只有字段契约 → **纯接口**。

### 6.4 评估矩阵（更新）


| 接口 / 树                      | 是否需要抽象基类？ | 理由                                                                 |
| ----------------------------- | --------- | -------------------------------------------------------------------- |
| `IGameEnvironment`（环境树）    | **是**     | 派生路径逻辑                                                           |
| `IResource` 树（资源树）         | **否**     | 基接口 + 默认实现；单继承限制                                              |
| `IGameSession` 树（会话树）      | **否**     | 纯接口；mod 多继承 / mock / 无共享逻辑                                      |
| `IGameConfiguration`          | **否**     | 字段直接读 INI                                                         |
| `ICnCNetSession`（网络层）       | **否**     | 一个生产实现 + N 个 mock                                                 |
| `IResourceCatalog`            | **否**     | sealed 适配器 + mock                                                   |
| `IResourceManifest`           | **否**     | 逻辑服务                                                               |
| `IUpdater`                    | **否**     | 同上                                                                   |
| `IMultiplayerColorCatalog`    | **否**     | 同上                                                                   |

---

## 7. EnvironmentServices 服务定位器

不引入 `Microsoft.Extensions.DependencyInjection`，做一个**极简**的服务定位器。

**★ v3 变更**：`Resolve<T>()` **不再硬编码 fallback** 到 `ProgramConstants`。未注册直接抛 `InvalidOperationException`，明确报错原因。INI 层未稳定期优先暴露 bug，而不是静默走错路径。

```csharp
// 新文件：ClientAvalonia/Environment/EnvironmentServices.cs

/// <summary>
/// 极简服务定位器。
///
/// 作用：替代 Microsoft.Extensions.DependencyInjection 容器。
/// 桌面客户端启动一次、运行时无热重载，DI 容器收益不抵复杂度。
/// 此类只做一件事：保存接口 → 工厂的映射，让 Resolve&lt;T&gt;() 返回实例。
/// 测试通过 Reset() 清理 + 重新 Register() 注入 mock。
///
/// ★ 未注册时抛 InvalidOperationException，不 fallback 到 ProgramConstants。
/// </summary>
public static class EnvironmentServices
{
    private static readonly Dictionary<Type, Func<object>> _factories = new();
    private static readonly object _sync = new();

    /// <summary>注册接口 T 的工厂。后注册的覆盖先注册的。</summary>
    public static void Register<T>(Func<T> factory) where T : class
    {
        lock (_sync) { _factories[typeof(T)] = () => factory(); }
    }

    /// <summary>
    /// 解析接口 T 的实例。
    /// 未注册则抛 InvalidOperationException（明确提示是否忘记 Register）。
    /// </summary>
    public static T Resolve<T>() where T : class
    {
        lock (_sync)
        {
            if (_factories.TryGetValue(typeof(T), out var f))
                return (T)f();
        }

        throw new InvalidOperationException(
            $"No factory registered for {typeof(T).Name}. " +
            "Did you forget to call EnvironmentServices.Register in PreStartup.Initialize or test setup?");
    }

    /// <summary>测试专用：清空所有注册。生产代码不要调用。</summary>
    internal static void Reset()
    {
        lock (_sync) { _factories.Clear(); }
    }
}
```

生产初始化（`PreStartup.Initialize`）：

```csharp
public static void Initialize(StartupParams parameters)
{
    // ... 现有逻辑 ...

    EnvironmentServices.Register<IGameEnvironment>(() => new ProgramConstantsGameEnvironment());
    EnvironmentServices.Register<IGameConfiguration>(() => new ClientConfigurationAdapter());
    EnvironmentServices.Register<ICnCNetSession>(() => CnCNetSessionService.Instance);
    EnvironmentServices.Register<IResourceCatalog>(() => new GameResourceCatalogAdapter(GameResourceCatalog.Instance));
    EnvironmentServices.Register<IResourceManifest>(() => new NoOpResourceManifest()); // L1 可先 NoOp
    EnvironmentServices.Register<IUpdater>(() => new UpdaterAdapter());
    EnvironmentServices.Register<IMultiplayerColorCatalog>(() => new MultiplayerColorCatalogAdapter());
}
```

测试初始化（`TempGameRoot.BindToProgramConstants`）：

```csharp
public void BindToProgramConstants()
{
    // 现有逻辑 ...

    EnvironmentServices.Reset();
    EnvironmentServices.Register<IGameEnvironment>(() => new MockGameEnvironment
    {
        LocalGame = ProgramConstants.LocalGame,
        GamePath = RootPath,
    });
    EnvironmentServices.Register<IMultiplayerColorCatalog>(() => new FakeColorCatalog(/* 8 colors */));
    // ... 其他接口按需注入；漏注册会立刻抛异常，便于发现测试 setup 缺陷
}
```

---

## 8. 迁移路径

### 8.1 阶段划分（工时按"资源接口化全做"上调）

```
Week 1: 接口与基础设施
  Day 1:   本设计定稿 + 评审拍板（§12 待确认问题）
  Day 2-3: IResource / IMapResource / IMissionResource / IGameModeResource
           + MapEntry 等默认实现适配 + IResourceManifest (NoOp)
  Day 4:   IGameEnvironment / GameEnvironmentBase / Mock + EnvironmentServices
  Day 5:   IGameSession 树 + IPlayerSlot + IGameOptionsState
           + IResourceCatalog / IMultiplayerColorCatalog / IUpdater / IGameConfiguration

Week 2: UI 层 + Session 迁移
  Day 1-2: MainWindow 构造函数改为依赖注入
  Day 3-4: IniUi Applier / Behaviors 改接 ISkirmishSession / IMapResource
  Day 5:   LobbyActionContext 瘦身为持 ISkirmishSession

Week 3: 网络适配 + 后补功能 + 测试
  Day 1-2: ICnCNetSession 适配；ActiveGameRoom 暴露 ICnCNetGameSession
  Day 3:   后补功能改造（§9 清单）
  Day 4-5: 全套测试 mock IGameSession 体系 + 验收
```

### 8.2 兼容策略

迁移期间旧类型与接口并存：

| 旧类型 | 新接口 | 策略 |
| ------ | ------ | ---- |
| `ProgramConstants.XXX` | `IGameEnvironment` | 适配器只读包装；旧代码不强制改 |
| `MapEntry` | `IMapResource` | `MapEntry : IMapResource` |
| `MissionEntry` | `IMissionResource` | `MissionEntry : IMissionResource` |
| `GameModeEntry` | `IGameModeResource` | `GameModeEntry : IGameModeResource` |
| `LobbyPlayerSlot` | `IPlayerSlot` | `LobbyPlayerSlot : IPlayerSlot` |
| `LobbyPlayerState` | `ISkirmishSession` 实现 | 渐进拆分；过渡期可适配器包装 |
| `CnCNetActiveGameRoom` + `CnCNetGameRoomSession` | `ICnCNetGameSession` | 适配器或让 GameRoomSession 实现接口 |
| `CnCNetSession` | `ICnCNetSession` | 适配器；**不**实现 `IGameSession` |

这样**永远不会破坏现有功能**，迁移可以按文件 / 按模块渐进推进。

### 8.3 回滚策略

每个 commit 都能独立回滚。`EnvironmentServices` **不提供 fallback**——若生产启动漏注册，启动期立刻崩溃并打出明确异常信息，避免半初始化状态悄然运行。回滚方式是 revert 引入 `Resolve` 调用的 commit，而不是在 Resolve 里悄悄走 `ProgramConstants`。

### 8.4 已落地代码（不能破坏，只能渐进迁移）

以下代码已在 main 落地，新设计不得破坏其行为，只能让它们未来渐进迁移到接口：

| 文件 | 现状 | 未来迁移方向 |
| ---- | ---- | ------------ |
| `IniUi/Actions/UiAction.cs` | Action 抽象基类（v2 已实现） | 保持 |
| `IniUi/Actions/Lobby/LobbyAction.cs` | Lobby 领域基类 + `LobbyActionContext` | Context 瘦身为持 `ISkirmishSession` |
| `IniUi/Actions/Lobby/SetPlayerColorAction.cs` | 已实现 | 经 Context 间接受益 |
| `IniUi/Actions/Lobby/ChangeMapAction.cs` | 接 `MapEntry` | 改接 `IMapResource` |
| `IniUi/Lobby/DefaultAiSlotPolicy.cs` | 接 `LobbyPlayerState` | 改接 `ISkirmishSession` |
| `CnCNet/Tunnels/TunnelSorter.cs` | 已实现 | 由 `ICnCNetSession.TunnelSorter` 暴露 |
| `CnCNet/Tunnels/TunnelPrewarmer.cs` | 已实现 | 保持；装配点改注入 |
| `CnCNet/Tunnels/TunnelMaintenanceLoop.cs` | 已实现 | 经 `ICnCNetGameSession.Tunnel` + `ICnCNetSession.Tunnels` 注入 |
| `CnCNet/Tunnels/IcmpTunnelPinger.cs` | 已实现 | 保持；继续写 `Logger.Log` |
| `CnCNet/Tunnels/ITunnelPinger.cs` | 已实现 | 保持 |
| `CnCNet/Tunnels/TunnelSortKey.cs` | 已实现 | 保持 |

### 8.5 字段反推依据（供实施时核对）

| 文件 | 反推目标 |
| ---- | -------- |
| `ClientAvalonia/Domain/MapEntry.cs` | `IMapResource` 字段 |
| `ClientAvalonia/Domain/MissionEntry.cs` | `IMissionResource` 字段 |
| `ClientAvalonia/Domain/GameModeEntry.cs` | `IGameModeResource` 字段 |
| `ClientAvalonia/Domain/LobbyPlayerSlot.cs` | `IPlayerSlot` 字段（`MaxSlots=8`） |
| `ClientAvalonia/Services/LobbyPlayerState.cs` | `IGameSession.PlayerSlots` / AI·队伍目录 |
| `ClientAvalonia/CnCNet/CnCNetActiveGameRoom.cs` | `ICnCNetGameSession` 元数据 |
| `ClientAvalonia/CnCNet/CnCNetGameRoomSession.cs` | `ICnCNetGameSession` 实现细节 |
| `ClientAvalonia/CnCNet/CnCNetSession.cs` | `ICnCNetSession`（网络层）；含 `TunnelSorter` |
| `ClientAvalonia/CnCNet/CnCNetGameOptionsState.cs` | `IGameOptionsState` 字段 |

---

## 9. 后补功能改造清单


| 改造项 | 当前代码 | 改造后 |
| ------ | -------- | ------ |
| `DefaultAiSlotPolicy.AutoFillToMapCapacity` | 接 `LobbyPlayerState` + 读 `ProgramConstants.PLAYERNAME` | 接 `ISkirmishSession`；玩家名从 `IGameEnvironment.PlayerName` 传入 |
| `DefaultAiSlotPolicy` 颜色计算 | 调 `MultiplayerColorCatalog.Load()` 静态 | 加参数 `IMultiplayerColorCatalog colors`，caller 注入 |
| `ChangeMapAction` | 接 `MapEntry` 具体类 | 接 `IMapResource` 接口 |
| `LobbyActionContext` | 持 `Player` (`LobbyPlayerState`) + `CnCNet` 两字段 | 持单个 `ISkirmishSession`；Action 自动适配所有 Session 子类 |
| `OnLobbySlotsMutated` | 调 `CnCNetSessionService.Instance` | 接 `ICnCNetGameSession` 注入 |
| `LobbyPlayerBindingApplier.WireSlot` 闭包 | `CnCNetSession.Instance.GameRoom` | 捕获 `ICnCNetGameSession` / `ICnCNetSession` |
| `TunnelMaintenanceLoop` | 通过 `CnCNetSessionService.Instance` 拿 tunnels/selected | 通过 `ICnCNetGameSession.Tunnel` + `ICnCNetSession.Tunnels` 注入 |
| `CnCNetSession.TunnelSorter` | 单例可变字段（可接受） | 暴露为 `ICnCNetSession.TunnelSorter`，本身不抽象 |
| `LobbyPlayerState` | 混 Skirmish + Multiplayer（`LobbyPlayerMode` 切换） | 拆为 `SkirmishSession.Slots` + `CnCNetGameSession.Slots`，分支消失 |
| `LobbyPlayerState.LoadCatalogs` | 读 `ProgramConstants.AI_PLAYER_NAMES` / `TEAMS` | 从 `IGameEnvironment` 传入 |

---

## 10. 不推荐做的事

1. **不要一次性删除 `ProgramConstants` 静态字段**。300+ 处调用点，分阶段迁移期间必须保留。
2. **不要引入 `Microsoft.Extensions.DependencyInjection`**。Desktop 客户端启动一次、运行时无热重载，DI 容器收益不抵复杂度。
3. **不要把 `Logger` 抽接口**。Logger 只写不读，跨测试无串扰风险，无需抽象。后补的 `TunnelSorter` / `IcmpTunnelPinger` / `TunnelMaintenanceLoop` 直接调 `Logger.Log` 是合理的。
4. **不要把 `CnCNetSession` 单例改成 scoped**。一个进程只能有一个 IRC 连接，singleton 是 by design。
5. **不要给每个接口都抽基类**。只有存在共性逻辑（如 `GameEnvironmentBase` 的派生路径）时才抽。
6. **不要给 Session 树做抽象基类**（★ v3 新增）。理由见 §6.3：mod 多继承需求、mock 友好、没有共享逻辑。
7. **不要让 `CnCNetSession` 实现 `IGameSession`**。网络层与游戏会话必须解耦；游戏会话走 `ActiveGameRoom`。
8. **不要在 `Resolve<T>()` 里硬编码 fallback**。未注册必须抛异常，优先暴露 bug。
9. **不要让 `ProgramConstantsGameEnvironment` 缓存字段值**。每次属性访问都直接读 `ProgramConstants`，保证单例切 mod 时立即生效。
10. **不要把颜色分配状态塞进 `IMultiplayerColorCatalog`**。目录与分配分离；分配在 `IPlayerSlot.ColorIndex`。

---

## 11. 验收标准

完成 L1 后应满足：

- [ ] 单元测试可以在不污染全局状态的前提下设置 `LocalGame = "lnod"`
- [ ] `LnodWorkspace_SynthesizesCncnetLnodChannels` 在全套测试中通过（不靠 `[Collection]` 串行也能通过）
- [ ] `MainWindow` 构造函数不再直接调用 `CnCNetSessionService.Instance` / `ProgramConstants` / `GameResourceCatalog.Instance`
- [ ] 后补功能 `DefaultAiSlotPolicy` / `OnLobbySlotsMutated` / `TunnelMaintenanceLoop` / `ChangeMapAction` 装配点全部走接口
- [ ] 所有 `public` 类的依赖通过构造函数显式声明（除非是数据 DTO）
- [ ] 新加的代码 review 检查项：是否引入了新的静态可变字段？如有，必须先评审
- [ ] **Session 接口完备性**（★ v3）：`IGameSession` / `ISkirmishSession` / `ICnCNetGameSession` / `ILANGameSession` / `IMissionSession` / `IPlayerSlot` / `IGameOptionsState` 均已定义；`CnCNetSession` **不**实现 `IGameSession`
- [ ] **资源接口完备性**（★ v3）：`IResource` / `IMapResource` / `IMissionResource` / `IGameModeResource` / `IResourceManifest` / `IResourceCatalog` 均已定义；`MapEntry` 等作为默认实现可编译通过
- [ ] `EnvironmentServices.Resolve<T>()` 在未注册时抛出含类型名与提示信息的 `InvalidOperationException`（无 ProgramConstants fallback）

---

## 12. 待确认问题

> **状态（2026-07-19）**：以下问题已由用户拍板，L1 已按此落地。

1. ~~`IResource` 元数据字段是否齐全？~~ → **按文档现状写入接口**；后续可扩展。
2. ~~`ICnCNetGameSession` 是否应包含 `SkillLevel` / `Passworded`？~~ → **两者都纳入**。
3. ~~`LobbyActionContext` 瘦身幅度？~~ → **直接只持 `ISkirmishSession`**（开发测试版可破坏过渡兼容）。
4. ~~`IResourceManifest` 的 L1 范围？~~ → **NoOp 适配器**。
5. ~~`CnCNetGameRoomSession` 直接实现还是适配器？~~ → **直接实现 `ICnCNetGameSession`**（冲港口，语义干净）。

---

## 13. 关键设计决策汇总

| # | 决策 | 理由 |
| - | ---- | ---- |
| 1 | 环境树用抽象基类 | 有派生属性逻辑（`ResourcesPath` / `BaseResourcesPath`） |
| 2 | 资源树用基接口 + 默认实现 | C# 单继承限制 + Map/Mission 需独立继承链 |
| 3 | 会话树纯接口继承 | ① mod 多继承需求 ② mock 友好 ③ 没有共享逻辑 |
| 4 | `CnCNetSession` 不实现 `IGameSession` | 网络层 vs 游戏会话解耦；经 `ActiveGameRoom` 暴露 |
| 5 | 保留旧 DTO 作默认实现（如 `MapEntry : IMapResource`） | 渐进迁移，旧代码不破坏 |
| 6 | `Resolve` 未注册抛异常 | INI 层未稳定期优先暴露 bug；无 fallback |
| 7 | 不引入 DI 容器 | desktop 客户端启动一次、无热重载，DI 收益不抵复杂度 |
| 8 | 不抽 Logger 接口 | 只写不读，无跨测试串扰 |
| 9 | 颜色目录与分配状态分离 | 目录 = `IMultiplayerColorCatalog`；分配 = `IPlayerSlot.ColorIndex` |
| 10 | 不做 L0 | v2 的 `[Collection]` + Reset 已实施 |
| 11 | 资源接口化全做 | 含完整元数据，为在线更新 / 增量包 / mod 扩展铺路 |
| 12 | `IMissionSession` 不继承 `ISkirmishSession` | 战役与遭遇战语义平级 |

### 工时预估


| 阶段 | v2 工时 | v3 工时 | 变化原因 |
| ---- | ------- | ------- | -------- |
| L1 接口设计 | 已完成 | 1 天（重写本设计） | 资源元数据 + Session 树重新设计 |
| L1 基础设施 | 3 天 | 5 天 | 加 `IResource` + `IResourceManifest` + Session 接口体系 |
| L1 UI 层迁移 | 4 天 | 5 天 | Applier 改接 `ISkirmishSession` |
| L1 后补功能改造 | 1 天 | 2 天 | `ChangeMapAction` → `IMapResource`、`LobbyActionContext` → `ISkirmishSession` |
| L1 测试改造 | 2 天 | 3 天 | mock `IGameSession` 体系 |
| **总计** | **10 天** | **~16 天（3 周）** | |
| L2 DI 容器（可选） | +2 周 | +2 周 | 仍不建议 |

---

*文档结束。实施前请先评审 §12 待确认问题并拍板。*
