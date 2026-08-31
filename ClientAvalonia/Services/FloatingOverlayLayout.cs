using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.Services;

/// <summary>INI windows shown as centered floating panels over MainMenu (independent viewport size).</summary>
public static class FloatingOverlayLayout
{
    /// <summary>
    /// Logical name of the campaign window. Campaign is a root panel navigation
    /// target by default; the floating-overlay path remains reachable as a mod
    /// fallback via <c>$LeftClickAction=OpenFloatingOverlay</c>.
    /// </summary>
    public const string CampaignWindowName = "CampaignSelector";

    private static readonly Dictionary<string, (int Width, int Height)> FallbackSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OptionsWindow"] = (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height),
            ["GameCreationWindow"] = (520, 580),
        };

    public static bool IsOverlayWindow(string windowSectionName)
        => FallbackSizes.ContainsKey(windowSectionName)
           || windowSectionName.Equals("GameCreationWindow", StringComparison.OrdinalIgnoreCase);

    /// <summary>Campaign window check (single source of truth for the logical name).</summary>
    public static bool IsCampaignWindow(string windowSectionName)
        => windowSectionName.Equals(CampaignWindowName, StringComparison.OrdinalIgnoreCase);

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
