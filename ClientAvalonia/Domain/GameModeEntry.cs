namespace ClientAvalonia.Domain;

public sealed class GameModeEntry
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required string UntranslatedUIName { get; init; }

    public string MapCodeIniName { get; init; } = string.Empty;

    public bool MultiplayerOnly { get; init; }
}
