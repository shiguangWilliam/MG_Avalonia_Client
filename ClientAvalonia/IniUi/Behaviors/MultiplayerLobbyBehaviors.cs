using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;
using ClientAvalonia.CnCNet;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet / LAN channel lobby behaviors (XNA CnCNetLobby create/join flow).</summary>
public static class MultiplayerLobbyBehaviors
{
    private static bool _handlersRegistered;

    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        string gameLobbyWindow = windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase)
            ? "LANGameLobby"
            : "CnCNetGameLobby";

        if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            EnsureCnCNetGameRoomHandlers(host, gameLobbyWindow);

        registry.Register("btnNewGame", _ =>
        {
            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus($"Creating game → {gameLobbyWindow}");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (CnCNetSessionService.Instance.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                return;
            }

            if (CnCNetSessionService.Instance.ActiveGameRoom != null)
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

            if (CnCNetSessionService.Instance.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                return;
            }

            if (CnCNetSessionService.Instance.ActiveGameRoom != null)
            {
                host.ShowStatus("Already in a game room.");
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            if (host.ActiveRoot != null)
                GameDataBindingApplier.SyncChannelGameSelection(host.ActiveRoot, CnCNetSessionService.Instance.LobbyState);

            if (!CnCNetSessionService.Instance.TryJoinSelectedGame(out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);
            host.NavigateTo(gameLobbyWindow);
        });

        registry.Register("btnLogout", _ =>
        {
            if (host.IsFloatingOverlayOpen)
            {
                host.CloseFloatingOverlay();
                host.ShowStatus("Create game cancelled.");
                return;
            }

            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
                CnCNetSessionService.Instance.Disconnect();

            host.ShowStatus("Logged out.");
            host.NavigateBack();
        });
    }

    private static void EnsureCnCNetGameRoomHandlers(IUiNavigationHost host, string gameLobbyWindow)
    {
        if (_handlersRegistered)
            return;

        _handlersRegistered = true;

        CnCNetSessionService.Instance.GameRoomJoined += room =>
        {
            host.ShowStatus($"Entered \"{room.RoomName}\".");
            if (!host.CurrentWindow.Equals(gameLobbyWindow, StringComparison.OrdinalIgnoreCase))
                host.NavigateTo(gameLobbyWindow);
            else
                host.RefreshCnCNetGameRoomPlayers();
        };

        CnCNetSessionService.Instance.GameRoomJoinFailed += message =>
        {
            host.ShowStatus(message);
            if (host.CurrentWindow.Equals(gameLobbyWindow, StringComparison.OrdinalIgnoreCase))
                host.NavigateBack();
        };
    }
}
