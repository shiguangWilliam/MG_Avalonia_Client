using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Ensures display-tab renderer controls exist (values loaded later by DisplayOptionsApplier).</summary>
internal static class RendererOptionsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("DisplayOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(tree, panel, "lblRenderer", "Renderer:");
        EnsureDropdown(tree, panel, "ddRenderer");
    }

    private static void EnsureLabel(UiNodeTree tree, UiNode panel, string id, string text)
    {
        UiNode? label = tree.FindNode(id);
        if (label == null)
        {
            label = new UiNode
            {
                Id = id,
                ControlType = "XNALabel",
                TemplateKey = "DxLabel",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            label.Props["Text"] = text;
            panel.Children.Add(label);
        }
        else if (label.Parent != panel)
        {
            AttachToPanel(panel, label);
        }
    }

    private static void EnsureDropdown(UiNodeTree tree, UiNode panel, string id)
    {
        UiNode? dropdown = tree.FindNode(id);
        if (dropdown == null)
        {
            dropdown = new UiNode
            {
                Id = id,
                ControlType = "XNAClientDropDown",
                TemplateKey = "DxComboBox",
                WindowName = "OptionsWindow",
                Parent = panel,
            };
            dropdown.Props["Width"] = 228.0;
            dropdown.Props["Height"] = 24.0;
            panel.Children.Add(dropdown);
        }
        else if (dropdown.Parent != panel)
        {
            AttachToPanel(panel, dropdown);
        }
    }

    private static void AttachToPanel(UiNode panel, UiNode node)
    {
        node.Parent?.Children.Remove(node);
        node.Parent = panel;
        if (!panel.Children.Contains(node))
            panel.Children.Add(node);
    }
}
