namespace ClientAvalonia.IniUi.Models;

/// <summary>
/// Logical UI node produced from an INI section. Props hold Avalonia-ready values;
/// RawAttributes preserve mod extensions and business-only keys.
/// </summary>
public sealed class UiNode
{
    public required string Id { get; init; }

    /// <summary>INI type name, e.g. XNAClientButton or GameLobbyCheckBox.</summary>
    public required string ControlType { get; init; }

    /// <summary>Internal Avalonia template key from ControlRegistry.</summary>
    public required string TemplateKey { get; set; }

    /// <summary>
    /// Render-property bag for INI-driven visuals (geometry, textures, text…).
    /// Issue #21 contract: binding/lifecycle flags (one-shot wiring markers)
    /// must NOT live here — use <see cref="Binding.LobbyUiState"/> instead —
    /// so Props stays a pure, INI-mirroring bag safe to dump/serialize.
    /// </summary>
    public Dictionary<string, object> Props { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Original INI strings including unrecognized keys for dynamic/business consumers.</summary>
    public Dictionary<string, string> RawAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<UiNode> Children { get; } = [];

    public UiNode? Parent { get; set; }

    public string? WindowName { get; init; }

    public double GetNumericProp(string key, double fallback = 0)
    {
        if (!Props.TryGetValue(key, out object? value))
            return fallback;

        return value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => fallback,
        };
    }

    public int GetIntProp(string key, int fallback = 0)
        => (int)GetNumericProp(key, fallback);
}
