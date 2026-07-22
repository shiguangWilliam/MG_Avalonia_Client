namespace ClientAvalonia.GlobalState.Environment;

/// <summary>
/// 极简服务定位器。
///
/// 作用：替代 Microsoft.Extensions.DependencyInjection 容器。
/// 桌面客户端启动一次、运行时无热重载，DI 容器收益不抵复杂度。
/// 此类只做一件事：保存接口 → 工厂的映射，让 Resolve&lt;T&gt;() 返回实例。
/// 测试通过 Reset() 清理 + 重新 Register() 注入 mock。
///
/// ★ 未注册时抛 InvalidOperationException，不 fallback 到 ProgramConstants。
/// </summary>
public static class EnvironmentServices
{
    private static readonly Dictionary<Type, Func<object>> _factories = new();
    private static readonly object _sync = new();

    /// <summary>注册接口 T 的工厂。后注册的覆盖先注册的。</summary>
    public static void Register<T>(Func<T> factory) where T : class
    {
        lock (_sync)
        {
            _factories[typeof(T)] = () => factory();
        }
    }

    /// <summary>
    /// 解析接口 T 的实例。
    /// 未注册则抛 InvalidOperationException（明确提示是否忘记 Register）。
    /// </summary>
    public static T Resolve<T>() where T : class
    {
        lock (_sync)
        {
            if (_factories.TryGetValue(typeof(T), out Func<object>? factory))
                return (T)factory();
        }

        throw new InvalidOperationException(
            $"No factory registered for {typeof(T).Name}. " +
            "Did you forget to call EnvironmentServices.Register in PreStartup.Initialize or test setup?");
    }

    /// <summary>
    /// 安全解析：未注册或工厂抛异常时返回 null（不传播异常）。
    /// 用于可选依赖或启动早期/关闭晚期——此时 DI 可能尚未就绪。
    /// </summary>
    public static T? TryResolve<T>() where T : class
    {
        lock (_sync)
        {
            if (!_factories.TryGetValue(typeof(T), out Func<object>? factory))
                return null;

            try
            {
                return (T)factory();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>测试专用：清空所有注册。生产代码不要调用。</summary>
    internal static void Reset()
    {
        lock (_sync)
        {
            _factories.Clear();
        }
    }
}
