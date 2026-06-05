using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Per-slot player name items and enable rules (XNA GameLobbyBase.CopyPlayerDataToUI).</summary>
public static class LobbyPlayerSlotUiRules
{
    private const string AiPlaceholder = "-";

    public static void ConfigureForSkirmish(LobbyPlayerState state)
    {
        state.Mode = LobbyPlayerMode.Skirmish;
        state.AllowHostPlayerOptions = true;
        state.LocalPlayerName = ProgramConstants.PLAYERNAME;
    }

    public static void ConfigureForMultiplayer(LobbyPlayerState state, string localNick, bool isHost)
    {
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = isHost;
        state.LocalPlayerName = string.IsNullOrWhiteSpace(localNick)
            ? ProgramConstants.PLAYERNAME
            : localNick;
    }

    public static string[] BuildNameItems(int slotIndex, LobbyPlayerState state)
    {
        LobbyPlayerSlot slot = state.Slots[slotIndex];

        if (slot.IsOccupied && !slot.IsAi)
            return [slot.Name];

        if (slot.IsAi)
        {
            var aiItems = new List<string> { AiPlaceholder };
            aiItems.AddRange(state.AiNames);
            return aiItems.ToArray();
        }

        if (IsNextOpenSlot(slotIndex, state) && state.AllowHostPlayerOptions)
        {
            var openItems = new List<string> { string.Empty };
            openItems.AddRange(state.AiNames);
            return openItems.ToArray();
        }

        return [string.Empty];
    }

    public static bool IsNameDropdownEnabled(int slotIndex, LobbyPlayerState state)
    {
        LobbyPlayerSlot slot = state.Slots[slotIndex];
        if (slot.IsOccupied && !slot.IsAi)
            return false;

        if (slot.IsAi)
            return state.AllowHostPlayerOptions;

        return IsNextOpenSlot(slotIndex, state) && state.AllowHostPlayerOptions;
    }

    public static bool ArePlayerOptionsEnabled(int slotIndex, LobbyPlayerState state)
    {
        LobbyPlayerSlot slot = state.Slots[slotIndex];
        if (!slot.IsOccupied)
            return false;

        if (slot.IsHumanLocal)
            return true;

        return state.AllowHostPlayerOptions;
    }

    public static int ResolveNameSelectedIndex(UiNodeViewModel dropdown, LobbyPlayerSlot slot, LobbyPlayerState state)
    {
        if (!slot.IsOccupied)
            return 0;

        for (int i = 0; i < dropdown.ComboItems.Count; i++)
        {
            if (dropdown.ComboItems[i].Equals(slot.Name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        if (slot.IsAi)
        {
            int aiIndex = IndexOfAiName(state.AiNames, slot.Name);
            return aiIndex >= 0 ? 1 + aiIndex : 1;
        }

        return 0;
    }

    private static bool IsNextOpenSlot(int slotIndex, LobbyPlayerState state)
    {
        int firstEmpty = state.FirstEmptySlotIndex();
        return firstEmpty >= 0 && slotIndex == firstEmpty;
    }

    private static int IndexOfAiName(IReadOnlyList<string> aiNames, string name)
    {
        for (int i = 0; i < aiNames.Count; i++)
        {
            if (aiNames[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
