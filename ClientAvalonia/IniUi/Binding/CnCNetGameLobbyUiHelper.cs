using ClientAvalonia.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Runtime controls for CnCNet in-game lobby (manual ready button).</summary>
public static class CnCNetGameLobbyUiHelper
{
    private const int ReadyButtonWidth = 152;
    private const int AutoToggleWidth = 92;
    private const int ClusterSpacing = 6;

    public static void ApplyJoinerToolbar(
        UiNodeViewModel root,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        bool isJoiner)
    {
        UiNodeViewModel? manualReady = EnsureManualReadyButton(root, resources, behaviors);
        UiNodeViewModel? autoReady = FindVm(root, "chkAutoReady");
        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");

        if (!isJoiner)
        {
            manualReady?.IsVisible = false;
            autoReady?.IsVisible = false;
            if (launch != null)
                launch.IsVisible = true;
            return;
        }

        if (manualReady != null)
        {
            manualReady.IsVisible = true;
            manualReady.IsEnabled = true;
            manualReady.Node.TemplateKey = "DxReadyActionButton";
        }

        if (launch != null)
            launch.IsVisible = false;

        if (autoReady != null)
        {
            autoReady.IsVisible = true;
            autoReady.Node.TemplateKey = "DxLobbyToolbarCheckBox";
            autoReady.SetDisplayText("Auto");
        }

        LayoutJoinerReadyCluster(root);
    }

    public static void UpdateManualReadyLabel(UiNodeViewModel root, bool isJoiner)
    {
        if (!isJoiner)
            return;

        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        if (manualReady == null)
            return;

        CnCNetGameRoomPlayer? local = CnCNetSessionService.Instance.GameRoom?.Players
            .FirstOrDefault(p => p.Name.Equals(CnCNetSessionService.Instance.LocalNick, StringComparison.OrdinalIgnoreCase));

        manualReady.SetDisplayText(local is { Ready: true } ? "Not Ready" : "I'm Ready");
        LayoutJoinerReadyCluster(root);
    }

    private static void LayoutJoinerReadyCluster(UiNodeViewModel root)
    {
        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        UiNodeViewModel? autoReady = FindVm(root, "chkAutoReady");
        if (launch == null || manualReady is not { IsVisible: true })
            return;

        int y = (int)launch.CanvasTop;
        int h = Math.Max((int)launch.Height, 34);
        int x = (int)launch.CanvasLeft;

        manualReady.SetCanvasPosition(x, y);
        manualReady.Node.Props["Width"] = (double)ReadyButtonWidth;
        manualReady.Node.Props["Height"] = (double)h;

        if (autoReady is { IsVisible: true })
        {
            autoReady.SetCanvasPosition(x + ReadyButtonWidth + ClusterSpacing, y);
            autoReady.Node.Props["Width"] = (double)AutoToggleWidth;
            autoReady.Node.Props["Height"] = (double)h;
        }
    }

    private static UiNodeViewModel? EnsureManualReadyButton(
        UiNodeViewModel root,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        UiNodeViewModel? existing = FindVm(root, "btnManualReady");
        if (existing != null)
            return existing;

        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");
        if (launch == null)
            return null;

        UiNodeViewModel? parent = FindParentOf(root, launch.Id);
        if (parent == null)
            return null;

        var node = new UiNode
        {
            Id = "btnManualReady",
            ControlType = launch.ControlType,
            TemplateKey = "DxReadyActionButton",
        };
        node.Props["CanvasLeft"] = launch.CanvasLeft;
        node.Props["CanvasTop"] = launch.CanvasTop;
        node.Props["Width"] = (double)ReadyButtonWidth;
        node.Props["Height"] = launch.Height;
        node.Props["IsVisible"] = false;
        node.Props["Text"] = "I'm Ready";

        var vm = new UiNodeViewModel(node, resources, behaviors);
        parent.Children.Add(vm);
        return vm;
    }

    private static UiNodeViewModel? FindParentOf(UiNodeViewModel root, string childId)
    {
        foreach (UiNodeViewModel child in root.Children)
        {
            if (child.Id.Equals(childId, StringComparison.OrdinalIgnoreCase))
                return root;

            UiNodeViewModel? found = FindParentOf(child, childId);
            if (found != null)
                return found;
        }

        return null;
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
