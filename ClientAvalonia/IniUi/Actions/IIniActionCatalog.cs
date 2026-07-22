using System;
using System.Collections.Generic;
using ClientAvalonia.IniUi.Behaviors;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// INI 动作名 → 执行委托的注册表。
///
/// 把 INI 里 <c>$LeftClickAction=LaunchSkirmish</c> 这类声明接到具体回调上。
/// Mod 可通过 INI 把任意按钮绑到已注册的动作名，无需改 C# 代码。
///
/// 设计原则（与设计文档 §2.1 一致）：
///   - 字符串名大小写不敏感（与 DX INI 习惯一致）
///   - 后注册的覆盖先注册的（与 BehaviorRegistry 一致）
///   - 未注册的名静默忽略（让调用方 fallback 到 ID 匹配或 DISABLE 处理）
///   - 这不是新引擎，只是「字符串 → 委托」的查找表；不接管布局、Session 同步、
///     IRC 等。复杂 Action 仍应继承 <see cref="UiAction{TContext}"/>，
///     通过工厂包成委托注册到 catalog。
/// </summary>
public interface IIniActionCatalog
{
    /// <summary>
    /// 注册动作名 → 执行委托。
    /// </summary>
    /// <param name="actionName">INI 里写的动作名（大小写不敏感）。</param>
    /// <param name="handler">
    /// 给定参数（冒号后部分，如 <c>NavigateTo:SkirmishLobby</c> → <c>SkirmishLobby</c>）
    /// 与触发时的 host，执行副作用。委托内可自行 throw，会被调用方记录但不传播。
    /// </param>
    void Register(string actionName, Action<string, IUiNavigationHost> handler);

    /// <summary>
    /// 尝试按动作名执行。返回是否命中已注册项。
    /// </summary>
    /// <param name="actionRawValue">
    /// INI 里 <c>$LeftClickAction</c> 的完整原始值（含冒号后参数），
    /// 例如 <c>"NavigateTo:SkirmishLobby"</c> 或 <c>"ExitApplication"</c>。
    /// </param>
    /// <param name="host">当前 UI 导航主机（提供 NavigateTo / ExitApplication 等）。</param>
    bool TryDispatch(string actionRawValue, IUiNavigationHost host);

    /// <summary>查询动作名是否已注册（用于测试 / 诊断）。</summary>
    bool IsRegistered(string actionName);

    /// <summary>已注册动作名（按注册顺序）。仅供诊断 / 测试。</summary>
    IReadOnlyCollection<string> RegisteredNames { get; }
}
