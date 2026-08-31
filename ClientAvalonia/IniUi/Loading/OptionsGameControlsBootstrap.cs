using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// XNA GameOptionsPanel creates player-name controls in code; inject them for the Avalonia options overlay.
/// MG OptionsWindow.ini often lists Phobos checkboxes without Text=.
/// </summary>
internal static class OptionsGameControlsBootstrap
{
    private static readonly (string Id, string DefaultEnglish, string L10NKey)[] GameCheckBoxLabels =
    [
        ("chkTooltipsExtra",
            "Sidebar Tooltip Descriptions",
            "Client:DTAConfig:ShowDescription"),
        ("chkPrioritySelection",
            "Mass Selection Filtering",
            "Client:DTAConfig:PrioritySelection"),
        ("chkBuildingPlacement",
            "Show Building Placement Preview",
            "Client:DTAConfig:PlacementPreview"),
    ];

    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("GameOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(
            tree,
            panel,
            "lblPlayerName",
            "Player name*:".L10N("Client:DTAConfig:PlayerName"));
        EnsureTextBox(tree, panel, "tbPlayerName");
        EnsureLabel(
            tree,
            panel,
            "lblPlayerNameNotice",
            "* If you are already connected to CnCNet, you need to log out and reconnect for the new name to apply."
                .L10N("Client:DTAConfig:ReconnectAfterRename"));

        foreach ((string id, string defaultEnglish, string l10NKey) in GameCheckBoxLabels)
        {
            UiNode? node = tree.FindNode(id);
            if (node == null)
                continue;

            if (HasDisplayText(node))
                continue;

            node.Props["Text"] = defaultEnglish.L10N(l10NKey);
            if (!node.Props.ContainsKey("Width"))
                node.Props["Width"] = 320.0;
        }
    }

    private static bool HasDisplayText(UiNode node)
    {
        if (!node.Props.TryGetValue("Text", out object? value))
            return false;
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    private static void EnsureLabel(UiNodeTree tree, UiNode panel, string id, string text)
    {
        UiNode? node = tree.FindNode(id);
        if (node == null)
        {
            node = new UiNode
            {
                Id = id,
                ControlType = "XNALabel",
                TemplateKey = "DxLabel",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(node);
        }
        else if (node.Parent != panel)
        {
            AttachToPanel(panel, node);
        }

        if (!HasDisplayText(node))
            node.Props["Text"] = text;
        node.Props["Width"] = 520.0;
        node.Props["Height"] = id.Contains("Notice", StringComparison.OrdinalIgnoreCase) ? 36.0 : 20.0;
    }

    private static void EnsureTextBox(UiNodeTree tree, UiNode panel, string id)
    {
        UiNode? node = tree.FindNode(id);
        if (node == null)
        {
            node = new UiNode
            {
                Id = id,
                ControlType = "XNATextBox",
                TemplateKey = "DxTextBox",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            panel.Children.Add(node);
        }
        else if (node.Parent != panel)
        {
            AttachToPanel(panel, node);
        }

        node.Props["Width"] = 228.0;
        node.Props["Height"] = 24.0;
        node.Props["MaxLength"] = AppState.Configuration.MaxNameLength;
    }

    private static void AttachToPanel(UiNode panel, UiNode node)
    {
        if (node.Parent != null)
            node.Parent.Children.Remove(node);

        node.Parent = panel;
        if (!panel.Children.Contains(node))
            panel.Children.Add(node);
    }
}
