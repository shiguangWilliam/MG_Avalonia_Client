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
}
