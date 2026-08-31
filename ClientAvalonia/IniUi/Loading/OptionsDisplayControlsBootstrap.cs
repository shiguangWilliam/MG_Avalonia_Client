using ClientAvalonia.IniUi.Models;
using ClientCore.Extensions;
using System.Linq;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// XNA DisplayOptionsPanel creates resolution/renderer/theme dropdowns in code;
/// MG overlay INI only supplies labels/checkboxes — inject missing dd* nodes here.
/// </summary>
internal static class OptionsDisplayControlsBootstrap
{
    private sealed record DropdownDef(string Id, string Items, int DefaultIndex = 0);

    private static readonly DropdownDef[] DisplayDropdowns =
    [
        new("ddIngameResolution", "800x600,1024x768,1280x720,1280x800,1920x1080"),
        new("ddDetailLevel", "低,中,高", 1),
        new("ddClientResolution", "(default),1280x720,1280x800,1920x1080"),
        new("ddClientTheme", "Moment of Genesis,Default"),
        new("ddVisualStyle", "Classic, Tactical", 0),
    ];

    private static readonly (string Id, string DefaultEnglish, string L10NKey)[] DisplayCheckBoxLabels =
    [
        ("chkMEDDraw",
            "Enable DDWrapper for Map Editor",
            "Client:DTAConfig:MapEditorDDWrapper"),
    ];

    public static void Apply(UiNodeTree tree)
    {
        UiNode? panel = tree.FindNode("DisplayOptionsPanel");
        if (panel == null)
            return;

        EnsureLabel(panel, "lblVisualStyle", "视觉风格:");

        foreach (DropdownDef def in DisplayDropdowns)
        {
            UiNode? existing = tree.FindNode(def.Id);
            if (existing != null)
            {
                if (existing.Parent != panel)
                    AttachToPanel(panel, existing);

                // MG OptionsWindow.ini often defines ddClientResolution without Items=.
                if (!HasNonEmptyItems(existing))
                {
                    existing.Props["Items"] = def.Id.Equals("ddClientResolution", StringComparison.OrdinalIgnoreCase)
                        ? BuildClientResolutionItems()
                        : def.Items;
                    if (!existing.Props.ContainsKey("DefaultIndex"))
                        existing.Props["DefaultIndex"] = def.DefaultIndex;
                    if (!existing.Props.ContainsKey("SelectedIndex"))
                        existing.Props["SelectedIndex"] = def.DefaultIndex;
                }

                continue;
            }

            var node = new UiNode
            {
                Id = def.Id,
                ControlType = "XNAClientDropDown",
                TemplateKey = "DxComboBox",
                WindowName = "OptionsWindow",
                Parent = panel,
            };

            node.Props["Width"] = 228.0;
            node.Props["Height"] = 24.0;
            node.Props["Items"] = def.Id.Equals("ddClientResolution", StringComparison.OrdinalIgnoreCase)
                ? BuildClientResolutionItems()
                : def.Items;
            node.Props["DefaultIndex"] = def.DefaultIndex;
            node.Props["SelectedIndex"] = def.DefaultIndex;

            panel.Children.Add(node);
        }

        foreach ((string id, string defaultEnglish, string l10NKey) in DisplayCheckBoxLabels)
        {
            UiNode? node = tree.FindNode(id);
            if (node == null)
                continue;

            if (HasDisplayText(node))
                continue;

            node.Props["Text"] = defaultEnglish.L10N(l10NKey);
            if (!node.Props.ContainsKey("Width"))
                node.Props["Width"] = 320.0;
        }
    }

    /// <summary>
    /// DX DisplayOptionsPanel builds this from ScreenResolution; Avalonia fills a practical subset
    /// plus desktop size when available (no XNA GraphicsAdapter dependency).
    /// </summary>
    internal static string BuildClientResolutionItems()
    {
        string recommended = "(recommended)".L10N("Client:DTAConfig:Recommended");
        var resolutions = new SortedSet<string>(StringComparer.Ordinal);
        string[] candidates =
        [
            "1024x600", "1024x720", "1280x600", "1280x720", "1280x768", "1280x800",
            "1366x768", "1440x900", "1600x900", "1680x1050", "1920x1080", "2560x1440",
        ];

        (int deskW, int deskH) = TryGetDesktopSize();
        foreach (string candidate in candidates)
        {
            if (!TryParseResolution(candidate, out int w, out int h))
                continue;
            if (deskW > 0 && deskH > 0 && (w > deskW || h > deskH))
                continue;
            resolutions.Add(candidate);
        }

        if (deskW >= 800 && deskH >= 600)
            resolutions.Add($"{deskW}x{deskH}");

        if (resolutions.Count == 0)
            resolutions.Add("1280x720");

        return recommended + "," + string.Join(",", resolutions);
    }

    private static (int Width, int Height) TryGetDesktopSize()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Screens?.Primary is { } primary)
            {
                return (primary.Bounds.Width, primary.Bounds.Height);
            }
        }
        catch
        {
            // Headless / early bootstrap — fall back to candidates only.
        }

        return (0, 0);
    }

    private static bool TryParseResolution(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = text.Split('x', 'X');
        return parts.Length == 2
               && int.TryParse(parts[0], out width)
               && int.TryParse(parts[1], out height)
               && width > 0
               && height > 0;
    }

    private static bool HasNonEmptyItems(UiNode node)
    {
        if (!node.Props.TryGetValue("Items", out object? value))
            return false;
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    private static bool HasDisplayText(UiNode node)
    {
        if (!node.Props.TryGetValue("Text", out object? value))
            return false;
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    private static void AttachToPanel(UiNode panel, UiNode node)
    {
        if (node.Parent != null)
            node.Parent.Children.Remove(node);

        node.Parent = panel;
        if (!panel.Children.Contains(node))
            panel.Children.Add(node);
    }

    private static void EnsureLabel(UiNode panel, string id, string text)
    {
        UiNode? existing = panel.Children.FirstOrDefault(c =>
            c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!HasDisplayText(existing))
                existing.Props["Text"] = text;
            return;
        }

        var node = new UiNode
        {
            Id = id,
            ControlType = "XNALabel",
            TemplateKey = "DxLabel",
            WindowName = "OptionsWindow",
            Parent = panel,
        };
        node.Props["Text"] = text;
        node.Props["Width"] = 228.0;
        node.Props["Height"] = 20.0;
        panel.Children.Add(node);
    }
}
