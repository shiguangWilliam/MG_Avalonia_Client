using ClientAvalonia.CnCNet;
using ClientAvalonia.Session;

namespace ClientAvalonia.CnCNet.Protocol;

/// <summary>
/// PlayerOptions (PO) CTCP 消息 ↔ 槽位数组的双向转换。
///
/// 设计目标：
///   - 纯函数，无状态，可独立单测。
///   - 收口原本散落在 <c>MultiplayerSlotLayout</c> 与
///     <c>CnCNetGameRoomSession.SyncPlayersFromLobby</c> 中的双向拷贝逻辑。
///   - 让 <c>CnCNetGameRoomPlayer</c> 降为「瞬时编解码 DTO」，
///     不再作为长期状态存在 Session 里。
///
/// 字段映射（DX PO 字段名 → C# 属性）：
///   Name / IsHost / IsAi / AiLevel / Ready / AutoReady
///   SideId   ↔ IPlayerSlot.SideIndex
///   ColorId  ↔ IPlayerSlot.ColorIndex
///   TeamId   ↔ IPlayerSlot.TeamIndex
///   StartingLocation ↔ IPlayerSlot.StartIndex
///   Ping / Port 仅 ICnCNetPlayerSlot 有。
/// </summary>
public static class PlayerOptionsCodec
{
    /// <summary>
    /// 把 Session 槽位编码成 PO DTO 列表（用于 CTCP 广播）。
    /// 跳过未占用的槽位；人类在前、AI 在后（与 DX PO 顺序一致）。
    /// </summary>
    /// <param name="slots">Session 当前槽位（通常是 8 槽固定长度）。</param>
    /// <param name="hostName">房主名（用于在 DTO 上标 IsHost / Ready）。</param>
    /// <param name="aiNames">AI 名字目录（按 AiLevel 索引）。</param>
    public static IReadOnlyList<CnCNetGameRoomPlayer> ToDto(
        IReadOnlyList<ICnCNetPlayerSlot> slots,
        string hostName,
        IReadOnlyList<string> aiNames)
    {
        var entries = new List<CnCNetGameRoomPlayer>();

        // 先人类（顺序与 BuildPoListFromState 一致）
        foreach (ICnCNetPlayerSlot slot in slots)
        {
            if (!slot.IsOccupied || slot.IsAi)
                continue;

            bool isHost = !string.IsNullOrEmpty(hostName)
                && slot.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase);

            entries.Add(new CnCNetGameRoomPlayer
            {
                Name = slot.Name,
                IsHost = isHost,
                IsAi = false,
                SideId = slot.SideIndex,
                ColorId = slot.ColorIndex,
                TeamId = slot.TeamIndex,
                StartingLocation = slot.StartIndex,
                Ready = isHost || slot.Ready,
                AutoReady = slot.AutoReady,
                Ping = slot.Ping,
                Port = slot.Port,
            });
        }

        // 再 AI
        foreach (ICnCNetPlayerSlot slot in slots)
        {
            if (!slot.IsOccupied || !slot.IsAi)
                continue;

            entries.Add(new CnCNetGameRoomPlayer
            {
                Name = ResolveAiName(aiNames, slot.AiLevel),
                IsAi = true,
                AiLevel = slot.AiLevel,
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
    /// 把收到的 PO DTO 应用到 Session 槽位（覆盖式）。
    /// 超出 <paramref name="slots"/> 容量的 DTO 项被丢弃；多余的槽位被 Clear。
    /// </summary>
    /// <param name="dto">收到的 PO 列表。</param>
    /// <param name="slots">目标槽位数组（通常是 8 槽）。</param>
    /// <param name="localNick">本机玩家名（用于标 IsHumanLocal）。</param>
    public static void ApplyDto(
        IReadOnlyList<CnCNetGameRoomPlayer> dto,
        IList<ICnCNetPlayerSlot> slots,
        string localNick)
    {
        foreach (ICnCNetPlayerSlot slot in slots)
            ClearSlot(slot);

        int row = 0;
        foreach (CnCNetGameRoomPlayer entry in dto)
        {
            if (row >= slots.Count)
                break;

            ICnCNetPlayerSlot slot = slots[row];
            if (entry.IsAi)
                ApplyAi(slot, entry);
            else
                ApplyHuman(slot, entry, localNick);

            row++;
        }
    }

    /// <summary>
    /// 判断两个 PO DTO 列表是否等价（用于避免无变化的广播）。
    /// 比较字段：Name / IsHost / IsAi / AiLevel / SideId / ColorId / TeamId / StartingLocation / Ready / AutoReady。
    /// 不比较 Ping / Port（运行期可变，不影响 PO 内容）。
    /// </summary>
    public static bool AreEquivalent(
        IReadOnlyList<CnCNetGameRoomPlayer>? a,
        IReadOnlyList<CnCNetGameRoomPlayer>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            CnCNetGameRoomPlayer x = a[i];
            CnCNetGameRoomPlayer y = b[i];
            if (!string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (x.IsHost != y.IsHost) return false;
            if (x.IsAi != y.IsAi) return false;
            if (x.AiLevel != y.AiLevel) return false;
            if (x.SideId != y.SideId) return false;
            if (x.ColorId != y.ColorId) return false;
            if (x.TeamId != y.TeamId) return false;
            if (x.StartingLocation != y.StartingLocation) return false;
            if (x.Ready != y.Ready) return false;
            if (x.AutoReady != y.AutoReady) return false;
        }

        return true;
    }

    private static void ApplyHuman(ICnCNetPlayerSlot slot, CnCNetGameRoomPlayer human, string localNick)
    {
        slot.Name = human.Name;
        slot.IsAi = false;
        slot.IsHumanLocal = !string.IsNullOrEmpty(localNick)
            && human.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
        slot.SideIndex = human.SideId;
        slot.ColorIndex = human.ColorId;
        slot.TeamIndex = human.TeamId;
        slot.StartIndex = Math.Max(0, human.StartingLocation);
        slot.IsHost = human.IsHost;
        slot.Ready = human.Ready;
        slot.AutoReady = human.AutoReady;
    }

    /// <summary>
    /// 重置槽位所有字段到默认值。不依赖具体类型（如 LobbyPlayerSlot.Clear），
    /// 因为 ICnCNetPlayerSlot 没有暴露 Clear() —— CnCNet 与 Skirmish 复用同一个
    /// 默认实现，但接口保持最小。
    /// </summary>
    private static void ClearSlot(ICnCNetPlayerSlot slot)
    {
        slot.Name = string.Empty;
        slot.IsAi = false;
        slot.IsHumanLocal = false;
        slot.SideIndex = 0;
        slot.ColorIndex = 0;
        slot.StartIndex = 0;
        slot.TeamIndex = 0;
        slot.AiLevel = 0;
        slot.IsHost = false;
        slot.Ready = false;
        slot.AutoReady = false;
        slot.Ping = -1;
        slot.Port = 0;
    }

    private static void ApplyAi(ICnCNetPlayerSlot slot, CnCNetGameRoomPlayer ai)
    {
        slot.Name = ai.Name;
        slot.IsAi = true;
        slot.IsHumanLocal = false;
        slot.AiLevel = ai.AiLevel;
        slot.SideIndex = ai.SideId;
        slot.ColorIndex = ai.ColorId;
        slot.TeamIndex = ai.TeamId;
        slot.StartIndex = Math.Max(0, ai.StartingLocation);
        slot.Ready = true;
    }

    private static string ResolveAiName(IReadOnlyList<string> aiNames, int aiLevel)
    {
        if (aiLevel >= 0 && aiLevel < aiNames.Count)
            return aiNames[aiLevel];

        return aiNames.Count > 0 ? aiNames[0] : "AI";
    }
}
