using ClientCore;
using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Multiplayer nickname persistence (UserINI [MultiPlayer] Handle), aligned with XNA GameOptionsPanel.</summary>
public static class PlayerNameSettings
{
    public static void ApplyFromUserSettings()
    {
        string name = Sanitize(UserINISettings.Instance.PlayerName.Value);
        if (!string.IsNullOrEmpty(name))
            ProgramConstants.PLAYERNAME = name;
    }

    public static void SaveFromInput(string rawName)
    {
        string name = Sanitize(rawName);
        if (string.IsNullOrEmpty(name))
            return;

        UserINISettings.Instance.PlayerName.Value = name;
        ProgramConstants.PLAYERNAME = name;
    }

    public static string LoadForDisplay()
    {
        string saved = Sanitize(UserINISettings.Instance.PlayerName.Value);
        if (!string.IsNullOrEmpty(saved))
            return saved;

        string current = Sanitize(AppState.Environment.PlayerName);
        return string.IsNullOrEmpty(current) || current.Equals("No name", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : current;
    }

    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return NameValidator.GetValidOfflineName(name);
    }
}
