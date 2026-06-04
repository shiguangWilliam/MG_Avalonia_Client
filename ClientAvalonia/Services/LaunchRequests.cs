using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Services;

/// <summary>Skirmish or campaign launch parameters.</summary>
public sealed class SkirmishLaunchRequest
{
    public required MapEntry Map { get; init; }

    public required GameModeEntry GameMode { get; init; }

    public LobbyPlayerState? Players { get; init; }

    public UiNodeViewModel? LobbyRoot { get; init; }
}

public sealed class CampaignLaunchRequest
{
    public required MissionEntry Mission { get; init; }

    public int DifficultyIndex { get; init; } = 1;

    public UiNodeViewModel? OverlayRoot { get; init; }
}
