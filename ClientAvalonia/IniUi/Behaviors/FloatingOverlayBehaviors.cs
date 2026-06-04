namespace ClientAvalonia.IniUi.Behaviors;

internal static class FloatingOverlayBehaviors
{
    public static void RegisterForOverlay(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        switch (windowName)
        {
            case "OptionsWindow":
                OptionsWindowBehaviors.Register(registry, host);
                OptionsOverlayBehaviors.Register(registry, host);
                break;
            case "CampaignSelector":
                CampaignOverlayBehaviors.Register(registry, host);
                break;
        }

        CommonWindowBehaviors.Register(registry, host);
    }
}
