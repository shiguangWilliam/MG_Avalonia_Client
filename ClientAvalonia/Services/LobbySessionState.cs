using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Tracks lobby/campaign UI selection state for launch and preview binding.</summary>
public sealed class LobbySessionState
{
    public const int FavoriteFilterIndex = 0;

    public MultiplayerLobbyState MultiplayerState { get; } = new();

    public IReadOnlyList<MapEntry> VisibleMaps { get; private set; } = [];

    public int FilterIndex { get; set; }

    public string MapSearchText { get; set; } = string.Empty;

    public CampaignSideFilter CampaignSideFilter { get; set; } = CampaignSideFilter.All;

    public int LastSelectableCampaignIndex { get; set; } = -1;

    public IReadOnlyList<MissionEntry> VisibleMissions { get; private set; } = [];

    // ---- UI 输入态（与具体 Session 无关的视图层选择） ----

    /// <summary>
    /// UI 选择的玩家模式（用于切换 Skirmish / Multiplayer 视图）。
    /// 注意：与 <c>IGameSession.Mode</c> 不同——后者是 Session 派生属性；
    /// 此字段描述"UI 上次切换到了哪个标签页"。
    /// </summary>
    public LobbyPlayerMode UIMode { get; set; } = LobbyPlayerMode.Skirmish;

    /// <summary>房主允许其他人改玩家选项（XNA AllowHostPlayerOptions 反向开关）。</summary>
    public bool AllowHostPlayerOptions { get; set; } = true;

    /// <summary>本地玩家显示名（UI 输入态）。</summary>
    public string LocalPlayerName { get; set; } = AppState.Environment.PlayerName;

    /// <summary>房主显示名（用于 UI 显示房主徽章等）。</summary>
    public string HostPlayerName { get; set; } = AppState.Environment.PlayerName;

    public void SetVisibleMaps(IReadOnlyList<MapEntry> maps) => VisibleMaps = maps;

    public void SetVisibleMissions(IReadOnlyList<MissionEntry> missions) => VisibleMissions = missions;

    public MapEntry? GetSelectedMap(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMaps.Count ? VisibleMaps[listIndex] : null;

    public MissionEntry? GetSelectedMission(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMissions.Count ? VisibleMissions[listIndex] : null;

    public bool IsFavoriteFilterSelected => FilterIndex == FavoriteFilterIndex;
}
