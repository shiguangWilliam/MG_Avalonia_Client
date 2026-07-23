namespace ClientAvalonia.Domain.Resources;

/// <summary>
/// 资源清单与在线更新逻辑服务。独立于 IResource 本身。
///
/// 作用：hash 校验、增量 diff、在线更新检查与应用。当前代码尚无对应实现；
/// 本接口为未来"增量包 / 在线更新"预留契约，L1 阶段可先提供 NoOp 适配器
///（VerifyHash 恒 true、CheckForUpdates 返回空），避免阻塞主路径。
/// </summary>
public interface IResourceManifest
{
    /// <summary>校验资源内容 hash 是否与 Sha1 字段一致。</summary>
    bool VerifyHash(IResource resource);

    /// <summary>
    /// 计算 baseline → current 的增量集合（新增 / 变更 / 删除由调用方解释）。
    /// 用于增量包生成与客户端差量更新。
    /// </summary>
    IReadOnlyList<IResource> ComputeDiff(
        IReadOnlyList<IResource> baseline,
        IReadOnlyList<IResource> current);

    /// <summary>检查是否有可用更新（异步）。</summary>
    Task<UpdateResult> CheckForUpdatesAsync(CancellationToken ct);

    /// <summary>对单个资源应用增量更新（异步）。</summary>
    Task<UpdateResult> ApplyIncrementalUpdateAsync(
        IResource target,
        CancellationToken ct);
}

/// <summary>更新操作结果。</summary>
public sealed record UpdateResult(bool Success, IReadOnlyList<IResource> Updated);

/// <summary>
/// 空操作资源清单实现。
///
/// 作用：L1 阶段占位，VerifyHash 恒为 true，更新检查返回空结果，
/// 不阻塞依赖 IResourceManifest 的调用方编译与运行。
/// </summary>
public sealed class NoOpResourceManifest : IResourceManifest
{
    /// <inheritdoc />
    public bool VerifyHash(IResource resource) => true;

    /// <inheritdoc />
    public IReadOnlyList<IResource> ComputeDiff(
        IReadOnlyList<IResource> baseline,
        IReadOnlyList<IResource> current)
        => [];

    /// <inheritdoc />
    public Task<UpdateResult> CheckForUpdatesAsync(CancellationToken ct)
        => Task.FromResult(new UpdateResult(Success: true, Updated: []));

    /// <inheritdoc />
    public Task<UpdateResult> ApplyIncrementalUpdateAsync(IResource target, CancellationToken ct)
        => Task.FromResult(new UpdateResult(Success: true, Updated: []));
}
