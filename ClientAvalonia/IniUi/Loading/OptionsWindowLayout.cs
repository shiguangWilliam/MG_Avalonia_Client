using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Loading;

internal static class OptionsWindowLayout
{
    private const int DialogWidth = OptionsOverlayConstants.Width;
    private const int DialogHeight = OptionsOverlayConstants.Height;
    private const int PanelLeft = 12;
    private const int PanelTop = 47;
    private const int PanelWidth = 552;
    private const int PanelHeight = 334;

    private static readonly string[] TabPanelIds =
    [
        "DisplayOptionsPanel",
        "AudioOptionsPanel",
        "GameOptionsPanel",
        "CnCNetOptionsPanel",
        "SecurityOptionsPanel",
        "UpdaterOptionsPanel",
        "ComponentsPanel",
    ];

    private static readonly string[] TabTitles =
    [
        "显示", "音频", "游戏", "CnCNet", "安全", "更新", "组件",
    ];

    private static readonly Dictionary<string, string> ControlToPanel = BuildControlToPanelMap();

    public static void Apply(UiNodeTree tree, LayoutContext context)
    {
        UiNode root = tree.Root;
        root.Props["Width"] = (double)DialogWidth;
        root.Props["Height"] = (double)DialogHeight;
        root.Props.Remove("CanvasLeft");
        root.Props.Remove("CanvasTop");

        EnsureTabPanels(tree, root);
        EnsureTabButtons(root);
        ReparentControlsToPanels(tree, root);
    }

    /// <summary>Runs after expression layout so MG/DTA INI cannot push panels off-screen.</summary>
    public static void FinalizeLayout(UiNodeTree tree)
    {
        UiNode root = tree.Root;
        root.Props["Width"] = (double)DialogWidth;
        root.Props["Height"] = (double)DialogHeight;

        UiNode? tabControl = tree.FindNode("tabControl");
        if (tabControl != null)
            tabControl.Props["IsVisible"] = false;

        PositionTabPanels(tree);
        PositionTabButtons(root);
        PositionWindowChrome(tree);
        EnsureFooterButtons(root);
        PositionFooterButtons(tree);
        ReparentControlsToPanels(tree, root);
        OptionsDisplayControlsBootstrap.Apply(tree);
        RendererOptionsBootstrap.Apply(tree);
        OptionsGameControlsBootstrap.Apply(tree);
        OptionsCnCNetControlsBootstrap.Apply(tree);
        OptionsSecurityControlsBootstrap.Apply(tree);
        OptionsAudioControlsBootstrap.Apply(tree);
        OptionsUpdaterControlsBootstrap.Apply(tree);
        OptionsComponentsControlsBootstrap.Apply(tree);
        OptionsPanelStackLayout.Apply(tree);
        OptionsFooterChrome.ApplyToTree(tree);
        SetActiveTab(root, 0);
    }

    private static void EnsureTabButtons(UiNode root)
    {
        string[] ids =
        [
            "btnTabDisplay", "btnTabAudio", "btnTabGame", "btnTabCnCNet",
            "btnTabSecurity", "btnTabUpdater", "btnTabComponents",
        ];
        int x = 12;
        const double tabWidth = 74.0;
        for (int i = 0; i < ids.Length; i++)
        {
            if (root.Children.Any(c => c.Id.Equals(ids[i], StringComparison.OrdinalIgnoreCase)))
                continue;

            var btn = CreatePanelNode(ids[i], "XNAClientButton", "DxTabButton");
            btn.Props["CanvasLeft"] = (double)x;
            btn.Props["CanvasTop"] = 12.0;
            btn.Props["Width"] = tabWidth;
            btn.Props["Height"] = 26.0;
            btn.Props["Text"] = TabTitles[i];
            btn.Props["TabIndex"] = i;
            btn.Props["IsTabSelected"] = i == 0;
            btn.Parent = root;
            root.Children.Add(btn);
            x += (int)tabWidth + 2;
        }

        foreach (UiNode child in root.Children)
        {
            if (!child.Id.StartsWith("btnTab", StringComparison.OrdinalIgnoreCase))
                continue;

            child.TemplateKey = "DxTabButton";
            if (!child.Props.ContainsKey("Width"))
                child.Props["Width"] = tabWidth;
            if (!child.Props.ContainsKey("Height"))
                child.Props["Height"] = 26.0;
        }
    }

    private static void PositionTabPanels(UiNodeTree tree)
    {
        for (int i = 0; i < TabPanelIds.Length; i++)
        {
            UiNode? panel = tree.FindNode(TabPanelIds[i]);
            if (panel == null)
                continue;

            panel.Props["CanvasLeft"] = (double)PanelLeft;
            panel.Props["CanvasTop"] = (double)PanelTop;
            panel.Props["Width"] = (double)PanelWidth;
            panel.Props["Height"] = (double)PanelHeight;
            panel.Props["SolidColorBackgroundTexture"] = ColorFromArgb(160, 12, 10, 8);
            panel.Props["TabIndex"] = i;
            panel.TemplateKey = "DxOptionsScrollPanel";
        }
    }

    private static void PositionTabButtons(UiNode root)
    {
        string[] ids =
        [
            "btnTabDisplay", "btnTabAudio", "btnTabGame", "btnTabCnCNet",
            "btnTabSecurity", "btnTabUpdater", "btnTabComponents",
        ];
        int x = 12;
        const double tabWidth = 74.0;
        for (int i = 0; i < ids.Length; i++)
        {
            UiNode? btn = root.Children.FirstOrDefault(c => c.Id.Equals(ids[i], StringComparison.OrdinalIgnoreCase));
            if (btn == null)
                continue;

            btn.Props["CanvasLeft"] = (double)x;
            btn.Props["CanvasTop"] = 12.0;
            btn.Props["Width"] = tabWidth;
            btn.Props["Height"] = 26.0;
            btn.TemplateKey = "DxTabButton";
            btn.Props["TabIndex"] = i;
            btn.Props["Text"] = TabTitles[i];
            x += (int)tabWidth + 2;
        }
    }

    private static void PositionWindowChrome(UiNodeTree tree)
    {
        SetRect(tree.FindNode("panelBorderTop"), 0, -8, DialogWidth, 9);
        SetRect(tree.FindNode("panelBorderBottom"), 0, DialogHeight - 1, DialogWidth, 9);
        SetRect(tree.FindNode("panelBorderLeft"), -8, 0, 9, DialogHeight);
        SetRect(tree.FindNode("panelBorderRight"), DialogWidth - 1, 0, 9, DialogHeight);
        SetRect(tree.FindNode("panelBorderCornerTL"), -8, -8, 9, 9);
        SetRect(tree.FindNode("panelBorderCornerTR"), DialogWidth - 1, -8, 9, 9);
        SetRect(tree.FindNode("panelBorderCornerBL"), -8, DialogHeight - 1, 9, 9);
        SetRect(tree.FindNode("panelBorderCornerBR"), DialogWidth - 1, DialogHeight - 1, 9, 9);
    }

    private static void EnsureFooterButtons(UiNode root)
    {
        // MG OptionsWindow.ini omits footer buttons; XNA OptionsWindow creates them in code.
        EnsureFooterButton(root, "btnSave", OptionsFooterChrome.ResolveSaveText());
        EnsureFooterButton(root, "btnCancel", OptionsFooterChrome.ResolveCancelText());
    }

    private static void EnsureFooterButton(UiNode root, string id, string text)
    {
        // Prefer a root-level footer button (not a nested Campaign/other btnCancel).
        UiNode? btn = root.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (btn == null)
        {
            btn = CreatePanelNode(id, "XNAClientButton", "DxButton");
            btn.Parent = root;
            root.Children.Add(btn);
        }
        else if (btn.Parent != root)
        {
            btn.Parent?.Children.Remove(btn);
            btn.Parent = root;
            if (!root.Children.Contains(btn))
                root.Children.Add(btn);
        }

        btn.TemplateKey = "DxButton";
        btn.Props["Text"] = text;
        btn.Props["IsVisible"] = true;
        btn.Props["Width"] = 92.0;
        btn.Props["Height"] = 32.0;
        btn.Props["ZIndex"] = 1000;
        // ThemeMG ships button.png (not 92pxbtn.png) — Classic must bind these
        // before UiNodeViewModel.LoadImages runs.
        btn.Props["IdleTexture"] = OptionsFooterChrome.IdleTexture;
        btn.Props["HoverTexture"] = OptionsFooterChrome.HoverTexture;
    }

    private static void PositionFooterButtons(UiNodeTree tree)
    {
        // DX OptionsWindow: Save left (12), Cancel right (Width-104) — bottom corners.
        UiNode? btnSave = tree.Root.Children.FirstOrDefault(c =>
            c.Id.Equals("btnSave", StringComparison.OrdinalIgnoreCase));
        if (btnSave != null)
        {
            btnSave.Props["CanvasLeft"] = 12.0;
            btnSave.Props["CanvasTop"] = (double)(DialogHeight - 40);
            btnSave.Props["IsVisible"] = true;
            btnSave.Props["Text"] = OptionsFooterChrome.ResolveSaveText();
            btnSave.Props["Width"] = 92.0;
            btnSave.Props["Height"] = 32.0;
            btnSave.Props["ZIndex"] = 1000;
            btnSave.Props["IdleTexture"] = OptionsFooterChrome.IdleTexture;
            btnSave.Props["HoverTexture"] = OptionsFooterChrome.HoverTexture;
        }

        UiNode? btnCancel = tree.Root.Children.FirstOrDefault(c =>
            c.Id.Equals("btnCancel", StringComparison.OrdinalIgnoreCase));
        if (btnCancel != null)
        {
            // Cancel = panel bottom-RIGHT (not left).
            btnCancel.Props["CanvasLeft"] = (double)(DialogWidth - 104);
            btnCancel.Props["CanvasTop"] = (double)(DialogHeight - 40);
            btnCancel.Props["IsVisible"] = true;
            btnCancel.Props["Text"] = OptionsFooterChrome.ResolveCancelText();
            btnCancel.Props["Width"] = 92.0;
            btnCancel.Props["Height"] = 32.0;
            btnCancel.Props["ZIndex"] = 1000;
            btnCancel.Props["IdleTexture"] = OptionsFooterChrome.IdleTexture;
            btnCancel.Props["HoverTexture"] = OptionsFooterChrome.HoverTexture;
        }
    }

    private static void SetRect(UiNode? node, int x, int y, int w, int h)
    {
        if (node == null)
            return;

        node.Props["CanvasLeft"] = (double)x;
        node.Props["CanvasTop"] = (double)y;
        node.Props["Width"] = (double)w;
        node.Props["Height"] = (double)h;
    }

    private static void EnsureTabPanels(UiNodeTree tree, UiNode root)
    {
        UiNode? tabControl = tree.FindNode("tabControl");
        if (tabControl == null)
        {
            tabControl = CreatePanelNode("tabControl", "XNAClientTabControl", "DxTabControl");
            tabControl.Props["CanvasLeft"] = 12.0;
            tabControl.Props["CanvasTop"] = 12.0;
            tabControl.Props["Width"] = (double)PanelWidth;
            tabControl.Props["Height"] = 23.0;
            tabControl.Parent = root;
            root.Children.Insert(0, tabControl);
        }

        for (int i = 0; i < TabPanelIds.Length; i++)
        {
            string panelId = TabPanelIds[i];
            UiNode? panel = tree.FindNode(panelId);
            if (panel == null)
            {
                panel = CreatePanelNode(panelId, "XNAPanel", "DxPanel");
                panel.Parent = root;
                root.Children.Add(panel);
            }

            panel.Props["CanvasLeft"] = (double)PanelLeft;
            panel.Props["CanvasTop"] = (double)PanelTop;
            panel.Props["Width"] = (double)PanelWidth;
            panel.Props["Height"] = (double)PanelHeight;
            panel.Props["SolidColorBackgroundTexture"] = ColorFromArgb(128, 0, 0, 0);
            panel.Props["TabTitle"] = TabTitles[i];
            panel.Props["TabIndex"] = i;
            panel.Props["IsVisible"] = i == 0;
            panel.TemplateKey = "DxOptionsScrollPanel";
        }
    }

    private static void ReparentControlsToPanels(UiNodeTree tree, UiNode root)
    {
        var toMove = new List<UiNode>();
        foreach (UiNode child in root.Children.ToList())
        {
            if (child.Id is "tabControl" or "btnOK" or "btnCancel" or "btnSave")
                continue;

            if (TabPanelIds.Contains(child.Id, StringComparer.OrdinalIgnoreCase))
                continue;

            if (ControlToPanel.ContainsKey(child.Id) || child.ControlType.Contains("CheckBox", StringComparison.OrdinalIgnoreCase)
                || child.ControlType.Contains("DropDown", StringComparison.OrdinalIgnoreCase)
                || child.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase)
                || child.Id.StartsWith("dd", StringComparison.OrdinalIgnoreCase))
                toMove.Add(child);
        }

        foreach (UiNode node in toMove)
        {
            string panelId = ControlToPanel.TryGetValue(node.Id, out string? mapped)
                ? mapped
                : InferPanel(node.Id);

            UiNode? panel = tree.FindNode(panelId);
            if (panel == null)
                continue;

            if (node.Parent != null)
                node.Parent.Children.Remove(node);

            node.Parent = panel;
            panel.Children.Add(node);
        }
    }

    private static string InferPanel(string controlId)
    {
        if (controlId.StartsWith("chkWaf", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("lblWaf", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("tbWaf", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("btnWaf", StringComparison.OrdinalIgnoreCase)
            || controlId.Equals("ddWafSensitivity", StringComparison.OrdinalIgnoreCase)
            || controlId.Equals("lbWafBlocklist", StringComparison.OrdinalIgnoreCase))
            return "SecurityOptionsPanel";

        if (controlId.StartsWith("chkPing", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkWriteInstall", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkPlaySound", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkNotify", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkSkip", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkDisablePrivate", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkPersistent", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkConnect", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkAllowGame", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkDiscord", StringComparison.OrdinalIgnoreCase)
            || controlId.StartsWith("chkSteam", StringComparison.OrdinalIgnoreCase)
            || controlId.Equals("lblAllowPrivateMessagesFrom", StringComparison.OrdinalIgnoreCase)
            || controlId.Equals("ddAllowPrivateMessagesFrom", StringComparison.OrdinalIgnoreCase))
            return "CnCNetOptionsPanel";

        if (controlId is "lblPlayerName" or "tbPlayerName" or "lblPlayerNameNotice")
            return "GameOptionsPanel";

        if (controlId is "chkreshape" or "chkvxllightec" or "chkTooltipsExtra" or "chkPrioritySelection" or "chkBuildingPlacement")
            return "GameOptionsPanel";

        return "DisplayOptionsPanel";
    }

    public static void SetActiveTab(UiNodeViewModel root, int index)
    {
        foreach (UiNodeViewModel child in root.Children)
        {
            if (TabPanelIds.Contains(child.Id, StringComparer.OrdinalIgnoreCase))
            {
                int tabIndex = child.Node.Props.TryGetValue("TabIndex", out object? ti) && ti is int i ? i : -1;
                child.IsVisible = tabIndex == index;
            }

            if (child.Id.StartsWith("btnTab", StringComparison.OrdinalIgnoreCase))
            {
                int tabIndex = child.Node.Props.TryGetValue("TabIndex", out object? ti) && ti is int i ? i : -1;
                child.Node.Props["IsTabSelected"] = tabIndex == index;
                child.RefreshLayout();
            }
        }
    }

    public static void SetActiveTab(UiNode root, int index)
    {
        foreach (UiNode child in root.Children)
        {
            if (TabPanelIds.Contains(child.Id, StringComparer.OrdinalIgnoreCase))
            {
                int tabIndex = child.Props.TryGetValue("TabIndex", out object? ti) && ti is int i ? i : -1;
                child.Props["IsVisible"] = tabIndex == index;
            }

            if (child.Id.StartsWith("btnTab", StringComparison.OrdinalIgnoreCase))
            {
                int tabIndex = child.Props.TryGetValue("TabIndex", out object? ti) && ti is int i ? i : -1;
                child.Props["IsTabSelected"] = tabIndex == index;
            }
        }
    }

    private static UiNode CreatePanelNode(string id, string controlType, string templateKey)
        => new()
        {
            Id = id,
            ControlType = controlType,
            TemplateKey = templateKey,
            // So DxNodeTemplateSelector does not treat Options btnCancel as Campaign chrome.
            WindowName = "OptionsWindow",
        };

    private static object ColorFromArgb(byte a, byte r, byte g, byte b)
        => Avalonia.Media.Color.FromArgb(a, r, g, b);

    private static Dictionary<string, string> BuildControlToPanelMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in new[]
        {
            "chkWindowedMode", "lblIngameResolution", "ddIngameResolution", "lblDetailLevel", "ddDetailLevel",
            "lblRenderer", "ddRenderer", "chkBorderlessWindowedMode", "chkBackBufferInVRAM", "lblClientResolution",
            "ddClientResolution",             "chkBorderlessClient", "lblClientTheme", "ddClientTheme", "chkStretchMovies",
            "chkMEDDraw", "ddReShade", "lblReShade",
        })
            map[id] = "DisplayOptionsPanel";

        foreach (string id in new[]
        {
            "chkPersistentMode", "chkPlaySoundOnGameHosted", "chkNotifyOnUserListChange", "chkSkipLoginWindow",
            "chkDisablePrivateMessagePopup", "chkConnectOnStartup", "chkAllowGameInvitesFromFriendsOnly",
            "chkDiscordIntegration", "chkPingUnofficialTunnels", "chkWriteInstallPathToRegistry",
            "chkSteamIntegration", "lblAllowPrivateMessagesFrom", "ddAllowPrivateMessagesFrom",
            "lblAllowPrivateMessagesFromHint",
            "lblGameBroadcastInterval", "ddGameBroadcastInterval",
        })
            map[id] = "CnCNetOptionsPanel";

        foreach (string id in new[] { "chkreshape", "chkvxllightec", "chkTooltipsExtra", "chkPrioritySelection", "chkBuildingPlacement" })
            map[id] = "GameOptionsPanel";

        foreach (string id in new[] { "lblPlayerName", "tbPlayerName", "lblPlayerNameNotice" })
            map[id] = "GameOptionsPanel";

        foreach (string id in new[]
        {
            "lblScoreVolume", "lblScoreVolumeValue", "trbScoreVolume",
            "lblSoundVolume", "lblSoundVolumeValue", "trbSoundVolume",
            "lblVoiceVolume", "lblVoiceVolumeValue", "trbVoiceVolume",
            "chkScoreShuffle",
            "lblClientVolume", "lblClientVolumeValue", "trbClientVolume",
            "chkMainMenuMusic", "chkStopMusicOnMenu", "chkStopGameLobbyMessageAudio",
        })
            map[id] = "AudioOptionsPanel";

        foreach (string id in new[]
        {
            "lblUpdaterDescription", "lbUpdateServerList", "btnMoveUp", "btnMoveDown",
            "chkAutoCheck", "btnForceUpdate",
        })
            map[id] = "UpdaterOptionsPanel";

        foreach (string id in new[]
        {
            "lblWafIntro", "chkWafEnabled", "chkWafCheckProtocol", "chkWafCheckListingText",
            "chkWafCheckChannelChat", "chkWafCheckPrivateChat", "chkWafAutoHideHighRisk",
            "chkWafAllowHeuristicDrop", "lblWafSensitivity", "ddWafSensitivity",
            "lblWafBlocklistCount", "lbWafBlocklist", "lblWafBlockKey", "tbWafBlockKey",
            "lblWafBlockNote", "tbWafBlockNote", "btnWafStrategies",
            "btnWafBlockAdd", "btnWafBlockRemove", "btnWafBlockClear",
        })
            map[id] = "SecurityOptionsPanel";

        return map;
    }
}
