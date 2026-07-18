using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// Pre-compile audit for lobby options panels: internal overflow or external sibling overlap
/// upgrades <see cref="GameOptionsPanel"/> / <see cref="GameRulesPanel"/> to a scroll host.
/// </summary>
public static class LobbyOptionsPanelLayoutPolicy
{
    public const string ScrollTemplateKey = "DxLobbyOptionsPanel";
    private const int ContentPadding = 8;
    private const int InternalSlack = 8;
    private const int ComboTemplateMinHeight = 26;
    private const int CheckBoxTemplateMinHeight = 24;

    private static readonly string[] OptionsPanelIds = ["GameOptionsPanel", "GameRulesPanel"];

    public static void Apply(UiNodeTree tree, string windowSectionName)
    {
        if (!windowSectionName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (string panelId in OptionsPanelIds)
        {
            UiNode? panel = tree.FindNode(panelId);
            if (panel == null || !IsVisible(panel))
                continue;

            EnsureLabelControlSpacing(panel);
            AuditResult audit = Audit(panel, tree.Root);
            if (!audit.ScrollNeeded)
                continue;

            panel.TemplateKey = ScrollTemplateKey;
            panel.Props["ScrollContentHeight"] = (double)(audit.ContentBottom + ContentPadding);
            panel.Props["LobbyOptionsScrollReason"] = audit.Reason;
        }
    }

    internal static AuditResult Audit(UiNode panel, UiNode windowRoot)
    {
        int panelHeight = Math.Max(panel.GetIntProp("Height"), 1);
        int contentBottom = MeasureContentBottom(panel);
        if (contentBottom + InternalSlack > panelHeight)
        {
            return new AuditResult(
                ScrollNeeded: true,
                ContentBottom: contentBottom,
                Reason: $"internalOverflow contentBottom={contentBottom} panelHeight={panelHeight}");
        }

        LayoutRect panelRect = GetAbsoluteRect(panel);
        foreach (UiNode sibling in windowRoot.Children)
        {
            if (ReferenceEquals(sibling, panel) || !IsVisible(sibling) || IsOverlapWhitelist(sibling))
                continue;

            LayoutRect siblingRect = GetAbsoluteRect(sibling);
            if (Intersects(panelRect, siblingRect))
            {
                return new AuditResult(
                    ScrollNeeded: true,
                    ContentBottom: contentBottom,
                    Reason: $"externalOverlap with {sibling.Id}");
            }
        }

        return new AuditResult(false, contentBottom, "fits");
    }

    internal static int MeasureContentBottom(UiNode panel)
    {
        int bottom = 0;
        foreach (UiNode child in panel.Children)
        {
            if (!IsVisible(child))
                continue;

            LayoutRect rect = GetEffectiveRect(child);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return bottom;
    }

    internal static bool IsLobbyOptionsPanel(UiNode node)
    {
        if (node.Id is not ("GameOptionsPanel" or "GameRulesPanel"))
            return false;

        UiNode? parent = node.Parent;
        return parent != null
               && parent.Id.Contains("Lobby", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureLabelControlSpacing(UiNode panel)
    {
        foreach (UiNode child in panel.Children)
        {
            if (!child.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase))
                continue;

            UiNode? control = FindPairedControl(panel, child.Id);
            if (control == null)
                continue;

            int labelBottom = child.GetIntProp("CanvasTop") + Math.Max(child.GetIntProp("Height"), 18);
            int controlTop = control.GetIntProp("CanvasTop");
            if (controlTop < labelBottom + 2)
                control.Props["CanvasTop"] = (double)(labelBottom + 4);
        }
    }

    private static UiNode? FindPairedControl(UiNode panel, string labelId)
    {
        if (labelId.Length <= 3)
            return null;

        string suffix = labelId[3..];
        UiNode? match = panel.Children.FirstOrDefault(c =>
            c.Id.Equals("dd" + suffix, StringComparison.OrdinalIgnoreCase)
            || c.Id.Equals("cmb" + suffix, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match;

        return panel.Children.FirstOrDefault(c =>
            c.Id.StartsWith("cmb" + suffix, StringComparison.OrdinalIgnoreCase)
            || c.Id.StartsWith("dd" + suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOverlapWhitelist(UiNode node)
    {
        if (node.Id.StartsWith("panelBorder", StringComparison.OrdinalIgnoreCase))
            return true;

        if (node.Id is "GenericWindow" or "GameLobbyBase")
            return true;

        return false;
    }

    private static bool IsVisible(UiNode node)
        => !node.Props.TryGetValue("IsVisible", out object? v) || v is not bool b || b;

    private static LayoutRect GetEffectiveRect(UiNode node)
    {
        int left = node.GetIntProp("CanvasLeft");
        int top = node.GetIntProp("CanvasTop");
        int width = Math.Max(node.GetIntProp("Width"), 24);
        int height = Math.Max(node.GetIntProp("Height"), GetTemplateMinHeight(node));
        return new LayoutRect(left, top, width, height);
    }

    private static int GetTemplateMinHeight(UiNode node)
    {
        return node.TemplateKey switch
        {
            "DxComboBox" => ComboTemplateMinHeight,
            "DxCheckBox" => CheckBoxTemplateMinHeight,
            _ when node.ControlType is "GameLobbyDropDown" or "XNAClientDropDown" => ComboTemplateMinHeight,
            _ when node.ControlType is "GameLobbyCheckBox" or "GameSessionCheckBox" => CheckBoxTemplateMinHeight,
            _ => 18,
        };
    }

    private static LayoutRect GetAbsoluteRect(UiNode node)
    {
        int left = node.GetIntProp("CanvasLeft");
        int top = node.GetIntProp("CanvasTop");
        int width = Math.Max(node.GetIntProp("Width"), 1);
        int height = Math.Max(node.GetIntProp("Height"), 1);

        for (UiNode? parent = node.Parent; parent != null; parent = parent.Parent)
        {
            left += parent.GetIntProp("CanvasLeft");
            top += parent.GetIntProp("CanvasTop");
        }

        return new LayoutRect(left, top, width, height);
    }

    private static bool Intersects(LayoutRect a, LayoutRect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    internal readonly record struct AuditResult(bool ScrollNeeded, int ContentBottom, string Reason);

    internal readonly struct LayoutRect(int left, int top, int width, int height)
    {
        public int Left { get; } = left;
        public int Top { get; } = top;
        public int Right { get; } = left + width;
        public int Bottom { get; } = top + height;
    }
}
