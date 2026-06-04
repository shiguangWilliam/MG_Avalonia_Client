using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Deterministic vertical / two-column layout for OptionsWindow tab panels (ignores broken INI coords).</summary>
internal static class OptionsPanelStackLayout
{
    private const int MarginLeft = 16;
    private const int MarginTop = 12;
    private const int RowGap = 10;
    private const int DropdownLeft = 176;
    private const int DropdownWidth = 228;
    private const int RightColumnLeft = 288;

    private static readonly string[] DisplayOrder =
    [
        "chkWindowedMode",
        "lblIngameResolution", "ddIngameResolution",
        "lblDetailLevel", "ddDetailLevel",
        "lblRenderer", "ddRenderer",
        "chkBorderlessWindowedMode",
        "chkBackBufferInVRAM",
        "lblClientResolution", "ddClientResolution",
        "chkBorderlessClient",
        "lblClientTheme", "ddClientTheme",
        "chkMEDDraw",
        "chkStretchMovies",
        "lblReShade", "ddReShade",
    ];

    private static readonly string[] CnCNetLeftColumn =
    [
        "chkPingUnofficialTunnels",
        "chkWriteInstallPathToRegistry",
        "chkPlaySoundOnGameHosted",
        "chkNotifyOnUserListChange",
        "chkDisablePrivateMessagePopup",
        "lblAllowPrivateMessagesFrom",
        "ddAllowPrivateMessagesFrom",
    ];

    private static readonly string[] CnCNetRightColumn =
    [
        "chkSkipLoginWindow",
        "chkPersistentMode",
        "chkConnectOnStartup",
        "chkDiscordIntegration",
        "chkAllowGameInvitesFromFriendsOnly",
        "chkSteamIntegration",
    ];

    private static readonly string[] GameOrder =
    [
        "lblPlayerName", "tbPlayerName", "lblPlayerNameNotice",
        "chkreshape",
        "chkvxllightec",
        "chkTooltipsExtra",
        "chkPrioritySelection",
        "chkBuildingPlacement",
    ];

    public static void Apply(UiNodeTree tree)
    {
        StackFormPanel(tree, tree.FindNode("DisplayOptionsPanel"), DisplayOrder);
        StackTwoColumnPanel(tree.FindNode("CnCNetOptionsPanel"), CnCNetLeftColumn, CnCNetRightColumn);
        StackFormPanel(tree, tree.FindNode("GameOptionsPanel"), GameOrder);
        StackAutoPanel(tree.FindNode("AudioOptionsPanel"));
        StackAutoPanel(tree.FindNode("UpdaterOptionsPanel"));
        StackAutoPanel(tree.FindNode("ComponentsPanel"));
    }

    private static void StackFormPanel(UiNodeTree tree, UiNode? panel, IReadOnlyList<string> order)
    {
        if (panel == null)
            return;

        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int y = MarginTop;
        int maxBottom = MarginTop;

        foreach (string id in order)
        {
            UiNode? node = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null || !IsVisible(node))
                continue;

            if (node.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase))
            {
                UiNode? dropdown = ResolveDropdown(tree, panel, node);
                y = PlaceLabelDropdownRow(node, dropdown, y);
                handled.Add(node.Id);
                if (dropdown != null)
                    handled.Add(dropdown.Id);
            }
            else
            {
                y = PlaceBlock(node, MarginLeft, y);
                handled.Add(node.Id);
            }

            maxBottom = Math.Max(maxBottom, y);
        }

        foreach (UiNode extra in panel.Children.Where(c => !handled.Contains(c.Id) && IsVisible(c)).OrderBy(c => c.Id))
        {
            y = PlaceBlock(extra, MarginLeft, y);
            maxBottom = Math.Max(maxBottom, y);
        }

        panel.Props["ScrollContentHeight"] = (double)(maxBottom + 8);
    }

    private static void StackTwoColumnPanel(UiNode? panel, IReadOnlyList<string> leftIds, IReadOnlyList<string> rightIds)
    {
        if (panel == null)
            return;

        int leftY = MarginTop;
        foreach (string id in leftIds)
        {
            if (id.Equals("ddAllowPrivateMessagesFrom", StringComparison.OrdinalIgnoreCase))
                continue;

            UiNode? node = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null || !IsVisible(node))
                continue;

            if (id.Equals("lblAllowPrivateMessagesFrom", StringComparison.OrdinalIgnoreCase))
            {
                UiNode? dropdown = panel.Children.FirstOrDefault(c =>
                    c.Id.Equals("ddAllowPrivateMessagesFrom", StringComparison.OrdinalIgnoreCase));
                leftY = PlaceLabelDropdownRow(node, dropdown, leftY);
                continue;
            }

            leftY = PlaceBlock(node, MarginLeft, leftY);
        }

        int rightY = MarginTop;
        foreach (string id in rightIds)
        {
            UiNode? node = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null || !IsVisible(node))
                continue;

            rightY = PlaceBlock(node, RightColumnLeft, rightY);
        }

        panel.Props["ScrollContentHeight"] = (double)(Math.Max(leftY, rightY) + 8);
    }

    private static void StackAutoPanel(UiNode? panel)
    {
        if (panel == null)
            return;

        int y = MarginTop;
        foreach (UiNode child in panel.Children.Where(IsVisible).OrderBy(c => c.GetIntProp("CanvasTop")).ThenBy(c => c.Id))
            y = PlaceBlock(child, MarginLeft, y);

        panel.Props["ScrollContentHeight"] = (double)(y + 8);
    }

    private static int PlaceLabelDropdownRow(UiNode label, UiNode? dropdown, int y)
    {
        int labelHeight = Math.Max(label.GetIntProp("Height"), 20);
        label.Props["CanvasLeft"] = (double)MarginLeft;
        label.Props["CanvasTop"] = (double)(y + 3);

        if (dropdown != null && IsVisible(dropdown))
        {
            int ddHeight = Math.Max(dropdown.GetIntProp("Height"), 24);
            dropdown.Props["CanvasLeft"] = (double)DropdownLeft;
            dropdown.Props["CanvasTop"] = (double)y;
            if (dropdown.GetIntProp("Width") < DropdownWidth / 2)
                dropdown.Props["Width"] = (double)DropdownWidth;

            return y + Math.Max(labelHeight, ddHeight) + RowGap;
        }

        return y + labelHeight + RowGap;
    }

    private static UiNode? ResolveDropdown(UiNodeTree tree, UiNode panel, UiNode label)
    {
        if (!label.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase) || label.Id.Length <= 3)
            return null;

        string ddId = "dd" + label.Id[3..];
        UiNode? dropdown = panel.Children.FirstOrDefault(c => c.Id.Equals(ddId, StringComparison.OrdinalIgnoreCase))
            ?? tree.FindNode(ddId);

        if (dropdown == null)
            return null;

        if (dropdown.Parent != panel)
            AttachToPanel(panel, dropdown);

        return dropdown;
    }

    private static void AttachToPanel(UiNode panel, UiNode node)
    {
        if (node.Parent != null)
            node.Parent.Children.Remove(node);

        node.Parent = panel;
        if (!panel.Children.Contains(node))
            panel.Children.Add(node);
    }

    private static int PlaceBlock(UiNode node, int x, int y)
    {
        if (node.Id.StartsWith("dd", StringComparison.OrdinalIgnoreCase))
            x = DropdownLeft;

        int height = Math.Max(node.GetIntProp("Height"), node.Id.StartsWith("chk", StringComparison.OrdinalIgnoreCase) ? 24 : 20);
        node.Props["CanvasLeft"] = (double)x;
        node.Props["CanvasTop"] = (double)y;

        if (node.Id.StartsWith("dd", StringComparison.OrdinalIgnoreCase) && node.GetIntProp("Width") < DropdownWidth / 2)
            node.Props["Width"] = (double)DropdownWidth;

        int maxWidth = 552 - x - 16;
        if (node.GetIntProp("Width") > maxWidth)
            node.Props["Width"] = (double)maxWidth;

        return y + height + RowGap;
    }

    private static bool IsVisible(UiNode node)
        => !node.Props.TryGetValue("IsVisible", out object? v) || v is not bool b || b;
}
