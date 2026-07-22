using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Per-slot player name items and enable rules (XNA GameLobbyBase.CopyPlayerDataToUI).</summary>
public static class LobbyPlayerSlotUiRules
{
    private const string AiPlaceholder = "-";
    internal const string KickLabel = "Kick";
    internal const string BanLabel = "Ban";

    // ---- Session / LobbySessionState 入口（Phase 2 缺口 2.5） ----

    /// <summary>
    /// Session-aware 入口：把 UI 输入态写到 <see cref="LobbySessionState"/>（不再写 LobbyPlayerState）。
    /// Skirmish 模式下 Mode 由 <paramref name="session"/> 类型决定。
    /// </summary>
    public static void ConfigureForSkirmish(LobbySessionState ui, ISkirmishSession session)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(session);

        ui.UIMode = LobbyPlayerMode.Skirmish;
        ui.AllowHostPlayerOptions = true;
        ui.LocalPlayerName = ProgramConstants.PLAYERNAME;
        ui.HostPlayerName = ProgramConstants.PLAYERNAME;
    }

    /// <summary>
    /// Session-aware 入口：把 UI 输入态写到 <see cref="LobbySessionState"/> + 把 PO DTO 应用到 Session 槽位。
    /// </summary>
    /// <param name="ui">UI 输入态载体。</param>
    /// <param name="session">CnCNet 房间 Session。</param>
    /// <param name="entries">CTCP 收到的 PO DTO。</param>
    /// <param name="localNick">本地玩家名。</param>
    /// <param name="hostName">房主名。</param>
    /// <param name="isHost">本机是否房主。</param>
    /// <param name="resetSlots">是否清空槽位（首次进入或模式切换时 true）。</param>
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
            ? ProgramConstants.PLAYERNAME
            : localNick;
        ui.HostPlayerName = string.IsNullOrWhiteSpace(hostName)
            ? ui.LocalPlayerName
            : hostName;

        MultiplayerSlotLayout.ApplyToSlots(session.PlayerSlots, entries, localNick);
    }

    // ---- 旧重载（保留门面，等 Phase 3 删） ----

    [Obsolete("Phase 3 P3-4: 改用 ConfigureForSkirmish(LobbySessionState, ISkirmishSession)。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static void ConfigureForSkirmish(LobbyPlayerState state)
    {
        state.Mode = LobbyPlayerMode.Skirmish;
        state.AllowHostPlayerOptions = true;
        state.LocalPlayerName = ProgramConstants.PLAYERNAME;
        state.HostPlayerName = ProgramConstants.PLAYERNAME;
    }

    [Obsolete("Phase 3 P3-4: 改用 ConfigureForMultiplayer(LobbySessionState, ICnCNetGameSession, ...)。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
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
        => GetUiRowKind(slotIndex, state.Slots, state.Mode, state.AllowHostPlayerOptions);

    /// <summary>
    /// Phase 3 P3-3：Session-aware 重载——直接吃 <see cref="IReadOnlyList{IPlayerSlot}"/> + 显式 mode / allowHost，
    /// 不再依赖 <see cref="LobbyPlayerState"/>。供 <see cref="LobbyPlayerStatusApplier"/> 等 Session-aware 路径使用。
    /// </summary>
    public static LobbyPlayerRowKind GetUiRowKind(
        int slotIndex,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions)
    {
        if (mode == LobbyPlayerMode.Multiplayer && allowHostPlayerOptions)
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

    /// <summary>
    /// Phase 5 P5-2：Session-aware 主入口——直接吃 <see cref="IReadOnlyList{IPlayerSlot}"/> +
    /// 显式 mode / allowHost / aiNames，不再依赖 <see cref="LobbyPlayerState"/>。
    /// 行为与旧 <see cref="BuildNameItems(int, LobbyPlayerState)"/> 完全等价。
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

    private static string[] BuildHumanNameItems(int slotIndex, LobbyPlayerSlot slot, LobbyPlayerState state)
        => BuildHumanNameItems(slotIndex, slot, state.Mode, state.AllowHostPlayerOptions);

    /// <summary>Phase 5 P5-2：纯参数版本，与新 Session-aware 入口共用。</summary>
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

    private static string[] BuildAiNameItems(LobbyPlayerState state)
        => BuildAiNameItems(state.AiNames);

    private static string[] BuildAiNameItems(IReadOnlyList<string> aiNames)
    {
        var items = new List<string> { AiPlaceholder };
        items.AddRange(aiNames);
        return items.ToArray();
    }

    private static string[] BuildOpenNameItems(LobbyPlayerState state)
        => BuildOpenNameItems(state.AiNames);

    private static string[] BuildOpenNameItems(IReadOnlyList<string> aiNames)
    {
        var items = new List<string> { string.Empty };
        items.AddRange(aiNames);
        return items.ToArray();
    }

    public static bool IsNameDropdownEnabled(int slotIndex, LobbyPlayerState state)
        => IsNameDropdownEnabled(slotIndex, state.Slots, state.Mode, state.AllowHostPlayerOptions);

    /// <summary>
    /// Phase 5 P5-2：Session-aware 主入口——吃 <see cref="IReadOnlyList{IPlayerSlot}"/> + 显式 mode/allowHost。
    /// </summary>
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

    /// <summary>XNA: allowOptionsChange || pInfo.Name == ProgramConstants.PLAYERNAME.</summary>
    public static bool ArePlayerOptionsEnabled(int slotIndex, LobbyPlayerState state)
        => ArePlayerOptionsEnabled(slotIndex, state.Slots, state.Mode, state.AllowHostPlayerOptions);

    /// <summary>
    /// Phase 5 P5-2：Session-aware 主入口——吃 <see cref="IReadOnlyList{IPlayerSlot}"/> + 显式 mode/allowHost。
    /// </summary>
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

    public static int ResolveNameSelectedIndex(UiNodeViewModel dropdown, LobbyPlayerSlot slot, LobbyPlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return ResolveNameSelectedIndex(dropdown, slot, state.AiNames);
    }

    /// <summary>
    /// Phase 5 P5-2：Session-aware 主入口——吃任意 <see cref="IPlayerSlot"/> + 显式 aiNames。
    /// </summary>
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
