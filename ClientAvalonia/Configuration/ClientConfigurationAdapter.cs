using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;

namespace ClientAvalonia.Configuration;

/// <summary>
/// 将 <see cref="ClientConfiguration.Instance"/> 适配为 <see cref="IGameConfiguration"/>。
/// </summary>
public sealed class ClientConfigurationAdapter : IGameConfiguration
{
    private readonly ClientConfiguration _config = ClientConfiguration.Instance;

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
