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

    private static readonly string[] AudioOrder =
    [
        "lblScoreVolume", "trbScoreVolume", "lblScoreVolumeValue",
        "lblSoundVolume", "trbSoundVolume", "lblSoundVolumeValue",
        "lblVoiceVolume", "trbVoiceVolume", "lblVoiceVolumeValue",
        "chkScoreShuffle",
        "lblClientVolume", "trbClientVolume", "lblClientVolumeValue",
        "chkMainMenuMusic",
        "chkStopMusicOnMenu",
        "chkStopGameLobbyMessageAudio",
    ];

    public static void Apply(UiNodeTree tree)
    {
        StackFormPanel(tree, tree.FindNode("DisplayOptionsPanel"), DisplayOrder);
        StackTwoColumnPanel(tree.FindNode("CnCNetOptionsPanel"), CnCNetLeftColumn, CnCNetRightColumn);
        StackFormPanel(tree, tree.FindNode("GameOptionsPanel"), GameOrder);
        StackAudioPanel(tree.FindNode("AudioOptionsPanel"), AudioOrder);
        StackUpdaterPanel(tree.FindNode("UpdaterOptionsPanel"));
        StackComponentsPanel(tree.FindNode("ComponentsPanel"));
    }

    private static void StackUpdaterPanel(UiNode? panel)
    {
        if (panel == null)
            return;

        int y = MarginTop;
        UiNode? description = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("lblUpdaterDescription", StringComparison.OrdinalIgnoreCase));
        if (description != null)
        {
            // Two-line tip must reserve enough vertical space or the list paints over it.
            description.Props["CanvasLeft"] = (double)MarginLeft;
            description.Props["CanvasTop"] = (double)y;
            description.Props["Width"] = 520.0;
            description.Props["Height"] = 56.0;
            y += 56 + 16;
        }

        UiNode? list = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("lbUpdateServerList", StringComparison.OrdinalIgnoreCase));
        if (list != null)
        {
            list.Props["CanvasLeft"] = (double)MarginLeft;
            list.Props["CanvasTop"] = (double)y;
            list.Props["Width"] = 520.0;
            list.Props["Height"] = 120.0;
            y += 120 + RowGap;
        }

        UiNode? moveUp = panel.Children.FirstOrDefault(c => c.Id.Equals("btnMoveUp", StringComparison.OrdinalIgnoreCase));
        UiNode? moveDown = panel.Children.FirstOrDefault(c => c.Id.Equals("btnMoveDown", StringComparison.OrdinalIgnoreCase));
        if (moveUp != null)
        {
            moveUp.Props["CanvasLeft"] = (double)MarginLeft;
            moveUp.Props["CanvasTop"] = (double)y;
            moveUp.Props["Width"] = 133.0;
        }

        if (moveDown != null)
        {
            moveDown.Props["CanvasLeft"] = (double)(MarginLeft + 520 - 133);
            moveDown.Props["CanvasTop"] = (double)y;
            moveDown.Props["Width"] = 133.0;
        }

        if (moveUp != null || moveDown != null)
            y += 23 + 24;

        UiNode? autoCheck = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("chkAutoCheck", StringComparison.OrdinalIgnoreCase));
        if (autoCheck != null)
            y = PlaceBlock(autoCheck, MarginLeft, y);

        UiNode? force = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("btnForceUpdate", StringComparison.OrdinalIgnoreCase));
        if (force != null)
        {
            force.Props["CanvasLeft"] = (double)(MarginLeft + 520 - 133);
            force.Props["CanvasTop"] = (double)y;
            force.Props["Width"] = 133.0;
            y += 23 + RowGap;
        }

        panel.Props["ScrollContentHeight"] = (double)(y + 8);
    }

    private static void StackComponentsPanel(UiNode? panel)
    {
        if (panel == null)
            return;

        int y = MarginTop;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UiNode? empty = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("lblNoComponents", StringComparison.OrdinalIgnoreCase));
        if (empty != null)
        {
            y = PlaceBlock(empty, MarginLeft, y);
            panel.Props["ScrollContentHeight"] = (double)(y + 8);
            return;
        }

        foreach (UiNode label in panel.Children
                     .Where(c => c.Id.StartsWith("lblComponent_", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => c.Id))
        {
            string suffix = label.Id["lblComponent_".Length..];
            UiNode? button = panel.Children.FirstOrDefault(c =>
                c.Id.Equals("btnComponent_" + suffix, StringComparison.OrdinalIgnoreCase));

            label.Props["CanvasLeft"] = (double)MarginLeft;
            label.Props["CanvasTop"] = (double)(y + 2);
            label.Props["Width"] = 360.0;
            handled.Add(label.Id);

            if (button != null)
            {
                button.Props["CanvasLeft"] = (double)(MarginLeft + 520 - 133);
                button.Props["CanvasTop"] = (double)y;
                button.Props["Width"] = 133.0;
                handled.Add(button.Id);
            }

            y += 35;
        }

        foreach (UiNode extra in panel.Children.Where(c => !handled.Contains(c.Id) && IsVisible(c)))
            y = PlaceBlock(extra, MarginLeft, y);

        panel.Props["ScrollContentHeight"] = (double)(y + 8);
    }

    private static void StackAudioPanel(UiNode? panel, IReadOnlyList<string> _)
    {
        if (panel == null)
            return;

        const int labelWidth = 140;
        const int valueWidth = 28;
        const int trackLeft = MarginLeft + labelWidth + 8;
        const int trackWidth = 280;
        const int valueLeft = trackLeft + trackWidth + 8;
        int y = MarginTop;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void PlaceVolumeRow(string labelId, string trackId, string valueId)
        {
            UiNode? label = panel.Children.FirstOrDefault(c => c.Id.Equals(labelId, StringComparison.OrdinalIgnoreCase));
            UiNode? track = panel.Children.FirstOrDefault(c => c.Id.Equals(trackId, StringComparison.OrdinalIgnoreCase));
            UiNode? value = panel.Children.FirstOrDefault(c => c.Id.Equals(valueId, StringComparison.OrdinalIgnoreCase));
            if (label != null)
            {
                label.Props["CanvasLeft"] = (double)MarginLeft;
                label.Props["CanvasTop"] = (double)y;
                label.Props["Width"] = (double)labelWidth;
                handled.Add(label.Id);
            }

            if (track != null)
            {
                track.Props["CanvasLeft"] = (double)trackLeft;
                track.Props["CanvasTop"] = (double)y;
                track.Props["Width"] = (double)trackWidth;
                handled.Add(track.Id);
            }

            if (value != null)
            {
                value.Props["CanvasLeft"] = (double)valueLeft;
                value.Props["CanvasTop"] = (double)y;
                value.Props["Width"] = (double)valueWidth;
                handled.Add(value.Id);
            }

            y += 34;
        }

        PlaceVolumeRow("lblScoreVolume", "trbScoreVolume", "lblScoreVolumeValue");
        PlaceVolumeRow("lblSoundVolume", "trbSoundVolume", "lblSoundVolumeValue");
        PlaceVolumeRow("lblVoiceVolume", "trbVoiceVolume", "lblVoiceVolumeValue");

        foreach (string id in new[] { "chkScoreShuffle" })
        {
            UiNode? node = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null)
                continue;
            node.Props["CanvasLeft"] = (double)MarginLeft;
            node.Props["CanvasTop"] = (double)y;
            handled.Add(node.Id);
            y += 28;
        }

        y += 8;
        PlaceVolumeRow("lblClientVolume", "trbClientVolume", "lblClientVolumeValue");

        foreach (string id in new[] { "chkMainMenuMusic", "chkStopMusicOnMenu", "chkStopGameLobbyMessageAudio" })
        {
            UiNode? node = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null)
                continue;
            node.Props["CanvasLeft"] = (double)MarginLeft;
            node.Props["CanvasTop"] = (double)y;
            handled.Add(node.Id);
            y += 28;
        }

        foreach (UiNode child in panel.Children)
        {
            if (handled.Contains(child.Id) || !IsVisible(child))
                continue;
            child.Props["CanvasLeft"] = (double)MarginLeft;
            child.Props["CanvasTop"] = (double)y;
            y += Math.Max(24, (int)child.GetNumericProp("Height", 24)) + RowGap;
        }

        panel.Props["ScrollContentHeight"] = (double)Math.Max(y + 16, panel.GetNumericProp("Height", 400));
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
                leftY = PlacePrivateMessagesRow(node, dropdown, leftY);
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

    /// <summary>
    /// Keep PM label + dropdown entirely in the left column (stacked), never between columns.
    /// Same-row layout at x≈176 overlaps the right column ("Current channel" bug).
    /// </summary>
    private static int PlacePrivateMessagesRow(UiNode label, UiNode? dropdown, int y)
    {
        const int leftColumnWidth = RightColumnLeft - MarginLeft - 16; // ~256px, clear of right column

        label.Props["CanvasLeft"] = (double)MarginLeft;
        label.Props["CanvasTop"] = (double)y;
        label.Props["Width"] = (double)leftColumnWidth;
        label.Props["Height"] = 22.0;
        y += 22 + 4;

        if (dropdown != null && IsVisible(dropdown))
        {
            dropdown.Props["CanvasLeft"] = (double)MarginLeft;
            dropdown.Props["CanvasTop"] = (double)y;
            dropdown.Props["Width"] = (double)Math.Min(240, leftColumnWidth);
            dropdown.Props["Height"] = 24.0;
            y += 24 + RowGap;
        }

        return y;
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
