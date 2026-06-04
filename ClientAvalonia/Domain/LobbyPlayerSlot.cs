namespace ClientAvalonia.Domain;

/// <summary>One skirmish/multiplayer lobby slot (aligned with XNA PlayerInfo subset).</summary>
public sealed class LobbyPlayerSlot
{
    public const int MaxSlots = 8;

    public string Name { get; set; } = string.Empty;

    public int SideIndex { get; set; }

    public int ColorIndex { get; set; }

    public int StartIndex { get; set; }

    public int TeamIndex { get; set; }

    public int AiLevel { get; set; } = 2;

    public bool IsAi { get; set; }

    public bool IsOccupied => !string.IsNullOrWhiteSpace(Name);

    public bool IsHumanLocal { get; set; }
}
