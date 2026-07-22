using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet / LAN channel lobby behaviors (XNA CnCNetLobby create/join flow).</summary>
public static class MultiplayerLobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        string gameLobbyWindow = windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase)
            ? "LANGameLobby"
            : "CnCNetGameLobby";

        registry.Register("btnNewGame", _ =>
        {
            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus($"Creating game → {gameLobbyWindow}");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (cncnet.ActiveGameRoom != null)
            {
                host.ShowStatus("Already in a game room — open the in-game lobby or leave first.");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            host.OpenGameCreationOverlay();
        });

        registry.Register("btnJoinGame", _ =>
        {
            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus("Select a game from the list, then join.");
                return;
            }

            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (cncnet.ActiveGameRoom != null)
            {
                host.ShowStatus("Already in a game room.");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (host.ActiveRoot != null)
                GameDataBindingApplier.SyncChannelGameSelection(host.ActiveRoot, cncnet.LobbyState);

            host.TryJoinSelectedCnCNetGame();
        });

        registry.Register("btnLogout", _ =>
        {
            if (host.IsFloatingOverlayOpen)
            {
                host.CloseFloatingOverlay();
                host.ShowStatus("Create game cancelled.");
                return;
            }

            host.LogoutToMainMenu();
        });
    }
}
