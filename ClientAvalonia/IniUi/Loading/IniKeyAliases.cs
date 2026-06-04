namespace ClientAvalonia.IniUi.Loading;

/// <summary>Normalizes INI keys to the schema names used by XNA/ClientGUI parsers.</summary>
internal static class IniKeyAliases
{
    public static string Normalize(string key)
        => key switch
        {
            "ClickSound" => "ClickSoundEffect",
            "HoverSound" => "HoverSoundEffect",
            "TextAnchor" => "$TextAnchor",
            "AnchorPoint" => "$AnchorPoint",
            "LeftClickAction" => "$LeftClickAction",
            _ => key,
        };
}
