using ClientAvalonia.Domain.Resources;

namespace ClientAvalonia.Session;

/// <summary>
/// 战役会话。平级于 ISkirmishSession，不继承遭遇战。
///
/// 作用：战役只有 Mission + 有限玩家配置，语义上不是"遭遇战"。
/// ModMetadata 预留战役 mod 自定义扩展点。
/// </summary>
public interface IMissionSession : IGameSession
{
    /// <summary>当前任务。对应 MissionEntry 选中项。</summary>
    IMissionResource Mission { get; }

    /// <summary>战役 mod 扩展元数据（解锁条件、脚本参数等）。</summary>
    IReadOnlyDictionary<string, object> ModMetadata { get; }
}
