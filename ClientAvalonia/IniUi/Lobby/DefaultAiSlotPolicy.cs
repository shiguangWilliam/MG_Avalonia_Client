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

        IReadOnlyList<IPlayerSlot> slots = session.PlayerSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            IPlayerSlot slot = slots[i];
            slot.Name = string.Empty;
            slot.IsAi = false;
            slot.IsHumanLocal = false;
            slot.SideIndex = 0;
            slot.ColorIndex = 0;
            slot.StartIndex = 0;
            slot.TeamIndex = 0;
            slot.AiLevel = 0;
        }

        IPlayerSlot human = slots[0];
        human.Name = playerName;
        human.IsHumanLocal = true;
        human.IsAi = false;
        human.AiLevel = 0;
        human.SideIndex = 0;
        human.ColorIndex = 0;
        human.TeamIndex = 0;
        human.StartIndex = 0;

        IReadOnlyList<string> names = aiNames ?? [];
        int colorCount = Math.Max(1, colors.Load().Count);
        for (int i = 1; i < maxPlayers; i++)
        {
            IPlayerSlot slot = slots[i];
            slot.Name = names.Count > 0 ? names[0] : $"AI {i}";
            slot.IsAi = true;
            slot.IsHumanLocal = false;
            slot.AiLevel = 0;
            slot.SideIndex = 0;
            slot.ColorIndex = i % colorCount;
            slot.TeamIndex = 0;
            slot.StartIndex = 0;
        }

        if (session is SkirmishSession skirmish)
            skirmish.NotifyStateChanged();
    }
}
