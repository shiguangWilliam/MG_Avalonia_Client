using System;
using System.Collections.Generic;

using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// Unified execution pipeline: runs the action, then invokes a configurable list
/// of refresh steps. Each refresh step swallows its own exceptions so a failure
/// in one step does not skip the rest. This is the heart of the auto-refresh
/// fix — every action gets the same refresh sequence, so UI drift is impossible.
/// </summary>
/// <typeparam name="TAction">Action type, must derive from <see cref="UiAction{TContext}"/>.</typeparam>
/// <typeparam name="TContext">Context type, must derive from <see cref="UiActionContext"/>.</typeparam>
public sealed class ActionExecutor<TAction, TContext>
    where TAction : UiAction<TContext>
    where TContext : UiActionContext
{
    private readonly TContext _ctx;
    private readonly IReadOnlyList<Action<TContext>> _refreshSteps;

    public ActionExecutor(TContext ctx, IReadOnlyList<Action<TContext>> refreshSteps)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _refreshSteps = refreshSteps ?? throw new ArgumentNullException(nameof(refreshSteps));
    }

    /// <summary>The bound context (for tests / introspection).</summary>
    public TContext Context => _ctx;

    /// <summary>
    /// Run <paramref name="action"/> then every refresh step. Logging is best-effort.
    /// </summary>
    public void Execute(TAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        try
        {
            action.Execute(_ctx);
            Logger.Log($"[Action] {action.DisplayName}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[Action] {action.DisplayName} threw: {ex}");
            throw;
        }

        foreach (Action<TContext> step in _refreshSteps)
        {
            try
            {
                step(_ctx);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Action] refresh step '{step.Method?.Name}' threw: {ex}");
            }
        }
    }

    /// <summary>
    /// Test seam: run the action's state change without triggering refresh.
    /// </summary>
    internal void ExecuteWithoutRefresh(TAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        action.Execute(_ctx);
    }

    /// <summary>
    /// Batch helper: run all actions, refresh only once at the end. Useful for
    /// programmatic repopulation (e.g. reloading a saved skirmish layout).
    /// </summary>
    public void ExecuteBatch(IEnumerable<TAction> actions)
    {
        if (actions == null) throw new ArgumentNullException(nameof(actions));

        foreach (TAction action in actions)
        {
            try
            {
                action.Execute(_ctx);
                Logger.Log($"[Action/Batch] {action.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[Action/Batch] {action.DisplayName} threw: {ex}");
                throw;
            }
        }

        foreach (Action<TContext> step in _refreshSteps)
        {
            try { step(_ctx); }
            catch (Exception ex) { Logger.Log($"[Action/Batch] refresh step '{step.Method?.Name}' threw: {ex}"); }
        }
    }
}
