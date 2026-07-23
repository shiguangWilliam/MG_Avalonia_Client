namespace ClientAvalonia.Session;

/// <summary>
/// 游戏选项状态（大厅 checkbox / dropdown + 协议参数）。
///
/// 作用：统一 Skirmish 本地选项与 CnCNet GO CTCP 载荷的共同面。
/// 字段参考 CnCNetGameOptionsState；Skirmish 可不使用协议相关字段。
/// </summary>
public interface IGameOptionsState
{
    /// <summary>Checkbox 值列表。对应 CnCNetGameOptionsState.CheckBoxValues。</summary>
    IReadOnlyList<bool> CheckBoxValues { get; }

    /// <summary>Dropdown 选中索引。对应 CnCNetGameOptionsState.DropDownIndices。</summary>
    IReadOnlyList<int> DropDownIndices { get; }

    /// <summary>当前地图是否官方。对应 CnCNetGameOptionsState.MapOfficial。</summary>
    bool MapOfficial { get; }

    /// <summary>当前地图 Sha1。对应 CnCNetGameOptionsState.MapSha1。</summary>
    string MapSha1 { get; }

    /// <summary>当前游戏模式名。对应 CnCNetGameOptionsState.GameModeName。</summary>
    string GameModeName { get; }

    /// <summary>FrameSendRate。对应 CnCNetGameOptionsState.FrameSendRate。</summary>
    int FrameSendRate { get; }

    /// <summary>MaxAhead。对应 CnCNetGameOptionsState.MaxAhead。</summary>
    int MaxAhead { get; }

    /// <summary>协议版本。对应 CnCNetGameOptionsState.ProtocolVersion。</summary>
    int ProtocolVersion { get; }

    /// <summary>随机种子。对应 CnCNetGameOptionsState.RandomSeed。</summary>
    int RandomSeed { get; }

    /// <summary>是否移除起点。对应 CnCNetGameOptionsState.RemoveStartingLocations。</summary>
    bool RemoveStartingLocations { get; }

    /// <summary>地图未本地化名。对应 CnCNetGameOptionsState.MapUntranslatedName。</summary>
    string MapUntranslatedName { get; }
}
