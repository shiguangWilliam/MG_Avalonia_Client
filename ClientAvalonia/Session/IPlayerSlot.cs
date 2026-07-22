namespace ClientAvalonia.Session;

/// <summary>
/// 单个玩家 / AI 槽位。
///
/// 作用：替代直接操作 LobbyPlayerSlot 具体类。字段从
/// ClientAvalonia/Domain/LobbyPlayerSlot.cs 反推。
/// ColorIndex 是分配状态；颜色目录在 IMultiplayerColorCatalog。
/// 默认实现：LobbyPlayerSlot : IPlayerSlot。
/// </summary>
public interface IPlayerSlot
{
    /// <summary>显示名（人名或 AI 名）。对应 LobbyPlayerSlot.Name。</summary>
    string Name { get; set; }

    /// <summary>阵营索引。对应 LobbyPlayerSlot.SideIndex。</summary>
    int SideIndex { get; set; }

    /// <summary>
    /// 颜色索引（分配状态）。对应 LobbyPlayerSlot.ColorIndex。
    /// 目录上限由 IMultiplayerColorCatalog.Load().Count 决定。
    /// </summary>
    int ColorIndex { get; set; }

    /// <summary>队伍索引。对应 LobbyPlayerSlot.TeamIndex。</summary>
    int TeamIndex { get; set; }

    /// <summary>起点索引。对应 LobbyPlayerSlot.StartIndex。</summary>
    int StartIndex { get; set; }

    /// <summary>是否 AI。对应 LobbyPlayerSlot.IsAi。</summary>
    bool IsAi { get; set; }

    /// <summary>AI 难度等级。对应 LobbyPlayerSlot.AiLevel。</summary>
    int AiLevel { get; set; }

    /// <summary>是否本机人类玩家。对应 LobbyPlayerSlot.IsHumanLocal。</summary>
    bool IsHumanLocal { get; set; }

    /// <summary>槽位是否被占用（Name 非空）。对应 LobbyPlayerSlot.IsOccupied。</summary>
    bool IsOccupied { get; }
}
