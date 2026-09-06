using ClientAvalonia.IniUi;
namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Registers click behaviors for the active INI window section.</summary>
public static class UiBehaviorCatalog
{
    public static void RegisterForWindow(BehaviorRegistry registry, string windowName, IUiNavigationHost host)
    {
        registry.Clear();
        CommonWindowBehaviors.Register(registry, host);

        if (Services.FloatingOverlayLayout.IsCampaignWindow(windowName))
        {
            CampaignOverlayBehaviors.Register(registry, host);
            return;
        }

        switch (windowName)
        {
            case WindowKind.MainMenu:
                MainMenuBehaviors.Register(registry, host);
                break;
            case "CnCNetLobby":
            case "LANLobby":
                MultiplayerLobbyBehaviors.Register(registry, host, windowName);
                break;
            case WindowKind.SkirmishLobby:
            case WindowKind.MultiplayerGameLobby:
            case WindowKind.CnCNetGameLobby:
            case WindowKind.LanGameLobby:
            case "CnCNetGameLoadingLobby":
            case "LANGameLoadingLobby":
            case "GameLoadingLobby":
                LobbyBehaviors.Register(registry, host, windowName);
                break;
            case WindowKind.OptionsWindow:
                OptionsWindowBehaviors.Register(registry, host);
                break;
            case "StatisticsWindow":
            case "ExtrasWindow":
                StubWindowBehaviors.Register(registry, host, windowName);
                break;
            default:
                StubWindowBehaviors.Register(registry, host, windowName);
                break;
        }
    }
}
