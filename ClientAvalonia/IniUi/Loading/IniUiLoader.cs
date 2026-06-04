using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Backward-compatible entry point; prefer LayoutEngine for M2+ pipeline.</summary>
public sealed class IniUiLoader
{
    private readonly LayoutEngine _engine;

    public IniUiLoader(LayoutEngine engine) => _engine = engine;

    public static IniUiLoader CreateDefault(
        int resolutionWidth = 1280,
        int resolutionHeight = 720,
        IReadOnlyDictionary<string, int>? parserConstants = null,
        ILocalizationService? localization = null)
    {
        var context = parserConstants == null
            ? LayoutContext.M2Default
            : new LayoutContext(resolutionWidth, resolutionHeight, parserConstants);
        return new IniUiLoader(new LayoutEngine(context, localization));
    }

    public UiNodeTree LoadWindow(string iniPath, string windowSectionName)
        => _engine.LoadWindow(iniPath, windowSectionName);
}
