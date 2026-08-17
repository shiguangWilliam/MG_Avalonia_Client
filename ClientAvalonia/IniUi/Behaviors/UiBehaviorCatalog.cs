namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Registers click behaviors for the active INI window section.</summary>
public static class UiBehaviorCatalog
{
    public static void RegisterForWindow(BehaviorRegistry registry, string windowName, IUiNavigationHost host)
    {
        registry.Clear();
        CommonWindowBehaviors.Register(registry, host);

        switch (windowName)
        {
            case "MainMenu":
                MainMenuBehaviors.Register(registry, host);
                break;
            case "CnCNetLobby":
            case "LANLobby":
                MultiplayerLobbyBehaviors.Register(registry, host, windowName);
                break;
            case "SkirmishLobby":
            case "MultiplayerGameLobby":
            case "CnCNetGameLobby":
            case "LANGameLobby":
            case "CnCNetGameLoadingLobby":
            case "LANGameLoadingLobby":
            case "GameLoadingLobby":
                LobbyBehaviors.Register(registry, host, windowName);
                break;
            case "OptionsWindow":
                OptionsWindowBehaviors.Register(registry, host);
                break;
            case "CampaignSelector":
                CampaignOverlayBehaviors.Register(registry, host);
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
