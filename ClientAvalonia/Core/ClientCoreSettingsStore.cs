using ClientAvalonia.Services;
using ClientCore;

namespace ClientAvalonia.Core;

/// <summary>User settings via ClientCore UserINISettings (typed settings + merge with UserDefaults.ini).</summary>
public sealed class ClientCoreSettingsStore : IUserSettingsStore
{
    public string SettingsPath
        => UserINISettings.Instance.SettingsIni.FileName
           ?? throw new InvalidOperationException("UserINISettings has no backing INI path.");

    public string GetString(string section, string key, string defaultValue)
        => UserINISettings.Instance.GetValue(section, key, defaultValue);

    public bool GetBool(string section, string key, bool defaultValue)
        => UserINISettings.Instance.GetValue(section, key, defaultValue);

    public int GetInt(string section, string key, int defaultValue)
        => UserINISettings.Instance.GetValue(section, key, defaultValue);

    public void SetString(string section, string key, string value)
        => UserINISettings.Instance.SetValue(section, key, value);

    public void SetBool(string section, string key, bool value)
        => UserINISettings.Instance.SetValue(section, key, value);

    public void SetInt(string section, string key, int value)
        => UserINISettings.Instance.SetValue(section, key, value);

    public void Save() => UserINISettings.Instance.SaveSettings();

    public void Reload() => UserINISettings.Instance.ReloadSettings();
}
