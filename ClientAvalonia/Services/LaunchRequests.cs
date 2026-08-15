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

    /// <summary>玩家槽位（通常是 <see cref="IGameSession.PlayerSlots"/>）。</summary>
    public IReadOnlyList<IPlayerSlot>? Slots { get; init; }

    /// <summary>可选阵营数（spawn.ini random side 上界）。当未指定时回退到 0。</summary>
    public int SideCount { get; init; }

    public UiNodeViewModel? LobbyRoot { get; init; }
}

public sealed class CampaignLaunchRequest
{
    public required MissionEntry Mission { get; init; }

    public int DifficultyIndex { get; init; } = 1;

    public UiNodeViewModel? OverlayRoot { get; init; }
}
