using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Derives Width/Height from textures and text before DistanceFromRightBorder layout.</summary>
public sealed class MeasurePass
{
    private const double DefaultLabelFontSize = 12;
    private const double LatinCharWidth = 7.2;
    private const double CjkCharWidth = 13.5;
    private const int CheckBoxGlyphWidth = 30;

    private readonly ResourceResolver _resources;

    public MeasurePass(ResourceResolver resources) => _resources = resources;

    public void Apply(UiNodeTree tree)
    {
        foreach (UiNode node in tree.AllNodes())
            MeasureNode(node);
    }

    private void MeasureNode(UiNode node)
    {
        bool hasWidth = node.GetIntProp("Width") > 0;
        bool hasHeight = node.GetIntProp("Height") > 0;

        if (!hasWidth || !hasHeight)
        {
            (int w, int h)? textureSize = TryMeasureTexture(node);
            if (textureSize != null)
            {
                if (!hasWidth)
                    node.Props["Width"] = (double)textureSize.Value.w;
                if (!hasHeight)
                    node.Props["Height"] = (double)textureSize.Value.h;
            }
            else
            {
                ApplyTextureFallback(node);
                ApplyStandardButtonMeasure(node);
            }
        }

        if (node.Props.TryGetValue("Text", out object? textObj) && textObj is string text && text.Length > 0)
        {
            int fontSize = node.Props.TryGetValue("FontSize", out object? fs) && fs is int fi ? fi : (int)DefaultLabelFontSize;
            int maxWrapWidth = GetMaxWrapWidth(node);
            int estimatedWidth = EstimateTextWidth(text, fontSize, maxWrapWidth);
            if (IsCheckBoxLike(node))
                estimatedWidth += CheckBoxGlyphWidth;

            int currentWidth = node.GetIntProp("Width");
            if (currentWidth == 0 || currentWidth < estimatedWidth)
                node.Props["Width"] = (double)estimatedWidth;

            int lineCount = EstimateLineCount(text, fontSize, Math.Max(node.GetIntProp("Width") - (IsCheckBoxLike(node) ? CheckBoxGlyphWidth : 0), 80));
            double lineHeight = fontSize + 6;
            int estimatedHeight = (int)Math.Ceiling(lineCount * lineHeight) + (IsCheckBoxLike(node) ? 6 : 4);
            int currentHeight = node.GetIntProp("Height");
            if (currentHeight == 0 || currentHeight < estimatedHeight)
                node.Props["Height"] = (double)estimatedHeight;
        }
        else if (node.GetIntProp("Height") == 0)
        {
            if (IsCheckBoxLike(node))
                node.Props["Height"] = 24.0;
        }

        if (IsCheckBoxLike(node))
        {
            if (node.GetIntProp("Width") < CheckBoxGlyphWidth + 40)
                node.Props["Width"] = (double)Math.Max(node.GetIntProp("Width"), CheckBoxGlyphWidth + 40);
            if (node.GetIntProp("Height") < 22)
                node.Props["Height"] = 22.0;
        }

        if (IsLabelLike(node) && node.GetIntProp("Width") > 0 && node.Props.TryGetValue("Text", out object? labelText)
            && labelText is string label && label.Length > 0)
        {
            int fontSize = node.Props.TryGetValue("FontSize", out object? fs) && fs is int fi ? fi : (int)DefaultLabelFontSize;
            int needed = EstimateTextWidth(label, fontSize, GetMaxWrapWidth(node));
            if (needed > node.GetIntProp("Width"))
                node.Props["Width"] = (double)needed;
        }
    }

    private static bool IsLabelLike(UiNode node)
        => node.TemplateKey == "DxLabel" || node.Id.StartsWith("lbl", StringComparison.OrdinalIgnoreCase);

    private static int GetMaxWrapWidth(UiNode node)
    {
        int parentW = node.Parent?.GetIntProp("Width") ?? 0;
        if (parentW <= 0)
            return 520;

        int x = node.GetIntProp("CanvasLeft");
        return Math.Max(120, parentW - x - 16);
    }

    private static int EstimateTextWidth(string text, int fontSize, int maxWrapWidth)
    {
        double scale = fontSize / DefaultLabelFontSize;
        string longestLine = text.Split('\n').DefaultIfEmpty(string.Empty).MaxBy(EstimateLineWidth)!;
        int lineWidth = (int)Math.Ceiling(EstimateLineWidth(longestLine) * scale) + 8;
        return Math.Min(lineWidth, maxWrapWidth);

        double EstimateLineWidth(string line)
        {
            double w = 0;
            foreach (char c in line)
                w += IsCjk(c) ? CjkCharWidth : LatinCharWidth;
            return w;
        }
    }

    private static int EstimateLineCount(string text, int fontSize, int wrapWidth)
    {
        double scale = fontSize / DefaultLabelFontSize;
        int totalLines = 0;
        foreach (string paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                totalLines++;
                continue;
            }

            double lineWidth = 0;
            int lines = 1;
            foreach (char c in paragraph)
            {
                double charW = (IsCjk(c) ? CjkCharWidth : LatinCharWidth) * scale;
                if (lineWidth + charW > wrapWidth && lineWidth > 0)
                {
                    lines++;
                    lineWidth = charW;
                }
                else
                {
                    lineWidth += charW;
                }
            }

            totalLines += lines;
        }

        return Math.Max(1, totalLines);
    }

    private static bool IsCjk(char c)
        => c >= 0x3000 && c <= 0x9FFF;

    private static bool IsCheckBoxLike(UiNode node)
        => node.ControlType.Contains("CheckBox", StringComparison.OrdinalIgnoreCase)
           || node.Id.StartsWith("chk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Aligns with XNAClientButton.Initialize when IdleTexture is absent from INI.</summary>
    private void ApplyStandardButtonMeasure(UiNode node)
    {
        if (!IsButtonLike(node) || node.Props.ContainsKey("IdleTexture"))
            return;

        int preferredWidth = node.GetIntProp("Width");
        foreach (int width in EnumerateButtonWidths(preferredWidth))
        {
            (int Width, int Height)? size = _resources.GetTextureSize($"{width}pxbtn.png");
            if (size == null)
                continue;

            if (node.GetIntProp("Width") == 0)
                node.Props["Width"] = (double)size.Value.Width;
            if (node.GetIntProp("Height") == 0)
                node.Props["Height"] = (double)size.Value.Height;
            return;
        }
    }

    private static IEnumerable<int> EnumerateButtonWidths(int preferredWidth)
    {
        if (preferredWidth > 0)
            yield return preferredWidth;

        foreach (int width in new[] { 147, 160, 133, 142, 121, 110, 97, 92, 75 })
        {
            if (width != preferredWidth)
                yield return width;
        }
    }

    private static bool IsButtonLike(UiNode node)
        => node.ControlType.Contains("Button", StringComparison.OrdinalIgnoreCase)
           || node.Id.StartsWith("btn", StringComparison.OrdinalIgnoreCase);

    /// <summary>MainMenu button row spacing is 54px when PNG assets are not present locally.</summary>
    private static void ApplyTextureFallback(UiNode node)
    {
        if (!node.Props.ContainsKey("IdleTexture"))
            return;

        if (node.GetIntProp("Width") == 0)
            node.Props["Width"] = 200;
        if (node.GetIntProp("Height") == 0)
            node.Props["Height"] = 54;
    }

    private (int, int)? TryMeasureTexture(UiNode node)
    {
        foreach (string key in new[] { "IdleTexture", "BackgroundTexture", "Background", "HoverTexture" })
        {
            if (!node.Props.TryGetValue(key, out object? val) || val is not string path)
                continue;

            (int Width, int Height)? size = _resources.GetTextureSize(path);
            if (size != null)
                return size;
        }

        return null;
    }
}
