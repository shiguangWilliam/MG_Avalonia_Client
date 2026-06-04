using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Loading;
using ClientCore;
using ClientUpdater;

namespace ClientAvalonia.Services;

public sealed class UiStateService : IUiStateService
{
    private readonly ClientEnvironment _environment;

    public UiStateService(ClientEnvironment environment)
    {
        _environment = environment;
        RefreshMainMenuState();
    }

    public string GameVersion { get; private set; } = string.Empty;

    public string UpdateStatusText { get; private set; } = string.Empty;

    public string OnlinePlayerCountText { get; private set; } = "—";

    public bool CanLaunchGame { get; private set; }

    public void RefreshMainMenuState()
    {
        GameVersion = ReadGameVersion();
        UpdateStatusText = "Click to check for updates.";
        int count = CnCNetSessionService.Instance.OnlinePlayerCount;
        OnlinePlayerCountText = count >= 0 ? count.ToString() : "—";
    }

    public void SetUpdateStatusText(string text) => UpdateStatusText = text;

    public void SetCanLaunchGame(bool enabled) => CanLaunchGame = enabled;

    public void SetOnlinePlayerCount(int count)
        => OnlinePlayerCountText = count >= 0 ? count.ToString() : "—";

    private string ReadGameVersion()
    {
        if (ClientStartupService.IsUpdaterInitialized && !ClientConfiguration.Instance.ModMode)
        {
            string version = Updater.GameVersion;
            if (!string.IsNullOrWhiteSpace(version) && !version.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return "v." + version;
        }

        if (!string.IsNullOrWhiteSpace(ProgramConstants.GAME_VERSION)
            && !ProgramConstants.GAME_VERSION.Equals("Undefined", StringComparison.OrdinalIgnoreCase)
            && !ProgramConstants.GAME_VERSION.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return "v." + ProgramConstants.GAME_VERSION;

        string versionFile = Path.Combine(_environment.GameRoot, "version");
        if (File.Exists(versionFile))
        {
            foreach (string line in File.ReadAllLines(versionFile))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                    return "v." + trimmed["Version=".Length..].Trim();
            }
        }

        return "v.?";
    }
}
