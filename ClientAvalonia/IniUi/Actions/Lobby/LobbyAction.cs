using ClientAvalonia.Domain.Resources;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Actions.Lobby;

/// <summary>
/// Lobby-specific dependency bundle. Session state is accessed only via
/// <see cref="Game"/> (<see cref="ISkirmishSession"/>).
/// </summary>
public sealed class LobbyActionContext : UiActionContext
{
    /// <summary>Current skirmish / lobby game session (map, slots, options).</summary>
    public required ISkirmishSession Game { get; init; }

    /// <summary>Lobby UI selection state (filter index, visible maps, search text).</summary>
    public required LobbySessionState Session { get; init; }

    /// <summary>Game resource catalog (maps, modes, missions).</summary>
    public required IResourceCatalog Resources { get; init; }

    /// <summary>Texture / file resolver for the current theme.</summary>
    public required ResourceResolver ResourceResolver { get; init; }

    /// <summary>
    /// 当前 lobby 窗口名（如 "SkirmishLobby" / "CnCNetGameLobby"）。
    /// 用于 <see cref="ChangeMapAction"/> 等需要按窗口类型走不同分支的动作。
    /// </summary>
    public string? WindowName { get; init; }

    /// <summary>Lobby root UI node tree（动作执行时操作 UI 节点用）。</summary>
    public Rendering.UiNodeViewModel? Root { get; init; }

    /// <summary>Behavior registry（动作执行时绑定 UI 行为用）。</summary>
    public IniUi.Behaviors.BehaviorRegistry? Behaviors { get; init; }
}

/// <summary>
/// Lobby-domain <see cref="UiAction{TContext}"/>.
/// </summary>
public abstract class LobbyAction : UiAction<LobbyActionContext>
{
}
