using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;
using ClientUpdater;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// DX <c>ComponentsPanel</c> builds per-component rows in code; inject a usable Avalonia tree.
/// </summary>
internal static class OptionsComponentsControlsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("ComponentsPanel");
        if (panel == null)
            return;

        // Drop previous dynamic rows so reopening options refreshes from Updater state.
        panel.Children.RemoveAll(c =>
            c.Id.StartsWith("lblComponent_", StringComparison.OrdinalIgnoreCase)
            || c.Id.StartsWith("btnComponent_", StringComparison.OrdinalIgnoreCase)
            || c.Id.Equals("lblNoComponents", StringComparison.OrdinalIgnoreCase));

        if (Updater.CustomComponents == null || Updater.CustomComponents.Count == 0)
        {
            UiNode empty = EnsureChild(panel, "lblNoComponents", "XNALabel", "DxLabel");
            empty.Props["Text"] = "No optional components are available for this installation."
                .L10N("Client:DTAConfig:NoComponents");
            empty.Props["Width"] = 520.0;
            empty.Props["Height"] = 40.0;
            return;
        }

        foreach (CustomComponent component in Updater.CustomComponents)
        {
            string safeId = SanitizeId(component.ININame);
            UiNode label = EnsureChild(panel, "lblComponent_" + safeId, "XNALabel", "DxLabel");
            label.Props["Text"] = string.IsNullOrWhiteSpace(component.GUIName) ? component.ININame : component.GUIName;
            label.Props["Width"] = 360.0;
            label.Props["Height"] = 24.0;
            label.Props["ComponentIniName"] = component.ININame;

            UiNode button = EnsureChild(panel, "btnComponent_" + safeId, "XNAClientButton", "DxButton");
            button.Props["Text"] = "Not Available".L10N("Client:DTAConfig:NotAvailable");
            button.Props["Width"] = 133.0;
            button.Props["Height"] = 23.0;
            button.Props["ComponentIniName"] = component.ININame;
        }
    }

    private static string SanitizeId(string iniName)
    {
        if (string.IsNullOrWhiteSpace(iniName))
            return "Unknown";

        char[] chars = iniName.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = '_';
        }

        return new string(chars);
    }

    private static UiNode EnsureChild(UiNode panel, string id, string controlType, string templateKey)
    {
        UiNode? existing = panel.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.TemplateKey = templateKey;
            return existing;
        }

        var node = new UiNode
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
}
