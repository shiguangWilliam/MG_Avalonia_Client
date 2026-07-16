using ClientAvalonia.IniUi.Loading;

namespace ClientAvalonia.Services;

/// <summary>INI windows shown as centered floating panels over MainMenu (independent viewport size).</summary>
public static class FloatingOverlayLayout
{
    private static readonly Dictionary<string, (int Width, int Height)> FallbackSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OptionsWindow"] = (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height),
            ["CampaignSelector"] = (800, 600),
            ["GameCreationWindow"] = (520, 580),
        };

    public static bool IsOverlayWindow(string windowSectionName)
        => FallbackSizes.ContainsKey(windowSectionName)
           || windowSectionName.Equals("GameCreationWindow", StringComparison.OrdinalIgnoreCase);

    public static (int Width, int Height) ResolveOverlaySize(string iniPath, string windowSectionName)
    {
        // Options chrome (tabs + footer Save/Cancel) is laid out against fixed constants.
        // Preferring a smaller INI Height clips/hides the Cancel button.
        if (windowSectionName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            return (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height);

        if (ClientEnvironment.ReadWindowSize(iniPath, windowSectionName) is { } fromIni)
            return fromIni;

        if (FallbackSizes.TryGetValue(windowSectionName, out (int Width, int Height) fallback))
            return fallback;

        return (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height);
    }
}
