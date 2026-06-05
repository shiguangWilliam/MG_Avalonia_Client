using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Per-slot player name items and enable rules (XNA GameLobbyBase.CopyPlayerDataToUI).</summary>
public static class LobbyPlayerSlotUiRules
{
    private const string AiPlaceholder = "-";
    internal const string KickLabel = "Kick";
    internal const string BanLabel = "Ban";

    public static void ConfigureForSkirmish(LobbyPlayerState state)
    {
        state.Mode = LobbyPlayerMode.Skirmish;
        state.AllowHostPlayerOptions = true;
        state.LocalPlayerName = ProgramConstants.PLAYERNAME;
        state.HostPlayerName = ProgramConstants.PLAYERNAME;
    }

    public static void ConfigureForMultiplayer(
        LobbyPlayerState state,
        string localNick,
        string hostName,
        bool isHost,
        bool resetSlots = false)
    {
        if (resetSlots || state.Mode != LobbyPlayerMode.Multiplayer)
            state.ClearSlots();

        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = isHost;
        state.LocalPlayerName = string.IsNullOrWhiteSpace(localNick)
            ? ProgramConstants.PLAYERNAME
            : localNick;
        state.HostPlayerName = string.IsNullOrWhiteSpace(hostName)
            ? state.LocalPlayerName
            : hostName;
    }

    /// <summary>
    /// Row type for UI enable/items. Host multiplayer: every row after humans is Open or Ai (never Closed).
    /// </summary>
    public static LobbyPlayerRowKind GetUiRowKind(int slotIndex, LobbyPlayerState state)
    {
        if (state.Mode == LobbyPlayerMode.Multiplayer && state.AllowHostPlayerOptions)
        {
            if (slotIndex < state.HumanRowCount)
                return LobbyPlayerRowKind.Human;

            LobbyPlayerSlot slot = state.Slots[slotIndex];
            if (slot.IsOccupied && slot.IsAi)
                return LobbyPlayerRowKind.Ai;

            return LobbyPlayerRowKind.Open;
        }

        return state.GetRowKind(slotIndex);
    }

    public static string[] BuildNameItems(int slotIndex, LobbyPlayerState state)
    {
        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, state);
        LobbyPlayerSlot slot = state.Slots[slotIndex];

        return rowKind switch
        {
            LobbyPlayerRowKind.Human => BuildHumanNameItems(slotIndex, slot, state),
            LobbyPlayerRowKind.Ai => BuildAiNameItems(state),
            LobbyPlayerRowKind.Open or LobbyPlayerRowKind.Closed => BuildOpenNameItems(state),
            _ => [string.Empty],
        };
    }

    private static string[] BuildHumanNameItems(int slotIndex, LobbyPlayerSlot slot, LobbyPlayerState state)
    {
        if (state.Mode == LobbyPlayerMode.Multiplayer
            && state.AllowHostPlayerOptions
            && slotIndex > 0
            && slot.IsOccupied
            && !slot.IsAi
            && !slot.IsHumanLocal)
        {
            return [slot.Name, string.Empty, KickLabel, BanLabel];
        }

        return [slot.Name];
    }

    private static string[] BuildAiNameItems(LobbyPlayerState state)
    {
        var items = new List<string> { AiPlaceholder };
        items.AddRange(state.AiNames);
        return items.ToArray();
    }

    private static string[] BuildOpenNameItems(LobbyPlayerState state)
    {
        var items = new List<string> { string.Empty };
        items.AddRange(state.AiNames);
        return items.ToArray();
    }

    public static bool IsNameDropdownEnabled(int slotIndex, LobbyPlayerState state)
    {
        if (!state.AllowHostPlayerOptions && state.Mode == LobbyPlayerMode.Multiplayer)
            return false;

        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, state);

        return rowKind switch
        {
            LobbyPlayerRowKind.Human => state.Mode == LobbyPlayerMode.Multiplayer
                && state.AllowHostPlayerOptions
                && slotIndex > 0,
            LobbyPlayerRowKind.Ai => state.AllowHostPlayerOptions,
            LobbyPlayerRowKind.Open => state.AllowHostPlayerOptions,
            _ => false,
        };
    }

    /// <summary>XNA: allowOptionsChange || pInfo.Name == ProgramConstants.PLAYERNAME.</summary>
    public static bool ArePlayerOptionsEnabled(int slotIndex, LobbyPlayerState state)
    {
        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, state);
        if (rowKind is LobbyPlayerRowKind.Closed or LobbyPlayerRowKind.Open)
            return false;

        LobbyPlayerSlot slot = state.Slots[slotIndex];
        if (!slot.IsOccupied)
            return false;

        if (state.Mode == LobbyPlayerMode.Skirmish)
            return true;

        if (slot.IsHumanLocal)
            return true;

        return state.AllowHostPlayerOptions;
    }

    public static int ResolveNameSelectedIndex(UiNodeViewModel dropdown, LobbyPlayerSlot slot, LobbyPlayerState state)
    {
        if (!slot.IsOccupied)
            return 0;

        if (slot.IsAi)
        {
            int aiIndex = IndexOfAiName(state.AiNames, slot.Name);
            return aiIndex >= 0 ? 1 + aiIndex : 1;
        }

        for (int i = 0; i < dropdown.ComboItems.Count; i++)
        {
            if (dropdown.ComboItems[i].Equals(slot.Name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    public static bool IsKickSelection(UiNodeViewModel dropdown)
        => dropdown.SelectedIndex >= 0
           && dropdown.SelectedIndex < dropdown.ComboItems.Count
           && dropdown.ComboItems[dropdown.SelectedIndex].Equals(KickLabel, StringComparison.OrdinalIgnoreCase);

    public static bool IsBanSelection(UiNodeViewModel dropdown)
        => dropdown.SelectedIndex >= 0
           && dropdown.SelectedIndex < dropdown.ComboItems.Count
           && dropdown.ComboItems[dropdown.SelectedIndex].Equals(BanLabel, StringComparison.OrdinalIgnoreCase);

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
