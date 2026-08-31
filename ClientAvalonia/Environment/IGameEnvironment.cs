namespace ClientAvalonia.GlobalState.Environment;

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
    /// Base 资源目录 = GamePath/Resources（与 ProgramConstants.GetBaseResourcePath 一致）。
    /// Renderers.ini、Compatibility\DLL、GameOptions.ini、ClientDefinitions.ini 等跨主题共享文件在此。
    /// 旧 DTA 布局若把文件放在 Resources/Base，由调用方做额外兜底搜索。
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
