namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 多人 / 遭遇战地图资源。
///
/// 作用：替代直接依赖 MapEntry 具体类。字段从 ClientAvalonia/Domain/MapEntry.cs
/// 反推；ChangeMapAction / MapListBindingApplier / spawn 写入均应依赖此接口。
/// 默认实现：MapEntry : IMapResource（渐进迁移，旧代码不破坏）。
/// </summary>
public interface IMapResource : IResource
{
    /// <summary>所属游戏模式名列表。对应 MapEntry.GameModes。</summary>
    IReadOnlyList<string> GameModes { get; }

    /// <summary>最少玩家数。对应 MapEntry.MinPlayers。</summary>
    int MinPlayers { get; }

    /// <summary>最多玩家数。对应 MapEntry.MaxPlayers。DefaultAiSlotPolicy 据此填充。</summary>
    int MaxPlayers { get; }

    /// <summary>是否强制 MaxPlayers 上限。对应 MapEntry.EnforceMaxPlayers。</summary>
    bool EnforceMaxPlayers { get; }

    /// <summary>是否仅多人可用。对应 MapEntry.MultiplayerOnly。</summary>
    bool MultiplayerOnly { get; }

    /// <summary>是否自定义地图。对应 MapEntry.IsCustom（与 Origin 互补，保留兼容语义）。</summary>
    bool IsCustom { get; }

    /// <summary>预览图相对路径。对应 MapEntry.PreviewRelativePath。</summary>
    string PreviewRelativePath { get; }

    /// <summary>附加 INI 名。对应 MapEntry.ExtraIniName。</summary>
    string ExtraIniName { get; }

    /// <summary>
    /// 起点 waypoint 原始 token。对应 MapEntry.Waypoints
    ///（MPMaps.ini Waypoint0..7 或自定义 [Waypoints]）。
    /// </summary>
    IReadOnlyList<string> Waypoints { get; }

    /// <summary>TDRA 地图原点 X。对应 MapEntry.MapX。</summary>
    int MapX { get; }

    /// <summary>TDRA 地图原点 Y。对应 MapEntry.MapY。</summary>
    int MapY { get; }

    /// <summary>TDRA 地图宽度。对应 MapEntry.MapWidth。</summary>
    int MapWidth { get; }

    /// <summary>TDRA 地图高度。对应 MapEntry.MapHeight。</summary>
    int MapHeight { get; }

    /// <summary>Isometric ActualSize（4 CSV ints）。对应 MapEntry.ActualSize。</summary>
    IReadOnlyList<string> ActualSize { get; }

    /// <summary>Isometric LocalSize（4 CSV ints）。对应 MapEntry.LocalSize。</summary>
    IReadOnlyList<string> LocalSize { get; }
}
