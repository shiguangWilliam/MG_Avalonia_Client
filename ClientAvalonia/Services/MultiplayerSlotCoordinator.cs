using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;

namespace ClientAvalonia.Services;

/// <summary>Host-side slot edits (XNA GameLobbyBase.CopyPlayerDataFromUI + MultiplayerGameLobby).</summary>
public static class MultiplayerSlotCoordinator
{
    /// <summary>
    /// Session-aware 入口：吃 <see cref="ICnCNetGameSession"/> + <see cref="IPlayerSlotSink"/>。
    /// </summary>
    public static void HandleHostSlotEdit(
        ICnCNetGameSession session,
        int slotIndex,
        ICnCNetPlayerSlot previous,
        UiNodeViewModel ddName,
        bool allowHostPlayerOptions,
        string hostPlayerName,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aiNames);

        if (!allowHostPlayerOptions)
            return;

        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName)
            || LobbyPlayerSlotUiRules.IsBanSelection(ddName))
        {
            session.KickPlayer(previous.Name);
            // UI sync already mutated the slot; restore prior content then broadcast.
            session.SlotSink.WriteSlot(slotIndex, ToFieldUpdate(previous));
            return;
        }

        LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(
            slotIndex,
            session.PlayerSlots,
            LobbyPlayerMode.Multiplayer,
            allowHostPlayerOptions);

        if (rowKind is LobbyPlayerRowKind.Open or LobbyPlayerRowKind.Ai)
            ApplyAiRowFromUi(session, slotIndex, ddName, aiNames);

        if (slotIndex >= 0 && slotIndex < session.PlayerSlots.Count)
        {
            IPlayerSlot slot = session.PlayerSlots[slotIndex];
            if (slot.IsOccupied && !slot.IsAi)
            {
                session.UpdateHuman(slot.Name, new SlotFieldUpdate
                {
                    SideIndex = slot.SideIndex,
                    ColorIndex = slot.ColorIndex,
                    TeamIndex = slot.TeamIndex,
                    StartIndex = slot.StartIndex,
                });
            }
        }

        session.BroadcastPlayerOptionsFromSlots(hostPlayerName, aiNames);
    }

    /// <summary>
    /// Session-aware：用 <see cref="ICnCNetGameSession.BroadcastPlayerOptionsFromSlots"/>
    /// 同步房主侧 side/color/team/start。
    /// </summary>
    public static void HandleHostOptionsEdit(
        ICnCNetGameSession session,
        string hostName,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aiNames);

        foreach (IPlayerSlot slot in session.PlayerSlots)
        {
            if (slot.IsOccupied && !slot.IsAi)
            {
                session.UpdateHuman(slot.Name, new SlotFieldUpdate
                {
                    SideIndex = slot.SideIndex,
                    ColorIndex = slot.ColorIndex,
                    TeamIndex = slot.TeamIndex,
                    StartIndex = slot.StartIndex,
                });
            }
        }

        session.BroadcastPlayerOptionsFromSlots(hostName, aiNames);
    }

    /// <summary>
    /// Session-aware：Joiner 改自己 side/color/start/team，发 OR CTCP。
    /// </summary>
    public static void HandleJoinerOptionsEdit(ICnCNetGameSession session, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (slotIndex < 0 || slotIndex >= session.PlayerSlots.Count)
            return;

        IPlayerSlot slot = session.PlayerSlots[slotIndex];
        if (!slot.IsHumanLocal)
            return;

        if (session is not CnCNetGameRoomSession concrete)
            return;

        concrete.RequestLocalPlayerOptions(ToLobbySlot(slot));
    }

    /// <summary>
    /// Skirmish：按 name dropdown 改 AI / Open 行。需要具体 <see cref="LobbyPlayerSlot"/> 数组。
    /// </summary>
    public static void HandleSkirmishNameEdit(
        IGameSession session,
        IReadOnlyList<string> aiNames,
        int slotIndex,
        UiNodeViewModel ddName)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aiNames);
        ArgumentNullException.ThrowIfNull(ddName);

        LobbyPlayerRowKind rowKind = session.GetRowKind(slotIndex);
        if (rowKind == LobbyPlayerRowKind.Human)
            return;

        if (rowKind is not (LobbyPlayerRowKind.Open or LobbyPlayerRowKind.Ai or LobbyPlayerRowKind.Closed))
            return;

        LobbyPlayerSlot[]? slots = session.PlayerSlots as LobbyPlayerSlot[]
            ?? (session is SkirmishSession skirmish ? skirmish.Slots : null);
        if (slots == null)
            return;

        MultiplayerSlotLayout.ApplySkirmishAiSelection(slots, aiNames, slotIndex, ddName.SelectedIndex);
    }

    private static void ApplyAiRowFromUi(
        ICnCNetGameSession session,
        int slotIndex,
        UiNodeViewModel ddName,
        IReadOnlyList<string> aiNames)
    {
        if (ddName.SelectedIndex < 1 || ddName.SelectedIndex - 1 >= aiNames.Count)
        {
            session.SlotSink.WriteSlot(slotIndex, new SlotFieldUpdate
            {
                Name = string.Empty,
                IsAi = false,
                IsHumanLocal = false,
                SideIndex = 0,
                ColorIndex = 0,
                TeamIndex = 0,
                StartIndex = 0,
                AiLevel = 0,
            });
            return;
        }

        string aiName = aiNames[ddName.SelectedIndex - 1];
        session.SlotSink.WriteSlot(slotIndex, new SlotFieldUpdate
        {
            Name = aiName,
            IsAi = true,
            IsHumanLocal = false,
            AiLevel = ddName.SelectedIndex - 1,
        });
    }

    private static SlotFieldUpdate ToFieldUpdate(ICnCNetPlayerSlot slot)
        => new()
        {
            Name = slot.Name,
            IsAi = slot.IsAi,
            IsHumanLocal = slot.IsHumanLocal,
            SideIndex = slot.SideIndex,
            ColorIndex = slot.ColorIndex,
            TeamIndex = slot.TeamIndex,
            StartIndex = slot.StartIndex,
            AiLevel = slot.AiLevel,
        };

    private static LobbyPlayerSlot ToLobbySlot(IPlayerSlot slot)
    {
        if (slot is LobbyPlayerSlot concrete)
            return concrete;

        return new LobbyPlayerSlot
        {
            Name = slot.Name,
            SideIndex = slot.SideIndex,
            ColorIndex = slot.ColorIndex,
            TeamIndex = slot.TeamIndex,
            StartIndex = slot.StartIndex,
            AiLevel = slot.AiLevel,
            IsAi = slot.IsAi,
            IsHumanLocal = slot.IsHumanLocal,
        };
    }
}
