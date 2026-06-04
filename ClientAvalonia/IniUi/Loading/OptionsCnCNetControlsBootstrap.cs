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

        UiNode? label = tree.FindNode("lblAllowPrivateMessagesFrom");
        if (label == null)
        {
            label = new UiNode
            {
                Id = "lblAllowPrivateMessagesFrom",
                ControlType = "XNALabel",
                TemplateKey = "DxLabel",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(label);
        }

        if (!HasDisplayText(label))
            label.Props["Text"] = "Allow Private Messages From:".L10N("Client:DTAConfig:AllowPMFrom");

        UiNode? dropdown = tree.FindNode("ddAllowPrivateMessagesFrom");
        if (dropdown == null)
        {
            dropdown = new UiNode
            {
                Id = "ddAllowPrivateMessagesFrom",
                ControlType = "XNAClientDropDown",
                TemplateKey = "DxComboBox",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(dropdown);
        }

        if (!dropdown.Props.ContainsKey("Items"))
        {
            dropdown.Props["Items"] =
                "All".L10N("Client:DTAConfig:PMAll") + "," +
                "Current channel".L10N("Client:DTAConfig:PMCurrentChannel") + "," +
                "Friends".L10N("Client:DTAConfig:PMFriends") + "," +
                "None".L10N("Client:DTAConfig:PMNone");
        }
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
