using ClientAvalonia.Domain.Resources;

namespace ClientAvalonia.Domain;

public sealed class GameModeEntry : IGameModeResource
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required string UntranslatedUIName { get; init; }

    public string MapCodeIniName { get; init; } = string.Empty;

    public bool MultiplayerOnly { get; init; }

    public long SizeBytes { get; init; }

    public VersionInfo Version { get; init; } = new(0, 0, 0, 0);

    string IResource.LogicalId => Name;

    string IResource.UntranslatedName => UntranslatedUIName;

    string IResource.FilePath => MapCodeIniName;

    string IResource.Sha1 => string.Empty;

    ResourceOrigin IResource.Origin => ResourceOrigin.Official;

    bool IResource.IsReadOnly => true;
}
