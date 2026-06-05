using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;
using ClientAvalonia.CnCNet;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet / LAN channel lobby behaviors wired to Core CnCNet session.</summary>
public static class MultiplayerLobbyBehaviors
{
    private static bool _gameRoomNavigationPending;

    public static void Register(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        string gameLobbyWindow = windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase)
            ? "LANGameLobby"
            : "CnCNetGameLobby";

        if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
        {
            void OnGameRoomJoined(CnCNetActiveGameRoom room)
            {
                if (!_gameRoomNavigationPending)
                    return;

                _gameRoomNavigationPending = false;
                UnregisterGameRoomHandlers();
                host.ShowStatus($"Entered \"{room.RoomName}\".");
                host.NavigateTo(gameLobbyWindow);
            }

            void OnGameRoomJoinFailed(string message)
            {
                if (!_gameRoomNavigationPending)
                    return;

                _gameRoomNavigationPending = false;
                UnregisterGameRoomHandlers();
                host.ShowStatus(message);
            }

            void UnregisterGameRoomHandlers()
            {
                CnCNetSessionService.Instance.GameRoomJoined -= OnGameRoomJoined;
                CnCNetSessionService.Instance.GameRoomJoinFailed -= OnGameRoomJoinFailed;
            }

            UnregisterGameRoomHandlers();
            CnCNetSessionService.Instance.GameRoomJoined += OnGameRoomJoined;
            CnCNetSessionService.Instance.GameRoomJoinFailed += OnGameRoomJoinFailed;
        }

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

            _gameRoomNavigationPending = true;

            if (!CnCNetSessionService.Instance.TryCreateGame(out string message))
            {
                _gameRoomNavigationPending = false;
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);
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

            _gameRoomNavigationPending = true;

            if (!CnCNetSessionService.Instance.TryJoinSelectedGame(out string message))
            {
                _gameRoomNavigationPending = false;
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);
        });

        registry.Register("btnLogout", _ =>
        {
            _gameRoomNavigationPending = false;

            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
                CnCNetSessionService.Instance.Disconnect();

            host.ShowStatus("Logged out.");
            host.NavigateBack();
        });
    }
}
