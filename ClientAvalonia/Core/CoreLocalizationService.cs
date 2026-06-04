using ClientAvalonia.IniUi.Loading;
using ClientCore.I18N;

namespace ClientAvalonia.Core;

/// <summary>INI control attribute localization via ClientCore Translation (same keys as ClientGUI).</summary>
public sealed class CoreLocalizationService : ILocalizationService
{
    public string Localize(string? windowName, string controlName, string attributeName, string defaultValue, bool notify = true)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return IniTextUtil.NormalizeDisplayText(defaultValue);

        string parent = string.IsNullOrWhiteSpace(windowName) ? "Global" : windowName;
        string key = $"INI:Controls:{parent}:{controlName}:{attributeName}";
        string globalKey = $"INI:Controls:Global:{controlName}:{attributeName}";

        return Translation.Instance.LookUp(key, fallbackKey: globalKey, defaultValue, notify);
    }
}
