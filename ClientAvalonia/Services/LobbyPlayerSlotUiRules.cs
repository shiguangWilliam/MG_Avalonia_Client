using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Per-slot player name items and enable rules (XNA GameLobbyBase.CopyPlayerDataToUI).</summary>
public static class LobbyPlayerSlotUiRules
{
    private const string AiPlaceholder = "-";
    internal const string KickLabel = "Kick";
    internal const string BanLabel = "Ban";

    /// <summary>
    /// Session-aware 入口：把 UI 输入态写到 <see cref="LobbySessionState"/>。
    /// </summary>
    public static void ConfigureForSkirmish(LobbySessionState ui, ISkirmishSession session)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(session);

        ui.UIMode = LobbyPlayerMode.Skirmish;
        ui.AllowHostPlayerOptions = true;
        ui.LocalPlayerName = AppState.Environment.PlayerName;
        ui.HostPlayerName = AppState.Environment.PlayerName;
    }

    /// <summary>
    /// Session-aware 入口：把 UI 输入态写到 <see cref="LobbySessionState"/> + 把 PO DTO 应用到 Session 槽位。
    /// </summary>
    public static void ConfigureForMultiplayer(
        LobbySessionState ui,
        ICnCNetGameSession session,
        IReadOnlyList<CnCNet.CnCNetGameRoomPlayer> entries,
        string localNick,
        string hostName,
        bool isHost,
        bool resetSlots = false)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(session);

        if (resetSlots || ui.UIMode != LobbyPlayerMode.Multiplayer)
            session.SlotSink.ClearAll();

        ui.UIMode = LobbyPlayerMode.Multiplayer;
        ui.AllowHostPlayerOptions = isHost;
        ui.LocalPlayerName = string.IsNullOrWhiteSpace(localNick)
            ? AppState.Environment.PlayerName
            : localNick;
        ui.HostPlayerName = string.IsNullOrWhiteSpace(hostName)
            ? ui.LocalPlayerName
            : hostName;

        MultiplayerSlotLayout.ApplyToSlots(session.PlayerSlots, entries, localNick);
    }

    /// <summary>
    /// Row type for UI enable/items.
    /// CnCNet/multiplayer override: empty rows are always Open (never Closed cascade).
    /// </summary>
    public static LobbyPlayerRowKind GetUiRowKind(
        int slotIndex,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions)
    {
        _ = allowHostPlayerOptions;

        if (mode == LobbyPlayerMode.Multiplayer)
        {
            int humanRowCount = slots.HumanRowCount();
            if (slotIndex < humanRowCount)
                return LobbyPlayerRowKind.Human;

            IPlayerSlot slot = slots[slotIndex];
            if (slot.IsOccupied && slot.IsAi)
                return LobbyPlayerRowKind.Ai;

            return LobbyPlayerRowKind.Open;
        }

        return slots.GetRowKind(slotIndex);
    }

    /// <summary>
    /// Session-aware：直接吃 <see cref="IReadOnlyList{IPlayerSlot}"/> + 显式 mode / allowHost / aiNames。
    /// </summary>
    public static string[] BuildNameItems(
        int slotIndex,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(aiNames);

        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, slots, mode, allowHostPlayerOptions);
        IPlayerSlot slot = slots[slotIndex];

        return rowKind switch
        {
            LobbyPlayerRowKind.Human => BuildHumanNameItems(slotIndex, slot, mode, allowHostPlayerOptions),
            LobbyPlayerRowKind.Ai => BuildAiNameItems(aiNames),
            LobbyPlayerRowKind.Open or LobbyPlayerRowKind.Closed => BuildOpenNameItems(aiNames),
            _ => [string.Empty],
        };
    }

    private static string[] BuildHumanNameItems(
        int slotIndex, IPlayerSlot slot, LobbyPlayerMode mode, bool allowHostPlayerOptions)
    {
        if (mode == LobbyPlayerMode.Multiplayer
            && allowHostPlayerOptions
            && slotIndex > 0
            && slot.IsOccupied
            && !slot.IsAi
            && !slot.IsHumanLocal)
        {
            return [slot.Name, string.Empty, KickLabel, BanLabel];
        }

        return [slot.Name];
    }

    private static string[] BuildAiNameItems(IReadOnlyList<string> aiNames)
    {
        var items = new List<string> { AiPlaceholder };
        items.AddRange(aiNames);
        return items.ToArray();
    }

    private static string[] BuildOpenNameItems(IReadOnlyList<string> aiNames)
    {
        var items = new List<string> { string.Empty };
        items.AddRange(aiNames);
        return items.ToArray();
    }

    public static bool IsNameDropdownEnabled(
        int slotIndex,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (!allowHostPlayerOptions && mode == LobbyPlayerMode.Multiplayer)
            return false;

        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, slots, mode, allowHostPlayerOptions);

        return rowKind switch
        {
            LobbyPlayerRowKind.Human => mode == LobbyPlayerMode.Multiplayer
                && allowHostPlayerOptions
                && slotIndex > 0,
            LobbyPlayerRowKind.Ai => allowHostPlayerOptions,
            LobbyPlayerRowKind.Open => allowHostPlayerOptions,
            _ => false,
        };
    }

    /// <summary>XNA: allowOptionsChange || pInfo.Name == AppState.Environment.PlayerName.</summary>
    public static bool ArePlayerOptionsEnabled(
        int slotIndex,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions)
    {
        ArgumentNullException.ThrowIfNull(slots);

        LobbyPlayerRowKind rowKind = GetUiRowKind(slotIndex, slots, mode, allowHostPlayerOptions);
        if (rowKind is LobbyPlayerRowKind.Closed or LobbyPlayerRowKind.Open)
            return false;

        if (slotIndex < 0 || slotIndex >= slots.Count)
            return false;

        IPlayerSlot slot = slots[slotIndex];
        if (!slot.IsOccupied)
            return false;

        if (mode == LobbyPlayerMode.Skirmish)
            return true;

        if (slot.IsHumanLocal)
            return true;

        return allowHostPlayerOptions;
    }

    public static int ResolveNameSelectedIndex(
        UiNodeViewModel dropdown,
        IPlayerSlot slot,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(dropdown);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(aiNames);

        if (!slot.IsOccupied)
            return 0;

        if (slot.IsAi)
        {
            int aiIndex = IndexOfAiName(aiNames, slot.Name);
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
