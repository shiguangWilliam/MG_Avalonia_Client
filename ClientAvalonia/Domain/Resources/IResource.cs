namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 所有游戏资源的公共元数据契约。
///
/// 作用：统一 Map / Mission / GameMode（及未来 mod 扩展资源）的身份标识、
/// 显示名、文件路径、内容 hash、版本与来源。在线更新 / 增量包 / 完整性校验
/// 都依赖此契约，而不是各 DTO 各自发明一套字段。
/// </summary>
public interface IResource
{
    /// <summary>
    /// 逻辑标识。Map 通常为 Sha1；Mission 为 SectionName；GameMode 为 Name。
    /// 用于 catalog 索引、增量 diff 的主键匹配。
    /// </summary>
    string LogicalId { get; }

    /// <summary>
    /// UI 显示名（已本地化）。对应 MapEntry.DisplayName / MissionEntry.DisplayName /
    /// GameModeEntry.DisplayName。
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 未本地化名（用于 hash、匹配、跨语言协议）。对应 MapEntry.UntranslatedName /
    /// GameModeEntry.UntranslatedUIName。Mission 可回退到 SectionName。
    /// </summary>
    string UntranslatedName { get; }

    /// <summary>
    /// 绝对路径或相对 GamePath 的路径。对应 MapEntry.CompleteFilePath /
    /// MapEntry.BaseFilePath；Mission 对应 Scenario 文件路径。
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 内容 hash（在线更新校验用）。对应 MapEntry.Sha1。
    /// Mission / GameMode 若无现成 hash，加载期计算或留空字符串。
    /// </summary>
    string Sha1 { get; }

    /// <summary>
    /// 文件大小（字节）。现有 DTO 无此字段；加载期从 FileInfo 填充。
    /// 增量包选型与进度条依赖此值。
    /// </summary>
    long SizeBytes { get; }

    /// <summary>
    /// 资源来源。对应 MapEntry.IsOfficial / IsCustom 的语义扩展。
    /// Official = 官方包；Custom = 用户自定义；ModExtension = mod 扩展；
    /// Downloaded = 在线下载缓存。
    /// </summary>
    ResourceOrigin Origin { get; }

    /// <summary>
    /// 资源版本（在线更新增量包用）。现有 DTO 无此字段；默认 (0,0,0,0)。
    /// </summary>
    VersionInfo Version { get; }

    /// <summary>
    /// official 资源不允许用户修改。通常 Origin == Official 时为 true。
    /// </summary>
    bool IsReadOnly { get; }
}

/// <summary>资源来源枚举。</summary>
public enum ResourceOrigin
{
    /// <summary>官方发行包内资源。</summary>
    Official,

    /// <summary>用户自定义（如 Custom Maps 目录）。</summary>
    Custom,

    /// <summary>Mod 扩展包提供。</summary>
    ModExtension,

    /// <summary>在线下载 / 增量更新缓存。</summary>
    Downloaded,
}

/// <summary>资源版本四元组。</summary>
public sealed record VersionInfo(int Major, int Minor, int Build, int Revision);
