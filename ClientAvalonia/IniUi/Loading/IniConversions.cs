namespace ClientAvalonia.IniUi.Loading;

internal static class IniConversions
{
    public static bool BooleanFromString(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => defaultValue,
        };
    }
}
