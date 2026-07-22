using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
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
            ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
            if (cncnet.IsGameRoomJoinPending)
            {
                host.ShowStatus("Joining game room — please wait...");
                return;
            }

            if (cncnet.ActiveGameRoom != null)
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

            if (!cncnet.TryCreateGame(request, out string message))
            {
                host.ShowStatus(message);
                return;
            }

            host.EnterCnCNetGameLobbyConnecting();
            host.ShowStatus(message);
        };
    }
}
