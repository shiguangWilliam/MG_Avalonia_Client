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
        };

    public static bool IsOverlayWindow(string windowSectionName)
        => FallbackSizes.ContainsKey(windowSectionName);

    public static (int Width, int Height) ResolveOverlaySize(string iniPath, string windowSectionName)
    {
        if (ClientEnvironment.ReadWindowSize(iniPath, windowSectionName) is { } fromIni)
            return fromIni;

        if (FallbackSizes.TryGetValue(windowSectionName, out (int Width, int Height) fallback))
            return fallback;

        return (OptionsOverlayConstants.Width, OptionsOverlayConstants.Height);
    }
}
