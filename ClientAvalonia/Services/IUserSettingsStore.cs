namespace ClientAvalonia.Services;

public interface IUserSettingsStore
{
    string SettingsPath { get; }

    string GetString(string section, string key, string defaultValue);

    bool GetBool(string section, string key, bool defaultValue);

    int GetInt(string section, string key, int defaultValue);

    void SetString(string section, string key, string value);

    void SetBool(string section, string key, bool value);

    void SetInt(string section, string key, int value);

    void Save();

    void Reload();
}
