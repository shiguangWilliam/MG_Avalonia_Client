# 全局可变状态重构设计文档

> **状态**：设计稿，待人工实施。本文件提供接口 / 抽象类定义、迁移路径，但不直接修改代码。
> **作者**：根据 DeepSeek 评审的"P1 全局可变状态较多"项整理。

## 1. 现状盘点

调研结果显示，全局可变状态散布在以下 7 个静态入口：

| 入口 | 类型 | 主要可变字段 | 用途 | 串扰风险 |
|---|---|---|---|---|
| `ProgramConstants` | static class | `LocalGame`, `PLAYERNAME`, `GamePath`, `GAME_VERSION`, `HostedGameRoot`, `RESOURCES_DIR` | 全局游戏常量 | **极高** |
| `ClientConfiguration.Instance` | singleton | `LocalGame`, `SettingsIniName`, `ModMode`, ... | INI 配置访问 | 高 |
| `CnCNetSession.Instance` | singleton | `LocalNick`, `Connection`, `ActiveGameRoom`, `GameRoom` | IRC 连接 + 房间 | 高 |
| `CnCNetSessionService.Instance` | singleton | 包 `CnCNetSession.Instance`，加线程调度 | UI 入口 facade | 中（已封装） |
| `Updater` | static class | `CustomComponents`, `GameVersion`, `OnLocalFileVersionsChecked` | 版本检查 / 自更新 | 中 |
| `GameResourceCatalog.Instance` | singleton | `Maps`, `GameModes`, `Missions` | 地图 / mode 缓存 | 中 |
| `Logger` | static class | log 文件路径 | 日志 | 低（只写） |

**实际观察到的串扰**：今天测试套件失败的 `LnodWorkspace_SynthesizesCncnetLnodChannels` 就是 `ProgramConstants.LocalGame` 在多个测试间残留 `"mg"` 值导致的。

## 2. 设计目标

1. **测试隔离**：单元测试能在不污染全局的情况下设置 `LocalGame = "lnod"` 等局部状态。
2. **依赖清晰**：所有可变全局依赖必须显式注入（构造函数 / 方法参数），不能用 `Instance` 隐式获取。
3. **不破坏现有调用点**：300+ 处使用 `ProgramConstants.LocalGame` 的代码不可能一次性改完，必须有兼容桥接。
4. **保留 singleton 性能优势**：生产路径上仍走单例（避免每次都走接口调度的虚函数开销）。
5. **跨线程安全**：多线程访问的入口（如 `CnCNetSession`）保留锁。

## 3. 分层重构策略

按"成本 / 收益"分三层：

| 层 | 范围 | 成本 | 收益 |
|---|---|---|---|
| **L0** 短期缓解 | 测试加 `[Collection]` 标注 | 5 分钟 | 立即修掉今天的测试失败 |
| **L1** 抽象接口 | 抽 `IGameEnvironment` / `ILocalGame` 等接口，UI 层依赖接口 | 1-2 周 | 测试可注入 mock |
| **L2** DI 容器 | 引入 `Microsoft.Extensions.DependencyInjection`，singleton 通过容器管理 | 3-4 周 | 完整解耦 |

**强烈建议先做 L0 + L1，跳过 L2**。L2 的 DI 容器对 desktop 客户端收益有限（启动一次性注入、运行时无热重载需求），且会大幅改动 `Program.cs` / `App.axaml.cs`。

## 4. L0：短期缓解（立即实施）

### 4.1 测试集合标注

为所有触达 `ProgramConstants` 的测试类加 `[Collection("ProgramConstantsSerial")]`：

```csharp
// ClientAvalonia.Tests/Integration/CnCNetMgAndLnodJoinIntegrationTests.cs
[Collection("ProgramConstantsSerial")]   // ← 已有，确认覆盖
[Trait("Category", "Integration")]
public sealed class CnCNetMgAndLnodJoinIntegrationTests : IDisposable
{
    // ...
}
```

需要补加的测试类（调研结果）：
- `CnCNetWelcomeChannelPlanTests`（已串扰）
- `CnCNetGameCollectionTests`
- `LobbyPlayerStateTests`
- `SkirmishSpawnWriterTests`
- 任何调用 `ProgramConstants.SetHostedGameRoot` / `BindToProgramConstants` 的测试

### 4.2 显式 Reset

`TempGameRoot` 已经在 `Dispose` 时清理 INI 文件，但**没清理 `ProgramConstants` 静态字段**。补充：

```csharp
// ClientAvalonia.Tests/Fixture/TempGameRoot.cs
public void Dispose()
{
    // 现有清理 ...
    ProgramConstants.LocalGame = null!;
    ProgramConstants.PLAYERNAME = null!;
    ProgramConstants.SetHostedGameRoot(string.Empty);
    ClientConfiguration.ResetInstance();
}
```

### 4.3 L0 工作量

**30 分钟内完成**。立刻解决今天的测试失败。

## 5. L1：接口抽象设计（核心）

### 5.1 接口清单

下面是建议引入的接口 + 抽象类。**只设计，不实施**——人工评审通过后再迁移代码。

#### 5.1.1 `IGameEnvironment`（替代 `ProgramConstants` 的读访问）

```csharp
// 新文件：ClientAvalonia/Environment/IGameEnvironment.cs
namespace ClientAvalonia.Environment;

/// <summary>
/// Read-only access to the resolved game environment. Mirrors ProgramConstants
/// but as an interface, so tests can inject mocks.
/// </summary>
public interface IGameEnvironment
{
    /// <summary>Local game identifier (e.g. "mg", "yr", "lnod"). Mirrors ProgramConstants.LocalGame.</summary>
    string LocalGame { get; }

    /// <summary>Resolved game root directory. Mirrors ProgramConstants.GamePath.</summary>
    string GamePath { get; }

    /// <summary>Resources directory (GamePath/Resources). Mirrors ProgramConstants.GetResourcePath().</summary>
    string ResourcesPath { get; }

    /// <summary>Base resource directory (Resources/Base). Mirrors ProgramConstants.GetBaseResourcePath().</summary>
    string BaseResourcesPath { get; }

    /// <summary>Current player name. Mirrors ProgramConstants.PLAYERNAME.</summary>
    string PlayerName { get; }

    /// <summary>Resolved game version, e.g. "1.0.4.2". May be "N/A" in ModMode.</summary>
    string GameVersion { get; }
}
```

#### 5.1.2 `IGameConfiguration`（替代 `ClientConfiguration.Instance` 的读访问）

```csharp
// 新文件：ClientAvalonia/Configuration/IGameConfiguration.cs
public interface IGameConfiguration
{
    string LocalGame { get; }
    string SettingsIniName { get; }
    string LongGameName { get; }
    string LongGameNameIRC { get; }
    bool ModMode { get; }
    bool CreateSavedGamesDirectory { get; }
    string CnCNetLiveStatusIdentifier { get; }

    // 设置访问（按需扩展）
    string GetCustomLocalizedString(string key);
    T GetSettingValue<T>(string key);
}
```

#### 5.1.3 `ICnCNetSession`（替代 `CnCNetSessionService.Instance`）

```csharp
// 新文件：ClientAvalonia/CnCNet/ICnCNetSession.cs
public interface ICnCNetSession
{
    CnCNetConnectionState ConnectionState { get; }
    string LocalNick { get; }
    CnCNetActiveGameRoom? ActiveGameRoom { get; }
    CnCNetGameRoomSession? GameRoom { get; }
    IReadOnlyList<CnCNetTunnel> Tunnels { get; }
    int OnlinePlayerCount { get; }

    event Action? StateChanged;
    event Action<CnCNetActiveGameRoom>? GameRoomJoined;
    event Action<string>? GameRoomJoinFailed;
    event Action<CnCNetStartGameInfo>? GameStarting;
    event Action? GameRoomHostAbandoned;

    void ConnectIfNeeded();
    void Disconnect();
    bool TryJoinGame(CnCNetHostedGameSummary game, string? password, out string message);
    bool TryLaunchHostedGame(out string message);
    void SendChatMessage(string message);
    // ... 其他 UI 调用点
}
```

#### 5.1.4 `IResourceCatalog`（替代 `GameResourceCatalog.Instance`）

```csharp
// 新文件：ClientAvalonia/Domain/IResourceCatalog.cs
public interface IResourceCatalog
{
    IReadOnlyList<MapEntry> Maps { get; }
    IReadOnlyList<GameModeEntry> GameModes { get; }
    IReadOnlyList<MissionEntry> Missions { get; }

    event Action? Loaded;

    void EnsureLoaded();
    GameModeEntry? GetGameModeForFilterIndex(int filterIndex);
    IReadOnlyList<MapEntry> GetMapsForFilterIndex(int filterIndex);
    int PickRandomMapIndex(IReadOnlyList<MapEntry> visible);
    bool ToggleFavoriteMap(MapEntry map, GameModeEntry? gameMode);
    IReadOnlyList<MapEntry> GetFavoriteMaps();
}
```

#### 5.1.5 `IUpdater`（替代 `Updater` static）

```csharp
// 新文件：ClientAvalonia/Updater/IUpdater.cs
public interface IUpdater
{
    string GameVersion { get; }
    IReadOnlyList<CustomComponent> CustomComponents { get; }

    event Action? OnLocalFileVersionsChecked;

    void Initialize(string gamePath, string baseResourcePath, string settingsIniName,
                    string localGame, string callingExecutable);
    void CheckLocalFileVersions();
}
```

### 5.2 抽象基类（可选）

为了减少 `Microsoft.Extensions.Logging` 等基础设施的重复，引入抽象基类：

```csharp
// 新文件：ClientAvalonia/Environment/GameEnvironmentBase.cs
public abstract class GameEnvironmentBase : IGameEnvironment
{
    public abstract string LocalGame { get; }
    public abstract string GamePath { get; }
    public abstract string PlayerName { get; }
    public abstract string GameVersion { get; }

    public virtual string ResourcesPath => Path.Combine(GamePath, "Resources");
    public virtual string BaseResourcesPath => Path.Combine(ResourcesPath, "Base");
}
```

子类：

```csharp
// 生产实现（包装 ProgramConstants）
internal sealed class ProgramConstantsGameEnvironment : GameEnvironmentBase
{
    public override string LocalGame => ProgramConstants.LocalGame;
    public override string GamePath => ProgramConstants.GamePath;
    public override string PlayerName => ProgramConstants.PLAYERNAME;
    public override string GameVersion => ProgramConstants.GAME_VERSION;
}

// 测试实现
internal sealed class MockGameEnvironment : GameEnvironmentBase
{
    public override string LocalGame { get; set; } = "mg";
    public override string GamePath { get; set; } = @"C:\fake\mg";
    public override string PlayerName { get; set; } = "TestPlayer";
    public override string GameVersion { get; set; } = "1.0.0";
}
```

### 5.3 接口注册中心（替代 DI 容器）

不引入 `Microsoft.Extensions.DependencyInjection`，做一个**极简**的服务定位器：

```csharp
// 新文件：ClientAvalonia/Environment/EnvironmentServices.cs
public static class EnvironmentServices
{
    private static readonly Dictionary<Type, Func<object>> _factories = new();
    private static readonly object _sync = new();

    public static void Register<T>(Func<T> factory) where T : class
    {
        lock (_sync) { _factories[typeof(T)] = factory; }
    }

    public static T Resolve<T>() where T : class
    {
        lock (_sync)
        {
            if (_factories.TryGetValue(typeof(T), out Func<object>? f))
                return (T)f();
            throw new InvalidOperationException($"No factory registered for {typeof(T).Name}");
        }
    }

    // Test seam: reset all registrations.
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
    EnvironmentServices.Register<IResourceCatalog>(() => GameResourceCatalog.Instance);
    EnvironmentServices.Register<IUpdater>(() => new UpdaterAdapter());
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
}
```

### 5.4 UI 层迁移（最关键的迁移路径）

`MainWindow` 当前直接调 `CnCNetSessionService.Instance`，迁移后通过 `ICnCNetSession` 接口：

```csharp
public partial class MainWindow : Window
{
    private readonly ICnCNetSession _cncnet;
    private readonly IGameEnvironment _env;
    private readonly IResourceCatalog _resources;
    private readonly IUpdater _updater;

    public MainWindow()
        : this(
            EnvironmentServices.Resolve<ICnCNetSession>(),
            EnvironmentServices.Resolve<IGameEnvironment>(),
            EnvironmentServices.Resolve<IResourceCatalog>(),
            EnvironmentServices.Resolve<IUpdater>())
    { }

    // 测试可注入的内部构造函数
    internal MainWindow(
        ICnCNetSession cncnet,
        IGameEnvironment env,
        IResourceCatalog resources,
        IUpdater updater)
    {
        _cncnet = cncnet;
        _env = env;
        _resources = resources;
        _updater = updater;
        // ... 现有 InitializeComponent 等
    }
}
```

**实际调用点改造**：约 50 处 `CnCNetSessionService.Instance.X` → `_cncnet.X`。

## 6. 迁移路径

### 6.1 阶段划分

```
Week 1: L0 + L1 基础设施
  Day 1-2: L0 测试集合 + Reset 补全（5min 实施，1d 测试）
  Day 3-4: 引入 IGameEnvironment / IGameConfiguration 接口
  Day 5:   EnvironmentServices 服务定位器

Week 2: L1 接口适配
  Day 1-2: ProgramConstantsGameEnvironment / ClientConfigurationAdapter 实现
  Day 3-4: MainWindow + LayoutEngine + BindingSession 改造为依赖接口
  Day 5:   测试改造，MockGameEnvironment 完备

Week 3 (可选): L1 完成
  Day 1-2: ICnCNetSession 适配（最复杂，CnCNet 路径）
  Day 3-4: IResourceCatalog + IUpdater 适配
  Day 5:   全套测试通过
```

### 6.2 兼容策略

迁移期间 `ProgramConstants` 和接口并存：
- 新代码：用 `IGameEnvironment`
- 旧代码：保留 `ProgramConstants.LocalGame`，不强制改
- 适配器（`ProgramConstantsGameEnvironment`）内部读 `ProgramConstants`，保证行为一致

这样**永远不会破坏现有功能**，迁移可以按文件 / 按模块渐进推进。

### 6.3 回滚策略

每个 commit 都能独立回滚。`EnvironmentServices` 失败时 fallback 到 `ProgramConstants`：

```csharp
public static T Resolve<T>() where T : class
{
    lock (_sync)
    {
        if (_factories.TryGetValue(typeof(T), out var f))
            return (T)f();
    }
    // Fallback: legacy singleton 路径
    if (typeof(T) == typeof(IGameEnvironment))
        return (T)(object)new ProgramConstantsGameEnvironment();
    throw new InvalidOperationException(...);
}
```

## 7. 工作量预估

| 阶段 | 工时 | 备注 |
|---|---|---|
| L0（必做） | 0.5 天 | 立即修测试串扰 |
| L1 接口设计 | 1 天 | 已完成（本文档） |
| L1 基础设施 | 3 天 | 接口 + 适配器 + 服务定位器 |
| L1 UI 层迁移 | 4 天 | MainWindow + LayoutEngine 等 |
| L1 测试改造 | 2 天 | 接口注入 mock |
| **L1 总计** | **~10 天**（2 周） | |
| L2 DI 容器（可选） | +2 周 | 不建议 |

## 8. 不推荐做的事

1. **不要一次性删除 `ProgramConstants` 静态字段**。300+ 处调用点，分阶段迁移期间必须保留。
2. **不要引入 `Microsoft.Extensions.DependencyInjection`**。Desktop 客户端启动一次、运行时无热重载，DI 容器收益不抵复杂度。
3. **不要把 `Logger` 抽接口**。Logger 只写不读，跨测试无串扰风险，无需抽象。
4. **不要把 `CnCNetSession` 单例改成 scoped**。一个进程只能有一个 IRC 连接，singleton 是 by design。

## 9. 验收标准

完成 L1 后应满足：
- [ ] 单元测试可以在不污染全局状态的前提下设置 `LocalGame = "lnod"`
- [ ] `LnodWorkspace_SynthesizesCncnetLnodChannels` 在全套测试中通过（不靠 `[Collection]` 串行也能通过）
- [ ] `MainWindow` 构造函数不再直接调用 `CnCNetSessionService.Instance` / `ProgramConstants` / `GameResourceCatalog.Instance`
- [ ] 所有 `public` 类的依赖通过构造函数显式声明（除非是数据 DTO）
- [ ] 新加的代码 review 检查项：是否引入了新的静态可变字段？如有，必须先评审

---

**请确认以下问题后开始实施**：
1. 是否同意先做 L0（30 分钟，立即修今天的测试失败）？
2. L1 是否同意本设计的接口划分？特别是 `IGameEnvironment` 的字段集合是否齐全？
3. 服务定位器（`EnvironmentServices`）vs DI 容器（`Microsoft.Extensions.DependencyInjection`），倾向哪个？
4. 是否同意 L2（DI 容器）暂时跳过？
