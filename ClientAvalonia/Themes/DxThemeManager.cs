using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ClientCore;

namespace ClientAvalonia.Themes;

/// <summary>
/// Visual style switching: keeps Classic (DxOfficialTheme) untouched and layers the
/// Tactical token dictionary on top when requested. Also resolves the user-configurable
/// accent color and derives the inverse (hue-rotated 180°) counterpart brush.
/// </summary>
public static class DxThemeManager
{
    public const string StyleDefault = "Default";
    public const string StyleTactical = "Tactical";

    private const string BaseUri = "avares://ClientAvalonia/Themes/";

    private static string _currentStyle = StyleDefault;
    private static Color? _userAccent;

    public static string CurrentStyle => _currentStyle;

    public static bool IsTactical => _currentStyle.Equals(StyleTactical, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads persisted preferences and applies the theme dictionary at startup.
    /// Call once from App.Initialize after the base dictionaries are merged.
    /// </summary>
    public static void InitializeFromSettings()
    {
        string style = StyleDefault;
        Color? accent = null;

        try
        {
            var settings = UserINISettings.Instance;
            style = settings.VisualStyle?.Value ?? StyleDefault;
            accent = ParseAccentColor(settings.AccentColor?.Value);
        }
        catch (InvalidOperationException)
        {
            // UserINISettings not initialized (unit tests) — fall back to defaults.
        }

        _userAccent = accent;
        Apply(style, animate: false);
    }

    /// <summary>Swaps the last merged dictionary (the theme layer) and refreshes derived brushes.
    /// When <paramref name="animate"/> is set, callers wrap this with the transition scrim.</summary>
    public static void Apply(string style, bool animate = true)
    {
        var app = Application.Current;
        if (app?.Resources is null)
            return;

        style = NormalizeStyle(style);

        var dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri(BaseUri + (style == StyleTactical ? "DxTheme-Tactical.axaml" : "DxOfficialTheme.axaml")));

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            // The theme layer is always the last dictionary; replacing it refreshes
            // DynamicResource lookups across the whole visual tree.
            merged[^1] = dictionary;
        }

        _currentStyle = style;
        ApplyAccentOverrides(app.Resources);
    }

    /// <summary>
    /// Warms up everything the Tactical skin needs on a background thread: loads and
    /// parses the theme dictionary and the Tactical campaign template dictionary.
    /// Throws on failure so the caller can fall back to Classic.
    /// </summary>
    public static void PreloadTacticalAssets()
    {
        _ = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri(BaseUri + "DxTheme-Tactical.axaml"));

        _ = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri(BaseUri + "DxCampaignTacticalStyles.axaml"));
    }

    public static string NormalizeStyle(string style)
        => style.Equals(StyleTactical, StringComparison.OrdinalIgnoreCase) ? StyleTactical : StyleDefault;

    /// <summary>Sets a user accent (null = theme default) and refreshes derived brushes.</summary>
    public static void SetUserAccent(Color? accent, bool persist = true)
    {
        _userAccent = accent;
        ApplyAccentOverrides(Application.Current?.Resources);

        if (!persist)
            return;

        try
        {
            var settings = UserINISettings.Instance;
            settings.AccentColor.Value = accent is { } c ? ToHex(c) : string.Empty;
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static Color? CurrentUserAccent => _userAccent;

    private static void ApplyAccentOverrides(IResourceDictionary? resources)
    {
        if (resources is null)
            return;

        if (resources.TryGetResource("DxAccentPrimaryBrush", null, out object? primaryObj)
            && primaryObj is SolidColorBrush primary)
        {
            Color baseColor = _userAccent ?? primary.Color;

            resources["DxAccentPrimaryBrush"] = new SolidColorBrush(baseColor);
            resources["DxAccentInverseBrush"] = new SolidColorBrush(InvertAccent(baseColor));
            resources["DxAccentSoftBrush"] = new SolidColorBrush(Lighten(baseColor, 0.28));
            resources["DxAccentGlowBrush"] = new SolidColorBrush(Color.FromArgb(0x55, baseColor.R, baseColor.G, baseColor.B));

            // Compatibility keys used by classic campaign templates.
            resources["DxAccentBrush"] = new SolidColorBrush(baseColor);
            resources["DxCampaignBorderGlowBrush"] = new SolidColorBrush(Color.FromArgb(0x55, baseColor.R, baseColor.G, baseColor.B));
            resources["DxCampaignListSelectedBorderBrush"] = new SolidColorBrush(baseColor);
        }
    }

    /// <summary>Hue rotation by 180°; near-gray colors fall back to brightness inversion.</summary>
    public static Color InvertAccent(Color c)
    {
        (double h, double s, double v) = RgbToHsv(c.R, c.G, c.B);
        if (s < 0.15)
        {
            return Color.FromRgb((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));
        }

        return HsvToRgb((h + 180.0) % 360.0, s, v);
    }

    private static Color Lighten(Color c, double amount)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));
    }

    public static Color? ParseAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        hex = hex.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        return hex.Length switch
        {
            6 => Color.Parse("#" + hex),
            8 => Color.Parse("#" + hex),
            _ => null,
        };
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        double h;
        if (delta < 1e-9)
        {
            h = 0;
        }
        else if (max == rf)
        {
            h = 60.0 * (((gf - bf) / delta) % 6.0);
        }
        else if (max == gf)
        {
            h = 60.0 * (((bf - rf) / delta) + 2.0);
        }
        else
        {
            h = 60.0 * (((rf - gf) / delta) + 4.0);
        }

        if (h < 0)
            h += 360.0;

        double s = max < 1e-9 ? 0 : delta / max;
        return (h, s, max);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = v - c;

        (double rf, double gf, double bf) = (h / 60.0) switch
        {
            < 1.0 => (c, x, 0.0),
            < 2.0 => (x, c, 0.0),
            < 3.0 => (0.0, c, x),
            < 4.0 => (0.0, x, c),
            < 5.0 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Clamp((rf + m) * 255.0, 0, 255),
            (byte)Math.Clamp((gf + m) * 255.0, 0, 255),
            (byte)Math.Clamp((bf + m) * 255.0, 0, 255));
    }
}
