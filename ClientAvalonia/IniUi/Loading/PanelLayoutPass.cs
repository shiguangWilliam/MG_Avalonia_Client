using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Resolves overlapping canvas children inside panels (options tabs, lobby sub-panels).</summary>
public sealed class PanelLayoutPass
{
    private const int ColumnTolerance = 56;
    private const int RowTolerance = 8;
    private const int Gap = 6;
    private const int MaxIterations = 12;

    public void Apply(UiNodeTree tree)
    {
        foreach (UiNode node in tree.AllNodes())
        {
            if (node == tree.Root || !IsContentPanel(node))
                continue;

            if (node.Children.Count < 2)
                continue;

            for (int pass = 0; pass < MaxIterations; pass++)
            {
                if (!ResolveOverlaps(node))
                    break;
            }
        }
    }

    private static bool IsContentPanel(UiNode node)
    {
        if (node.Id.EndsWith("OptionsPanel", StringComparison.OrdinalIgnoreCase)
            || node.Id is "ComponentsPanel" or "GameOptionsPanel" or "GameRulesPanel" or "MapListPanel")
            return true;

        if (node.Id.EndsWith("Panel", StringComparison.OrdinalIgnoreCase)
            && node.Parent?.Id.Contains("Lobby", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }

    private static bool IsLayoutContainer(UiNode node)
        => IsContentPanel(node);

    private static bool ResolveOverlaps(UiNode panel)
    {
        bool moved = false;
        List<UiNode> children = panel.Children
            .OrderBy(c => c.GetIntProp("CanvasTop"))
            .ThenBy(c => c.GetIntProp("CanvasLeft"))
            .ToList();

        for (int i = 0; i < children.Count; i++)
        {
            UiNode current = children[i];
            var rect = GetRect(current);

            for (int j = 0; j < i; j++)
            {
                UiNode previous = children[j];
                if (!IsVisibleNode(previous))
                    continue;

                var prevRect = GetRect(previous);
                if (!Overlaps(rect, prevRect))
                    continue;

                if (SameColumn(rect, prevRect))
                {
                    int newTop = prevRect.Bottom + Gap;
                    if (newTop > rect.Top)
                    {
                        current.Props["CanvasTop"] = (double)newTop;
                        rect = GetRect(current);
                        moved = true;
                    }
                }
                else if (SameRow(rect, prevRect))
                {
                    int newLeft = prevRect.Right + Gap;
                    if (newLeft > rect.Left)
                    {
                        current.Props["CanvasLeft"] = (double)newLeft;
                        rect = GetRect(current);
                        moved = true;
                    }
                }
                else
                {
                    int newTop = prevRect.Bottom + Gap;
                    if (newTop > rect.Top)
                    {
                        current.Props["CanvasTop"] = (double)newTop;
                        rect = GetRect(current);
                        moved = true;
                    }
                }
            }
        }

        return moved;
    }

    private static bool IsVisibleNode(UiNode node)
        => !node.Props.TryGetValue("IsVisible", out object? v) || v is not bool b || b;

    private static bool SameColumn(Rect a, Rect b)
        => Math.Abs(a.Left - b.Left) <= ColumnTolerance;

    private static bool SameRow(Rect a, Rect b)
        => Math.Abs(a.Top - b.Top) <= RowTolerance;

    private static bool Overlaps(Rect a, Rect b)
        => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    private static Rect GetRect(UiNode node)
    {
        int left = node.GetIntProp("CanvasLeft");
        int top = node.GetIntProp("CanvasTop");
        int width = Math.Max(node.GetIntProp("Width"), 24);
        int height = Math.Max(node.GetIntProp("Height"), 18);
        return new Rect(left, top, width, height);
    }

    private readonly struct Rect(int left, int top, int width, int height)
    {
        public int Left { get; } = left;
        public int Top { get; } = top;
        public int Right { get; } = left + width;
        public int Bottom { get; } = top + height;
    }
}
