using ClientAvalonia.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Overlays;

public static class GameCreationOverlayBehaviors
{
    public static void Wire(
        GameCreationOverlayContext context,
        IUiNavigationHost host,
        string gameLobbyWindow)
    {
        context.CancelButton.Click += (_, _) => host.CloseGameCreationOverlay();

        context.CreateButton.Click += (_, _) =>
        {
            if (CnCNetSessionService.Instance.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                return;
            }

            if (CnCNetSessionService.Instance.ActiveGameRoom != null)
            {
                host.ShowStatus("Already in a game room.");
                host.CloseGameCreationOverlay();
                host.NavigateTo(gameLobbyWindow);
                return;
            }

            CnCNetGameCreationRequest? request = GameCreationOverlayBuilder.TryBuildRequest(context, out string validationMessage);
            if (request == null)
            {
                host.ShowStatus(validationMessage);
                return;
            }

            host.CloseGameCreationOverlay();

            if (!CnCNetSessionService.Instance.TryCreateGame(request, out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.EnterCnCNetGameLobbyConnecting();
            host.ShowStatus(message);
        };
    }
}
