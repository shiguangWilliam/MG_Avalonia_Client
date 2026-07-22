using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Runtime toolbar layout for CnCNet in-game lobby (host launch bar vs joiner ready cluster).</summary>
public static class CnCNetGameLobbyUiHelper
{
    private const int ReadyButtonWidth = 152;
    private const int AutoToggleWidth = 92;
    private const int ClusterSpacing = 8;
    private const int CheckboxVerticalOffset = 3;

    public static void ApplyToolbarRole(
        UiNodeViewModel root,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        bool isJoiner)
    {
        if (isJoiner)
            ApplyJoinerToolbar(root, resources, behaviors);
        else
            ApplyHostToolbar(root);
    }

    public static void ApplyJoinerToolbar(
        UiNodeViewModel root,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        UiNodeViewModel? manualReady = EnsureManualReadyButton(root, resources, behaviors);
        UiNodeViewModel? autoReady = FindVm(root, "chkAutoReady");
        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? lockGame = FindVm(root, "btnLockGame");
        UiNodeViewModel? autoSave = FindVm(root, "chkAutoSave");

        lockGame?.IsVisible = false;
        autoSave?.IsVisible = false;
        launch?.IsVisible = false;

        if (manualReady != null)
        {
            manualReady.IsVisible = true;
            manualReady.Node.TemplateKey = "DxReadyActionButton";
        }

        if (autoReady != null)
        {
            autoReady.IsVisible = true;
            autoReady.IsEnabled = true;
            autoReady.Node.TemplateKey = "DxLobbyToolbarCheckBox";
            autoReady.SetDisplayText("Auto Accept");
        }

        LayoutJoinerReadyCluster(root);
    }

    public static void ApplyHostToolbar(UiNodeViewModel root)
    {
        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        UiNodeViewModel? autoReady = FindVm(root, "chkAutoReady");
        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? lockGame = FindVm(root, "btnLockGame");
        UiNodeViewModel? autoSave = FindVm(root, "chkAutoSave");

        manualReady?.IsVisible = false;
        launch?.IsVisible = true;
        lockGame?.IsVisible = true;
        autoSave?.IsVisible = true;

        if (autoReady != null)
        {
            autoReady.IsVisible = false;
            autoReady.IsEnabled = false;
            autoReady.IsChecked = false;
        }
    }

    public static void UpdateManualReadyLabel(UiNodeViewModel root, bool isJoiner)
    {
        if (!isJoiner)
            return;

        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        if (manualReady == null)
            return;

        ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
        CnCNetGameRoomPlayer? local = cncnet.GameRoom?.Players
            .FirstOrDefault(p => p.Name.Equals(cncnet.LocalNick, StringComparison.OrdinalIgnoreCase));

        manualReady.SetDisplayText(local is { Ready: true } ? "Not Ready" : "I'm Ready");
        LayoutJoinerReadyCluster(root);
    }

    public static void SetJoinerReadyEnabled(UiNodeViewModel root, bool enabled)
    {
        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        if (manualReady is { IsVisible: true })
            manualReady.IsEnabled = enabled;
    }

    private static void LayoutJoinerReadyCluster(UiNodeViewModel root)
    {
        UiNodeViewModel? launch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? manualReady = FindVm(root, "btnManualReady");
        UiNodeViewModel? autoReady = FindVm(root, "chkAutoReady");
        if (launch == null || manualReady is not { IsVisible: true })
            return;

        int barY = (int)launch.CanvasTop;
        int barHeight = Math.Max((int)launch.Height, 34);
        int x = (int)launch.CanvasLeft;

        manualReady.SetCanvasPosition(x, barY);
        manualReady.Node.Props["Width"] = (double)ReadyButtonWidth;
        manualReady.Node.Props["Height"] = (double)barHeight;

        if (autoReady is { IsVisible: true })
        {
            int checkY = barY + CheckboxVerticalOffset;
            int checkHeight = Math.Max(barHeight - CheckboxVerticalOffset, 28);
            autoReady.SetCanvasPosition(x + ReadyButtonWidth + ClusterSpacing, checkY);
            autoReady.Node.Props["Width"] = (double)AutoToggleWidth;
            autoReady.Node.Props["Height"] = (double)checkHeight;
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
