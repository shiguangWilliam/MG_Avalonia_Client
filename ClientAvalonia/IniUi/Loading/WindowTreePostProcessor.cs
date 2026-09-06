using ClientAvalonia.IniUi;
using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Post-load fixes for official windows: hierarchy cleanup, dialog centering, options tabs.
/// Shell removal honors explicit INI declaration (Issue #18) before pixel heuristics.</summary>
public static class WindowTreePostProcessor
{
    // Pixel heuristics kept as-is for compatibility (DX baseline); window INI keys
    // ShellMaxWidthLobby / ShellMaxWidthWindow override them per window.
    private const int DefaultShellMaxWidthLobby = 400;
    private const int DefaultShellMaxWidthWindow = 200;

    public static void Apply(UiNodeTree tree, string windowSectionName, LayoutContext context, IReadOnlySet<string> overlaySections)
    {
        RemoveDuplicateRootNodes(tree);
        RemoveForeignWindowSections(tree, windowSectionName);

        if (windowSectionName.Equals(WindowKind.OptionsWindow, StringComparison.OrdinalIgnoreCase))
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
            if (IsForeignWindowShell(tree.Root, child, windowSectionName))
            {
                // Issue #18: every heuristic removal is now observable in
                // client.log (node id, size, reason) instead of silently
                // vanishing — modders can spot over-eager pruning immediately.
                Logger.Log(
                    $"WindowTreePostProcessor: removed foreign shell node '{child.Id}' " +
                    $"({child.GetIntProp("Width")}x{child.GetIntProp("Height")}) from '{windowSectionName}' " +
                    "(generic shell id / IsShell=false / pixel heuristic).");
                tree.Root.Children.RemoveAt(i);
            }
        }
    }

    private static bool IsForeignWindowShell(UiNode windowRoot, UiNode node, string activeWindow)
    {
        if (node.Id is "GenericWindow" or "GameLobbyBase" or "LoadingScreen")
            return true;

        // Issue #18: explicit INI declaration on the node wins over every heuristic.
        if (node.RawAttributes.TryGetValue("IsShell", out string? declared))
        {
            if (declared.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                return false;  // kept: this node IS the intended shell
            if (declared.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                return true;   // removed: author opted this node out
        }

        // Thresholds are per-WINDOW tuning knobs: read from the active window's
        // own section (e.g. [MyWindow] ShellMaxWidthWindow=500).
        int lobbyMax = ReadThreshold(windowRoot, "ShellMaxWidthLobby", DefaultShellMaxWidthLobby);
        int windowMax = ReadThreshold(windowRoot, "ShellMaxWidthWindow", DefaultShellMaxWidthWindow);

        if (node.Id.EndsWith("Lobby", StringComparison.OrdinalIgnoreCase)
            && !node.Id.Equals(activeWindow, StringComparison.OrdinalIgnoreCase)
            && node.TemplateKey == "DxPanel"
            && node.GetIntProp("Width") > lobbyMax)
            return true;

        if (node.Id.EndsWith("Window", StringComparison.OrdinalIgnoreCase)
            && !node.Id.Equals(activeWindow, StringComparison.OrdinalIgnoreCase)
            && node.GetIntProp("Width") > windowMax
            && !node.Id.StartsWith("btn", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>INI override (Issue #18): a bad/absent value falls back to the default.</summary>
    private static int ReadThreshold(UiNode node, string key, int fallback)
    {
        if (!node.RawAttributes.TryGetValue(key, out string? raw) || !int.TryParse(raw, out int parsed) || parsed <= 0)
            return fallback;

        return parsed;
    }
}
