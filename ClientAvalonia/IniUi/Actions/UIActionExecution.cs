using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// <see cref="IUIAction"/> 派发时的轻量执行上下文（struct）。
/// </summary>
/// <remarks>
/// 设计理由（见 <c>docs/design/layered-architecture.md</c> §2.3）：
/// <list type="bullet">
/// <item>State 和 Command 共享同一上下文类型——StateAction 主要用 <see cref="Session"/>；CmdAction 主要用 <see cref="Services"/>。</item>
/// <item>用 struct + readonly 字段，避免每次派发分配新对象。</item>
/// <item>未来扩展字段（如 Logger / CancellationToken）不破坏 action 签名。</item>
/// </list>
///
/// 与 <see cref="UiActionContext"/>（abstract class）的区别：
/// 后者是 <c>UiAction&lt;TContext&gt;</c> 体系的依赖容器基类（多态、复杂场景）；
/// 本 struct 是 <see cref="IIniActionCatalogUIExtensions.RegisterState"/>/<see cref="IIniActionCatalogUIExtensions.RegisterCommand"/>
/// 注册的轻量委托的入参（值类型、零分配）。
/// </remarks>
public readonly struct UIActionContext
{
    /// <summary>INI 里冒号后的参数（如 "NavigateTo:SkirmishLobby" 的 "SkirmishLobby"）。</summary>
    public string Args { get; init; }

    /// <summary>触发动作的 UI 节点（用于读控件 ID、$Tag 等；可能为 null）。</summary>
    public UiNodeViewModel? Source { get; init; }

    /// <summary>
    /// 当前 Session 抽象。StateAction 主用（写 SlotSink）；CmdAction 只读。
    /// 可能为 null（如应用启动早期、MainMenu 的 Exit）。
    /// </summary>
    public IGameSession? Session { get; init; }

    /// <summary>
    /// Service 容器（CmdAction 主用）。
    /// </summary>
    public IServiceHub? Services { get; init; }

    /// <summary>
    /// UI 导航主机（旧代码兼容，<see cref="IIniActionCatalog"/> 现有签名需要）。
    /// </summary>
    public IUiNavigationHost? Host { get; init; }

    /// <summary>便捷构造：从现有 catalog 派发参数创建。</summary>
    public static UIActionContext Create(
        string args,
        UiNodeViewModel? source,
        IGameSession? session,
        IServiceHub? services,
        IUiNavigationHost? host)
        => new()
        {
            Args = args ?? string.Empty,
            Source = source,
            Session = session,
            Services = services,
            Host = host,
        };
}
