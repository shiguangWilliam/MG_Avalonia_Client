using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Lobby;

/// <summary>
/// Single-mode skirmish AI slot policy: clear and fill to map capacity.
/// Per auto-ai-slots.md v2 — intentionally non-preserving.
/// </summary>
public static class DefaultAiSlotPolicy
{
    /// <summary>
    /// Resets the session slots: 1 local human + (maxPlayers - 1) default AIs.
    /// </summary>
    public static void AutoFillToMapCapacity(
        ISkirmishSession session,
        int maxPlayers,
        string playerName,
        IMultiplayerColorCatalog colors,
        IReadOnlyList<string>? aiNames = null)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        if (string.IsNullOrWhiteSpace(playerName))
            playerName = "Player";

        if (maxPlayers < 1) maxPlayers = 1;
        if (maxPlayers > LobbyPlayerSlot.MaxSlots) maxPlayers = LobbyPlayerSlot.MaxSlots;

        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();

        grid[0].Name = playerName;
        grid[0].IsHumanLocal = true;
        grid[0].IsAi = false;

        IReadOnlyList<string> names = aiNames ?? [];
        int colorCount = Math.Max(1, colors.Load().Count);
        for (int i = 1; i < maxPlayers; i++)
        {
            LobbyPlayerSlot slot = grid[i];
            slot.Name = names.Count > 0 ? names[(i - 1) % names.Count] : $"AI {i}";
            slot.IsAi = true;
            slot.IsHumanLocal = false;
            slot.AiLevel = 0;
            slot.SideIndex = 0;
            slot.ColorIndex = i % colorCount;
            slot.TeamIndex = 0;
            slot.StartIndex = 0;
        }

        LobbySlotGrid.ApplyToSink(session, grid);
    }
}
