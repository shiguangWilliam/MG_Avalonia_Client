# Mix 资源按模式切换设计文档（方案 A：启动前换文件）

> **状态**：设计定稿，待实现（可作为方案 B 的回退后端）。
> **决策日期**：2026-07-25。
> **范围**：仅 ClientAvalonia 启动路径；DXMainClient 不在本期。
> **对照**：方案 B（Straw 虚表劫持 + 参数化启动）见 [`mix-asset-straw-injection.md`](./mix-asset-straw-injection.md)。产品倾向 **B 为主、A 为回退**（`Backend=File|Straw|Auto`）。

## 1. 问题陈述

MG mod 需要按游戏模式加载不同的 `expandmg99.mix` 内容：

| 模式 | 期望资源 |
|---|---|
| 战役（Campaign） | 加载含战役 `rulesmg.ini` 的 `expandmg99.mix` |
| 遭遇战 / 多人（Skirmish / CnCNet / LAN） | 加载含遭遇战版 `rulesmg.ini` 的 `expandmg99.mix` |

**方案 A 的硬约束**（仅本文件路径）：

1. `.mix` 的加载由游戏引擎内部完成；本方案**不**改引擎。
2. 客户端只在 `Process.Start` 之前做**文件层面**操作。
3. 不原地改 mix 包内文件。

因此本方案让引擎槽位名 `expandmg99.mix` 在启动前指向不同源文件。若改为运行时劫持 Straw 读路径，见方案 B。

## 2. 目标与非目标

### 2.1 目标

- 启动前按模式把引擎槽位文件对齐到对应源 mix。
- **解耦**：mix 切换逻辑不知道「战役 / 遭遇战」语义；session 不知道 mix 文件名；启动服务只调一行。
- **稳定**：配置缺失、源文件缺失、磁盘操作失败都有明确、可测的行为；幂等；可诊断。
- **可扩展**：新增第二个可切换 mix（如 `audiomd.mix`）只改 INI，零代码。
- **可单测**：config / policy / switcher 三层可独立测；`GameLaunchService` 用 mock 测。

### 2.2 非目标

- 不修改游戏引擎 / Ares / Phobos 的 mix 加载逻辑。
- 不在运行时解包 / 重打包 mix。
- 不在本期接入 DXMainClient。
- 不在游戏退出后「还原」槽位（每次启动强制对齐即可）。
- 不处理非 `expand*.mix` 命名规则之外的自定义加载列表（若 MG 另有加载表，需另开议题）。

## 3. 方案选型

| 方案 | 做法 | 结论 |
|---|---|---|
| **A. 两套 mix 文件切换**（本文） | 源文件常驻；启动前硬链接/复制到引擎槽位名 | **采用为 File 后端 / 回退** |
| **B. Straw 虚表劫持** | 解密器类 DLL 挂 Straw::Get；客户端写 Mode 契约 | **产品主路径**，见 [`mix-asset-straw-injection.md`](./mix-asset-straw-injection.md) |
| C. 外部 `rulesmg.ini` 覆盖 | 游戏目录散文件覆盖 mix 内部 | **否决**：YR/Ares 下 mix 内通常优先；不可靠 |
| D. spawn.ini 段映射 | 指望 spawn 改写 rules 路径 | **否决**：引擎不支持 |
| E. 原地改 mix | 启动前改包内文件 | **否决**：格式/并发/校验风险高 |

## 4. 文件布局

游戏目录（`ProgramConstants.GamePath`）下：

```
<GamePath>/
  expandmg99-campaign.mix    ← 战役源（含战役 rulesmg.ini），常驻
  expandmg99-skirmish.mix    ← 遭遇战/多人源，常驻
  expandmg99.mix             ← 引擎实际加载的槽位（= 上面其中一个的硬链接或副本）
```

**命名约定**：

- 源文件带 `-campaign` / `-skirmish` 后缀，**不匹配**引擎 `expand<数字>.mix` 通配，因此不会被误加载。
- 槽位名必须与引擎实际加载名一致（默认 `expandmg99.mix`；可在 INI 配置）。

首次部署步骤见 §11。

## 5. 架构：四层单向依赖

```
┌─────────────────────────────────────────────────────────┐
│ GameLaunchService.TryLaunch                             │  启动编排，只调一行
│   mode = policy.ResolveMode(session)                    │
│   result = switcher.Apply(mode)                         │
│   if result.IsFatal → abort launch                      │
└──────────────────────────┬──────────────────────────────┘
                           │ 依赖
┌──────────────────────────▼──────────────────────────────┐
│ IMixAssetPolicy                                         │  唯一知道「游戏模式」的层
│   LaunchModeLabel → Mode 字符串（查 INI ModeMapping）    │
└──────────────────────────┬──────────────────────────────┘
                           │ 依赖
┌──────────────────────────▼──────────────────────────────┐
│ IMixAssetSwitcher                                       │  唯一碰磁盘的层
│   Apply(mode): 对每个 Slot，对齐到 mode 对应的源文件      │
└──────────────────────────┬──────────────────────────────┘
                           │ 依赖
┌──────────────────────────▼──────────────────────────────┐
│ MixAssetSwitchingConfig                                 │  唯一知道文件名的层
│   纯数据，从 Resources/MixAssetSwitching.ini 加载         │
└─────────────────────────────────────────────────────────┘
```

**解耦保证**：

| 层 | 知道什么 | 不知道什么 |
|---|---|---|
| Config | 槽位名、源文件名、Mode 映射表 | session、启动流程 |
| Policy | `LaunchModeLabel` → Mode | 文件路径、磁盘操作 |
| Switcher | Mode → 文件对齐 | Campaign / Skirmish 语义 |
| GameLaunchService | 调 policy + switcher | mix 文件名 |

新增槽位 / 改文件名 / 改模式映射 → **只改 INI**。

## 6. 配置：`Resources/MixAssetSwitching.ini`

独立文件（不并入 `ClientDefinitions.ini`）。缺失或解析失败 → 功能关闭，启动照常（见 §8）。

```ini
[General]
Enabled=yes

; 每个 [Slot.<id>] 定义一个可切换的引擎槽位
[Slot.ExpandMG]
EngineFile=expandmg99.mix
Mode.Campaign=expandmg99-campaign.mix
Mode.Skirmish=expandmg99-skirmish.mix
; 源文件缺失时的行为：Fail = 中止启动；Skip = 跳过该槽位继续
OnSourceMissing=Fail

; 未来加第二个槽位，零代码：
; [Slot.AudioMD]
; EngineFile=audiomd.mix
; Mode.Campaign=audiomd-campaign.mix
; Mode.Skirmish=audiomd-skirmish.mix
; OnSourceMissing=Fail

[ModeMapping]
; IGameLaunchSession.LaunchModeLabel → Mode 名（与 Mode.* 键对应）
Skirmish=Skirmish
CnCNetMultiplayer=Skirmish
LanMultiplayer=Skirmish
Campaign=Campaign
```

### 6.1 解析规则

- `[General].Enabled`：缺省 `yes`；`no` / `false` / `0` → 功能关闭。
- `[Slot.*]`：段名以 `Slot.` 开头即视为一个槽位；`SlotId` = 段名去掉前缀。
- `EngineFile`：必填；空 → 该槽位跳过并记 WARN。
- `Mode.<Name>=<SourceFile>`：至少一个 Mode 才有效；未知 Mode 在 Apply 时跳过该槽位。
- `OnSourceMissing`：`Fail`（默认，本期决策）或 `Skip`。
- `[ModeMapping]`：未知 `LaunchModeLabel` → Policy 返回 `null` → Switcher no-op（不视为 Fatal）。

### 6.2 加载路径

```
SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "MixAssetSwitching.ini")
```

与 `ClientDefinitions.ini`、`FHCConfig.ini` 同级（`Resources/` 下）。若存在多分辨率 / 基于 `BasedOn` 的覆盖链需求，本期不做；保持单文件。

## 7. 启动接线

### 7.1 现有启动链（不变部分）

```
GameLaunchService.TryLaunch
  ├── session.PrepareSpawnFiles()          ← 写 spawn.ini / spawnmap.ini
  ├── ★ MixAsset 切换（新增）
  └── GameProcessLauncher.TryStart
        └── GameLaunchPreparation.PrepareForLaunch()
              └── Process.Start(Syringe / gamemd -SPAWN)
```

参考实现位置：

- `ClientAvalonia/Services/GameLaunchService.cs`（`TryLaunch`，约 L99–107）
- `ClientAvalonia/Services/GameLaunchSessions.cs`（`IGameLaunchSession` + 三个实现）
- `ClientAvalonia/Services/GameProcessLauncher.cs`
- `ClientAvalonia/Services/GameLaunchPreparation.cs`

### 7.2 Session 不改接口

`IGameLaunchSession` **不**新增 `MixAssetMode` 属性。Policy 只读已有的 `LaunchModeLabel`：

| `LaunchModeLabel` | 默认映射 Mode |
|---|---|
| `Skirmish` | `Skirmish` |
| `CnCNetMultiplayer` | `Skirmish` |
| `LanMultiplayer` | `Skirmish` |
| `Campaign` | `Campaign` |

这样 session 层零改动，符合解耦目标。

### 7.3 `TryLaunch` 伪代码

```csharp
session.PrepareSpawnFiles();
LogSpawnArtifacts();

MixAssetSwitchResult mixResult = _mixAssets.Apply(session); // policy + switcher
GameLaunchDiagnostics.LogMixAssetResult(mixResult);
if (mixResult.IsFatal)
{
    ProgramConstants.IsLaunchingGame = false;
    ResolveCnCNet()?.EndLaunchPresenceKeepAlive();
    message = mixResult.UserFacingMessage; // 见 §8.3
    ClientDialogService.ShowError(errorOwner, "Error launching game", message);
    return false;
}

if (!GameProcessLauncher.TryStart(...))
    ...
```

`_mixAssets` 为注入的 `IMixAssetFacade`（或等价委托），构造时从 config 组装；测试可替换。

## 8. 稳定性与错误处理

### 8.1 决策矩阵

| 情况 | 行为 | 是否 Fatal（中止启动） |
|---|---|---|
| 配置文件不存在 | 功能关闭，记 INFO，启动继续 | 否 |
| 配置解析异常 | 功能关闭，记 ERROR，启动继续 | 否 |
| `Enabled=no` | no-op | 否 |
| `LaunchModeLabel` 无映射 | Policy 返回 null → no-op | 否 |
| 槽位无对应 Mode 源 | 该槽位 Skipped，记 WARN | 否 |
| **源 mix 文件缺失** | 按 `OnSourceMissing`；**默认 Fail** | **是（默认）** |
| 硬链接失败 | 回退 `File.Copy`（`FileExtensions.CreateHardLinkFromSource` 已内置 fallback） | 否（仅当 copy 也失败才看是否 Fatal） |
| 硬链接 + copy 均失败 | 记 ERROR；`IsFatal=true` | **是** |
| 槽位文件只读 / 占用 | 清只读后删；删失败短延迟重试 1 次；仍失败 → Fatal | **是** |
| 切换中途进程崩溃 | 下次启动幂等对齐，自动修正 | — |

**本期用户决策**：源缺失 = **Fail**（中止启动并弹窗）。每个 slot 仍可在 INI 用 `OnSourceMissing=Skip` 覆盖。

### 8.2 幂等与对齐判定

每次启动都走 Apply，不依赖「退出后还原」。

对齐判定（由快到慢）：

1. **快路径**：`Length` + `LastWriteTimeUtc` 一致（与 `GameLaunchPreparation.FilesMatchByMetadata` 同模式）→ 视为已对齐，跳过。
2. **硬链接快路径**（可选增强）：同卷且 Windows 下 `BY_HANDLE_FILE_INFORMATION.nFileIndex` 相同 → 已对齐。
3. **慢路径**：SHA1 一致 → 已对齐（仅在快路径不确定时使用；避免每次对几十 MB mix 算哈希）。

已对齐 → `AlreadyCurrent`，零磁盘写入。

### 8.3 用户可见错误文案

Fatal 时弹窗标题：`Error launching game`。

建议文案（可进翻译表，本期可用英文硬编码 + 后续接 Translation）：

```
Required game asset is missing or could not be prepared.

Mode: Campaign
Slot: ExpandMG
Expected file: expandmg99-campaign.mix
Game directory: <GamePath>

Please restore the missing file and try again.
```

日志侧同步输出完整路径与异常。

### 8.4 与 FHSH / 联机校验

`FileHashCalculator` 会哈希游戏目录下的 mix。联机时双方必须用**同一份** Skirmish 源（通过槽位对齐后哈希一致）。

- 战役与遭遇战 mix 哈希不同 → **正常**；联机不会走 Campaign 映射。
- 若房主与加入方的 `expandmg99-skirmish.mix` 内容不一致 → FHSH 不匹配，与现有「资源不一致」行为一致，不在本设计额外处理。

### 8.5 引擎误加载风险

源文件名 `expandmg99-campaign.mix` **不匹配** `expand##.mix`（数字后缀）模式，引擎不应加载。

**验收要求**：两份源都放进目录、槽位故意指错或清空时，启动战役/遭遇战行为符合预期（见 §12）。若 MG 另有自定义加载列表包含这些文件名，需改源文件命名或从加载列表排除。

## 9. 类型与文件清单

命名空间：`ClientAvalonia.Services.MixAssets`。

| 文件 | 职责 |
|---|---|
| `MixAssetSwitchingConfig.cs` | INI 加载与纯数据模型 |
| `IMixAssetPolicy.cs` + `ModeMappingPolicy.cs` | `LaunchModeLabel` → Mode |
| `IMixAssetSwitcher.cs` + `MixAssetSwitcher.cs` | Mode → 磁盘对齐 |
| `MixAssetSwitchResult.cs` | 不可变结果（含每槽状态、`IsFatal`、`UserFacingMessage`） |
| `IMixAssetFacade.cs` + `MixAssetFacade.cs` | 组装 policy+switcher，供 `GameLaunchService` 一行调用 |
| `Resources/MixAssetSwitching.ini` | 配置（部署到游戏 `Resources/`） |

接线改动：

| 文件 | 改动 |
|---|---|
| `GameLaunchService.cs` | 构造注入 facade；`TryLaunch` 在 spawn 后调用；Fatal 则中止 |
| `GameLaunchDiagnostics.cs` | 增加 `LogMixAssetResult` |
| （可选）DI / 构造处 | 默认从 `Resources/MixAssetSwitching.ini` 加载；加载失败则注入 no-op facade |

复用现有能力：

- `ClientCore.Extensions.FileExtensions.CreateHardLinkFromSource`（硬链接 + copy fallback）
- `Rampastring.Tools.Utilities.CalculateSHA1ForFile`（慢路径比对）
- `ClientDialogService.ShowError`（Fatal 弹窗）
- `ProgramConstants.GamePath` / `GetBaseResourcePath()`

## 10. 接口草图

```csharp
namespace ClientAvalonia.Services.MixAssets;

public enum OnSourceMissingBehavior { Fail, Skip }

public sealed class MixAssetSlotConfig
{
    public required string SlotId { get; init; }
    public required string EngineFile { get; init; }
    public required IReadOnlyDictionary<string, string> ModeToSource { get; init; }
    public OnSourceMissingBehavior OnSourceMissing { get; init; } = OnSourceMissingBehavior.Fail;
}

public sealed class MixAssetSwitchingConfig
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<MixAssetSlotConfig> Slots { get; init; } = Array.Empty<MixAssetSlotConfig>();
    public IReadOnlyDictionary<string, string> ModeMapping { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static MixAssetSwitchingConfig? TryLoad(string iniPath); // null = 功能关闭
}

public interface IMixAssetPolicy
{
    /// <returns>Mode 名；无映射时返回 null（表示不切换）。</returns>
    string? ResolveMode(IGameLaunchSession session);
}

public interface IMixAssetSwitcher
{
    MixAssetSwitchResult Apply(string? mode);
}

public interface IMixAssetFacade
{
    MixAssetSwitchResult Apply(IGameLaunchSession session);
}

public sealed class MixAssetSwitchResult
{
    public bool IsFatal { get; init; }
    public string UserFacingMessage { get; init; } = "";
    public IReadOnlyList<SlotSwitchResult> Slots { get; init; } = Array.Empty<SlotSwitchResult>();

    public static MixAssetSwitchResult Disabled { get; }
    public static MixAssetSwitchResult NoOp { get; }
}

public enum SlotSwitchStatus
{
    Applied,
    AlreadyCurrent,
    Skipped,
    SourceMissing,
    Failed,
}

public sealed class SlotSwitchResult
{
    public required string SlotId { get; init; }
    public required SlotSwitchStatus Status { get; init; }
    public string? Detail { get; init; }
}
```

`MixAssetSwitcher.Apply` 关键行为：

```
if !config.Enabled or mode is null/empty → Disabled / NoOp
foreach slot:
  if slot 无 Mode.mode → Skipped
  if 源文件不存在:
    Fail → 收集 Fatal；Skip → Skipped
  if 已对齐 → AlreadyCurrent
  else Swap(槽位 ← 源):
    清只读 → 删槽位 → CreateHardLinkFromSource(源, 槽位)
    成功 → Applied；失败 → Failed + Fatal
若任一 Fatal → IsFatal=true + 汇总 UserFacingMessage
```

## 11. 部署与首次准备

### 11.1 源 mix 准备（人工 / 打包脚本，非客户端职责）

1. 将现有战役用 `expandmg99.mix` 复制为 `expandmg99-campaign.mix`。
2. 将遭遇战版（内部 `rulesmg.ini` 已替换）命名为 `expandmg99-skirmish.mix`。
3. 两份放入 `GamePath`。
4. 原 `expandmg99.mix`：可保留；首次启动由 switcher 按模式覆盖为硬链接。若担心旧文件干扰，可先删后由 switcher 创建。

### 11.2 客户端部署

- 发布包带上 `Resources/MixAssetSwitching.ini`。
- `Scripts/build-clientavalonia.ps1` / patch 包需包含该 INI（与其他 Resources 一致）。
- **不**把两份源 mix 打进客户端仓库（体积大、属游戏资源）；由 mod 发行包提供。

### 11.3 缺源时的体验

因默认 `OnSourceMissing=Fail`：用户若未放置源 mix，点开始会弹窗中止，而不是静默用错资源。这是刻意的稳定性选择。

## 12. 测试计划

目录：`ClientAvalonia.Tests/Services/MixAssets/`。

### 12.1 单元测试

| 测试类 | 用例 |
|---|---|
| `MixAssetSwitchingConfigTests` | 正常解析；缺文件 → null；`Enabled=no`；空 Slot；非法 `OnSourceMissing`；大小写不敏感 ModeMapping |
| `ModeMappingPolicyTests` | 四个 `LaunchModeLabel` 映射正确；未知 label → null |
| `MixAssetSwitcherTests` | 临时目录：首次 Applied；二次 AlreadyCurrent；源缺失 Fail；源缺失 Skip；只读槽位可切换；源与槽位内容不同时强制切换 |
| `MixAssetFacadeTests` | 组装后 `Apply(session)` 端到端（假 session + 临时目录） |
| `GameLaunchService`（既有或新测） | facade 返回 Fatal → `TryLaunch` 返回 false 且不调 `GameProcessLauncher`；非 Fatal → 继续启动 |

### 12.2 手工验收（测试区）

1. 两份源齐全：战役启动后游戏内规则符合战役；遭遇战符合遭遇战。
2. 删掉 `expandmg99-campaign.mix` 后开战役 → 弹窗中止，不启动 Syringe。
3. 删配置 INI → 功能关闭，启动成功（槽位保持现状）。
4. `Enabled=no` → 同上。
5. 连续：战役 → 遭遇战 → 战役，槽位每次正确对齐；第二次同模式启动日志为 `AlreadyCurrent`。
6. CnCNet 联机双方均有相同 skirmish 源 → FHSH 通过并可开局。
7. 日志含 `MixAssetSwitcher` / `GameLaunchDiagnostics` mix 段，便于对比。

## 13. 实现顺序（建议）

1. **Config + Result 类型** + 单元测试。
2. **Switcher**（临时目录测）+ 复用 `CreateHardLinkFromSource`。
3. **Policy + Facade**。
4. **接线** `GameLaunchService` + Diagnostics；默认 `OnSourceMissing=Fail`。
5. **投放** `Resources/MixAssetSwitching.ini` + 构建脚本确认拷贝。
6. **手工验收**（§12.2）后再合入。

预估：核心实现 0.5–1 天；测试 + 接线 + 验收 0.5 天。

## 14. 风险与开放问题

| 项 | 说明 | 状态 |
|---|---|---|
| 引擎槽位名是否确为 `expandmg99.mix` | INI 可改；需 mod 侧确认 | **待确认** |
| 两份源 mix 是否已制作 | 客户端不制作；发行包需带齐 | **待确认** |
| 自定义加载列表是否误加载带后缀的源文件 | §8.5；需实测 | 验收项 |
| 翻译文案 | 本期英文硬编码；后续接 Translation.ini | 可后续 |
| DXMainClient 是否跟进 | 本期不做 | 非目标 |
| 跨卷游戏目录 | 硬链接失败会 copy；启动可能变慢，有日志 | 已知可接受 |

## 15. 相关代码索引

| 路径 | 相关性 |
|---|---|
| `ClientAvalonia/Services/GameLaunchService.cs` | 统一启动编排；接线点 |
| `ClientAvalonia/Services/GameLaunchSessions.cs` | `LaunchModeLabel` 来源 |
| `ClientAvalonia/Services/GameProcessLauncher.cs` | `Process.Start` |
| `ClientAvalonia/Services/GameLaunchPreparation.cs` | 启动前准备；`FilesMatchByMetadata` 模式可参考 |
| `ClientAvalonia/Services/GameLaunchDiagnostics.cs` | 诊断日志扩展点 |
| `ClientCore/Extensions/FileExtensions.cs` | `CreateHardLinkFromSource` |
| `ClientAvalonia/CnCNet/FileHashCalculator.cs` | FHSH 与 mix 哈希关系 |
| `Docs/design/error-handling.md` | 启动失败必须让用户看见原因 |

---

## 附录 A：为何不用「退出后还原」

退出还原会引入：

- 退出钩子失败 → 槽位残留错误状态；
- 崩溃 / 强杀 → 无法还原；
- 与「下次启动幂等对齐」重复。

采用**每次启动强制对齐 + 幂等跳过**更简单、更稳。

## 附录 B：与翻译文件同步的类比

`GameLaunchPreparation.SyncTranslationGameFilesIfNeeded` 已实践「源 → 目标硬链接 + 元数据快路径 + 只读」模式。本设计的 Switcher 是同一模式在 mix 槽位上的应用，并额外引入：

- 多槽位 / 多 Mode（配置驱动）；
- 源缺失 Fatal；
- 与启动模式映射解耦。
