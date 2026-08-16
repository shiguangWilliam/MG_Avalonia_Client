using ClientAvalonia.Domain.Resources;

namespace ClientAvalonia.Domain;

public sealed class MissionEntry : IMissionResource
{
    public required string SectionName { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Scenario { get; init; } = string.Empty;

    public string SideName { get; init; } = string.Empty;

    public int Side { get; init; }

    public int CampaignId { get; init; } = -1;

    public bool Enabled { get; init; } = true;

    public bool BuildOffAlly { get; init; }

    public bool RequiredAddon { get; init; }

    public bool PlayerAlwaysOnNormalDifficulty { get; init; }

    /// <summary>Latitude in degrees for the Tactical globe (-90..90). Null = unspecified.</summary>
    public double? GlobeLatitude { get; init; }

    /// <summary>Longitude in degrees for the Tactical globe (-180..180). Null = unspecified.</summary>
    public double? GlobeLongitude { get; init; }

    public bool IsHeader => string.IsNullOrWhiteSpace(Scenario);

    public long SizeBytes { get; init; }

    public VersionInfo Version { get; init; } = new(0, 0, 0, 0);

    public IReadOnlyDictionary<string, object> ModMetadata { get; init; }
        = new Dictionary<string, object>();

    string IResource.LogicalId => SectionName;

    string IResource.UntranslatedName => SectionName;

    string IResource.FilePath => Scenario;

    string IResource.Sha1 => string.Empty;

    ResourceOrigin IResource.Origin => ResourceOrigin.Official;

    bool IResource.IsReadOnly => true;
}
