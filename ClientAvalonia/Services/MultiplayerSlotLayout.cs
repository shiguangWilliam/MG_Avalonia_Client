using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Maps CnCNet PO order to lobby rows (XNA Players + AIPlayers → consecutive UI rows).</summary>
public static class MultiplayerSlotLayout
{
    /// <summary>
    /// 把 PO DTO 写入任意 <see cref="IPlayerSlot"/> 数组。
    /// 不调用 ClearSlots——调用方负责清空（Session 路径下由 SlotSink.ClearAll 完成）。
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

    /// <summary>
    /// 从任意 <see cref="IPlayerSlot"/> 数组重建 PO DTO。
    /// </summary>
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

    /// <summary>
    /// Skirmish：按 name dropdown 选择 AI / 清空，并重排 AI 行。
    /// </summary>
    public static void ApplySkirmishAiSelection(
        LobbyPlayerSlot[] slots,
        IReadOnlyList<string> aiNames,
        int rowIndex,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(aiNames);

        if (rowIndex < 0 || rowIndex >= slots.Length)
            return;

        if (selectedIndex < 1 || selectedIndex - 1 >= aiNames.Count)
        {
            slots[rowIndex].Clear();
            RebuildAiRowsFromUi(slots, ((IReadOnlyList<IPlayerSlot>)slots).HumanRowCount());
            return;
        }

        string aiName = aiNames[selectedIndex - 1];
        LobbyPlayerSlot slot = slots[rowIndex];
        slot.Name = aiName;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = selectedIndex - 1;
        RebuildAiRowsFromUi(slots, ((IReadOnlyList<IPlayerSlot>)slots).HumanRowCount());
    }

    /// <summary>Rebuild AI rows from UI starting at first AI row (XNA CopyPlayerDataFromUI).</summary>
    public static void RebuildAiRowsFromUi(LobbyPlayerSlot[] slots, int firstAiRow)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var preserved = new List<LobbyPlayerSlot>();
        for (int i = firstAiRow; i < slots.Length; i++)
        {
            LobbyPlayerSlot slot = slots[i];
            if (slot.IsOccupied && slot.IsAi)
                preserved.Add(slot.Clone());
        }

        for (int i = firstAiRow; i < slots.Length; i++)
            slots[i].Clear();

        int row = firstAiRow;
        foreach (LobbyPlayerSlot ai in preserved)
        {
            if (row >= slots.Length)
                break;

            slots[row] = ai;
            row++;
        }
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
