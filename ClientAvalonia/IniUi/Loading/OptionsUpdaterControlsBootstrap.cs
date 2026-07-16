using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// DX <c>UpdaterOptionsPanel</c> builds controls in code; OptionsWindow.ini has no Updater section.
/// </summary>
internal static class OptionsUpdaterControlsBootstrap
{
    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("UpdaterOptionsPanel");
        if (panel == null)
            return;

        UiNode tip = EnsureChild(panel, "lblUpdaterDescription", "XNALabel", "DxLabel");
        tip.Props["Text"] = ("To change download server priority, select a server from the list and\n" +
                             "use the Move Up / Down buttons to change its priority.")
            .L10N("Client:DTAConfig:ServerPriorityTip");
        tip.Props["Width"] = 520.0;
        tip.Props["Height"] = 56.0;

        UiNode list = EnsureChild(panel, "lbUpdateServerList", "XNAListBox", "DxListBox");
        list.Props["Width"] = 520.0;
        list.Props["Height"] = 120.0;

        EnsureButton(panel, "btnMoveUp", "Move Up".L10N("Client:DTAConfig:MoveUp"));
        EnsureButton(panel, "btnMoveDown", "Move Down".L10N("Client:DTAConfig:MoveDown"));
        EnsureCheckBox(
            panel,
            "chkAutoCheck",
            "Check for updates automatically".L10N("Client:DTAConfig:AutoCheckUpdate"));
        EnsureButton(panel, "btnForceUpdate", "Force Update".L10N("Client:DTAConfig:ForceUpdate"));
    }

    private static void EnsureButton(UiNode panel, string id, string text)
    {
        UiNode node = EnsureChild(panel, id, "XNAClientButton", "DxButton");
        node.Props["Text"] = text;
        node.Props["Width"] = 133.0;
        node.Props["Height"] = 23.0;
    }

    private static void EnsureCheckBox(UiNode panel, string id, string text)
    {
        UiNode node = EnsureChild(panel, id, "XNAClientCheckBox", "DxCheckBox");
        node.Props["Text"] = text;
        node.Props["Width"] = 420.0;
        node.Props["Height"] = 24.0;
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
