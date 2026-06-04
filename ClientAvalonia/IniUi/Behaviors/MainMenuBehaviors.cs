using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Behaviors;

public static class MainMenuBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host)
    {
        RegisterOpen(registry, host, "btnSkirmish", "SkirmishLobby");
        registry.Register("btnOptions", _ => host.OpenOptionsOverlay());
        RegisterOpen(registry, host, "btnLan", "LANLobby");
        RegisterOpen(registry, host, "btnCnCNet", "CnCNetLobby");
        registry.Register("btnNewCampaign", _ => host.OpenCampaignOverlay());
        RegisterOpen(registry, host, "btnStatistics", "StatisticsWindow");
        RegisterOpen(registry, host, "btnExtras", "ExtrasWindow");

        registry.Register("btnLoadGame", vm =>
            host.ShowStatus("Click: Load Game — not wired in Avalonia client yet"));
        registry.Register("btnMapEditor", vm =>
            host.ShowStatus("Click: Map Editor — launches external tool in XNA client"));
        registry.Register("btnRankedMatch", vm =>
            host.ShowStatus("Click: Ranked Match — not wired in Avalonia client yet"));
        registry.Register("btnCredits", vm =>
            host.ShowStatus("Click: Credits — opens URL in XNA client"));

        registry.Register("btnExit", _ => host.ExitApplication());

        registry.Register("lblVersion", vm =>
            host.ShowStatus("Click: version info (stub)"));
        registry.Register("lblUpdateStatus", _ => host.CheckForUpdates());
    }

    private static void RegisterOpen(BehaviorRegistry registry, IUiNavigationHost host, string controlId, string windowName)
        => registry.Register(controlId, vm =>
        {
            host.ShowStatus($"Open: {windowName} ({controlId})");
            host.NavigateTo(windowName);
        });
}
