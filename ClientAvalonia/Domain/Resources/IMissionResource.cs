namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 战役 / 任务资源。
///
/// 作用：替代直接依赖 MissionEntry。字段从 ClientAvalonia/Domain/MissionEntry.cs
/// 反推。ModMetadata 预留 mod 自定义扩展点（战役脚本参数、解锁条件等）。
/// 默认实现：MissionEntry : IMissionResource。
/// </summary>
public interface IMissionResource : IResource
{
    /// <summary>INI section 名（逻辑主键）。对应 MissionEntry.SectionName。</summary>
    string SectionName { get; }

    /// <summary>任务描述文本。对应 MissionEntry.Description。</summary>
    string Description { get; }

    /// <summary>场景地图文件名。对应 MissionEntry.Scenario。空 = UI 分组标题行。</summary>
    string Scenario { get; }

    /// <summary>阵营显示名。对应 MissionEntry.SideName。</summary>
    string SideName { get; }

    /// <summary>阵营索引。对应 MissionEntry.Side。</summary>
    int Side { get; }

    /// <summary>所属战役 ID。对应 MissionEntry.CampaignId（-1 = 无）。</summary>
    int CampaignId { get; }

    /// <summary>是否启用。对应 MissionEntry.Enabled。</summary>
    bool Enabled { get; }

    /// <summary>是否需要资料片。对应 MissionEntry.RequiredAddon。</summary>
    bool RequiredAddon { get; }

    /// <summary>是否允许盟友建筑。对应 MissionEntry.BuildOffAlly。</summary>
    bool BuildOffAlly { get; }

    /// <summary>玩家是否始终普通难度。对应 MissionEntry.PlayerAlwaysOnNormalDifficulty。</summary>
    bool PlayerAlwaysOnNormalDifficulty { get; }

    /// <summary>是否为 UI 分组标题行（Scenario 为空）。对应 MissionEntry.IsHeader。</summary>
    bool IsHeader { get; }

    /// <summary>
    /// Mod 扩展元数据（战役 mod 自定义键值）。现有 MissionEntry 无此字段；
    /// 默认空字典。未来 mod 可通过此字典传递解锁条件、脚本参数等。
    /// </summary>
    IReadOnlyDictionary<string, object> ModMetadata { get; }
}
