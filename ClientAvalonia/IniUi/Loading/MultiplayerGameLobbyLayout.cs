using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Bottom action bar alignment for CnCNet / LAN game lobbies (MG theme overlap fixes).</summary>
internal static class MultiplayerGameLobbyLayout
{
    private const int ToolbarSpacing = 8;

    public static void Apply(UiNodeTree tree)
    {
        if (tree.FindNode("chkAutoReady") == null && tree.FindNode("btnLockGame") == null)
            return;

        UiNode? launch = tree.FindNode("btnLaunchGame");
        if (launch == null)
            return;

        EnsureManualReadyNode(tree, launch);

        int barY = launch.GetIntProp("CanvasTop");
        int barHeight = Math.Max(launch.GetIntProp("Height"), 34);
        int leftX = launch.GetIntProp("CanvasLeft");
        int parentWidth = tree.Root.GetIntProp("Width");
        if (parentWidth <= 0)
            parentWidth = 1230;

        int sideMargin = launch.GetIntProp("CanvasLeft");
        if (sideMargin <= 0)
            sideMargin = 22;

        foreach (string id in new[] { "btnManualReady", "btnLaunchGame", "btnLockGame", "chkAutoSave" })
        {
            UiNode? node = tree.FindNode(id);
            if (node == null || !IsVisible(node))
                continue;

            if (id is "chkAutoSave" or "chkAutoReady")
            {
                node.TemplateKey = "DxLobbyToolbarCheckBox";
                node.Props["Height"] = (double)barHeight;
            }

            int width = id is "btnManualReady" or "btnLaunchGame" or "btnLockGame"
                ? Math.Max(node.GetIntProp("Width"), 133)
                : EstimateCheckboxWidth(node);

            node.Props["CanvasTop"] = (double)barY;
            node.Props["CanvasLeft"] = (double)leftX;
            if (id is "chkAutoSave" or "chkAutoReady")
                node.Props["Width"] = (double)width;

            leftX += width + ToolbarSpacing;
        }

        string[] rightIds = ["btnGameLobbySettings", "btnChangeTunnel", "btnLeaveGame"];
        int cursor = parentWidth - sideMargin;
        for (int i = rightIds.Length - 1; i >= 0; i--)
        {
            UiNode? node = tree.FindNode(rightIds[i]);
            if (node == null || !IsVisible(node))
                continue;

            int width = Math.Max(node.GetIntProp("Width"), 133);
            cursor -= width;
            node.Props["CanvasLeft"] = (double)cursor;
            node.Props["CanvasTop"] = (double)barY;
            node.Props["Height"] = (double)barHeight;
            cursor -= ToolbarSpacing;
        }
    }

    private static void EnsureManualReadyNode(UiNodeTree tree, UiNode launch)
    {
        if (tree.FindNode("btnManualReady") != null)
            return;

        UiNode? parent = FindParentNode(tree.Root, launch.Id);
        if (parent == null)
            return;

        var node = new UiNode
        {
            Id = "btnManualReady",
            ControlType = launch.ControlType,
            TemplateKey = "DxReadyActionButton",
        };

        node.Props["CanvasLeft"] = launch.Props.TryGetValue("CanvasLeft", out object? left) ? left : 0d;
        node.Props["CanvasTop"] = launch.Props.TryGetValue("CanvasTop", out object? top) ? top : 0d;
        node.Props["Width"] = launch.Props.TryGetValue("Width", out object? width) ? width : 133d;
        node.Props["Height"] = launch.Props.TryGetValue("Height", out object? height) ? height : 34d;
        node.Props["IsVisible"] = false;
        node.Props["Text"] = "I'm Ready";

        parent.Children.Add(node);
    }

    private static UiNode? FindParentNode(UiNode root, string childId)
    {
        foreach (UiNode child in root.Children)
        {
            if (child.Id.Equals(childId, StringComparison.OrdinalIgnoreCase))
                return root;

            UiNode? found = FindParentNode(child, childId);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool IsVisible(UiNode node)
    {
        if (node.Props.TryGetValue("IsVisible", out object? v) && v is bool b)
            return b;
        return true;
    }

    private static int EstimateCheckboxWidth(UiNode node)
    {
        string text = ReadText(node);
        int textWidth = string.IsNullOrEmpty(text) ? 80 : text.Length * 7 + 44;
        return Math.Clamp(textWidth, 96, 168);
    }

    private static string ReadText(UiNode node)
    {
        if (node.Props.TryGetValue("Text", out object? v) && v != null)
            return v.ToString() ?? string.Empty;

        return node.RawAttributes.GetValueOrDefault("Text", string.Empty);
    }
}
