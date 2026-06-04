namespace ClientAvalonia.IniUi.Loading;

internal static class IniTextUtil
{
    /// <summary>XNA UI uses @ as line break in Text/ToolTip.</summary>
    public static string NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace('@', '\n').Replace("\\n", "\n", StringComparison.Ordinal);
    }
}
