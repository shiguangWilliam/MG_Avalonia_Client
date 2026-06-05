using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Overlays;

/// <summary>
/// Opens the CnCNet create-game dialog: dedicated INI first, GenericWindow.ini section second,
/// programmatic UI last (XNA builds GameCreationWindow in code when INI has no controls).
/// </summary>
public static class GameCreationOverlayHost
{
    public const string WindowName = "GameCreationWindow";

    public const int FallbackWidth = 560;

    public const int FallbackHeight = 380;

    public sealed class OpenResult
    {
        public required bool Success { get; init; }

        public required string Source { get; init; }

        public string? Message { get; init; }

        public int Width { get; init; } = FallbackWidth;

        public int Height { get; init; } = FallbackHeight;
    }

    public static OpenResult TryResolveLayout(ClientEnvironment environment)
    {
        if (environment.TryResolveOverlaySection(WindowName, WindowName) is not { } resolved)
        {
            return new OpenResult
            {
                Success = true,
                Source = "programmatic-default",
                Width = FallbackWidth,
                Height = FallbackHeight,
            };
        }

        (int width, int height) = FloatingOverlayLayout.ResolveOverlaySize(resolved.IniPath, resolved.Section);
        return new OpenResult
        {
            Success = true,
            Source = resolved.IniPath.Contains("GenericWindow", StringComparison.OrdinalIgnoreCase)
                ? "GenericWindow.ini"
                : $"{WindowName}.ini",
            Width = width,
            Height = height,
        };
    }

    /// <returns>INI tree when the section defines interactive controls; otherwise null.</returns>
    public static UiNodeViewModel? TryBuildIniOverlay(
        ClientEnvironment environment,
        BehaviorRegistry behaviors,
        IUiNavigationHost host,
        out (string IniPath, string Section)? resolved,
        out string? failureReason)
    {
        resolved = null;
        failureReason = null;

        if (environment.TryResolveOverlaySection(WindowName, WindowName) is not { } match)
        {
            failureReason = "No GameCreationWindow.ini or GenericWindow.ini section.";
            return null;
        }

        resolved = match;

        try
        {
            LayoutEngine engine = LayoutEngine.CreateForWindow(environment, match.IniPath, match.Section);
            UiNodeTree tree = engine.LoadWindow(match.IniPath, match.Section);
            if (!HasInteractiveControls(tree))
            {
                failureReason = "INI section has no create-game controls (size/background only).";
                return null;
            }

            var factory = new UiViewModelFactory(engine.Resources, behaviors);
            UiNodeViewModel root = factory.CreateTree(tree);
            IniBehaviorApplier.Apply(root, behaviors, host);
            GameCreationIniOverlayBehaviors.Register(behaviors, host, root);
            return root;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return null;
        }
    }

    private static bool HasInteractiveControls(UiNodeTree tree)
    {
        foreach (UiNode node in tree.AllNodes())
        {
            string id = node.Id.ToLowerInvariant();
            if (id is "btncreategame" or "btnnewgame" or "btncancel" or "tbgamename" or "tbroomname")
                return true;
        }

        return false;
    }
}
