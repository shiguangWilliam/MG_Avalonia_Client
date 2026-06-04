namespace ClientAvalonia.Domain;

public sealed class MissionEntry
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

    public bool IsHeader => string.IsNullOrWhiteSpace(Scenario);
}
