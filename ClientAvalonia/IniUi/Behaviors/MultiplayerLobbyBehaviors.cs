using ClientAvalonia.IniUi;
using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Lan;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet / LAN channel lobby behaviors (XNA CnCNetLobby create/join flow).</summary>
public static class MultiplayerLobbyBehaviors
{
    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        bool isLan = windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase);
        string gameLobbyWindow = isLan ? WindowKind.LanGameLobby : WindowKind.CnCNetGameLobby;

        registry.Register("btnNewGame", _ =>
        {
            if (isLan)
            {
                ILanSession lan = AppState.Lan;
                lan.StartLobby();
                if (lan.ActiveGameRoom != null)
                {
                    host.ShowStatus("Already in a LAN game room.");
                    host.NavigateTo(gameLobbyWindow);
                    return;
                }

                string? mapName = null;
                string? modeName = null;
                if (host.ActiveRoot != null)
                {
                    // Best-effort labels; room session still owns real map selection in lobby.
                    mapName = "LAN Map";
                    modeName = "Standard";
                }

                if (!lan.TryHostNewGame(mapName, modeName, out string message))
                {
                    host.ShowStatus(message);
                    return;
                }

                host.ShowStatus(message);
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus($"Creating game 鈫?{gameLobbyWindow}");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room —?please wait...");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (cncnet.ActiveGameRoom != null)
            {
                host.ShowStatus("Already in a game room —?open the in-game lobby or leave first.");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            host.OpenGameCreationOverlay();
        });

        registry.Register("btnJoinGame", _ =>
        {
            if (isLan)
            {
                ILanSession lan = AppState.Lan;
                lan.StartLobby();
                LanHostedGame? game = lan.GetSelectedOrFirstUnlocked();
                if (game == null)
                {
                    host.ShowStatus("No LAN games found. Wait for a host broadcast, then join.");
                    return;
                }

                if (!lan.TryJoinGame(game, out string message))
                {
                    host.ShowStatus(message);
                    return;
                }

                host.ShowStatus(message);
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus("Select a game from the list, then join.");
                return;
            }

            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room —?please wait...");
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

        void LogoutToMainMenu()
        {
            if (host.IsFloatingOverlayOpen)
            {
                host.CloseFloatingOverlay();
                host.ShowStatus("Create game cancelled.");
                return;
            }

            if (isLan)
            {
                AppState.Lan.LeaveActiveRoom();
                AppState.Lan.StopLobby();
            }

            host.LogoutToMainMenu();
        }

        registry.Register("btnLogout", _ => LogoutToMainMenu());
        // LANLobby.ini defines btnMainMenu (XNA parity); same action as CnCNet btnLogout.
        registry.Register("btnMainMenu", _ => LogoutToMainMenu());
    }
}
