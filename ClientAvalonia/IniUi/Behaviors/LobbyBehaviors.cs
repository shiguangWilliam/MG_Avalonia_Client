using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Behaviors;

public static class LobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        RegisterLaunch(registry, host, windowName);

        registry.Register("btnPickRandomMap", _ => host.PickRandomLobbyMap());
        registry.Register("ddGameMode", _ => host.RefreshLobbyMapList());
        registry.Register("MapPreviewBox", _ => host.ToggleFavoriteLobbyMap());
    }

    private static void RegisterLaunch(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        registry.Register("btnLaunchGame", _ =>
        {
            if (windowName.Equals("SkirmishLobby", StringComparison.OrdinalIgnoreCase))
            {
                TryLaunchSkirmish(host);
                return;
            }

            host.ShowStatus("Multiplayer in-game launch is not implemented in ClientAvalonia yet.");
        });
    }

    private static void TryLaunchSkirmish(IUiNavigationHost host)
    {
        if (host.TryLaunchSkirmish(out string message))
        {
            host.ShowStatus(message);
            return;
        }

        host.ShowStatus($"Launch failed: {message}");
        if (host is Avalonia.Controls.Window window)
            ClientDialogService.ShowError(window, "Cannot launch game", message);
    }
}
