using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>CnCNet / LAN channel lobby behaviors wired to Core CnCNet session.</summary>
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

            if (!CnCNetLobbyOperations.TryCreateGame(out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);

            void OnGameRoomJoined(CnCNetActiveGameRoom _)
            {
                CnCNetSessionService.Instance.GameRoomJoined -= OnGameRoomJoined;
                host.NavigateTo(gameLobbyWindow);
            }

            CnCNetSessionService.Instance.GameRoomJoined += OnGameRoomJoined;
        });

        registry.Register("btnJoinGame", _ =>
        {
            if (!windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                host.ShowStatus("Select a game from the list, then join.");
                return;
            }

            if (host.ActiveRoot != null)
                GameDataBindingApplier.SyncChannelGameSelection(host.ActiveRoot, CnCNetSessionService.Instance.LobbyState);

            if (!CnCNetLobbyOperations.TryJoinSelectedGame(out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.ShowStatus(message);

            void OnGameRoomJoined(CnCNetActiveGameRoom _)
            {
                CnCNetSessionService.Instance.GameRoomJoined -= OnGameRoomJoined;
                host.NavigateTo(gameLobbyWindow);
            }

            CnCNetSessionService.Instance.GameRoomJoined += OnGameRoomJoined;
        });

        registry.Register("btnLogout", _ =>
        {
            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
                CnCNetSessionService.Instance.Disconnect();

            host.ShowStatus("Logged out.");
            host.NavigateBack();
        });
    }
}
