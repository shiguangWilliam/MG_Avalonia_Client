using System;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Root abstraction for all UI-driven state mutations. Subclasses are responsible
/// for the *state change only* — the unified refresh pipeline is invoked by
/// <see cref="ActionExecutor{TAction,TContext}"/> after Execute returns.
/// </summary>
/// <typeparam name="TContext">
/// The action's dependency bundle. Must derive from <see cref="UiActionContext"/>.
///</typeparam>
/// <remarks>
/// Design (auto-refresh-design.md v2):
///  - Top-level base class so future <c>MenuAction</c> / <c>OptionsAction</c> can
///    extend without touching <c>LobbyAction</c>.
///  - Generic to keep Execute type-safe; the executor wraps the refresh pipeline.
///  - <see cref="Undo"/> defaults to throw — only override on naturally reversible
///    actions (color, side, team). Most lobby mutations are not reversible.
/// </remarks>
public abstract class UiAction<TContext> where TContext : UiActionContext
{
    /// <summary>Human-readable label for logging / audit / replay.</summary>
    public virtual string DisplayName => GetType().Name;

    /// <summary>When the action was created (UTC). For replay/debug.</summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>Mutate state. Do NOT refresh UI here — the executor handles it.</summary>
    public abstract void Execute(TContext ctx);

    /// <summary>
    /// Reverse the state change. Default throws because most actions are not
    /// naturally reversible. Override only where the action is reversible.
    /// </summary>
    public virtual void Undo(TContext ctx)
        => throw new NotSupportedException($"{GetType().Name} does not support Undo.");
}
