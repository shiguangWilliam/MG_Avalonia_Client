using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Tracks lobby/campaign UI selection state for launch and preview binding.</summary>
public sealed class LobbySessionState
{
    public const int FavoriteFilterIndex = 0;

    public LobbyPlayerState PlayerState { get; }

    public MultiplayerLobbyState MultiplayerState { get; } = new();

    public LobbySessionState()
    {
        // Phase 2 P2-1：建立反向引用，让 PlayerState 的 5 个 UI 字段双向转发到本类，
        // 消除"双份真相"。MainWindow 既有的 _lobbySession.PlayerState.Mode = X 调用
        // 会自动写 UIMode；反之亦然。
        PlayerState = new LobbyPlayerState { Owner = this };
    }

    public IReadOnlyList<MapEntry> VisibleMaps { get; private set; } = [];

    public int FilterIndex { get; set; }

    public string MapSearchText { get; set; } = string.Empty;

    public CampaignSideFilter CampaignSideFilter { get; set; } = CampaignSideFilter.All;

    public int LastSelectableCampaignIndex { get; set; } = -1;

    public IReadOnlyList<MissionEntry> VisibleMissions { get; private set; } = [];

    // ---- Slice 4: 从 LobbyPlayerState 迁移过来的 UI 输入态（与具体 Session 无关的视图层选择） ----

    /// <summary>
    /// UI 选择的玩家模式（用于切换 Skirmish / Multiplayer 视图）。
    /// 注意：与 <c>IGameSession.Mode</c> 不同——后者是 Session 派生属性，描述"当前 Session 是哪种"；
    /// 此字段描述"UI 上次切换到了哪个标签页"，用于在 Session 还未建立时的导航展示。
    /// 一旦 Session 建立，UI 应使用 <c>session.Mode</c> 而非此字段。
    /// </summary>
    public LobbyPlayerMode UIMode { get; set; } = LobbyPlayerMode.Skirmish;

    /// <summary>房主允许其他人改玩家选项（XNA AllowHostPlayerOptions 反向开关）。</summary>
    public bool AllowHostPlayerOptions { get; set; } = true;

    /// <summary>本地玩家显示名（UI 输入态，可被玩家在 lobby 修改；区别于 IGameEnvironment.PlayerName）。</summary>
    public string LocalPlayerName { get; set; } = ProgramConstants.PLAYERNAME;

    /// <summary>房主显示名（用于 UI 显示房主徽章等）。</summary>
    public string HostPlayerName { get; set; } = ProgramConstants.PLAYERNAME;

    /// <summary>
    /// UI→state 同步时的重入保护（兼容期保留）。
    /// 注意：长期目标是 <c>IGameSession.Revision</c> 原子脏读 tag 取代此布尔标志。
    /// Slice 5 之后 BindingApplier 改用 Revision，此字段会变为 deprecated。
    /// Phase 2 P2-2：标记为已过时——新代码应使用 <see cref="IGameSession.Revision"/> 比对来检测重入。
    /// 仍保留功能（兼容期）。
    /// </summary>
    [Obsolete("Phase 2 P2-2: 改用 IGameSession.Revision 比对来检测 UI 重入。Phase 4 完成 MainWindow Revision 切换；Phase 5 删除此字段。")]
    public bool PlayerUpdatingInProgress { get; set; }

    public void SetVisibleMaps(IReadOnlyList<MapEntry> maps) => VisibleMaps = maps;

    public void SetVisibleMissions(IReadOnlyList<MissionEntry> missions) => VisibleMissions = missions;

    public MapEntry? GetSelectedMap(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMaps.Count ? VisibleMaps[listIndex] : null;

    public MissionEntry? GetSelectedMission(int listIndex)
        => listIndex >= 0 && listIndex < VisibleMissions.Count ? VisibleMissions[listIndex] : null;

    public bool IsFavoriteFilterSelected => FilterIndex == FavoriteFilterIndex;
}
