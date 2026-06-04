using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

internal static class LobbyLayout
{
    public static void ApplyPanelVisibility(UiNodeTree tree)
    {
        ApplyPanelOverlay(tree, "GameOptionsPanel");
        ApplyPanelOverlay(tree, "GameRulesPanel");
        ApplyPanelOverlay(tree, "MapListPanel");
        ApplyLabelDropdownSpacing(tree);
    }

    /// <summary>Align random-map button and search box on one row (MG theme places search at far right).</summary>
    public static void ApplyMapToolbarLayout(UiNodeTree tree)
    {
        UiNode? randomBtn = tree.FindNode("btnPickRandomMap");
        UiNode? searchBox = tree.FindNode("tbMapSearch");
        UiNode? mapList = tree.FindNode("lbMapList");
        if (randomBtn == null || searchBox == null || mapList == null)
            return;

        const int spacing = 6;
        int rowY = randomBtn.GetIntProp("CanvasTop");
        int rowHeight = Math.Max(randomBtn.GetIntProp("Height"), searchBox.GetIntProp("Height"));
        if (rowHeight <= 0)
            rowHeight = 21;

        int randomLeft = randomBtn.GetIntProp("CanvasLeft");
        int randomWidth = randomBtn.GetIntProp("Width");
        int mapListRight = mapList.GetIntProp("CanvasLeft") + mapList.GetIntProp("Width");
        int searchLeft = randomLeft + randomWidth + spacing;
        int searchWidth = mapListRight - searchLeft;

        if (searchWidth < 80)
            return;

        randomBtn.Props["CanvasTop"] = (double)rowY;
        randomBtn.Props["Height"] = (double)rowHeight;
        searchBox.Props["CanvasLeft"] = (double)searchLeft;
        searchBox.Props["CanvasTop"] = (double)rowY;
        searchBox.Props["Width"] = (double)searchWidth;
        searchBox.Props["Height"] = (double)rowHeight;
    }

    private static void ApplyPanelOverlay(UiNodeTree tree, string panelId)
    {
        UiNode? panel = tree.FindNode(panelId);
        if (panel == null || panel.Props.ContainsKey("SolidColorBackgroundTexture"))
            return;

        panel.Props["SolidColorBackgroundTexture"] = Avalonia.Media.Color.FromArgb(96, 0, 0, 0);
    }

    private static void ApplyLabelDropdownSpacing(UiNodeTree tree)
    {
        UiNode? gameOptions = tree.FindNode("GameOptionsPanel");
        if (gameOptions == null)
            return;

        foreach (UiNode child in gameOptions.Children)
        {
            if (!child.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase))
                continue;

            string ddId = "dd" + child.Id[3..];
            UiNode? dropdown = gameOptions.Children.FirstOrDefault(c => c.Id.Equals(ddId, StringComparison.OrdinalIgnoreCase));
            if (dropdown == null)
                continue;

            int labelBottom = child.GetIntProp("CanvasTop") + Math.Max(child.GetIntProp("Height"), 18);
            int ddTop = dropdown.GetIntProp("CanvasTop");
            if (ddTop < labelBottom + 2)
                dropdown.Props["CanvasTop"] = (double)(labelBottom + 4);
        }
    }
}
