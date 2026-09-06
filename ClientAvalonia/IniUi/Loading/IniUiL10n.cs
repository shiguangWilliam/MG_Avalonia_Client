using ClientAvalonia.Core;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// Issue #20: shared localization entry for code-driven UI text that is
/// applied OUTSIDE the PropertyResolver pipeline (window post-processors,
/// binding appliers, dialog services). Key convention matches
/// <see cref="CoreLocalizationService"/>:
/// <c>INI:Controls:{Window}:{Control}:{Attribute}</c> with a
/// <c>INI:Controls:Global:{Control}:{Attribute}</c> fallback.
/// Default values MUST be English — zh-CN lives in Translation.ini.
/// </summary>
public static class IniUiL10n
{
    private static ILocalizationService _service = new PassthroughLocalizationService();

    /// <summary>Swaps the localization backend (set once during startup).</summary>
    public static void Initialize(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>True when a real (non-passthrough) localization backend is active.</summary>
    public static bool IsInitialized => _service is not PassthroughLocalizationService;

    /// <summary>
    /// Wires <see cref="CoreLocalizationService"/> when ClientCore is
    /// bootstrapped; otherwise stays passthrough (unit tests, early startup).
    /// </summary>
    public static void EnsureInitialized()
    {
        if (IsInitialized)
            return;

        if (ClientCoreBootstrap.IsInitialized)
            Initialize(new CoreLocalizationService());
    }

    /// <summary>Localizes an attribute of a control within a window.</summary>
    public static string Text(
        string? windowName,
        string controlName,
        string attributeName,
        string englishDefault,
        bool notify = true)
    {
        EnsureInitialized();
        return _service.Localize(windowName, controlName, attributeName, englishDefault, notify);
    }

    /// <summary>Localizes a <c>Text</c> attribute of a control within a window.</summary>
    public static string Text(string? windowName, string controlName, string englishDefault)
        => Text(windowName, controlName, "Text", englishDefault);
}
