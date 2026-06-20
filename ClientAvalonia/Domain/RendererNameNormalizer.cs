namespace ClientAvalonia.Domain;

/// <summary>Maps legacy Avalonia dropdown labels to Renderers.ini internal names.</summary>
internal static class RendererNameNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return name.Trim() switch
        {
            // Legacy hardcoded dropdown labels only — do not remap mod Renderers.ini internal names (e.g. CNC-DDRAW).
            "CNC-DDraw" or "Cnc-DDraw" => "CNC-DDRAW",
            "TS-DDraw" or "TS-DDRAW" => "TS_DDRAW",
            "TS-DDraw-2" or "TS-DDRAW-2" => "TS_DDRAW-GDI",
            "DDrawCompat" or "DDraw Compat" => "DDrawCompat",
            "Software" or "Stock" or "Default" => name.Trim(),
            _ => name.Trim(),
        };
    }
}
