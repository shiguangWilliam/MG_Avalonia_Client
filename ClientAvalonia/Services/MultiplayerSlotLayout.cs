using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>Maps CnCNet PO order to lobby rows (XNA Players + AIPlayers → consecutive UI rows).</summary>
public static class MultiplayerSlotLayout
{
    public static void ApplyToState(
        LobbyPlayerState state,
        IReadOnlyList<CnCNetGameRoomPlayer> entries,
        string localNick)
    {
        state.ClearSlots();

        int row = 0;
        foreach (CnCNetGameRoomPlayer entry in entries)
        {
            if (row >= LobbyPlayerSlot.MaxSlots)
                break;

            if (entry.IsAi)
                ApplyAi(state.Slots[row], entry);
            else
                ApplyHuman(state.Slots[row], entry, localNick);

            row++;
        }
    }

    public static List<LobbyPlayerSlot> ExtractAiRows(LobbyPlayerState state)
    {
        var ais = new List<LobbyPlayerSlot>();
        int start = state.HumanRowCount;
        for (int i = start; i < start + state.AiRowCount; i++)
            ais.Add(state.Slots[i].Clone());

        return ais;
    }

    public static List<CnCNetGameRoomPlayer> BuildPoListFromState(LobbyPlayerState state, string hostName)
    {
        var entries = new List<CnCNetGameRoomPlayer>();

        for (int i = 0; i < state.HumanRowCount; i++)
        {
            LobbyPlayerSlot slot = state.Slots[i];
            entries.Add(new CnCNetGameRoomPlayer
            {
                Name = slot.Name,
                IsHost = slot.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase),
                SideId = slot.SideIndex,
                ColorId = slot.ColorIndex,
                TeamId = slot.TeamIndex,
                StartingLocation = slot.StartIndex + 1,
                Ready = slot.IsHumanLocal && slot.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase),
            });
        }

        for (int i = state.HumanRowCount; i < LobbyPlayerSlot.MaxSlots; i++)
        {
            LobbyPlayerSlot slot = state.Slots[i];
            if (!slot.IsOccupied || !slot.IsAi)
                continue;

            entries.Add(new CnCNetGameRoomPlayer
            {
                IsAi = true,
                AiLevel = slot.AiLevel,
                Name = ResolveAiName(state.AiNames, slot.AiLevel),
                SideId = slot.SideIndex,
                ColorId = slot.ColorIndex,
                TeamId = slot.TeamIndex,
                StartingLocation = slot.StartIndex + 1,
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

    private static void ApplyHuman(LobbyPlayerSlot slot, CnCNetGameRoomPlayer human, string localNick)
    {
        slot.Name = human.Name;
        slot.IsAi = false;
        slot.IsHumanLocal = human.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
        slot.SideIndex = human.SideId;
        slot.ColorIndex = human.ColorId;
        slot.TeamIndex = human.TeamId;
        slot.StartIndex = Math.Max(0, human.StartingLocation - 1);
    }

    private static void ApplyAi(LobbyPlayerSlot slot, CnCNetGameRoomPlayer ai)
    {
        slot.Name = ai.Name;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = ai.AiLevel;
        slot.SideIndex = ai.SideId;
        slot.ColorIndex = ai.ColorId;
        slot.TeamIndex = ai.TeamId;
        slot.StartIndex = Math.Max(0, ai.StartingLocation - 1);
    }

    private static string ResolveAiName(IReadOnlyList<string> aiNames, int aiLevel)
    {
        if (aiLevel >= 0 && aiLevel < aiNames.Count)
            return aiNames[aiLevel];

        return aiNames.Count > 0 ? aiNames[0] : "AI";
    }
}
