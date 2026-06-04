using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Post-load fixes for official windows: hierarchy cleanup, dialog centering, options tabs.</summary>
public static class WindowTreePostProcessor
{
    public static void Apply(UiNodeTree tree, string windowSectionName, LayoutContext context, IReadOnlySet<string> overlaySections)
    {
        RemoveDuplicateRootNodes(tree);
        RemoveForeignWindowSections(tree, windowSectionName);

        if (windowSectionName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            OptionsWindowLayout.Apply(tree, context);

        if (windowSectionName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            LobbyLayout.ApplyPanelVisibility(tree);
    }

    private static void RemoveDuplicateRootNodes(UiNodeTree tree)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = tree.Root.Children.Count - 1; i >= 0; i--)
        {
            UiNode child = tree.Root.Children[i];
            if (!seen.Add(child.Id))
                tree.Root.Children.RemoveAt(i);
        }
    }

    private static void RemoveForeignWindowSections(UiNodeTree tree, string windowSectionName)
    {
        for (int i = tree.Root.Children.Count - 1; i >= 0; i--)
        {
            UiNode child = tree.Root.Children[i];
            if (IsForeignWindowShell(child, windowSectionName))
                tree.Root.Children.RemoveAt(i);
        }
    }

    private static bool IsForeignWindowShell(UiNode node, string activeWindow)
    {
        if (node.Id is "GenericWindow" or "GameLobbyBase" or "LoadingScreen")
            return true;

        if (node.Id.EndsWith("Lobby", StringComparison.OrdinalIgnoreCase)
            && !node.Id.Equals(activeWindow, StringComparison.OrdinalIgnoreCase)
            && node.TemplateKey == "DxPanel"
            && node.GetIntProp("Width") > 400)
            return true;

        if (node.Id.EndsWith("Window", StringComparison.OrdinalIgnoreCase)
            && !node.Id.Equals(activeWindow, StringComparison.OrdinalIgnoreCase)
            && node.GetIntProp("Width") > 200
            && !node.Id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
