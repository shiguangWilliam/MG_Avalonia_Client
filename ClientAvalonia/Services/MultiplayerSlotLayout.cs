using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Maps CnCNet PO order to lobby rows (XNA Players + AIPlayers → consecutive UI rows).</summary>
public static class MultiplayerSlotLayout
{
    /// <summary>Legacy 入口（Phase 3 P3-4：标记为已过时）。新代码用 <see cref="ApplyToSlots"/> + <see cref="IPlayerSlotSink"/>。</summary>
    [Obsolete("Phase 3 P3-4: 改用 ApplyToSlots + session.SlotSink。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static void ApplyToState(
        LobbyPlayerState state,
        IReadOnlyList<CnCNetGameRoomPlayer> entries,
        string localNick)
    {
        state.ClearSlots();
        ApplyToSlots(state.Slots, entries, localNick);
    }

    /// <summary>
    /// Session-aware 重载（Phase 2 缺口 2.5）：把 PO DTO 写入任意 <see cref="IPlayerSlot"/> 数组。
    /// 不调用 ClearSlots——调用方负责清空（在 Session 路径下，清空由 SlotSink.ClearAll 完成）。
    /// </summary>
    public static void ApplyToSlots(
        IReadOnlyList<IPlayerSlot> slots,
        IReadOnlyList<CnCNetGameRoomPlayer> entries,
        string localNick)
    {
        ArgumentNullException.ThrowIfNull(slots);

        int row = 0;
        foreach (CnCNetGameRoomPlayer entry in entries)
        {
            if (row >= slots.Count)
                break;

            if (entry.IsAi)
                ApplyAi(slots[row], entry);
            else
                ApplyHuman(slots[row], entry, localNick);

            row++;
        }
    }

    [Obsolete("Phase 3 P3-4: 改用 IReadOnlyList<IPlayerSlot> 重载。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static List<LobbyPlayerSlot> ExtractAiRows(LobbyPlayerState state)
    {
        var ais = new List<LobbyPlayerSlot>();
        int start = state.HumanRowCount;
        for (int i = start; i < start + state.AiRowCount; i++)
            ais.Add(state.Slots[i].Clone());

        return ais;
    }

    /// <summary>Legacy 入口（Phase 3 P3-4：标记为已过时）。新代码用 <see cref="BuildPoList"/>。</summary>
    [Obsolete("Phase 3 P3-4: 改用 BuildPoList(IReadOnlyList<IPlayerSlot>, string, IReadOnlyList<string>)。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static List<CnCNetGameRoomPlayer> BuildPoListFromState(LobbyPlayerState state, string hostName)
        => BuildPoList(state.Slots, hostName, state.AiNames);

    /// <summary>
    /// Session-aware 重载（Phase 2 缺口 2.5）：从任意 <see cref="IPlayerSlot"/> 数组重建 PO DTO。
    /// </summary>
    /// <param name="slots">槽位数组（按顺序：先人类，再 AI；空位跳过）。</param>
    /// <param name="hostName">房主名（用于 IsHost 标记）。</param>
    /// <param name="aiNames">AI 名字目录（按 AiLevel 索引）。</param>
    public static List<CnCNetGameRoomPlayer> BuildPoList(
        IReadOnlyList<IPlayerSlot> slots,
        string hostName,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(aiNames);

        var entries = new List<CnCNetGameRoomPlayer>();
        int humanCount = slots.HumanRowCount();

        for (int i = 0; i < humanCount; i++)
        {
            IPlayerSlot slot = slots[i];
            entries.Add(new CnCNetGameRoomPlayer
            {
                Name = slot.Name,
                IsHost = slot.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase),
                SideId = slot.SideIndex,
                ColorId = slot.ColorIndex,
                TeamId = slot.TeamIndex,
                StartingLocation = slot.StartIndex,
                Ready = slot.IsHumanLocal && slot.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase),
            });
        }

        for (int i = humanCount; i < slots.Count; i++)
        {
            IPlayerSlot slot = slots[i];
            if (!slot.IsOccupied || !slot.IsAi)
                continue;

            entries.Add(new CnCNetGameRoomPlayer
            {
                IsAi = true,
                AiLevel = slot.AiLevel,
                Name = ResolveAiName(aiNames, slot.AiLevel),
                SideId = slot.SideIndex,
                ColorId = slot.ColorIndex,
                TeamId = slot.TeamIndex,
                StartingLocation = slot.StartIndex,
                Ready = true,
            });
        }

        return entries;
    }

    public static void ApplySkirmishAiSelection(LobbyPlayerState state, int rowIndex, int selectedIndex)
    {
        if (selectedIndex < 1 || selectedIndex - 1 >= state.AiNames.Count)
        {
            state.Slots[rowIndex].Clear();
            state.RebuildAiRowsFromUi(state.HumanRowCount);
            return;
        }

        string aiName = state.AiNames[selectedIndex - 1];
        LobbyPlayerSlot slot = state.Slots[rowIndex];
        slot.Name = aiName;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = selectedIndex - 1;
        state.RebuildAiRowsFromUi(state.HumanRowCount);
    }

    private static void ApplyHuman(IPlayerSlot slot, CnCNetGameRoomPlayer human, string localNick)
    {
        slot.Name = human.Name;
        slot.IsAi = false;
        slot.IsHumanLocal = human.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
        slot.SideIndex = human.SideId;
        slot.ColorIndex = human.ColorId;
        slot.TeamIndex = human.TeamId;
        slot.StartIndex = Math.Max(0, human.StartingLocation);
    }

    private static void ApplyAi(IPlayerSlot slot, CnCNetGameRoomPlayer ai)
    {
        slot.Name = ai.Name;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = ai.AiLevel;
        slot.SideIndex = ai.SideId;
        slot.ColorIndex = ai.ColorId;
        slot.TeamIndex = ai.TeamId;
        slot.StartIndex = Math.Max(0, ai.StartingLocation);
    }

    private static string ResolveAiName(IReadOnlyList<string> aiNames, int aiLevel)
    {
        if (aiLevel >= 0 && aiLevel < aiNames.Count)
            return aiNames[aiLevel];

        return aiNames.Count > 0 ? aiNames[0] : "AI";
    }
}
