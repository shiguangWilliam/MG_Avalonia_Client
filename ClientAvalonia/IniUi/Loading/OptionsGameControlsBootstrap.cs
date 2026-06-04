using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// XNA GameOptionsPanel creates player-name controls in code; inject them for the Avalonia options overlay.
/// </summary>
internal static class OptionsGameControlsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("GameOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(tree, panel, "lblPlayerName", "玩家昵称*：");
        EnsureTextBox(tree, panel, "tbPlayerName");
        EnsureLabel(tree, panel, "lblPlayerNameNotice",
            "* 若已连接 CnCNet，修改昵称后需退出并重新进入大厅才会生效。");
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
        node.Props["MaxLength"] = ClientCore.ClientConfiguration.Instance.MaxNameLength;
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
