using System.Globalization;
using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

public sealed class LayoutResolver
{
    private readonly ExpressionEvaluator _evaluator;

    public LayoutResolver(ExpressionEvaluator evaluator) => _evaluator = evaluator;

    public void UpdateResolution(int width, int height, IReadOnlyDictionary<string, int>? parserConstants = null)
        => _evaluator.UpdateResolution(width, height, parserConstants);

    public void ApplyLayoutPass(UiNodeTree tree)
    {
        foreach (UiNode node in tree.AllNodes())
            ApplyDistanceAndFill(tree, node);

        foreach (UiNode node in tree.AllNodes())
            ResolveExpressionLayoutKeys(tree, node);

        foreach (UiNode node in tree.AllNodes())
            ApplyLabelAnchor(tree, node);
    }

    private void ApplyDistanceAndFill(UiNodeTree tree, UiNode node)
    {
        int x = node.GetIntProp("CanvasLeft");
        int y = node.GetIntProp("CanvasTop");
        int w = node.GetIntProp("Width");
        int h = node.GetIntProp("Height");

        if (node.RawAttributes.TryGetValue("DistanceFromRightBorder", out string? rightDist))
        {
            int parentW = node.Parent?.GetIntProp("Width") ?? 0;
            int dist = EvaluateOrParse(tree, rightDist, node);
            x = parentW - w - dist;
            node.Props["CanvasLeft"] = (double)x;
        }

        if (node.RawAttributes.TryGetValue("DistanceFromBottomBorder", out string? bottomDist))
        {
            int parentH = node.Parent?.GetIntProp("Height") ?? 0;
            int dist = EvaluateOrParse(tree, bottomDist, node);
            y = parentH - h - dist;
            node.Props["CanvasTop"] = (double)y;
        }

        if (node.RawAttributes.TryGetValue("FillWidth", out string? fillW))
        {
            int parentW = node.Parent?.GetIntProp("Width") ?? 0;
            int inset = EvaluateOrParse(tree, fillW, node);
            w = parentW - inset;
            node.Props["Width"] = (double)w;
        }

        if (node.RawAttributes.TryGetValue("FillHeight", out string? fillH))
        {
            int parentH = node.Parent?.GetIntProp("Height") ?? 0;
            int inset = EvaluateOrParse(tree, fillH, node);
            h = parentH - inset;
            node.Props["Height"] = (double)h;
        }
    }

    private void ResolveExpressionLayoutKeys(UiNodeTree tree, UiNode node)
    {
        foreach (string key in node.RawAttributes.Keys.ToList())
        {
            if (!IsLayoutExpressionKey(key))
                continue;

            string value = node.RawAttributes[key];
            if (key is "DistanceFromRightBorder" or "DistanceFromBottomBorder" or "FillWidth" or "FillHeight")
                continue;

            int result = EvaluateOrParse(tree, value, node);
            string? prop = key switch
            {
                "$X" or "X" => "CanvasLeft",
                "$Y" or "Y" => "CanvasTop",
                "$Width" or "Width" => "Width",
                "$Height" or "Height" => "Height",
                _ => null,
            };

            if (prop != null)
                node.Props[prop] = (double)result;
        }

        if (node.RawAttributes.TryGetValue("DrawOrder", out string? drawOrder)
            && int.TryParse(drawOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
            node.Props["ZIndex"] = -order;
    }

    private int EvaluateOrParse(UiNodeTree tree, string value, UiNode parsingNode)
    {
        value = value.Trim();
        if (NeedsExpression(value))
            return _evaluator.Evaluate(value, tree, parsingNode);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;
    }

    private static bool IsLayoutExpressionKey(string key)
        => key is "$X" or "$Y" or "$Width" or "$Height" or "X" or "Y" or "Width" or "Height"
           or "DistanceFromRightBorder" or "DistanceFromBottomBorder" or "FillWidth" or "FillHeight";

    private void ApplyLabelAnchor(UiNodeTree tree, UiNode node)
    {
        if (!TryGetRawAttribute(node, "$AnchorPoint", out string? anchorExpr)
            && !TryGetRawAttribute(node, "AnchorPoint", out anchorExpr))
            return;

        string[] parts = anchorExpr.Split(',');
        if (parts.Length != 2)
            return;

        int anchorX = EvaluateOrParse(tree, parts[0].Trim(), node);
        int anchorY = EvaluateOrParse(tree, parts[1].Trim(), node);
        TryGetRawAttribute(node, "$TextAnchor", out string? textAnchor);
        textAnchor ??= node.RawAttributes.GetValueOrDefault("TextAnchor") ?? "LEFT";

        int width = node.GetIntProp("Width");
        int height = node.GetIntProp("Height");
        int x = anchorX;
        int y = anchorY;

        switch (textAnchor.Trim().ToUpperInvariant())
        {
            case "RIGHT":
                x = anchorX - width;
                break;
            case "HORIZONTAL_CENTER":
                x = anchorX - width / 2;
                break;
            case "BOTTOM":
                y = anchorY - height;
                break;
            case "VERTICAL_CENTER":
                y = anchorY - height / 2;
                break;
        }

        node.Props["CanvasLeft"] = (double)x;
        node.Props["CanvasTop"] = (double)y;
    }

    private static bool TryGetRawAttribute(UiNode node, string key, out string value)
    {
        if (node.RawAttributes.TryGetValue(key, out value!))
            return true;

        value = string.Empty;
        return false;
    }

    public static bool NeedsExpression(string value)
        => value.Contains('(') || value.Contains('+') || value.Contains('-')
           || value.Contains('*') || value.Contains('/') || char.IsUpper(value.FirstOrDefault());
}
