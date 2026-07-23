using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;

namespace ClientAvalonia.Session;

/// <summary>
/// <see cref="IGameSession"/> 派生只读查询的扩展方法集。
///
/// 设计理由（见 docs/design/layered-architecture.md §1 + layered-architecture-progress-report.md §9.5 Slice 1）：
/// <list type="bullet">
/// <item>这些方法原本散落在 <c>LobbyPlayerState</c> 内部，依赖 <c>LobbyPlayerSlot[]</c> 具体类型。</item>
/// <item>提到扩展方法后，任何持有 <c>IReadOnlyList&lt;IPlayerSlot&gt;</c> 的调用方（Session、Applier、Coordinator）
/// 都可直接调用，无需经 <c>LobbyPlayerState</c>。</item>
/// <item>扩展方法是纯函数（无副作用、不持有状态），可独立单测。</item>
/// </list>
/// </summary>
public static class GameSessionExtensions
{
    /// <summary>
    /// 连续占用的人类玩家行数（XNA Players 列表语义）。
    /// 从槽位 0 起算，遇到第一个非占用或 AI 即停止。
    /// </summary>
    public static int HumanRowCount(this IReadOnlyList<IPlayerSlot> slots)
    {
        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsOccupied && !slots[i].IsAi)
                count++;
            else
                break;
        }
        return count;
    }

    /// <summary>
    /// 紧接人类玩家后的连续 AI 行数（XNA AIPlayers 列表语义）。
    /// 起算位置是 <see cref="HumanRowCount(IReadOnlyList{IPlayerSlot})"/> 返回值。
    /// </summary>
    public static int AiRowCount(this IReadOnlyList<IPlayerSlot> slots)
    {
        int start = slots.HumanRowCount();
        int count = 0;
        for (int i = start; i < slots.Count; i++)
        {
            if (slots[i].IsOccupied && slots[i].IsAi)
                count++;
            else
                break;
        }
        return count;
    }

    /// <summary>占用的总行数（连续段：人类 + AI）。</summary>
    public static int OccupiedRowCount(this IReadOnlyList<IPlayerSlot> slots)
        => slots.HumanRowCount() + slots.AiRowCount();

    /// <summary>占用的槽位总数（非连续）——扫描整个 slots 列表。</summary>
    public static int OccupiedSlotCount(this IReadOnlyList<IPlayerSlot> slots)
        => slots.Count(s => s.IsOccupied);

    /// <summary>
    /// 返回指定索引处的行类型（Human / AI / Open / Closed）。
    /// 与 <c>LobbyPlayerState.GetRowKind</c> 语义一致：
    /// <list type="bullet">
    /// <item>0..HumanRowCount → Human</item>
    /// <item>HumanRowCount..HumanRowCount+AiRowCount → AI</item>
    /// <item>下一格 → Open</item>
    /// <item>其余 → Closed</item>
    /// </list>
    /// </summary>
    public static LobbyPlayerRowKind GetRowKind(this IReadOnlyList<IPlayerSlot> slots, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
            return LobbyPlayerRowKind.Closed;

        int humans = slots.HumanRowCount();
        int ais = slots.AiRowCount();

        if (slotIndex < humans)
            return LobbyPlayerRowKind.Human;
        if (slotIndex < humans + ais)
            return LobbyPlayerRowKind.Ai;
        if (slotIndex == humans + ais)
            return LobbyPlayerRowKind.Open;
        return LobbyPlayerRowKind.Closed;
    }

    /// <summary>
    /// 第一个未占用槽位的索引；全占用时返回 -1。
    /// </summary>
    public static int FirstEmptySlotIndex(this IReadOnlyList<IPlayerSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsOccupied)
                return i;
        }
        return -1;
    }

    // ---- IGameSession 便捷重载（直接转发到 PlayerSlots）----

    /// <summary>从 Session 读取派生属性的重载。</summary>
    public static int HumanRowCount(this IGameSession session)
        => session.PlayerSlots.HumanRowCount();

    public static int AiRowCount(this IGameSession session)
        => session.PlayerSlots.AiRowCount();

    public static int OccupiedRowCount(this IGameSession session)
        => session.PlayerSlots.OccupiedRowCount();

    public static int OccupiedSlotCount(this IGameSession session)
        => session.PlayerSlots.OccupiedSlotCount();

    public static LobbyPlayerRowKind GetRowKind(this IGameSession session, int slotIndex)
        => session.PlayerSlots.GetRowKind(slotIndex);

    public static int FirstEmptySlotIndex(this IGameSession session)
        => session.PlayerSlots.FirstEmptySlotIndex();
}
