using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Themes;

/// <summary>
/// Generates the Tactical skin's procedural textures at runtime (no PNG assets):
/// razor-thin 1px borders, near-black surfaces, subtle scan lines and a crisp
/// accent notch. Colors are pulled from the live theme so a user-configured
/// accent recolors every generated texture.
/// </summary>
public static class TacticalAssetFactory
{
    private const int ButtonHeight = 34;

    private static readonly Dictionary<int, Bitmap> ButtonCache = new();
    private static Color _cachedAccent = Color.FromRgb(0x2E, 0xE6, 0xC5);

    public static int StandardButtonHeight => ButtonHeight;

    /// <summary>Idle/hover pair for a standard-width action button.</summary>
    public static (Bitmap Idle, Bitmap Hover) CreateButton(int width)
    {
        width = Math.Clamp(width, 60, 600);
        if (ButtonCache.TryGetValue(width, out Bitmap? cached))
            return (cached, cached);

        var idle = new RenderTargetBitmap(new PixelSize(width, ButtonHeight));
        using (DrawingContext ctx = idle.CreateDrawingContext())
        {
            DrawButtonBase(ctx, width, hover: false);
        }

        var hover = new RenderTargetBitmap(new PixelSize(width, ButtonHeight));
        using (DrawingContext ctx = hover.CreateDrawingContext())
        {
            DrawButtonBase(ctx, width, hover: true);
        }

        ButtonCache[width] = idle;
        ButtonCache[-width] = hover;
        return (idle, hover);
    }

    internal static Bitmap CreateButtonHover(int width)
    {
        width = Math.Clamp(width, 60, 600);
        if (ButtonCache.TryGetValue(-width, out Bitmap? cached))
            return cached;

        _ = CreateButton(width);
        return ButtonCache[-width];
    }

    public static Bitmap CreateCheckbox(bool isChecked)
    {
        const int size = 22;
        var bmp = new RenderTargetBitmap(new PixelSize(size, size));
        using DrawingContext ctx = bmp.CreateDrawingContext();
        Color accent = ResolveAccent();

        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10)), null, new Rect(0, 0, size, size));
        ctx.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x33, 0x3B)), 1),
            new Rect(0.5, 0.5, size - 1, size - 1));

        if (isChecked)
        {
            var check = new StreamGeometry();
            using (StreamGeometryContext gc = check.Open())
            {
                gc.BeginFigure(new Point(5, 11.5), false);
                gc.LineTo(new Point(9.5, 16));
                gc.LineTo(new Point(17, 6));
            }

            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.6, null, PenLineCap.Round), check);
        }

        return bmp;
    }

    /// <summary>
    /// Dark cold-surface window chrome texture with scan lines and an accent rail.
    /// Alpha 0xD8 (84%) lets the shared 3D solar-system backdrop show through
    /// behind INI window roots while keeping text contrast.
    /// </summary>
    public static Bitmap CreateWindowChrome(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bmp = new RenderTargetBitmap(new PixelSize(width, height));
        using DrawingContext ctx = bmp.CreateDrawingContext();
        Color accent = ResolveAccent();

        var baseBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xD8, 0x08, 0x0A, 0x0E), 0),
                new GradientStop(Color.FromArgb(0xD8, 0x0D, 0x11, 0x16), 0.5),
                new GradientStop(Color.FromArgb(0xD8, 0x07, 0x09, 0x0D), 1),
            },
        };
        ctx.DrawRectangle(baseBrush, null, new Rect(0, 0, width, height));

        // Sparse horizontal scan lines (1px every 6px, barely visible).
        var scanBrush = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
        for (double y = 5; y < height; y += 6)
            ctx.DrawRectangle(scanBrush, null, new Rect(0, y, width, 1));

        // Left edge accent rail (3px) — the signature cold tactical line.
        ctx.DrawRectangle(new SolidColorBrush(accent), null, new Rect(0, 0, 3, height));

        return bmp;
    }

    private static void DrawButtonBase(DrawingContext ctx, int width, bool hover)
    {
        Color accent = ResolveAccent();
        Color surface = hover ? Color.FromRgb(0x13, 0x17, 0x1D) : Color.FromRgb(0x0B, 0x0E, 0x12);
        Color line = hover ? accent : Color.FromRgb(0x2C, 0x33, 0x3B);

        var surfaceBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x10, 0x14, 0x1A), 0),
                new GradientStop(surface, 0.5),
                new GradientStop(Color.FromRgb(0x08, 0x0A, 0x0E), 1),
            },
        };
        ctx.DrawRectangle(surfaceBrush, null, new Rect(0, 0, width, ButtonHeight));

        // Hairline border on pixel centers for crisp 1px rendering.
        ctx.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(line), 1),
            new Rect(0.5, 0.5, width - 1, ButtonHeight - 1));

        // Top highlight: single translucent pixel row.
        ctx.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            null,
            new Rect(1, 1, width - 2, 1));

        // Accent notch: 12px segment on the top-left, 1px thick — machined detail.
        ctx.DrawRectangle(new SolidColorBrush(accent), null, new Rect(1, 0.5, 12, 1));

        // Faint diagonal hatch in the right quarter — tactical greeble, ~4% alpha.
        var hatch = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        for (int x = width - (width / 4); x < width - 2; x += 7)
        {
            var seg = new StreamGeometry();
            using (StreamGeometryContext gc = seg.Open())
            {
                gc.BeginFigure(new Point(x, 3), false);
                gc.LineTo(new Point(x - ButtonHeight + 7, ButtonHeight - 3));
            }

            ctx.DrawGeometry(null, new Pen(hatch, 1), seg);
        }
    }

    private static Color ResolveAccent()
    {
        if (Application.Current?.Resources.TryGetResource("DxAccentPrimaryBrush", null, out object? obj) == true
            && obj is SolidColorBrush scb)
        {
            _cachedAccent = scb.Color;
            return scb.Color;
        }

        return _cachedAccent;
    }

    /// <summary>Clears cached bitmaps; call after an accent change so textures regenerate.</summary>
    public static void InvalidateCache()
    {
        foreach (Bitmap bmp in ButtonCache.Values)
            bmp.Dispose();
        ButtonCache.Clear();
    }
}
