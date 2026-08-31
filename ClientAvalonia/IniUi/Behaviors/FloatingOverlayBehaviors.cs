namespace ClientAvalonia.IniUi.Behaviors;

internal static class FloatingOverlayBehaviors
{
    public static void RegisterForOverlay(BehaviorRegistry registry, IUiNavigationHost host, string windowName)
    {
        if (windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            OptionsWindowBehaviors.Register(registry, host);
        else if (Services.FloatingOverlayLayout.IsCampaignWindow(windowName))
            CampaignOverlayBehaviors.Register(registry, host);

        CommonWindowBehaviors.Register(registry, host);

        // Register after CommonWindowBehaviors so overlay-specific Save/Cancel win.
        if (windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            OptionsOverlayBehaviors.Register(registry, host);
    }
}
