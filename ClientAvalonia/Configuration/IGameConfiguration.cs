using ClientCore;
using ClientCore.Enums;
using Rampastring.Tools;

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

    /// <summary>当前 mod 的游戏类型（TS / YR / Ares）。决定 spawn / 文件扫描分支。</summary>
    ClientType ClientGameType { get; }

    /// <summary>玩家名最大长度（IRC 注册 / UI 校验）。</summary>
    int MaxNameLength { get; }

    /// <summary>GetGameExecutableName()：主游戏可执行文件名（如 gamemd.exe）。</summary>
    string GetGameExecutableName();

    /// <summary>GameLauncherExecutableName：启动器可执行文件名（可能为空）。</summary>
    string GameLauncherExecutableName { get; }

    /// <summary>MPMapsIniPath：MPMaps.ini 完整相对路径。</summary>
    string MPMapsIniPath { get; }

    /// <summary>DefaultFrameSendRate：CnCNet spawn 协议默认值。</summary>
    int DefaultFrameSendRate { get; }

    /// <summary>DefaultMaxAhead：CnCNet spawn 协议默认值。</summary>
    int DefaultMaxAhead { get; }

    /// <summary>DefaultProtocolVersion：CnCNet spawn 协议默认值。</summary>
    int DefaultProtocolVersion { get; }

    /// <summary>DefaultSkillLevelIndex：建房默认技能等级索引。</summary>
    int DefaultSkillLevelIndex { get; }

    /// <summary>SkillLevelOptions：建房技能等级选项 CSV。</summary>
    string SkillLevelOptions { get; }

    /// <summary>SidebarHack：spawn.ini 是否启用 SidebarHack。</summary>
    bool SidebarHack { get; }

    /// <summary>取自定义本地化字符串（ClientDefinitions.ini [CustomStrings]）。</summary>
    string GetCustomLocalizedString(string key);

    // ---- Phase A 扩展：高频读路径（原 ClientConfiguration.Instance 直读）----

    string InstallationPathRegKey { get; }
    string MapFileExtension { get; }
    string AllowedCustomGameModes { get; }
    string BattleFSFileName { get; }
    string FinalSunIniPath { get; }
    string CnCNetChatChannel { get; }
    string CnCNetGameBroadcastChannel { get; }
    string CnCNetPlayerCountURL { get; }
    string DefaultChatColor { get; }
    int DefaultPersonalChatColorIndex { get; }
    string Sides { get; }
    string ChangelogURL { get; }
    string SpectatorInternalSideIndex { get; }
    string InternalSideIndices { get; }
    bool CopyMissionsToSpawnmapINI { get; }
    int SendSleep { get; }
    string ExtraExeCommandLineParameters { get; }
    string UnixGameExecutableName { get; }
    string TranslationIniName { get; }
    IEnumerable<string> IRCServers { get; }
    OSVersion GetOperatingSystemVersion();
    IniSection? GetParserConstants();
    void RefreshSettings();
    void RefreshTranslationGameFiles();
    IEnumerable<ClientCore.I18N.TranslationGameFile> TranslationGameFiles { get; }

    /// <summary>
    /// Escape hatch：尚未抽到接口的冷门成员。生产代码优先用上方显式成员；
    /// 仅 Adapter 实现；测试可抛 NotSupportedException。
    /// </summary>
    ClientConfiguration Legacy { get; }
}
