using System;
using System.Collections.Generic;

namespace ClientAvalonia.Domain;

/// <summary>
/// Pure assignment / clear rules for map starting locations.
/// Mirrors DX <c>MapPreviewBox.Indicator_LeftClick</c> / <c>Indicator_RightClick</c>
/// / <c>ContextMenu_OptionSelected</c> without depending on UI widgets.
/// </summary>
public static class MapStartLocationRules
{
    /// <summary>
    /// Whether a joiner (non-host) may claim <paramref name="startLocation1Based"/>.
    /// Returns <c>false</c> when <paramref name="enforceMaxPlayers"/> and the spot is occupied.
    /// </summary>
    public static bool CanJoinerSelect(
        IList<LobbyPlayerSlot> slots,
        int startLocation1Based,
        bool enforceMaxPlayers)
    {
        if (startLocation1Based <= 0)
            return true;

        if (!enforceMaxPlayers)
            return true;

        for (int i = 0; i < slots.Count; i++)
        {
            LobbyPlayerSlot slot = slots[i];
            if (slot.IsOccupied && slot.StartIndex == startLocation1Based)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Apply a local (joiner) starting-location selection. Returns <c>false</c> if blocked by occupancy.
    /// </summary>
    public static bool TryApplyJoinerSelection(
        IList<LobbyPlayerSlot> slots,
        string localPlayerName,
        int startLocation1Based,
        bool enforceMaxPlayers)
    {
        if (startLocation1Based > 0
            && !CanJoinerSelect(slots, startLocation1Based, enforceMaxPlayers))
        {
            return false;
        }

        LobbyPlayerSlot? local = FindLocal(slots, localPlayerName);
        if (local == null)
            return false;

        local.StartIndex = Math.Max(0, startLocation1Based);
        return true;
    }

    /// <summary>
    /// Host assigns <paramref name="startLocation1Based"/> to the slot at <paramref name="targetSlotIndex"/>.
    /// When <paramref name="enforceMaxPlayers"/>, clears any other occupant of that spot first.
    /// </summary>
    public static bool TryApplyHostAssignment(
        IList<LobbyPlayerSlot> slots,
        int targetSlotIndex,
        int startLocation1Based,
        bool enforceMaxPlayers)
    {
        if (targetSlotIndex < 0 || targetSlotIndex >= slots.Count)
            return false;

        LobbyPlayerSlot target = slots[targetSlotIndex];
        if (!target.IsOccupied)
            return false;

        if (enforceMaxPlayers && startLocation1Based > 0)
            ClearOccupantsOf(slots, startLocation1Based);

        target.StartIndex = Math.Max(0, startLocation1Based);
        return true;
    }

    /// <summary>Host clears every occupant of the given starting location.</summary>
    public static void ClearOccupantsOf(IList<LobbyPlayerSlot> slots, int startLocation1Based)
    {
        if (startLocation1Based <= 0)
            return;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].StartIndex == startLocation1Based)
                slots[i].StartIndex = 0;
        }
    }

    /// <summary>
    /// Joiner right-click: clear only if the indicated spot is currently their own.
    /// </summary>
    public static bool TryClearLocalIfOwn(
        IList<LobbyPlayerSlot> slots,
        string localPlayerName,
        int startLocation1Based)
    {
        LobbyPlayerSlot? local = FindLocal(slots, localPlayerName);
        if (local == null)
            return false;

        if (local.StartIndex != startLocation1Based)
            return false;

        local.StartIndex = 0;
        return true;
    }

    private static LobbyPlayerSlot? FindLocal(IList<LobbyPlayerSlot> slots, string localPlayerName)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            LobbyPlayerSlot slot = slots[i];
            if (slot.IsHumanLocal
                || (!slot.IsAi
                    && slot.IsOccupied
                    && slot.Name.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase)))
            {
                return slot;
            }
        }

        return null;
    }
}
