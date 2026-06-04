# ClientCore（核心库）

## 这是什么
`ClientCore` 是客户端的“非 UI 核心层”库，负责：
- 读取与组织各类 INI 配置（客户端配置、游戏选项、网络定义等）
- 路径/常量/启动环境相关信息
- 用户设置（带默认值合并、强类型 Setting 封装）
- 翻译/本地化（I18N）
- INI 预处理（例如 section 继承）
- 统计数据读写/解析
- 进程启动、存档管理、文本敏感词过滤等通用工具

该项目被 `ClientGUI`、`ClientUpdater` 以及主程序项目引用。

## 依赖
- NuGet：`System.Text.Encoding.CodePages`、`Ude.NetStandard`
- Project：`Rampastring.Tools`（在 `Rampastring.XNAUI` 仓库内）

## 目录结构概览
- `Enums/`：核心枚举与解析器（例如客户端类型、排序方向、私信权限策略等）
- `Extensions/`：常用扩展方法（字符串、枚举、集合、文件、IniFile 等）
- `I18N/`：翻译/本地化核心
- `INIProcessing/`：INI 预处理（生成/合并/后台任务等）
- `Settings/`：强类型 INI Setting（Bool/Int/String/List/Range…）及用户设置容器
- `Statistics/`：战局/玩家统计数据库读取与管理
- 顶层 `.cs`：路径常量、配置聚合、加载界面资源选择、存档管理、进程启动等

## 关键模块/文件说明
### 配置与常量
- `ClientConfiguration.cs`
  - 读取并缓存多个 INI：客户端设置（`DTACnCNetClient.ini`）、游戏选项（`GameOptions.ini`）、客户端定义（`ClientDefinitions.ini`）、网络定义（优先 `NetworkDefinitions.local.ini`）等
  - 暴露大量强类型属性（主题数量、窗口标题、URL、渲染限制、音效/提示配置等）
  - 负责刷新翻译相关的“随语言切换需要复制/校验”的游戏文件列表（见 `RefreshTranslationGameFiles()` 调用链）

- `ProgramConstants.cs`
  - 启动路径与游戏根目录推断（向上查找 `Resources` 目录）
  - 网络/协议常量、端口、文件名常量（如 `spawn.ini`、`spawnmap.ini` 等）
  - 玩家名变更事件 `PlayerNameChanged`

- `UserINISettings.cs`
  - 用户设置单例（必须 `Initialize(userIniFileName)` 之后才能使用）
  - 将 `Resources/UserDefaults.ini` 与用户 ini 合并（用户值覆盖默认值）
  - 通过 `ClientCore.Settings/*` 提供强类型设置项（视频、音频、多人、过滤器等）

### INI 与继承/预处理
- `CCIniFile.cs`
  - 在 `IniFile` 基础上增加“基类 section 合并”能力：section 中可声明 `$BaseSection` 进行键继承
  - 支持 `INISystem/BasedOn` 形式的“基于某个 ini”合并（含 `$THEME_DIR$` 替换）

- `INIProcessing/IniPreprocessor.cs`
  - 输入 ini -> 输出生成 ini（清理旧输出）
  - 支持 `BaseSection` 递归继承，将缺失键从基 section 补齐
  - 输出文件带注释，提示其来源与生成方式

### 本地化（I18N）
- `I18N/Translation.cs`
  - 翻译表载入与合并（从 `TranslationIniName` 读取 `Values` 字典）
  - 翻译元数据（Name/Author/Culture/MapEncoding），以及缺失 key 跟踪
  - 自动选择默认语言：从启动时的 UI Culture 向父 Culture 回退匹配

- `I18N/TranslationGameFile.cs`
  - 描述“随翻译一起分发/复制到游戏目录”的文件（Source/Target/是否参与完整性校验）

### 其它核心工具
- `LoadingScreenController.cs`
  - 根据 `UserINISettings` 的分辨率高度与阵营 `sideId`，构造加载图资源路径（`Resources/l{height}s{sideId}{rand}.pcx`）

- `SavedGameManager.cs`
  - 多人存档管理：计数、时间戳列表、初始化（拷贝 `spawn.ini` 到 `Saved Games/spawnSG.ini`）、保存重命名（`SAVEGAME.NET` -> `SVGM_XXX.NET`）、清理旧存档等

- `ProcessLauncher.cs`
  - 简单封装 `Process.Start`（`UseShellExecute = true`），用于启动外部进程/链接

- `ProfanityFilter.cs`
  - 文本敏感词/辱骂词过滤：支持通配符转正则，提供 `IsOffensive` 与 `CensorText`（替换为 `*`）
  - 代码中自带一组默认词表（README 不展开具体内容）

- `OSVersion.cs`
  - 操作系统枚举（Windows/Unix 等），供上层根据平台选择行为

### 统计
- `Statistics/StatisticsManager.cs`
  - 读取/迁移统计数据库（如 `Client/dscore.dat`），支持按版本解析并在需要时触发重存
  - 解析比赛与玩家统计（`MatchStatistics`/`PlayerStatistics` 等）

## 常见调用关系（简略）
- UI 层（`ClientGUI`）通过 `ClientConfiguration`/`UserINISettings` 获取配置、主题、翻译与 UI 参数
- 更新器（`ClientUpdater`）复用 `ClientCore` 的路径、扩展与工具能力

## 文件速查（按路径）
### 顶层
- `CCIniFile.cs`：增强版 IniFile（section 继承 + BasedOn 合并）。
- `ClientConfiguration.cs`：客户端/游戏/网络等配置聚合与读取。
- `LoadingScreenController.cs`：按分辨率与阵营选择加载图资源路径。
- `OSVersion.cs`：操作系统枚举。
- `ProcessLauncher.cs`：启动外部进程的轻量封装。
- `ProfanityFilter.cs`：文本敏感词检测与打码（支持通配符）。
- `ProgramConstants.cs`：路径探测、协议/端口常量、全局运行参数。
- `SavedGameManager.cs`：多人存档清理、初始化与重命名。
- `UserINISettings.cs`：用户设置单例（合并 UserDefaults + 强类型 setting）。

### Enums/
- `AllowPrivateMessagesFromEnum.cs`：私信权限策略枚举。
- `ClientType.cs`：客户端/游戏类型枚举。
- `ClientTypeHelper.cs`：客户端类型字符串解析/映射。
- `SortDirection.cs`：排序方向枚举。

### Extensions/
- `ArrayExtensions.cs`：数组相关扩展。
- `EnumerableExtensions.cs`：集合/枚举扩展。
- `EnumExtensions.cs`：枚举扩展（常用转换/辅助）。
- `FileExtensions.cs`：文件操作扩展（例如硬链接等，供上层复用）。
- `IniFileExtensions.cs`：IniFile 读写辅助扩展。
- `StringExtensions.cs`：字符串处理扩展（拆分、清理、INI 字符串处理等）。

### I18N/
- `Translation.cs`：翻译表、缺失 key 追踪、默认语言选择与 dump。
- `TranslationGameFile.cs`：翻译附带文件描述（复制/校验策略）。

### INIProcessing/
- `IniPreprocessInfoStore.cs`：记录 Base ini 与生成 ini 的哈希，用于判断是否需要重新预处理。
- `IniPreprocessor.cs`：按 `BaseSection` 规则生成预处理后的 ini。
- `PreprocessorBackgroundTask.cs`：后台批量预处理 `/INI/Base/*.ini`，并更新 `ProcessedIniInfo.ini`。

### PlatformShim/
- `EncodingExt.cs`：编码相关“平台补丁”（注册 CodePages provider、提供 ANSI/UTF8NoBOM 等）。

### Settings/
- `IIniSetting.cs` / `INISetting.cs`：Setting 抽象与基类。
- `BoolSetting.cs` / `IntSetting.cs` / `DoubleSetting.cs` / `StringSetting.cs`：常用强类型 setting。
- `IntRangeSetting.cs`：带范围约束的整数 setting。
- `StringListSetting.cs`：字符串列表 setting。

### Statistics/
- `GenericStatisticsManager.cs`：统计管理基类（版本读取、按索引访问）。
- `StatisticsManager.cs`：统计数据库读取/迁移（`dscore.dat`）与清理。
- `MatchStatistics.cs`：单局比赛统计模型，支持从日志解析与序列化写入。
- `PlayerStatistics.cs`：单个玩家统计模型，支持序列化写入。
- `DataWriter.cs`：二进制写入辅助扩展（int/long/bool/string）。
- `GenericMatchParser.cs`：比赛统计解析器基类。
- `GameParsers/LogFileStatisticsParser.cs`：从游戏日志（如 `DTA.log`）抽取 K/D/Score/Economy 等。
