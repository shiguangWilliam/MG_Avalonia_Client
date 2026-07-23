using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;

namespace ClientAvalonia.Services;

/// <summary>Skirmish or campaign launch parameters.</summary>
public sealed class SkirmishLaunchRequest
{
    public required MapEntry Map { get; init; }

    public required GameModeEntry GameMode { get; init; }

    /// <summary>
    /// Phase 3 P3-2：Session-aware 玩家槽位（通常是 <see cref="IGameSession.PlayerSlots"/>）。
    /// 当非空时优先于 <see cref="Players"/>（已过时）。
    /// </summary>
    public IReadOnlyList<IPlayerSlot>? Slots { get; init; }

    /// <summary>
    /// Phase 3 P3-2：可选阵营数（spawn.ini random side 上界）。当未指定时回退到 0。
    /// </summary>
    public int SideCount { get; init; }

    /// <summary>Legacy 玩家状态载体（Phase 3 P3-2：标记为已过时，新代码用 <see cref="Slots"/>）。</summary>
    [Obsolete("Phase 3 P3-2: 改用 Slots + SideCount。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public LobbyPlayerState? Players { get; init; }

    public UiNodeViewModel? LobbyRoot { get; init; }
}

public sealed class CampaignLaunchRequest
{
    public required MissionEntry Mission { get; init; }

    public int DifficultyIndex { get; init; } = 1;

    public UiNodeViewModel? OverlayRoot { get; init; }
}
