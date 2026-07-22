using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Abstract base for all UI action contexts (dependency bundles).
///
/// Subclasses (<c>LobbyActionContext</c>, etc.) add domain-specific dependencies
/// such as <c>IGameSession</c>, <c>LobbySessionState</c>, <c>ResourceResolver</c>.
///
/// <see cref="UiAction{TContext}"/> constrains <c>TContext</c> to this base so the
/// executor (<see cref="ActionExecutor{TAction,TContext}"/>) can handle the
/// unified refresh pipeline generically.
/// </summary>
/// <remarks>
/// 与 <see cref="UIActionContext"/>（struct）共存——后者是 catalog 派发的轻量上下文（值类型、零分配），
/// 本抽象类是 <c>UiAction&lt;TContext&gt;</c> 体系的依赖容器基类（多态、复杂场景）。
/// </remarks>
public abstract class UiActionContext
{
    /// <summary>
    /// Optional triggering UI node (for reading $Tag, control ID, etc.).
    /// </summary>
    public UiNodeViewModel? Source { get; init; }
}
