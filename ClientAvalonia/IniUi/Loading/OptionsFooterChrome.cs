using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore.Extensions;
using System.Linq;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// INI-independent footer labels/layout for OptionsWindow.
/// Layout mirrors DX OptionsWindow: Save bottom-left, Cancel bottom-right.
/// Classic chrome uses ThemeMG <c>button.png</c> (MG has no 92pxbtn.png).
/// </summary>
internal static class OptionsFooterChrome
{
    public const string SaveFallback = "保存";
    public const string CancelFallback = "取消";

    /// <summary>Classic button atlas present under ThemeMG (not 92pxbtn.png).</summary>
    public const string IdleTexture = "button.png";
    public const string HoverTexture = "button_c.png";

    public static string ResolveSaveText()
        => NonEmpty("保存".L10N("Client:Main:ButtonSave"), SaveFallback);

    public static string ResolveCancelText()
        => NonEmpty("取消".L10N("Client:Main:ButtonCancel"), CancelFallback);

    public static void ApplyToTree(UiNodeTree tree)
    {
        ApplyNodeText(tree.FindNode("btnSave"), ResolveSaveText());
        ApplyNodeText(tree.FindNode("btnCancel"), ResolveCancelText());
    }

    public static void ApplyToViewModel(UiNodeViewModel? root)
    {
        if (root == null)
            return;

        // Prefer root-level footer buttons — never a nested Campaign/other cancel.
        UiNodeViewModel? save = FindDirectChild(root, "btnSave") ?? Find(root, "btnSave");
        UiNodeViewModel? cancel = FindDirectChild(root, "btnCancel") ?? Find(root, "btnCancel");

        // Save = bottom-LEFT; Cancel = bottom-RIGHT (panel corner).
        PositionFooterButton(
            save,
            ResolveSaveText(),
            canvasLeft: 12.0,
            canvasTop: OptionsOverlayConstants.Height - 40);

        PositionFooterButton(
            cancel,
            ResolveCancelText(),
            canvasLeft: OptionsOverlayConstants.Width - 104,
            canvasTop: OptionsOverlayConstants.Height - 40);
    }

    private static void PositionFooterButton(
        UiNodeViewModel? button,
        string text,
        double canvasLeft,
        double canvasTop)
    {
        if (button == null)
            return;

        button.Node.Props["Text"] = text;
        button.SetDisplayText(text);
        button.IsVisible = true;
        button.Node.Props["Width"] = 92.0;
        button.Node.Props["Height"] = 32.0;
        button.Node.Props["CanvasLeft"] = canvasLeft;
        button.Node.Props["CanvasTop"] = canvasTop;
        button.Node.Props["ZIndex"] = 1000;
        button.Node.Props["IdleTexture"] = IdleTexture;
        button.Node.Props["HoverTexture"] = HoverTexture;
        button.RefreshLayout();
    }

    private static void ApplyNodeText(UiNode? node, string text)
    {
        if (node == null)
            return;

        node.Props["Text"] = text;
        node.Props["IsVisible"] = true;
        if (node.GetIntProp("Width") <= 0)
            node.Props["Width"] = 92.0;
        if (node.GetIntProp("Height") <= 0)
            node.Props["Height"] = 32.0;
        node.Props["IdleTexture"] = IdleTexture;
        node.Props["HoverTexture"] = HoverTexture;
        node.Props["ZIndex"] = 1000;
    }

    private static string NonEmpty(string candidate, string fallback)
        => string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;

    private static UiNodeViewModel? FindDirectChild(UiNodeViewModel root, string id)
        => root.Children.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static UiNodeViewModel? Find(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
