# Mix 资源注入切换设计（方案 B：Straw 虚表劫持）

> **状态**：设计稿（IDA 已核对钩点；实现未开始）。
> **决策日期**：2026-07-25。
> **对照**：方案 A（启动前换文件）见 [`mix-asset-switching.md`](./mix-asset-switching.md)。
> **范围**：引擎侧 DLL（解密器扩展 / 独立 `*.dll`）+ ClientAvalonia **参数化启动**；不在 ClientAvalonia 仓库内实现原生 DLL 本体（可另仓），但客户端侧通道与契约在本仓库落地。

## 1. 问题陈述

MG 需要按模式使用不同的 `expandmg99.mix` 内容（典型差异：内部 `rulesmg.ini`）。

| 模式 | 期望 |
|---|---|
| Campaign | 战役版 mix / rules |
| Skirmish / CnCNet / LAN | 遭遇战版 mix / rules |

**方案 A** 在 `Process.Start` 前把槽位文件硬链接到不同源 mix。  
**方案 B**（本文）：不换磁盘上的 `expandmg99.mix`，而是在引擎 **Straw 读路径**上劫持，按启动参数决定返回哪套内容。

### 1.1 命名纠正

口语里的 “strap” **不是** `MixFileClass::Bootstrap`，而是 **Straw**（`FileStraw` / `BlowStraw`）的虚表读入口。

| 名称 | 地址（YR） | 作用 |
|---|---|---|
| `MixFileClass::Bootstrap` | `0x5301A0` | 扫 `expand99`→`expand00`、generics、cache…，多次 `new` + **call `Bootstrap_0`**；**不**承担解密主钩 |
| `MixFileClass::Bootstrap_0`（CTOR） | `0x5B3C20` | 建 `CCFileClass` + **`FileStraw`**，再经 **`[vtable+8]`（`Straw::Get`）** 读头/索引 |
| `Hook_MixSetup` 落点 | `0x5B3CE5` 一带 | **读 4 字节头**的 `call [edx+8]` 之前/之中（IDA：`0x5B3CF4`） |
| `Hook_MixBody` 落点 | `0x5B3DCB` 一带 | **读完 index 表**的 `call [edx+8]` 之后（IDA：`0x5B3DC8` call，下一指令 `0x5B3DCB`） |

## 2. IDA 核对结论（2026-07-25）

### 2.1 CTOR 读路径（`Bootstrap_0` @ `0x5B3C20`）

关键顺序（与现有解密器一致）：

1. `*this = MixFileClass::vftable`
2. `CCFileClass::ctor` + `strdup` 文件名
3. **`FileStraw::vftable` 写入栈上 Straw 对象**（约 `0x5B3CC2`）
4. `Exists`；失败则早退
5. **`call [FileStraw.vtable+8](straw, &header, 4)`** ← MixSetup / 头
6. 视 flags 可能挂上 BlowStraw 链（加密 mix）
7. 再 `Get` 读 count/size（6 字节等）
8. `operator new(12 * count)` 分配 index
9. **`call [straw.vtable+8](straw, index, 12*count)`** ← MixBody / 索引
10. `Seek` 算 data base；挂入 mix 链表
11. 析构栈上 BlowStraw / FileStraw

钩必须挂在 **Straw::Get（vtable+8）**，而不是 `Bootstrap` 入口。`Bootstrap` 只负责「开哪些 mix 文件」。

### 2.2 `Bootstrap` @ `0x5301A0`

- `for (i = 99; i >= 0; --i)`：存在则 `MixFileClass::Bootstrap_0()`（即 `expand##.mix`）
- 随后固定再挂 generics / cache / cachemd / local 等
- **解密主逻辑不在这里**；对方案 B 的意义是：`expandmg99.mix` 会在该循环里被 CTOR 打开一次，Straw 钩对该文件生效一次即可

### 2.3 与现有解密器的关系

现有解密器（类 `basic_patch.dll` 形态）已用同一手法：在 CTOR 内把 Straw 虚表换成己方，再在 `Get` 里做头/体处理。

方案 B 可选：

| 路线 | 说明 | 风险 |
|---|---|---|
| **B1. 扩展现有解密器** | 在已有 `Hook_MixSetup` / `Hook_MixBody` 中增加「按 Mode 替换/重定向」 | 与解密强耦合；升级解密器要回归 |
| **B2. 独立 DLL（推荐）** | 新建 `mg_mix_mode.dll`（名称待定），同样劫持 Straw，或 **链式** 接到解密器之后/之前 | 需约定与解密器的钩链顺序，避免互相覆盖虚表 |

**推荐 B2**：职责分离——解密器只解密；mode DLL 只做参数化内容策略。若两者都要改同一 Straw，必须实现 **vtable 链式转发**（保存原 `Get`，己方 `Get` 末尾 call 原函数），禁止「后加载 DLL 直接盖掉前一个虚表且不转发」。

## 3. 目标与非目标

### 3.1 目标

- **运行时**按启动参数决定 `expandmg99.mix`（或其中 `rulesmg.ini`）的有效内容。
- **可控参数化启动**：ClientAvalonia 在 `Process.Start` 前写入 Mode；DLL 在 mix 挂载时读取。
- **解耦**：
  - 客户端不知道 Straw/地址；
  - DLL 不知道 Campaign UI；
  - 契约只有「Mode 字符串 + 可选资源表」。
- **稳定**：无参数 / 参数非法 / 资源缺失时行为明确；可日志；不拖垮启动（策略见 §8）。
- 与方案 A **可并存或互斥**（配置开关），便于回退。

### 3.2 非目标

- 不把 native DLL 源码塞进本 C# 仓库（可 submodule / 旁仓）。
- 不修改 `gamemd.exe` 静态补丁（一律 Syringe 注入）。
- 不在 `Bootstrap` 上挂主逻辑。
- 不依赖「外部散文件 `rulesmg.ini` 覆盖 mix 内同名」（不可靠，属旧方案 B，已废弃命名）。

## 4. 方案对比（A vs B）

| 维度 | A 文件槽位切换 | B Straw 注入 |
|---|---|---|
| 改动面 | 仅客户端 + 两份源 mix | 客户端通道 + native DLL +（可选）旁路资源 |
| 磁盘 | 启动前改 `expandmg99.mix` 指向 | 槽位文件可保持不变 |
| FHSH | 联机双方槽位内容必须一致 | 磁盘 mix 哈希可稳定；**内容由 DLL 决定**，联机双方 DLL/参数/旁路资源必须一致 |
| 与解密器 | 无交互 | **必须**协调虚表链 |
| 引擎版本敏感 | 低 | **高**（地址 / 内联变化需跟进） |
| 失败模式 | 源缺失可 Fail 弹窗 | 钩失败可能静默用原 mix，或直接崩溃——需显式检测 |
| 实现复杂度 | 低 | 中高 |

**建议**：产品上以 **B 为主路径**（一次 mix、参数化）；A 保留为 **无 DLL / DLL 失败时的回退**（`MixAssetSwitching.ini` + `MixAssetBackend=File|Straw|Auto`）。

## 5. 总体架构

```
┌─────────────────────────────────────────────────────────────┐
│ ClientAvalonia                                              │
│  GameLaunchService.TryLaunch                                │
│    session.PrepareSpawnFiles()                              │
│    MixLaunchParamWriter.Write(mode)     ← 唯一碰「契约文件」 │
│    [可选] MixAssetSwitcher (方案 A 回退)                     │
│    GameProcessLauncher → Syringe → gamemd-spawn             │
└───────────────────────────┬─────────────────────────────────┘
                            │ 契约：Mode + 资源表（见 §6）
                            ▼
┌─────────────────────────────────────────────────────────────┐
│ Syringe 注入链                                              │
│  Ares / 解密器 / mg_mix_mode.dll …                          │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│ mg_mix_mode.dll                                             │
│  启动：读契约 → 缓存 Mode                                   │
│  钩：MixFileClass CTOR 内 Straw vtable+8                    │
│    Hook_MixSetup  @ 头 4 字节读                             │
│    Hook_MixBody   @ index 读完后                            │
│  策略：仅当目标 mix（如 expandmg99）匹配 Mode 时改写/重定向  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.1 分层（解耦）

| 层 | 职责 | 不知道 |
|---|---|---|
| `IMixLaunchParamWriter`（C#） | 按 `LaunchModeLabel` 映射 Mode，写契约 | Straw、地址、mix 格式 |
| 契约文件 / 环境（数据） | Mode、目标 mix 名、旁路资源路径 | 谁写谁读之外的实现 |
| `mg_mix_mode.dll` | 读契约、钩 Straw、执行策略 | Avalonia / Session |
| 现有解密器 | 解密 | Mode（除非走 B1 合并） |

客户端 **不** 调用任何 native export 做切换；只保证「启动瞬间契约正确」。

## 6. 参数化启动契约

### 6.1 通道选型

| 通道 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| **Sidecar 文件**（推荐） | 与 Syringe/工作目录天然契合；易日志；易手工复现 | 需约定路径与生命周期 | **默认** |
| 环境变量 | 实现简单 | 部分注入链 / 子进程不一定继承；难排查 | 备选 |
| 命令行 | 可见 | Syringe 参数格式敏感；易与 `-SPAWN` 冲突 | 不推荐 |
| 共享内存 / 管道 | 无落盘 | 过重；DLL 加载时序难 | 否 |

### 6.2 Sidecar 格式（建议）

路径（相对 `GamePath`）：

```
mix_launch_mode.ini
```

```ini
[General]
; 与方案 A ModeMapping 同一套名字，便于 Auto 回退共用配置
Mode=Skirmish
; 写契约时的 Unix 秒或 ISO 时间，便于诊断陈旧文件
WrittenAt=2026-07-25T12:00:00Z
; 可选：客户端版本 / pipeline id
ClientTag=ClientAvalonia

[Targets]
; 仅处理这些 mix 基名（大小写不敏感）
Mix=expandmg99.mix

[Skirmish]
; 策略见 §7；示例：旁路 rules 文件或旁路整包
RulesOverride=Resources/MixOverrides/rulesmg.skirmish.ini
; 或：RedirectMix=expandmg99-skirmish.mix

[Campaign]
RulesOverride=Resources/MixOverrides/rulesmg.campaign.ini
```

**生命周期**：

1. 客户端每次 `TryLaunch` **覆盖写入**（幂等）。
2. DLL 在 **首次需要 Mode 时** 读取并缓存到进程内；可选读后 **不删**（便于 syringe.log / 崩溃分析）或删（防下次误用）。推荐：**读后保留，下次启动覆盖**；另加 `WrittenAt` 过期告警（例如 > 1h 视为陈旧 WARN）。
3. 若文件缺失：按配置 `OnMissingParam=UseDefault|Fail`（默认 `UseDefault=Campaign` 或 `Passthrough` 不改写——产品需定一条，见 §8）。

### 6.3 客户端映射（与方案 A 对齐）

复用同一张表，避免两套 Mode 名：

| `LaunchModeLabel` | Mode |
|---|---|
| `Skirmish` | `Skirmish` |
| `CnCNetMultiplayer` | `Skirmish` |
| `LanMultiplayer` | `Skirmish` |
| `Campaign` | `Campaign` |

配置可放在现有 `Resources/MixAssetSwitching.ini` 的 `[ModeMapping]` + 新增：

```ini
[General]
Backend=Straw          ; File | Straw | Auto
; Auto：优先 Straw（DLL 在场）；否则 File

[Straw]
ParamFile=mix_launch_mode.ini
OnMissingParam=Passthrough
DllProbe=mg_mix_mode.dll   ; Auto 时用于探测
```

## 7. DLL 策略：钩子里做什么

在 **已确认是目标 mix**（文件名匹配 `[Targets].Mix`）且 Mode 有效时：

### 7.1 策略选项

| 策略 | 时机 | 做法 | 适用 |
|---|---|---|---|
| **S1. 整包重定向** | MixSetup 前 / FileStraw 绑定文件时 | 打开 `RedirectMix` 代替原路径 | 两套完整 mix 已存在；实现接近 A，但发生在引擎内 |
| **S2. Index 项改写** | Hook_MixBody 后 | 在 index 中定位 `rulesmg.ini`（CRC/ID），把 offset/size 指到旁路 blob（DLL 内嵌或外部文件映射进可读缓冲） | 只换 rules，包其余不变 |
| **S3. Get 期数据替换** | 后续 `Straw::Get` / mix 读 body | 命中 rules 数据范围时喂替代字节 | 灵活；状态机复杂 |

**推荐落地顺序**：先 **S1**（行为清晰、易测），再视体积/发行需求做 **S2**。

### 7.2 钩子职责划分

```
Hook_MixSetup（头 4 字节 Get 附近）:
  - 识别当前 Mix 文件名（CTOR 参数 / CCFileClass 路径）
  - 若非目标 → 原样转发
  - 若 S1 → 切换底层文件后让原 Get 读替代包头
  - 与解密器链：先/后约定见 §7.3

Hook_MixBody（index Get 返回后）:
  - 若 S2 → 改 index 中 rules 项
  - 校验 count/size，防止越界
  - 写诊断（OutputDebugString / 旁路 log 文件）
```

### 7.3 与解密器虚表链

```
FileStraw::Get  (引擎)
    ↑ 被替换为
Decryptor::Get  （若存在：解密）
    ↑ 再包一层或被包
MixMode::Get    （Mode 策略）
```

约定（写进双方 README）：

1. **后加载者必须保存并调用先前 `vtable[Get]`**。
2. 或：解密器提供显式「注册后处理」回调（若可改解密器，优于双钩）。
3. 加载顺序由 Syringe `.inj` / 文件名排序决定 → **必须在发行说明中钉死**，并在验收中验证「加密 expand + Mode 替换」同时成立。

## 8. 稳定性

| 情况 | 行为 |
|---|---|
| 无 sidecar | `OnMissingParam=Passthrough`：不改写，记 WARN；或 `Fail`：DLL 置错误标记，客户端无法直接知悉——**若要求 Fail，应用方案 A 的客户端预检或 DLL 写 `mix_mode_status.ini` 供下次读**（同期启动难反馈）。产品建议：**客户端在 Straw 模式下仍校验旁路资源存在，缺失则中止启动**（与方案 A 的 Fail 对齐）。 |
| Mode 未知 | Passthrough + ERROR 日志 |
| 目标 mix 未打开 | 无操作（钩子按文件名过滤） |
| 虚表链被第三方打断 | 可能崩溃或静默失效 → 启动后可用「哨兵文件」或已知 rules 指纹做 smoke（进阶） |
| 引擎地址变更 | DLL 内地址表 / 特征码扫描；发版注明 YR/Ares 版本 |
| 联机 | 双方 Mode 必须同为 Skirmish；旁路资源纳入 FHSH 或单独约定哈希 |

**客户端 Fatal 预检（推荐，解耦且稳）**：

即使内容替换在 DLL，**资源是否存在**仍由 C# 在启动前检查（读同一份 INI 的 `RulesOverride` / `RedirectMix`）。缺失 → 弹窗中止，与方案 A 体验一致。DLL 内再失败则属于引擎期，只能日志。

## 9. ClientAvalonia 落地清单

本仓库实现（轻）：

| 项 | 说明 |
|---|---|
| `MixLaunchParamWriter` | `Write(IGameLaunchSession)` → 映射 Mode → 写 `mix_launch_mode.ini` |
| 配置 | `MixAssetSwitching.ini` 增加 `[Straw]` / `Backend` |
| `GameLaunchService.TryLaunch` | spawn 后、`TryStart` 前调用 Writer；可选 File 后端 |
| 预检 | Straw 模式下检查 Override/Redirect 文件存在 |
| 测试 | Writer 单测；映射表单测；不测 native |

旁仓 / 游戏目录实现（重）：

| 项 | 说明 |
|---|---|
| `mg_mix_mode.dll` + Syringe 注入描述 | Straw 链 + S1/S2 |
| 与解密器联调说明 | 加载序、链式 Get |
| 旁路资源 | `rulesmg.*.ini` 或第二套 mix |

## 10. 实现顺序

1. **钉契约**：定 sidecar 路径、字段、Mode 名、OnMissingParam。
2. **客户端 Writer + 预检 + 接线**（可先不依赖 DLL，手工改 ini 验证「DLL 读得到」）。
3. **DLL 最小 S1**：仅 `expandmg99` + RedirectMix；打日志。
4. **与解密器联调**（加密包）。
5. （可选）S2 只替 `rulesmg.ini`。
6. （可选）`Backend=Auto` 与方案 A 回退。

## 11. 验收

1. IDA/运行时：确认钩在 `0x5B3CF4` / `0x5B3DC8` 的 Get 路径上触发，且 **Bootstrap@5301A0 无解密主钩依赖**。
2. Campaign / Skirmish 各一局：游戏内规则差异符合预期；磁盘 `expandmg99.mix` 可不变（S1 重定向时打开的是旁路文件）。
3. 缺 sidecar / 缺旁路资源：客户端预检 Fail；或 Passthrough 行为符合配置。
4. 与解密器同时启用：加密 mix 仍可读 + Mode 生效。
5. CnCNet：双方 Skirmish 参数一致可开局。
6. 连续切换模式多次：无陈旧 Mode（每次启动覆盖写入）。

## 12. 开放问题

| 项 | 状态 |
|---|---|
| B1 扩展解密器 vs B2 独立 DLL | **倾向 B2**；待你最终拍板 |
| S1 整包重定向 vs S2 只换 rules | **先 S1** |
| `OnMissingParam` 默认 Passthrough 还是 Fail | 建议客户端预检 Fail + DLL Passthrough |
| DLL 正式文件名 / Syringe 注入方式 | 待定 |
| 旁路资源是否进 FHSH | 联机必须定 |

## 13. 相关索引

| 资源 | 说明 |
|---|---|
| IDA `MixFileClass::Bootstrap_0` | `0x5B3C20` |
| IDA `call [vtable+8]` 头 | `0x5B3CF4`（Hook_MixSetup 区 `0x5B3CE5`） |
| IDA `call [vtable+8]` index | `0x5B3DC8`（Hook_MixBody 后 `0x5B3DCB`） |
| IDA `MixFileClass::Bootstrap` | `0x5301A0`（仅挂载循环） |
| [`mix-asset-switching.md`](./mix-asset-switching.md) | 方案 A |
| `GameLaunchService` / `GameLaunchSessions` | 参数化启动接线 |
| `FileExtensions.CreateHardLinkFromSource` | 仅方案 A |

---

## 附录 A：CTOR 伪代码（核对用）

```
MixFileClass::Bootstrap_0(name, ...):
  vftable = MixFileClass
  file = CCFileClass(name)
  straw.vftable = FileStraw          // 解密器/Mode DLL 在此之后劫持
  if !file.Exists: fail
  straw.Get(&hdr, 4)                 // ← MixSetup
  // flags → maybe BlowStraw chain
  straw.Get(&meta, 6)
  index = new (12 * count)
  straw.Get(index, 12 * count)       // ← MixBody
  link into MixFile list
  destroy temporary straws
```

## 附录 B：客户端一行接线（示意）

```csharp
session.PrepareSpawnFiles();
_mixLaunch.Apply(session); // Writer + 可选 File 后端 + 预检
if (_mixLaunch.IsFatal) { /* ShowError; return false; */ }
GameProcessLauncher.TryStart(...);
```

`_mixLaunch` 内部按 `Backend` 分支，保证 GameLaunchService 仍然只依赖一个门面。
