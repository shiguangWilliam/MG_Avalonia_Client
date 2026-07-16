namespace ClientAvalonia.Domain;

public sealed class MapEntry
{
    public required string BaseFilePath { get; init; }

    public required string DisplayName { get; init; }

    public required string UntranslatedName { get; init; }

    public required IReadOnlyList<string> GameModes { get; init; }

    public string Sha1 { get; init; } = string.Empty;

    public string PreviewRelativePath { get; init; } = string.Empty;

    public string ExtraIniName { get; init; } = string.Empty;

    public bool IsOfficial { get; init; } = true;

    public bool IsCustom { get; init; }

    public bool MultiplayerOnly { get; init; }

    public int MinPlayers { get; init; }

    public int MaxPlayers { get; init; }

    public bool EnforceMaxPlayers { get; init; }

    public string CompleteFilePath { get; init; } = string.Empty;

    /// <summary>
    /// Raw waypoint tokens from MPMaps.ini (<c>Waypoint0..7</c>) or custom map
    /// <c>[Waypoints]</c>. Empty when the map has no starting locations described.
    /// </summary>
    public IReadOnlyList<string> Waypoints { get; init; } = [];

    /// <summary>Isometric map <c>Size</c> (actualSize) — 4 CSV ints. Unused for TDRA.</summary>
    public IReadOnlyList<string> ActualSize { get; init; } = ["0", "0", "0", "0"];

    /// <summary>Isometric map <c>LocalSize</c> — 4 CSV ints. Unused for TDRA.</summary>
    public IReadOnlyList<string> LocalSize { get; init; } = ["0", "0", "0", "0"];

    /// <summary>TDRA map cell origin X (from map section <c>X</c>).</summary>
    public int MapX { get; init; }

    /// <summary>TDRA map cell origin Y (from map section <c>Y</c>).</summary>
    public int MapY { get; init; }

    /// <summary>TDRA map cell width.</summary>
    public int MapWidth { get; init; }

    /// <summary>TDRA map cell height.</summary>
    public int MapHeight { get; init; }
}
