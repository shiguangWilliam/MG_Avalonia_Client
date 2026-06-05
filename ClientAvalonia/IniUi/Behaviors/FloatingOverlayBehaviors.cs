namespace ClientAvalonia.IniUi.Behaviors;

internal static class FloatingOverlayBehaviors
{
    public static void RegisterForOverlay(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        switch (windowName)
        {
            case "OptionsWindow":
                OptionsWindowBehaviors.Register(registry, host);
                break;
            case "CampaignSelector":
                CampaignOverlayBehaviors.Register(registry, host);
                break;
        }

        CommonWindowBehaviors.Register(registry, host);

        // Register after CommonWindowBehaviors so overlay-specific Save/Cancel win.
        if (windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            OptionsOverlayBehaviors.Register(registry, host);
    }
}
