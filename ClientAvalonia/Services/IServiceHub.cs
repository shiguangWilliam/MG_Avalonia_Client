using System;
using ClientAvalonia.GlobalState.Environment;

namespace ClientAvalonia.Services;
/// <summary>
/// Service 容器接口——CmdAction 通过它访问 Service 层组件。
/// </summary>
/// <remarks>
/// 设计理由（见 <c>docs/design/layered-architecture.md</c> §4.1）：
/// <list type="bullet">
/// <item>CmdAction 不应直接调 <c>EnvironmentServices.Resolve&lt;T&gt;()</c>——那样测试时无法注入 mock。</item>
/// <item>Hub 接口允许 mock，且明确列出可用的 Service。</item>
/// <item>默认实现 <see cref="DefaultServiceHub"/> 转发给 <see cref="EnvironmentServices"/>。</item>
/// </list>
/// </remarks>
public interface IServiceHub
{
    /// <summary>解析必需的 Service；未注册时抛 <see cref="InvalidOperationException"/>。</summary>
    T Get<T>() where T : class;

    /// <summary>尝试解析；未注册时返回 false 且 service 为 null。</summary>
    bool TryGet<T>(out T? service) where T : class;
}

/// <summary>
/// 默认实现——把所有解析转发给 <see cref="EnvironmentServices"/>。
/// </summary>
public sealed class DefaultServiceHub : IServiceHub
{
    /// <summary>单例实例（CmdAction 派发时复用，避免重复构造）。</summary>
    public static DefaultServiceHub Instance { get; } = new();

    private DefaultServiceHub() { }

    /// <inheritdoc />
    public T Get<T>() where T : class
        => EnvironmentServices.Resolve<T>();

    /// <inheritdoc />
    public bool TryGet<T>(out T? service) where T : class
    {
        try
        {
            service = EnvironmentServices.Resolve<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            service = null;
            return false;
        }
    }
}
