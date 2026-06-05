using ClientAvalonia.Services;
using ClientAvalonia.CnCNet;

namespace ClientAvalonia.IniUi.Behaviors;

public static class LobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        RegisterLaunch(registry, host, windowName);

        registry.Register("btnPickRandomMap", _ => host.PickRandomLobbyMap());
        registry.Register("ddGameMode", _ =>
        {
            host.RefreshLobbyMapList();
            RefreshCnCNetListing(host, windowName);
        });
        registry.Register("MapPreviewBox", _ => host.ToggleFavoriteLobbyMap());
        registry.Register("btnPlayerExtraOptionsOpen", _ => host.TogglePlayerExtraOptionsPanel());
        registry.Register("btnSaveLoadGameOptions", _ =>
            host.ShowStatus("Load/save game options menu is not implemented in ClientAvalonia yet."));
        registry.Register("BtnSaveLoadGameOptions", _ =>
            host.ShowStatus("Load/save game options menu is not implemented in ClientAvalonia yet."));

        if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            CnCNetGameLobbyBehaviors.Register(registry, host);
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

            if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            {
                TryLaunchCnCNet(host);
                return;
            }

            host.ShowStatus("Multiplayer in-game launch is not implemented for this lobby.");
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

    private static void TryLaunchCnCNet(IUiNavigationHost host)
    {
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room == null)
        {
            host.ShowStatus("Not in a CnCNet game room.");
            return;
        }

        if (room.IsHost)
        {
            if (!CnCNetSessionService.Instance.TryLaunchHostedGame(out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);
            return;
        }

        CnCNetSessionService.Instance.SetGameRoomReady(true);
        host.ShowStatus("Ready — waiting for host to launch.");
    }

    private static void RefreshCnCNetListing(IUiNavigationHost host, string windowName)
    {
        if (!windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            return;

        host.RefreshCnCNetGameListing();
    }
}
