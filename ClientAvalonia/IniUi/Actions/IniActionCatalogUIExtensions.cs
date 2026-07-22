using System;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;
using ClientAvalonia.Services;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// <see cref="IIniActionCatalog"/> 的扩展——把现有字符串→委托注册接到 IUIAction 双通道模型上。
/// </summary>
/// <remarks>
/// 设计理由（见 layered-architecture.md §2.4）：
/// <list type="bullet">
/// <item>现有 <see cref="IIniActionCatalog.Register"/> 签名是 <c>Action&lt;string, IUiNavigationHost&gt;</c>，与 IUIAction 模型签名不同。</item>
/// <item>用扩展方法添加 <c>RegisterState</c> / <c>RegisterCommand</c> 而不修改原接口——向后兼容。</item>
/// <item>Service 解析延迟到派发时（懒解析），让"未注册"不阻塞 catalog 注册。</item>
/// </list>
/// </remarks>
public static class IniActionCatalogUIExtensions
{
    /// <summary>
    /// 注册一个 StateAction（写 <see cref="IGameSession.SlotSink"/>，不调 Service）。
    /// </summary>
    /// <param name="catalog">目标 catalog。</param>
    /// <param name="name">INI 里写的动作名（大小写不敏感）。</param>
    /// <param name="handler">
    /// 执行委托：(args, session, source) → CmdResult。
    /// session 不会为 null（派发时强制解析；解析失败时 catalog 静默记录）。
    /// </param>
    public static void RegisterState(
        this IIniActionCatalog catalog,
        string name,
        Func<string, IGameSession, UiNodeViewModel?, CmdResult> handler)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        catalog.Register(name, (args, host) =>
        {
            IGameSession? session = ResolveSession(host);
            if (session == null)
            {
                Logger.Log($"[IUIAction] State action '{name}' dispatched but IGameSession not registered.");
                return;
            }

            try
            {
                CmdResult result = handler(args, session, null);
                if (!result.Success)
                    Logger.Log($"[IUIAction] State action '{name}' failed: {result.Message}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[IUIAction] State action '{name}' threw: {ex}");
            }
        });
    }

    /// <summary>
    /// 注册一个 CmdAction（调 Service，可能读写 Session）。
    /// </summary>
    /// <param name="catalog">目标 catalog。</param>
    /// <param name="name">INI 里写的动作名（大小写不敏感）。</param>
    /// <param name="handler">
    /// 执行委托：(args, services, session, source) → CmdResult。
    /// services 不会为 null。session 可能为 null（某些命令如 Exit 不需要 session）。
    /// </param>
    public static void RegisterCommand(
        this IIniActionCatalog catalog,
        string name,
        Func<string, IServiceHub, IGameSession?, UiNodeViewModel?, CmdResult> handler)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        catalog.Register(name, (args, host) =>
        {
            IServiceHub services = DefaultServiceHub.Instance;
            IGameSession? session = ResolveSession(host);

            try
            {
                CmdResult result = handler(args, services, session, null);
                if (!result.Success)
                    Logger.Log($"[IUIAction] Cmd action '{name}' failed: {result.Message}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[IUIAction] Cmd action '{name}' threw: {ex}");
            }
        });
    }

    /// <summary>
    /// 从 host 解析 <see cref="IGameSession"/>。
    /// </summary>
    /// <remarks>
    /// 优先尝试 host.GetService（若 <see cref="IUiNavigationHost"/> 暴露）；否则回退到 EnvironmentServices。
    /// 当前 IUiNavigationHost 没有 GetService，直接用 EnvironmentServices。
    /// </remarks>
    private static IGameSession? ResolveSession(IUiNavigationHost host)
    {
        try
        {
            return DefaultServiceHub.Instance.TryGet<IGameSession>(out IGameSession? session)
                ? session
                : null;
        }
        catch
        {
            return null;
        }
    }
}
