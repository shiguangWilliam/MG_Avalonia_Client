using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// XNA DisplayOptionsPanel creates resolution/renderer/theme dropdowns in code;
/// MG overlay INI only supplies labels/checkboxes — inject missing dd* nodes here.
/// </summary>
internal static class OptionsDisplayControlsBootstrap
{
    private sealed record DropdownDef(string Id, string Items, int DefaultIndex = 0);

    private static readonly DropdownDef[] DisplayDropdowns =
    [
        new("ddIngameResolution", "800x600,1024x768,1280x720,1280x800,1920x1080"),
        new("ddDetailLevel", "低,中,高", 1),
        new("ddClientResolution", "(default),1280x720,1280x800,1920x1080"),
        new("ddClientTheme", "Moment of Genesis,Default"),
    ];

    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("DisplayOptionsPanel");
        if (panel == null)
            return;

        foreach (DropdownDef def in DisplayDropdowns)
        {
            UiNode? existing = tree.FindNode(def.Id);
            if (existing != null)
            {
                if (existing.Parent != panel)
                    AttachToPanel(panel, existing);
                continue;
            }

            var node = new UiNode
            {
                Id = def.Id,
                ControlType = "XNAClientDropDown",
                TemplateKey = "DxComboBox",
                WindowName = "OptionsWindow",
                Parent = panel,
            };

            node.Props["Width"] = 228.0;
            node.Props["Height"] = 24.0;
            node.Props["Items"] = def.Items;
            node.Props["DefaultIndex"] = def.DefaultIndex;
            node.Props["SelectedIndex"] = def.DefaultIndex;

            panel.Children.Add(node);
        }
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
