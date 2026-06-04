using System.Diagnostics;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Behaviors;

/// <summary>Registers INI-declared click behaviors ($LeftClickAction, URL) on top of code behaviors.</summary>
public static class IniBehaviorApplier
{
    public static void Apply(UiNodeViewModel root, BehaviorRegistry registry, IUiNavigationHost host)
    {
        ApplyNode(root, root, registry, host);
    }

    private static void ApplyNode(
        UiNodeViewModel root,
        UiNodeViewModel vm,
        BehaviorRegistry registry,
        IUiNavigationHost host)
    {
        ApplyIniClickAction(root, vm, registry, host);
        ApplyLinkUrl(vm, registry, host);

        foreach (UiNodeViewModel child in vm.Children)
            ApplyNode(root, child, registry, host);
    }

    private static void ApplyIniClickAction(
        UiNodeViewModel root,
        UiNodeViewModel vm,
        BehaviorRegistry registry,
        IUiNavigationHost host)
    {
        if (!TryGetIniAction(vm, out string? action))
            return;

        switch (action.Trim().ToUpperInvariant())
        {
            case "DISABLE":
                registry.RegisterAfter(vm.Id, _ => DisableRoot(root, host, vm.Id));
                break;
        }
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

    private static bool TryGetIniAction(UiNodeViewModel vm, out string action)
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
