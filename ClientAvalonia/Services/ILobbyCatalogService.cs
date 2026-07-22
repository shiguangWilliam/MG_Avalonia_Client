using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientCore;

namespace ClientAvalonia.Services;

/// <summary>
/// 大厅目录服务——提供阵营/AI 名/队伍名目录的只读访问与刷新。
/// </summary>
/// <remarks>
/// 设计理由（见 layered-architecture-progress-report.md §9.5 Slice 2）：
/// <list type="bullet">
/// <item>这些目录原本散落在 <c>LobbyPlayerState</c> 的 SideNames/AiNames/TeamNames 字段里——
/// 它们其实是 Service 层资源（从 INI/ProgramConstants 加载），不是 Session 状态。</item>
/// <item>提取到独立 Service 后，BindingApplier / Coordinator 可以直接拿目录，
/// 不必经 <c>LobbyPlayerState</c>。</item>
/// <item>Service 实现内部仍委托 <c>LobbySideCatalog</c> 与 <c>ProgramConstants</c>，
/// 避免大幅迁移；长期可换为基于 INI 文件流的实现。</item>
/// </list>
/// </remarks>
public interface ILobbyCatalogService
{
    /// <summary>阵营显示名列表（与 <see cref="SideEntries"/> 顺序一致）。</summary>
    IReadOnlyList<string> SideNames { get; }

    /// <summary>阵营条目（含 InternalName / Icon / DisplayName 等）。</summary>
    IReadOnlyList<LobbySideEntry> SideEntries { get; }

    /// <summary>AI 名列表（按 AiLevel 索引；来自 ProgramConstants.AI_PLAYER_NAMES）。</summary>
    IReadOnlyList<string> AiNames { get; }

    /// <summary>队伍名列表（来自 ProgramConstants.TEAMS）。</summary>
    IReadOnlyList<string> TeamNames { get; }

    /// <summary>
    /// 重新从底层资源加载目录（缓存失效）。
    /// </summary>
    /// <param name="includeSpectator">是否包含旁观者阵营。</param>
    void Reload(bool includeSpectator = true);
}

/// <summary>
/// 默认实现——委托给 <see cref="LobbySideCatalog"/> 与 <see cref="ProgramConstants"/>。
/// </summary>
public sealed class LobbyCatalogService : ILobbyCatalogService
{
    /// <summary>单例（生产代码用；测试可自建实例或 mock）。</summary>
    public static LobbyCatalogService Instance { get; } = new();

    private bool _includeSpectator = true;

    /// <summary>构造（公开以便单元测试 new 出独立实例；生产代码用 <see cref="Instance"/>）。</summary>
    public LobbyCatalogService() { }

    /// <inheritdoc />
    public IReadOnlyList<string> SideNames { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<LobbySideEntry> SideEntries { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> AiNames { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> TeamNames { get; private set; } = [];

    /// <inheritdoc />
    public void Reload(bool includeSpectator = true)
    {
        _includeSpectator = includeSpectator;
        LobbySideCatalog.InvalidateCache();
        LobbySideCatalogSnapshot snapshot = LobbySideCatalog.GetSnapshot(includeSpectator);
        SideEntries = snapshot.Entries;
        SideNames = snapshot.Entries.Select(s => s.DisplayName).ToList();
        AiNames = ProgramConstants.AI_PLAYER_NAMES.ToList();
        TeamNames = ProgramConstants.TEAMS.ToList();
    }
}
