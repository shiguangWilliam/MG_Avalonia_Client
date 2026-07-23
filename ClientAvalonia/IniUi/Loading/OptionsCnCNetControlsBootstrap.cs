using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// MG OptionsWindow.ini omits Text= on many CnCNet checkboxes; XNA sets them in CnCNetOptionsPanel.Initialize.
/// </summary>
internal static class OptionsCnCNetControlsBootstrap
{
    private static readonly (string Id, string DefaultEnglish, string L10NKey)[] CheckBoxLabels =
    [
        ("chkPingUnofficialTunnels", "Ping unofficial CnCNet tunnels", "Client:DTAConfig:PingUnofficial"),
        ("chkWriteInstallPathToRegistry",
            "Write game installation path to Windows Registry (makes it possible to join other games' game rooms on CnCNet)",
            "Client:DTAConfig:WriteGameRegistry"),
        ("chkPlaySoundOnGameHosted", "Play sound when a game is hosted", "Client:DTAConfig:PlaySoundGameHosted"),
        ("chkNotifyOnUserListChange",
            "Show player join / quit messages on CnCNet lobby",
            "Client:DTAConfig:ShowPlayerJoinQuit"),
        ("chkDisablePrivateMessagePopup", "Disable Popups from Private Messages", "Client:DTAConfig:DisablePMPopup"),
        ("chkSkipLoginWindow", "Skip login dialog", "Client:DTAConfig:SkipLoginDialog"),
        ("chkPersistentMode", "Stay connected outside of the CnCNet lobby", "Client:DTAConfig:StayConnect"),
        ("chkConnectOnStartup", "Connect automatically on client startup", "Client:DTAConfig:ConnectOnStart"),
        ("chkDiscordIntegration", "Show detailed game info in Discord status", "Client:DTAConfig:DiscordStatus"),
        ("chkAllowGameInvitesFromFriendsOnly",
            "Only receive game invitations from friends",
            "Client:DTAConfig:FriendsOnly"),
        ("chkSteamIntegration", "Show the game being played in Steam", "Client:DTAConfig:SteamStatus"),
    ];

    public static void Apply(UiNodeTree tree)
    {
        foreach ((string id, string defaultEnglish, string l10NKey) in CheckBoxLabels)
        {
            UiNode? node = tree.FindNode(id);
            if (node == null)
                continue;

            if (HasDisplayText(node))
                continue;

            string label = defaultEnglish.L10N(l10NKey);
            node.Props["Text"] = NormalizeLineBreaks(label);
            if (!node.Props.ContainsKey("Width"))
                node.Props["Width"] = 248.0;
        }

        EnsureAllowPrivateMessagesDropdown(tree);
    }

    private static void EnsureAllowPrivateMessagesDropdown(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("CnCNetOptionsPanel");
        if (panel == null)
            return;

        UiNode label = EnsureOnPanel(tree, panel, "lblAllowPrivateMessagesFrom", "XNALabel", "DxLabel");
        if (!HasDisplayText(label))
            label.Props["Text"] = "Allow Private Messages From:".L10N("Client:DTAConfig:AllowPMFrom");

        UiNode dropdown = EnsureOnPanel(tree, panel, "ddAllowPrivateMessagesFrom", "XNAClientDropDown", "DxComboBox");
        dropdown.TemplateKey = "DxComboBox";
        dropdown.Props["Width"] = 240.0;
        dropdown.Props["Height"] = 24.0;

        if (!dropdown.Props.ContainsKey("Items") || string.IsNullOrWhiteSpace(dropdown.Props["Items"]?.ToString()))
        {
            dropdown.Props["Items"] =
                "All".L10N("Client:DTAConfig:PMAll") + "," +
                "Current channel".L10N("Client:DTAConfig:PMCurrentChannel") + "," +
                "Friends".L10N("Client:DTAConfig:PMFriends") + "," +
                "None".L10N("Client:DTAConfig:PMNone");
        }

        UiNode hint = EnsureOnPanel(tree, panel, "lblAllowPrivateMessagesFromHint", "XNALabel", "DxLabel");
        hint.Props["Text"] = "提示：来源策略优先于内容防护；选「所有人」时完全依赖设置→安全中的入网 WAF。";
        hint.Props["Width"] = 520.0;
        hint.Props["Height"] = 32.0;
    }

    /// <summary>Find-or-create and always reparent onto the CnCNet panel (INI often leaves the dropdown on root).</summary>
    private static UiNode EnsureOnPanel(
        UiNodeTree tree,
        UiNode panel,
        string id,
        string controlType,
        string templateKey)
    {
        UiNode? node = tree.FindNode(id);
        if (node == null)
        {
            node = new UiNode
            {
                Id = id,
                ControlType = controlType,
                TemplateKey = templateKey,
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(node);
            return node;
        }

        if (node.Parent != panel)
        {
            node.Parent?.Children.Remove(node);
            node.Parent = panel;
            if (!panel.Children.Contains(node))
                panel.Children.Add(node);
        }

        node.TemplateKey = templateKey;
        return node;
    }

    private static bool HasDisplayText(UiNode node)
    {
        if (!node.Props.TryGetValue("Text", out object? value))
            return false;

        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    private static string NormalizeLineBreaks(string text)
        => text.Replace("@", Environment.NewLine).Replace("\\n", Environment.NewLine);
}
