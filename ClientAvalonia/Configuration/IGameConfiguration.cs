namespace ClientAvalonia.Configuration;

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
}
