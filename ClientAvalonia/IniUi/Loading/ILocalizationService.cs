namespace ClientAvalonia.IniUi.Loading;

public interface ILocalizationService
{
    string Localize(string? windowName, string controlName, string attributeName, string defaultValue, bool notify = true);
}

/// <summary>Passthrough localizer until ClientCore Translation is wired.</summary>
public sealed class PassthroughLocalizationService : ILocalizationService
{
    public string Localize(string? windowName, string controlName, string attributeName, string defaultValue, bool notify = true)
        => IniTextUtil.NormalizeDisplayText(defaultValue);
}
