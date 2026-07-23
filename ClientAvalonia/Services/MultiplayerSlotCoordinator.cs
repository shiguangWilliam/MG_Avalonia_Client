using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;

namespace ClientAvalonia.Services;

/// <summary>Host-side slot edits (XNA GameLobbyBase.CopyPlayerDataFromUI + MultiplayerGameLobby).</summary>
public static class MultiplayerSlotCoordinator
{
    /// <summary>
    /// Session-aware 入口（Slice 5 新增）：吃 <see cref="ICnCNetGameSession"/> + <see cref="IPlayerSlotSink"/>。
    /// 模式与 AllowHostPlayerOptions 由调用方传入（来自 LobbySessionState UI 输入态）。
    /// 仍委托给既有 <see cref="HandleHostSlotEdit(LobbyPlayerState, int, LobbyPlayerSlot, UiNodeViewModel, CnCNetGameRoomSession?)"/>。
    /// </summary>
    public static void HandleHostSlotEdit(
        ICnCNetGameSession session,
        int slotIndex,
        ICnCNetPlayerSlot previous,
        UiNodeViewModel ddName,
        bool allowHostPlayerOptions,
        string hostPlayerName)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 兼容期：仍走 LobbyPlayerState 视图。Slice 6 删除 LobbyPlayerState 后会改为直接操作 IPlayerSlotSink。
        if (session is not CnCNetGameRoomSession concrete)
            return;

        var state = BuildLobbyView(session, allowHostPlayerOptions, hostPlayerName);
        var prev = previous as LobbyPlayerSlot ?? new LobbyPlayerSlot();
        HandleHostSlotEdit(state, slotIndex, prev, ddName, concrete);
    }

    private static LobbyPlayerState BuildLobbyView(ICnCNetGameSession session, bool allow, string host)
    {
        // 临时视图，仅为复用既有逻辑；写回通过 session.SlotSink 完成（既有方法会触发 StateChanged）。
        // Phase 2 P2-3：投影 session.PlayerSlots 到临时视图，使 HandleHostSlotEdit 旧逻辑读到最新真相。
        var state = new LobbyPlayerState
        {
            Mode = LobbyPlayerMode.Multiplayer,
            AllowHostPlayerOptions = allow,
            HostPlayerName = host,
            LocalPlayerName = host,
        };
        state.SyncFromSlots(session.PlayerSlots);
        return state;
    }

    [Obsolete("Phase 3 P3-4: 改用 HandleHostSlotEdit(ICnCNetGameSession, ...) 重载。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
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

    [Obsolete("Phase 3 P3-4: 改用 HandleHostOptionsEdit(ICnCNetGameSession, string, IReadOnlyList<string>)。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
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

    /// <summary>
    /// Phase 2 P2-3：Session-aware 重载——用 <see cref="ICnCNetGameSession.BroadcastPlayerOptionsFromSlots"/>
    /// 替代旧 <see cref="CnCNetGameRoomSession.SyncPlayersFromLobby"/>。不再依赖 <see cref="LobbyPlayerState"/>。
    /// 读 <see cref="IGameSession.PlayerSlots"/> 当前所有人类玩家的 side/color/team/start，写回 _players 后广播。
    /// </summary>
    /// <param name="session">CnCNet 房间 Session。</param>
    /// <param name="hostName">房主名（用于 PO DTO 中 IsHost 标记）。</param>
    /// <param name="aiNames">AI 名字目录（按 AiLevel 索引）。</param>
    public static void HandleHostOptionsEdit(
        ICnCNetGameSession session,
        string hostName,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(session);

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

    /// <summary>Joiner side/color/start/team edits (XNA MultiplayerGameLobby.CopyPlayerDataFromUI → OR CTCP).</summary>
    [Obsolete("Phase 3 P3-4: 改用 HandleJoinerOptionsEdit(ICnCNetGameSession, int)。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static void HandleJoinerOptionsEdit(
        LobbyPlayerState state,
        int slotIndex,
        CnCNetGameRoomSession? gameRoom)
    {
        if (state.Mode != LobbyPlayerMode.Multiplayer || state.AllowHostPlayerOptions || gameRoom == null)
            return;

        LobbyPlayerSlot slot = state.Slots[slotIndex];
        if (!slot.IsHumanLocal)
            return;

        gameRoom.RequestLocalPlayerOptions(slot);
    }

    /// <summary>
    /// Phase 2 P2-3：Session-aware 重载——Joiner 改自己 side/color/start/team，
    /// 直接读 <see cref="IGameSession.PlayerSlots"/> 找本地玩家槽位，发 OR CTCP。
    /// </summary>
    /// <param name="session">CnCNet 房间 Session。</param>
    /// <param name="slotIndex">UI 触发槽位（必须是本地玩家槽位才发）。</param>
    public static void HandleJoinerOptionsEdit(ICnCNetGameSession session, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (slotIndex < 0 || slotIndex >= session.PlayerSlots.Count)
            return;

        IPlayerSlot slot = session.PlayerSlots[slotIndex];
        if (!slot.IsHumanLocal)
            return;

        if (slot is LobbyPlayerSlot lobby)
        {
            if (session is CnCNetGameRoomSession concrete)
                concrete.RequestLocalPlayerOptions(lobby);
        }
        else if (session is CnCNetGameRoomSession concrete)
        {
            // IPlayerSlot 不是 LobbyPlayerSlot —— 转换后发送（保持 OR CTCP 协议契约）。
            concrete.RequestLocalPlayerOptions(ToLobbySlot(slot));
        }
    }

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
