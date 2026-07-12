# ClientAvalonia 冗余度评估设计稿

> 本文件为**纯设计评判稿**，不涉及任何源码改动。
> 范围：`ClientAvalonia/` 项目当前快照（2026-07-12）。
> 对标基线：`DXMainClient/` 同名职责的 XNA/DX 实现。
> 评级约定：
> - **冗余度**：实际存在的重复 / 不该有的层级 / 死代码。低 = 好。
> - **风险**：保留该冗余可能引发的真实后果。低 / 中 / 高。
> - **建议**：纯设计层面的修复方向，明确是否在本次范围内执行。

---

## 0. 总体结论

| 维度 | 结论 |
|---|---|
| 当前 ClientAvalonia 冗余度 | **中**（少量真实冗余 + 一批"伪冗余"是合理 Avalonia 复刻） |
| 与 DXMainClient 的对齐性 | **良好**（行为契约对齐；少量 Avalonia 必需的分叉） |
| 阻塞性问题 | **0**（无致命重复 / 无循环依赖 / 无死锁级全局状态） |
| 真正需要处理的冗余 | **2 项**（见 §1） |
| 建议但不在本次范围处理的冗余 | **5 项**（见 §2，列入长期技术债清单） |

整体判断：**ClientAvalonia 不是"冗余堆叠"项目**。绝大多数看似重复的代码段，本质是 Avalonia 平台对 XNA 的 1:1 契约复刻，**不能合并**。真正值得处理的只有 §1 列出的两类。

---

## 1. 真实冗余（建议处理）

### R1. `TryWriteEarlyBoundInstallPath` 为死代码

**位置**

`ClientAvalonia/Core/InstallationRegistry.cs`（公开静态方法 `TryWriteEarlyBoundInstallPath`）

**证据**

- 全工作区文本搜索 `TryWriteEarlyBoundInstallPath`：
  - `InstallationRegistry.cs` —— 方法定义本身
  - 无任何调用方
- `PreStartup.cs:69` 现在只调用 `TryRepairAllCandidates`，且注释已说明：

```69:69:ClientAvalonia/Core/PreStartup.cs
        InstallationRegistry.TryRepairAllCandidates(gameRoot);
```

**成因**

上一轮"启动早期注册表自愈"修复时，引入了 `TryWriteEarlyBoundInstallPath` 作为 PreStartup 阶段的早期写入路径；后来统一收敛到 `TryRepairAllCandidates`，但旧公开方法没有删除。

**冗余度**：高（公开 API + 零调用方）
**风险**：中
- 该方法签名 `public static`，作为客户端公开表面会被外部插件 / 二次开发误用。
- 一旦被外部依赖，未来清理将引入破坏性变更。

**设计建议**（不在本次执行）
1. 将其标记为 `[Obsolete("Use TryRepairAllCandidates instead.")]`，或
2. 直接删除（推荐——目前确无调用方，删除无破坏）。
3. 如果保留，应加 `internal` 而非 `public`，避免进入公共 API 表面。

---

### R2. `"SOFTWARE\\" + InstallationPathRegKey` 字符串拼接在 3 个文件重复

**位置**

| 文件 | 行号 | 角色 |
|---|---|---|
| `ClientAvalonia/Core/InstallationRegistry.cs` | 44 | **规范源**（`RegistryKeyPath` 属性） |
| `ClientAvalonia/Core/InstallationRegistry.cs` | 67 / 108 / 165 | 内部对每个候选 key 遍历，**合理**（候选名动态） |
| `ClientAvalonia/CnCNet/CnCNetIdentity.cs` | 22, 56 | **冗余复制** |
| `ClientAvalonia/CnCNet/CnCNetOnlineIdentity.cs` | 68, 107 | **冗余复制** |

**证据**

```43:44:ClientAvalonia/Core/InstallationRegistry.cs
    public static string RegistryKeyPath =>
        "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey;
```

```21:22:ClientAvalonia/CnCNet/CnCNetIdentity.cs
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
```

```55:56:ClientAvalonia/CnCNet/CnCNetIdentity.cs
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
```

```67:68:ClientAvalonia/CnCNet/CnCNetOnlineIdentity.cs
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
```

```106:107:ClientAvalonia/CnCNet/CnCNetOnlineIdentity.cs
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    "SOFTWARE\\" + ClientConfiguration.Instance.InstallationPathRegKey);
```

**冗余度**：中（同一表达式出现 5 处，其中 4 处可改用规范源）
**风险**：低
- 不会产生运行期 bug；表达式足够简单。
- 但属于"显式知识扩散"：注册表路径的构造规则散落在多个无依赖文件里，任何规则变更（例如未来要支持 HKLM 回退、要加版本号子键）必须改 4 处。

**设计建议**（不在本次执行）
- `CnCNetIdentity` / `CnCNetOnlineIdentity` 改为引用 `InstallationRegistry.RegistryKeyPath`，自身不再字符串拼接。
- 不要把 `InstallationRegistry` 内部那 3 处（候选 key 遍历）也合并——它们是动态 key 名，语义不同。

---

## 2. 伪冗余 / 长期技术债（建议但不在本次处理）

以下项**不是当前必须处理的真实冗余**，列入清单便于后续技术债排期。

### T1. `PreStartup._ran` 与 `Startup.BootstrapSucceeded` 双层启动守卫

**现象**

- `PreStartup._ran`（实例静态布尔）防止 `Initialize` 被重复调用。
- `Startup.BootstrapSucceeded` + `Startup.BootstrapError` 在更下游做同样的失败短路。

**判定**：**伪冗余**
- 两者职责不同：前者防"重复初始化"，后者携带"为什么失败"的状态给 UI 层。
- 合并反而会让 `App.axaml.cs` 的失败提示链路失去信息载体。
- 不建议处理。

### T2. `CnCNetSession` 是"上帝类"

**现象**

`ClientAvalonia/CnCNet/CnCNetSession.cs` 单文件聚合：IRC 连接管理、频道状态、玩家列表、tunnel 列表、消息分发、登录状态机。

**判定**：**真实设计问题，但非"冗余"**
- 这是"职责过宽"，不是"重复代码"。
- 拆分收益高，但属于架构重构而非去冗余，本次不做。
- 建议长期路线：按子域（Connection / Channel / Players / Tunnels / Login）拆分为 5 个协作类。

### T3. `MainWindow.axaml.cs` Code-behind 过重

**现象**

`ClientAvalonia/Views/MainWindow.axaml.cs` 同时承载窗口生命周期、菜单路由、错误弹窗、版本号点击。

**判定**：**真实设计问题，但非"冗余"**
- 与 DXMainClient 的 XNA `MainWindow` 一一对应（DX 那边也是这样写的），属于"忠实复刻"。
- 改造建议（提取 ViewModel / Command 路由）属于 MVVM 化重构，**与 DXMainClient 对齐优先级 > 去冗余**。
- 不在本次处理。

### T4. Singleton 模式遍布全栈

**现象**

`ClientConfiguration.Instance`、`UserINISettings.Instance`、`CnCNetSession.Instance`、`ProgramConstants.*`（静态字段群）等全局可变状态。

**判定**：**伪冗余**
- 这些 Singleton 各自管理不同子域的配置 / 状态，并非同一份数据的重复存放。
- 全局可变状态的主要代价是**并发安全**与**测试隔离**（已在测试侧通过 `[Collection("ProgramConstantsSerial")]` + `internal` 测试缝缓解），不是冗余。
- 不建议作为"去冗余"目标处理。

### T5. DXMainClient 与 ClientAvalonia 之间的同名类

**现象**

例如 `Startup`、`PreStartup`、`ClientConfiguration`（在 ClientCore，共享）、`ProgramConstants`（共享）等同时存在于两个项目。

**判定**：**伪冗余，且是设计意图**
- 两个项目是**同一产品的两个 UI 平台实现**（XNA / Avalonia），故意保留同名同职责类以便行为对齐和契约测试。
- 合并两者会破坏"两个产品独立可发布"的约束。
- 不在本次处理。

---

## 3. 评估方法论（透明披露）

为避免"凭印象挑冗余"，本次评估采用以下三条客观依据：

1. **文本搜索**：在工作区所有 `.cs` 文件中按标识符 / 字面量检索重复出现。
2. **调用图反查**：对每个公开 API 反查调用方，零调用方者判为死代码候选。
3. **DX 对齐比对**：对每个"看似重复"的实现，先比对 DXMainClient 同职责代码，若 DX 侧亦如此则视为"契约复刻"而非冗余。

未采用的判定方式（避免误报）：
- 不以"代码行数"判冗余。
- 不以"是否有注释"判冗余。
- 不以"是否使用了某个现代特性（如 record / pattern matching）"判冗余。

---

## 4. 建议落地路径

| 优先级 | 项 | 建议动作 | 本次是否做 |
|---|---|---|---|
| P1 | R1 | 删除 `TryWriteEarlyBoundInstallPath` 或标 `[Obsolete]` | ❌（仅设计稿） |
| P2 | R2 | `CnCNetIdentity` / `CnCNetOnlineIdentity` 改引用 `RegistryKeyPath` | ❌（仅设计稿） |
| P3 | T2 | 拆分 `CnCNetSession` 上帝类（5 个子域） | ❌（独立重构 PR） |
| P4 | T3 | `MainWindow` MVVM 化 | ❌（独立重构 PR） |
| – | T1 / T4 / T5 | 保留现状 | – |

---

## 5. 范围声明

- 本稿仅覆盖 **ClientAvalonia** 子项目。
- 本稿不评判 `ClientCore`、`DXMainClient`、`Resources/` 的内部冗余（除非作为对齐基线引用）。
- 本稿的所有"行号 / 证据"基于 2026-07-12 工作区快照，后续代码变更可能使引用失效，引用前请重新检索。
- 本稿**不修改任何源码、测试、构建脚本**。
