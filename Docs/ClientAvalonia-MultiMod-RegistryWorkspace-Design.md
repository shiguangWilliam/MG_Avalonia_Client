# ClientAvalonia：多 Mod 注册表工作区 — 评估与设计稿

| 项 | 内容 |
|----|------|
| 状态 | 设计稿（未实现） |
| 范围 | 注册器 + 启动时 Mod 选择器 + ClientDefinitions 兼容兜底 |
| 产品落点 | Settings 默认窗口（注册器 / 回退选择器） |
| 关联现状 | `InstallationRegistry`、`FindGameRoot`、DX `WriteInstallPathToRegistry` |

---

## 1. 设计意图（采纳的核心思路）

1. **注册范式统一**  
   注册器按「mod 名」写入同结构注册表键：

   `HKCU\SOFTWARE\{{ModName}}\InstallPath = <游戏根绝对路径>`

   各 mod 共用同一范式，仅 `{{ModName}}` 不同（与 DX `RegistryInstallPath` / Avalonia EarlyBound 候选键一致）。

2. **启动优先读注册表 + 显式选择**  
   启动器启动时优先枚举全部候选键下的有效 `InstallPath`，弹出选择框；玩家选定后，将该目录设为**工作区（GameRoot / CWD）**。

3. **工作区 + 相对路径**  
   既有文件操作（Resources、INI、贴图、Updater、spawn 等）均建立在「绝对工作区 + 相对子路径」上，因此换工作区即可换 mod，无需改路径拼接范式。

4. **ClientDefinitions 兼容兜底**  
   对非关键键（例如部分环境有、YR/MG 扩展才有的键）采用「**缺省则忽略 / 用安全默认值**」，避免跨 mod、跨版本因缺键崩溃。

5. **产品形态**  
   将「注册器」与「回退 Mod 选择器」做到 **Settings 默认窗口**（首次启动或无有效工作区时也可作为引导页），而不是隐藏的命令行工具。

---

## 2. 评估结论

| 维度 | 结论 |
|------|------|
| 总体可行性 | **可行**，与现有架构同向 |
| 与 DX 兼容 | **兼容**：DX 本就写单键 `InstallPath`；本设计是「多键索引 + 显式选择」的超集 |
| 相对当前 Avalonia | 需把「第一个有效键抢占」改为「枚举 + 用户选择」；否则脏键会劫持 |
| 工程量 | 中（Hub/选择 UI + 注册器面板 + 启动闸门 + 读配置容忍） |
| 最大风险 | 选择前误绑工作区；非关键键被当成必需；向错误键批量写路径 |

**一句话：** 核心链路「注册表索引 → 选手动工作区 → 相对路径加载」成立；必须坚持**显式选择**，并用 ClientDefinitions 分层（关键 / 可选）保证跨版本。

---

## 3. 现状对照

| 能力 | 传统 DX | 当前 Avalonia | 本设计 |
|------|---------|---------------|--------|
| 找根 | 基本靠 exe/Resources 旁路 | 注册表优先 → CWD → exe，**取第一个有效** | 注册表枚举 → **选择框** → 绑定工作区 |
| 写注册表 | 本 mod 单键 `InstallPath`（可关） | 启动写 + 多键修复 | Settings 内注册器按 `{{ModName}}` 写入 |
| 配置 | 本目录 `ClientDefinitions.ini` | 选定根下同文件 | 同左；缺非关键键忽略 |

参考样例（MG 测试区 `ClientDefinitions.ini`）：含 `LocalGame`、`ClientGameType=YR`、`GameExecutableNames` 等；**未必含** `RegistryInstallPath`。说明注册键名与 ini 关键键必须解耦或可回退（见 §6）。

---

## 4. 注册表契约

### 4.1 键值范式（强制）

```
Hive:   HKEY_CURRENT_USER
Key:    SOFTWARE\{{ModName}}
Value:  InstallPath (REG_SZ) = 游戏根绝对路径（建议去尾部 \）
```

- `{{ModName}}`：与 DX `RegistryInstallPath` / 候选表一致的短名，如 `MomentOfGenesis`、`TiberianSun`、`MentalOmega`、`YR`。
- **禁止**把多个 mod 的路径写到同一个键。
- **禁止** Hub/注册器用「当前选中路径」批量覆盖所有候选键。

### 4.2 内置候选（可配置扩展）

默认扫描列表（与现网 EarlyBound 对齐，可配置追加）：

| ModName（键） | 典型用途 |
|---------------|----------|
| MomentOfGenesis | MG |
| TiberianSun | DTA / TS |
| CnCNet | 通用 / YR 系 |
| YR | YR |
| MentalOmega | MO |
| TwistedInsurrection | TI |

扩展方式：Settings 可维护「额外候选键」列表；或从已知根的 `RegistryInstallPath` 回读加入。

### 4.3 有效性校验

路径视为 **Ready** 当且仅当：

1. `InstallPath` 非空且目录存在；  
2. 存在 `{{InstallPath}}\Resources\ClientDefinitions.ini`。

否则标记 **Stale**（可提示「修复 / 清除 / 重新注册」）。

---

## 5. 启动与选择器（核心流程）

```
启动
  → 枚举候选键 InstallPath + 校验 Ready/Stale
  → 若存在 ≥1 个 Ready：
        打开「工作区 / Mod 选择」窗（Settings 默认页或专用启动闸门）
        用户选择 → 绑定工作区
  → 若 0 个 Ready：
        进入同窗口：注册器 / 浏览文件夹 / 从 CWD·exe 旁路探测
  → SetHostedGameRoot(workspace) + Environment.CurrentDirectory = workspace
  → 加载 ClientDefinitions（关键必须 / 可选忽略）
  → 进入主客户端流程
```

### 5.1 绑定工作区之后

所有 IO 继续走现有模型：

- `ProgramConstants.GamePath` = 工作区绝对路径  
- 资源：`GamePath + Resources/...`、主题相对路径  
- 用户文件：`GamePath + Client/`、`SettingsFile` 等  

### 5.2 退回 Mod 选择（会话级重绑，非真·热切换）

支持在客户端内提供 **「退回 / 切换 Mod」** 按钮：回到 Mod 选择阶段，用户重新选择根后再绑定工作区。  
这不是主界面内无缝热替换资源，而是 **主动退回选择器 → 清理旧会话 → 再绑新根 → 重走加载**，与「工作区 + 相对路径」完全兼容。

```
主界面 / Settings
  → 用户点击「退回 Mod 选择」
  → 【必须】清理当前工作区会话（见 §5.3）
  → 打开 Mod / 工作区选择窗（与启动闸门同一套 UI）
  → 用户选择新根（或同一根）
  → SetHostedGameRoot + CWD
  → 重新加载 ClientDefinitions / 主题 / 主流程
```

### 5.3 退回时必须清理的状态（强制）

退回选择器之前 **必须** 拆掉一切绑定旧 `GamePath` 的运行时状态，禁止半切换残留。至少包括：

| 类别 | 清理内容 |
|------|----------|
| UI | 关闭浮动层（Options / Campaign / 建房等）；丢弃当前窗口树与导航栈；释放主题/贴图缓存（ResourceResolver 搜索根） |
| 会话绑定 | 清除「当前 ModName / InstallPath」运行时标记；在选定新根前不得再读旧相对路径业务文件 |
| CnCNet | 断开 IRC / 离开房间；清空大厅与房间会话、聊天时间线、广播与 tunnel 状态；停止 launch keepalive |
| 启动与游戏 | 取消进行中的 launch / 预热；结束对旧根 spawn、渲染器、文件哈希会话的依赖 |
| Updater / 资源目录 | 使 Updater、地图/战役目录、自定义组件列表等按旧根缓存的数据失效，待新根再加载 |
| 设置绑定 | 丢弃未提交的 Options 绑定会话；新根后按新 `SettingsFile` 重新 Apply |
| 音频 / 杂项 | 停止依赖旧根资源的 BGM/音效（若有）；重置仅与当前工作区相关的服务单例状态 |

清理完成前 **不得** 展示新根的主菜单。选定新根后，按与冷启动绑定相同的路径重新 bootstrap（允许复用进程，不要求杀进程）。

### 5.4 记忆与默认

- 记忆「上次成功启动的 ModName + InstallPath」；下次仍展示选择框，但默认高亮上次项。  
- **不提供**「静默使用第一个有效键」作为默认生产行为（可留调试开关）。

### 5.5 与进程形态

推荐：**同一 ClientAvalonia 进程**在选定工作区后加载该根的 UI/资源；退回选择器后在同进程内重绑（§5.2）。  
可选增强：若某 mod 要求专用 exe，选择器可显示「用外部启动器打开」，但非本设计必选项。

**明确不做：** 不卸载 UI 的「真热切换」（主界面内直接换根且不清理会话）。换根唯一正规路径 = 退回选择器 + §5.3 清理。

---

## 6. ClientDefinitions：关键键 vs 可选键（兼容兜底）

目标：不同 mod / 不同客户端版本的 ini 键集合不一致时，**缺键不致命**。

### 6.1 关键键（缺失 → 拒绝绑定或明确报错）

用于标识与启动最低集：

| 键 | 作用 |
|----|------|
| `LocalGame` | CnCNet / 游戏集合标识（可有硬默认，但建议存在） |
| `SettingsFile` | 用户设置文件名 |
| `GameExecutableNames` 或等价启动可执行配置 | 能否开局 |
| （主题）`[Themes]` 至少一条 | UI 资源根 |

工作区校验阶段至少要求：`Resources/ClientDefinitions.ini` 可读。

### 6.2 注册相关

| 键 | 策略 |
|----|------|
| `RegistryInstallPath` | **推荐存在**。若缺失：注册器使用用户在 UI 中选定的 `{{ModName}}`，或 `LocalGame` 映射表，**不得崩溃**。 |

### 6.3 可选 / 非关键键（缺失 → 忽略或安全默认）

下列键在部分 mod（含 YR 系扩展）中才有意义；**无则忽略或用 ClientCore 已有默认值**，不阻断启动：

示例（非穷尽，实现时落成白名单/特性表）：

- `ClientGameType`（缺省可按 LocalGame 推断或默认 YR/TS）  
- `CnCNetProtocolRevision`、`CnCNetLiveStatusIdentifier`  
- `DiscordAppId`、`DisplayPlayerCountInTopBar`  
- `DisableComponentOptions`、`DisableMultiplayerGameLoading`  
- `MapCellSizeX/Y`、`SidebarHack`、`UseBuiltStatistic`  
- `LoadingScreenCount`、`BattleFSFileName`  
- URL 类：`CreditsURL`、`ChangelogURL`、`LongSupportURL`…  
- Unix / MapEditor / FSIni 等平台专用路径  

原则：

1. **读配置 API**：`GetOptionalString/Bool/Int(key, default)`，缺键不抛。  
2. **功能门控**：依赖某键的 UI（如组件页）在键缺失或显式 Disable 时隐藏，而不是空白崩溃。  
3. **禁止**把可选键提升为「文件存在性」硬依赖，除非该功能被用户点开。

### 6.4 与「YR」示例

MG 工作区可能声明 `ClientGameType=YR` 并带有一批 YR 向键；DTA/TS 工作区可能没有这些键。选择器换根后，加载逻辑必须按 **当前工作区 ini** 重新解析，并对缺键走 §6.3，从而实现跨 mod 兼容。

---

## 7. Settings 默认窗口产品设计

将能力收敛到 **Settings（或启动闸门复用同一 UI）**：

### 7.1 页：工作区 / Mod（默认首页）

| 区域 | 内容 |
|------|------|
| 列表 | 候选键 → 显示名（来自 LongGameName/WindowTitle）/ 路径 / Ready\|Stale |
| 操作 | 启动（绑定并进入）/ 设为默认高亮 / 清除陈旧键 / 打开文件夹 |
| 添加 | 「浏览文件夹…」→ 校验 ClientDefinitions → 可选写入注册表 |
| 退回入口 | 主界面或 Settings 提供「切换 Mod / 退回选择」→ §5.2～5.3 |

### 7.2 页或区：注册器

| 步骤 | 行为 |
|------|------|
| 1 | 选择或浏览游戏根 |
| 2 | 读取 ClientDefinitions；解析建议 `{{ModName}}`（RegistryInstallPath 或映射） |
| 3 | 允许用户确认/改选 ModName（防写错键） |
| 4 | 写入 `SOFTWARE\{{ModName}}\InstallPath` |
| 5 | 刷新选择列表 |

遵守用户设置中与 DX 对齐的「是否写入注册表」开关时：注册器可提示「当前禁止写入」，但仍允许**仅本次会话**选用该文件夹作为工作区（Manual 源）。

### 7.3 首次启动

无 Ready 项 → 直接进入该默认窗口，文案引导「注册本机 mod 或浏览安装目录」。

---

## 8. 模块职责（实现边界，供后续开发）

| 模块 | 职责 |
|------|------|
| `ModRegistryCatalog` | 枚举候选键、读 InstallPath、Ready/Stale |
| `ModWorkspaceBinder` | SetHostedGameRoot、CWD、会话内当前 ModName；**退回选择前执行 §5.3 清理** |
| `ModRegistrar` | UI + 写单键 InstallPath |
| `ClientDefinitionsLoader` | 关键失败快失败；可选键默认/忽略 |
| `WorkspacePickerView` | Settings 默认页 / 启动闸门 / 退回后的选择 UI |

**明确不做（本阶段）：**

- 主界面内不清理会话的「真热切换」  
- 向所有候选键写入同一路径  
- 用一份 ThemeMG 服务全部 mod  

---

## 9. 验收标准

1. 本机注册 ≥2 个不同 `{{ModName}}` 且路径不同时，启动必现选择列表，且两项均可选。  
2. 选择 A 后，日志 / 实际加载的 `ClientDefinitions`、`SettingsFile`、主题目录均来自 A。  
3. 故意缺少若干可选键的 ini 仍能进主菜单；缺少 `Resources/ClientDefinitions.ini` 不能标 Ready。  
4. 注册器只修改用户确认的那一个 `SOFTWARE\{{ModName}}`。  
5. 关闭「写入注册表」时，仍可用浏览方式绑定工作区（会话级）。  
6. 不存在「未点选择就因 first-hit 进入错误 mod」的默认路径。  
7. 「退回 Mod 选择」后：旧 IRC/房间/UI 树/主题缓存不得残留；再选另一 Ready 根可正常进主菜单。

---

## 10. 分阶段建议

| 阶段 | 交付 |
|------|------|
| P0 | 启动闸门：枚举 + 选择 + 绑定工作区；去掉生产路径上的静默 first-hit |
| P1 | Settings 默认页：注册器 + 清除 Stale + 浏览添加；上次选择记忆；**退回选择 + §5.3 清理清单落地** |
| P2 | ClientDefinitions 可选键白名单与功能门控；跨 MG/DTA 冒烟 |
| P3 | 候选键可配置、显示名/图标增强 |

---

## 11. 决策摘要

| 决策 | 选择 |
|------|------|
| 索引 | `HKCU\SOFTWARE\{{ModName}}\InstallPath` 同范式多键 |
| 启动 | 优先注册表枚举 + **强制选择**（记忆仅高亮） |
| 运行时 | 工作区绝对路径 + 相对资源 |
| 换根 | **退回选择器** + 强制清理会话（§5.2～5.3），非真热切换 |
| 兼容 | 非关键 ClientDefinitions 键缺省忽略 |
| 产品 | 注册器 + 选择器落在 Settings 默认窗口 |

本文档仅描述设计与评估，**不包含代码改动**。落地时另开实现任务，按 §10 分阶段提交。
