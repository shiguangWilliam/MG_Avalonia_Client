using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore.Extensions;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// INI-independent footer labels for OptionsWindow. Translation keys may exist but be empty —
/// always fall back to hard-coded Chinese so Save/Cancel never render blank.
/// </summary>
internal static class OptionsFooterChrome
{
    public const string SaveFallback = "保存";
    public const string CancelFallback = "取消";

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

        UiNodeViewModel? save = Find(root, "btnSave");
        UiNodeViewModel? cancel = Find(root, "btnCancel");
        if (save != null)
        {
            save.Node.Props["Text"] = ResolveSaveText();
            save.SetDisplayText(ResolveSaveText());
            save.IsVisible = true;
            save.Node.Props["Height"] = 32.0;
            save.RefreshLayout();
        }

        if (cancel != null)
        {
            cancel.Node.Props["Text"] = ResolveCancelText();
            cancel.SetDisplayText(ResolveCancelText());
            cancel.IsVisible = true;
            cancel.Node.Props["Width"] = 92.0;
            cancel.Node.Props["Height"] = 32.0;
            cancel.Node.Props["CanvasLeft"] = (double)(OptionsOverlayConstants.Width - 104);
            cancel.Node.Props["CanvasTop"] = (double)(OptionsOverlayConstants.Height - 40);
            cancel.RefreshLayout();
        }
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
    }

    private static string NonEmpty(string candidate, string fallback)
        => string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;

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
