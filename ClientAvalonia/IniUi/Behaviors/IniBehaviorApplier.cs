using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>
/// Registers INI-declared click behaviors ($LeftClickAction, URL) on top of code behaviors.
/// </summary>
/// <remarks>
/// INI 动作分两路处理：
///   1. <c>DISABLE</c>：特殊内置语义——禁用整个 UI 容器。仍在这里处理。
///   2. 已注册到 <see cref="IIniActionCatalog"/> 的动作名：派发给 catalog。
///   3. 未注册的：忽略（与 DX 启动器一致，避免 crash）。
/// </remarks>
public static class IniBehaviorApplier
{
    /// <summary>
    /// 旧入口：不接 catalog。仅处理 DISABLE 特殊动作。
    /// 调用方若希望支持完整 INI 动作派发，应改用 <see cref="Apply(UiNodeViewModel, BehaviorRegistry, IUiNavigationHost, IIniActionCatalog?)"/>。
    /// </summary>
    public static void Apply(UiNodeViewModel root, BehaviorRegistry registry, IUiNavigationHost host)
        => Apply(root, registry, host, catalog: null);

    /// <summary>
    /// 新入口：可注入 <paramref name="catalog"/> 以支持任意已注册的 INI 动作名。
    /// catalog 为 null 时退化为旧语义（仅 DISABLE）。
    /// </summary>
    public static void Apply(
        UiNodeViewModel root,
        BehaviorRegistry registry,
        IUiNavigationHost host,
        IIniActionCatalog? catalog)
    {
        ApplyNode(root, root, registry, host, catalog);
    }

    private static void ApplyNode(
        UiNodeViewModel root,
        UiNodeViewModel vm,
        BehaviorRegistry registry,
        IUiNavigationHost host,
        IIniActionCatalog? catalog)
    {
        ApplyIniClickAction(root, vm, registry, host, catalog);
        ApplyLinkUrl(vm, registry, host);

        foreach (UiNodeViewModel child in vm.Children)
            ApplyNode(root, child, registry, host, catalog);
    }

    private static void ApplyIniClickAction(
        UiNodeViewModel root,
        UiNodeViewModel vm,
        BehaviorRegistry registry,
        IUiNavigationHost host,
        IIniActionCatalog? catalog)
    {
        if (!TryGetIniAction(vm, out string? action))
            return;

        // DISABLE 优先（特殊内置语义，与 catalog 无关）。
        if (IniActionName.IsDisable(action))
        {
            registry.RegisterAfter(vm.Id, _ => DisableRoot(root, host, vm.Id));
            return;
        }

        // catalog 未配置 → 退化为旧行为（什么都不做）。
        if (catalog == null)
            return;

        // 动作名不在 catalog 注册表里 → 不绑回调（与 DX 行为一致，避免 crash）。
        // 也要防"已注册 ID-matching 的控件"双重触发：若 BehaviorRegistry 已注册过该 ID，
        // 我们就让 INI catalog 派发；ID 匹配的回调先跑（这是现有行为），catalog 在之后跑。
        // 但为了避免重复，我们只在 catalog 真的命中时才覆盖默认回调。
        if (!catalog.IsRegistered(IniActionName.ParseName(action)))
            return;

        registry.Register(vm.Id, _ => catalog.TryDispatch(action, host));
    }

    private static void ApplyLinkUrl(UiNodeViewModel vm, BehaviorRegistry registry, IUiNavigationHost host)
    {
        string? url = ResolveLinkUrl(vm);
        if (string.IsNullOrWhiteSpace(url))
            return;

        registry.Register(vm.Id, _ =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                host.ShowStatus($"Opened: {url}");
            }
            catch (Exception ex)
            {
                host.ShowStatus($"Failed to open URL: {ex.Message}");
            }
        });
    }

    private static bool TryGetIniAction(UiNodeViewModel vm, [NotNullWhen(true)] out string action)
    {
        if (vm.Extensions.TryGetValue("$LeftClickAction", out action!)
            || vm.Extensions.TryGetValue("LeftClickAction", out action!))
            return !string.IsNullOrWhiteSpace(action);

        action = string.Empty;
        return false;
    }

    private static string? ResolveLinkUrl(UiNodeViewModel vm)
    {
        if (!vm.ControlType.Contains("Link", StringComparison.OrdinalIgnoreCase))
            return null;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (vm.Extensions.TryGetValue("UnixURL", out string? unixUrl) && !string.IsNullOrWhiteSpace(unixUrl))
                return unixUrl;
        }

        if (vm.Extensions.TryGetValue("URL", out string? url) && !string.IsNullOrWhiteSpace(url))
            return url;

        return null;
    }

    private static void DisableRoot(UiNodeViewModel root, IUiNavigationHost host, string controlId)
    {
        SetEnabledRecursive(root, false);
        host.ShowStatus($"{controlId}: window disabled");
    }

    private static void SetEnabledRecursive(UiNodeViewModel vm, bool enabled)
    {
        vm.IsEnabled = enabled;
        foreach (UiNodeViewModel child in vm.Children)
            SetEnabledRecursive(child, enabled);
    }
}
