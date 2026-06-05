using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Services;

/// <summary>Host-side slot edits (XNA GameLobbyBase.CopyPlayerDataFromUI + MultiplayerGameLobby).</summary>
public static class MultiplayerSlotCoordinator
{
    public static void HandleHostSlotEdit(
        LobbyPlayerState state,
        int slotIndex,
        LobbyPlayerSlot previous,
        UiNodeViewModel ddName,
        CnCNetGameRoomSession? gameRoom)
    {
        if (state.Mode != LobbyPlayerMode.Multiplayer || !state.AllowHostPlayerOptions || gameRoom == null)
            return;

        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName))
        {
            gameRoom.KickPlayer(previous.Name);
            state.Slots[slotIndex] = previous;
            return;
        }

        if (LobbyPlayerSlotUiRules.IsBanSelection(ddName))
        {
            gameRoom.KickPlayer(previous.Name);
            state.Slots[slotIndex] = previous;
            return;
        }

        LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slotIndex, state);
        if (rowKind == LobbyPlayerRowKind.Open || rowKind == LobbyPlayerRowKind.Ai)
            ApplyAiRowFromUi(state, slotIndex, ddName);

        if (state.Slots[slotIndex].IsOccupied && !state.Slots[slotIndex].IsAi)
            gameRoom.UpdateHumanFromSlot(state.Slots[slotIndex]);

        gameRoom.SyncPlayersFromLobby(state, state.HostPlayerName);
    }

    public static void HandleHostOptionsEdit(LobbyPlayerState state, CnCNetGameRoomSession? gameRoom)
    {
        if (state.Mode != LobbyPlayerMode.Multiplayer || !state.AllowHostPlayerOptions || gameRoom == null)
            return;

        foreach (LobbyPlayerSlot slot in state.Slots)
        {
            if (slot.IsOccupied && !slot.IsAi)
                gameRoom.UpdateHumanFromSlot(slot);
        }

        gameRoom.SyncPlayersFromLobby(state, state.HostPlayerName);
    }

    public static void HandleSkirmishNameEdit(LobbyPlayerState state, int slotIndex, UiNodeViewModel ddName)
    {
        LobbyPlayerRowKind rowKind = state.GetRowKind(slotIndex);

        if (rowKind == LobbyPlayerRowKind.Human)
            return;

        if (rowKind is LobbyPlayerRowKind.Open or LobbyPlayerRowKind.Ai or LobbyPlayerRowKind.Closed)
            MultiplayerSlotLayout.ApplySkirmishAiSelection(state, slotIndex, ddName.SelectedIndex);
    }

    private static void ApplyAiRowFromUi(LobbyPlayerState state, int slotIndex, UiNodeViewModel ddName)
    {
        bool hostMultiplayer = state.Mode == LobbyPlayerMode.Multiplayer && state.AllowHostPlayerOptions;

        if (ddName.SelectedIndex < 1 || ddName.SelectedIndex - 1 >= state.AiNames.Count)
        {
            state.Slots[slotIndex].Clear();
            if (!hostMultiplayer)
                CompactOccupiedRows(state);
            return;
        }

        string aiName = state.AiNames[ddName.SelectedIndex - 1];
        LobbyPlayerSlot slot = state.Slots[slotIndex];
        slot.Name = aiName;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = ddName.SelectedIndex - 1;
        if (!hostMultiplayer)
            CompactOccupiedRows(state);
    }

    private static void CompactOccupiedRows(LobbyPlayerState state)
    {
        int firstAiRow = state.HumanRowCount;
        state.RebuildAiRowsFromUi(firstAiRow);
    }
}
