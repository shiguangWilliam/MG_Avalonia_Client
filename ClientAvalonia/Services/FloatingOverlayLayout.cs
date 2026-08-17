using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.Services;

/// <summary>INI windows shown as centered floating panels over MainMenu (independent viewport size).</summary>
public static class FloatingOverlayLayout
{
    private static readonly Dictionary<string, (int Width, int Height)> FallbackSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OptionsWindow"] = (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height),
            ["GameCreationWindow"] = (520, 580),
        };

    /// <summary>Tactical campaign console target size (globe-dominant composition).</summary>
    public static (int Width, int Height) TacticalCampaignSize => (1240, 660);

    public static bool IsOverlayWindow(string windowSectionName)
        => FallbackSizes.ContainsKey(windowSectionName)
           || windowSectionName.Equals("GameCreationWindow", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Campaign is a root panel navigation target (not a floating overlay).
    /// Kept here only as a size hint for legacy callers.
    /// </summary>
    public static bool IsCampaignWindow(string windowSectionName)
        => windowSectionName.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase);

    public static (int Width, int Height) ResolveOverlaySize(string iniPath, string windowSectionName)
    {
        // Options chrome (tabs + footer Save/Cancel) is laid out against fixed constants.
        // Preferring a smaller INI Height clips/hides the Cancel button.
        if (windowSectionName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            return (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height);

        if (ClientEnvironment.ReadWindowSize(iniPath, windowSectionName) is { } fromIni)
        {
            return fromIni;
        }

        if (FallbackSizes.TryGetValue(windowSectionName, out (int Width, int Height) fallback))
            return fallback;

        return (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height);
    }
}
