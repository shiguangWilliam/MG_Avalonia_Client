using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.Services;

/// <summary>Standalone user INI read/write aligned with ClientCore UserINISettings merge rules.</summary>
public sealed class UserIniSettingsStore : IUserSettingsStore
{
    private readonly string _settingsPath;
    private readonly string _defaultsPath;
    private IniDocument _userDocument = new();
    private IniDocument? _defaultsDocument;

    public UserIniSettingsStore(ClientEnvironment environment)
    {
        _settingsPath = environment.UserSettingsPath;
        _defaultsPath = Path.Combine(environment.GameRoot, "Resources", "UserDefaults.ini");
        Reload();
    }

    public string SettingsPath => _settingsPath;

    public string GetString(string section, string key, string defaultValue)
    {
        if (_userDocument.GetSection(section)?.KeyExists(key) == true)
            return _userDocument.GetStringValue(section, key, defaultValue);

        if (_defaultsDocument?.GetSection(section)?.KeyExists(key) == true)
            return _defaultsDocument.GetStringValue(section, key, defaultValue);

        return defaultValue;
    }

    public bool GetBool(string section, string key, bool defaultValue)
        => IniConversions.BooleanFromString(GetString(section, key, string.Empty), defaultValue);

    public int GetInt(string section, string key, int defaultValue)
        => int.TryParse(GetString(section, key, string.Empty).Trim(), out int parsed) ? parsed : defaultValue;

    public void SetString(string section, string key, string value)
        => _userDocument.SetStringValue(section, key, value);

    public void SetBool(string section, string key, bool value)
        => _userDocument.SetBooleanValue(section, key, value);

    public void SetInt(string section, string key, int value)
        => _userDocument.SetIntValue(section, key, value);

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        _userDocument.Save(_settingsPath);
    }

    public void Reload()
    {
        _userDocument = File.Exists(_settingsPath)
            ? IniDocument.Load(_settingsPath)
            : new IniDocument();

        _userDocument.FilePath = _settingsPath;
        _defaultsDocument = File.Exists(_defaultsPath) ? IniDocument.Load(_defaultsPath) : null;
    }
}
