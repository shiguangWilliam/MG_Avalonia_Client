using ClientCore;
using ClientCore.Enums;
using ClientCore.Extensions;
using ClientCore.I18N;
using Rampastring.Tools;

namespace ClientAvalonia.Configuration;

/// <summary>
/// 将 <see cref="ClientConfiguration.Instance"/> 适配为 <see cref="IGameConfiguration"/>。
/// </summary>
public sealed class ClientConfigurationAdapter : IGameConfiguration
{
    private readonly ClientConfiguration _config = ClientConfiguration.Instance;

    /// <inheritdoc />
    public ClientConfiguration Legacy => _config;

    /// <inheritdoc />
    public string LocalGame => _config.LocalGame;

    /// <inheritdoc />
    public string SettingsIniName => _config.SettingsIniName;

    /// <inheritdoc />
    public string LongGameName => _config.LongGameName;

    /// <inheritdoc />
    public string LongGameNameIRC => ReadSettingsString("LongGameNameIRC", LongGameName);

    /// <inheritdoc />
    public bool ModMode => _config.ModMode;

    /// <inheritdoc />
    public bool CreateSavedGamesDirectory => _config.CreateSavedGamesDirectory;

    /// <inheritdoc />
    public string CnCNetLiveStatusIdentifier => _config.CnCNetLiveStatusIdentifier;

    /// <inheritdoc />
    public ClientType ClientGameType => _config.ClientGameType;

    /// <inheritdoc />
    public int MaxNameLength => _config.MaxNameLength;

    /// <inheritdoc />
    public string GetGameExecutableName() => _config.GetGameExecutableName();

    /// <inheritdoc />
    public string GameLauncherExecutableName => _config.GameLauncherExecutableName;

    /// <inheritdoc />
    public string MPMapsIniPath => _config.MPMapsIniPath;

    /// <inheritdoc />
    public int DefaultFrameSendRate => _config.DefaultFrameSendRate;

    /// <inheritdoc />
    public int DefaultMaxAhead => _config.DefaultMaxAhead;

    /// <inheritdoc />
    public int DefaultProtocolVersion => _config.DefaultProtocolVersion;

    /// <inheritdoc />
    public int DefaultSkillLevelIndex => _config.DefaultSkillLevelIndex;

    /// <inheritdoc />
    public string SkillLevelOptions => _config.SkillLevelOptions;

    /// <inheritdoc />
    public bool SidebarHack => _config.SidebarHack;

    /// <inheritdoc />
    public string InstallationPathRegKey => _config.InstallationPathRegKey;

    /// <inheritdoc />
    public string MapFileExtension => _config.MapFileExtension;

    /// <inheritdoc />
    public string AllowedCustomGameModes => _config.AllowedCustomGameModes;

    /// <inheritdoc />
    public string BattleFSFileName => _config.BattleFSFileName;

    /// <inheritdoc />
    public string FinalSunIniPath => _config.FinalSunIniPath;

    /// <inheritdoc />
    public string CnCNetChatChannel => _config.CnCNetChatChannel;

    /// <inheritdoc />
    public string CnCNetGameBroadcastChannel => _config.CnCNetGameBroadcastChannel;

    /// <inheritdoc />
    public string CnCNetPlayerCountURL => _config.CnCNetPlayerCountURL;

    /// <inheritdoc />
    public string DefaultChatColor => _config.DefaultChatColor;

    /// <inheritdoc />
    public int DefaultPersonalChatColorIndex => _config.DefaultPersonalChatColorIndex;

    /// <inheritdoc />
    public string Sides => _config.Sides;

    /// <inheritdoc />
    public string ChangelogURL => _config.ChangelogURL;

    /// <inheritdoc />
    public string SpectatorInternalSideIndex => _config.SpectatorInternalSideIndex;

    /// <inheritdoc />
    public string InternalSideIndices => _config.InternalSideIndices;

    /// <inheritdoc />
    public bool CopyMissionsToSpawnmapINI => _config.CopyMissionsToSpawnmapINI;

    /// <inheritdoc />
    public int SendSleep => _config.SendSleep;

    /// <inheritdoc />
    public string ExtraExeCommandLineParameters => _config.ExtraExeCommandLineParameters;

    /// <inheritdoc />
    public string UnixGameExecutableName => _config.UnixGameExecutableName;

    /// <inheritdoc />
    public string TranslationIniName => _config.TranslationIniName;

    /// <inheritdoc />
    public IEnumerable<string> IRCServers => _config.IRCServers;

    /// <inheritdoc />
    public OSVersion GetOperatingSystemVersion() => _config.GetOperatingSystemVersion();

    /// <inheritdoc />
    public IniSection? GetParserConstants() => _config.GetParserConstants();

    /// <inheritdoc />
    public void RefreshSettings() => _config.RefreshSettings();

    /// <inheritdoc />
    public void RefreshTranslationGameFiles() => _config.RefreshTranslationGameFiles();

    /// <inheritdoc />
    public IEnumerable<TranslationGameFile> TranslationGameFiles => _config.TranslationGameFiles;

    /// <inheritdoc />
    public string GetCustomLocalizedString(string key)
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.CLIENT_DEFS);
        var ini = new IniFile(path);
        string raw = ini.GetStringValue("CustomStrings", key, key);
        return raw.L10N($"INI:CustomStrings:{key}");
    }

    private string ReadSettingsString(string key, string fallback)
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.CLIENT_DEFS);
        var ini = new IniFile(path);
        return ini.GetStringValue("Settings", key, fallback);
    }
}
