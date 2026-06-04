using ClientAvalonia.Domain;

namespace ClientAvalonia.Services;

/// <summary>Tracks lobby/campaign UI selection state for launch and preview binding.</summary>
public sealed class LobbySessionState
{
    public const int FavoriteFilterIndex = 0;

    public LobbyPlayerState PlayerState { get; } = new();

    public MultiplayerLobbyState MultiplayerState { get; } = new();

    public IReadOnlyList<MapEntry> VisibleMaps { get; private set; } = [];

    public int FilterIndex { get; set; }

    public string MapSearchText { get; set; } = string.Empty;

    public CampaignSideFilter CampaignSideFilter { get; set; } = CampaignSideFilter.All;

    public int LastSelectableCampaignIndex { get; set; } = -1;

    public IReadOnlyList<MissionEntry> VisibleMissions { get; private set; } = [];

    public void SetVisibleMaps(IReadOnlyList<MapEntry> maps) => VisibleMaps = maps;

    public void SetVisibleMissions(IReadOnlyList<MissionEntry> missions) => VisibleMissions = missions;

    public MapEntry? GetSelectedMap(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMaps.Count ? VisibleMaps[listIndex] : null;

    public MissionEntry? GetSelectedMission(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMissions.Count ? VisibleMissions[listIndex] : null;

    public bool IsFavoriteFilterSelected => FilterIndex == FavoriteFilterIndex;
}
