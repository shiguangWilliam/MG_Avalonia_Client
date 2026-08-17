using ClientAvalonia.Rendering;
using ClientCore;
using System.Diagnostics;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Behaviors;

public static class MainMenuBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        RegisterOpen(registry, host, "btnSkirmish", "SkirmishLobby");
        registry.Register("btnOptions", _ => host.OpenOptionsOverlay());
        RegisterOpen(registry, host, "btnLan", "LANLobby");
        RegisterOpen(registry, host, "btnCnCNet", "CnCNetLobby");
        registry.Register("btnNewCampaign", _ =>
        {
            host.ShowStatus("Open: CampaignSelector (btnNewCampaign)");
            host.NavigateTo("CampaignSelector");
        });
        RegisterOpen(registry, host, "btnStatistics", "StatisticsWindow");
        RegisterOpen(registry, host, "btnExtras", "ExtrasWindow");

        registry.Register("btnLoadGame", _ => host.OpenLoadGameOverlay());
        registry.Register("btnMapEditor", vm =>
            host.ShowStatus("Click: Map Editor — launches external tool in XNA client"));
        registry.Register("btnRankedMatch", vm =>
            host.ShowStatus("Click: Ranked Match — not wired in Avalonia client yet"));
        registry.Register("btnCredits", vm =>
            host.ShowStatus("Click: Credits — opens URL in XNA client"));

        registry.Register("btnExit", _ => host.ExitApplication());

        // Aligned with DX MainMenu.LblVersion_LeftClick: opens ChangelogURL.
        // ProcessLauncher guards empty URLs (MG ClientDefinitions.ini has ChangelogURL=)
        // so the client no longer crashes when the URL is unconfigured.
        registry.Register("lblVersion", vm => OpenChangelogUrl(host));
        registry.Register("lblUpdateStatus", _ => host.CheckForUpdates());
    }

    private static void OpenChangelogUrl(IUiNavigationHost host)
    {
        string url = AppState.Configuration.Legacy.ChangelogURL ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            host.ShowStatus("Changelog URL is not configured.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            host.ShowStatus($"Opened: {url}");
        }
        catch (Exception ex)
        {
            host.ShowStatus($"Failed to open changelog: {ex.Message}");
        }
    }

    private static void RegisterOpen(BehaviorRegistry registry, IUiNavigationHost host, string controlId, string windowName)
        => registry.Register(controlId, vm =>
        {
            host.ShowStatus($"Open: {windowName} ({controlId})");
            host.NavigateTo(windowName);
            if (windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase))
                AppState.Lan.StartLobby();
        });
}
