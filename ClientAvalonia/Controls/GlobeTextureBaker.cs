using System;
using Avalonia;
using Avalonia.Media.Imaging;
using ClientAvalonia.Assets;
using Rampastring.Tools;

namespace ClientAvalonia.Controls;

/// <summary>
/// Equirectangular RGBA pixel source for the OpenGL globe texture, with a
/// three-tier strategy (first hit wins):
///   1. GlobeVectorBaker — Natural Earth vector bake, mathematically exact
///      lat/lon alignment (default; see GlobeStyle setting).
///   2. GLM-Image world_map asset (holographic AI art).
///   3. Minimal procedural planet fallback.
/// Pixels are produced once and uploaded a single time in OnOpenGlInit —
/// no per-frame work.
/// </summary>
internal static class GlobeTextureBaker
{
    private static byte[]? _rgba;
    private static int _width;
    private static int _height;

    public static void WarmUp() => _ = Pixels;

    private static byte[] Pixels => _rgba ??= Bake();

    /// <summary>RGBA8 pixels ready for glTexImage2D; always non-empty.</summary>
    public static bool TryGetPixels(out byte[] pixels, out int width, out int height)
    {
        WarmUp();
        pixels = _rgba ?? Array.Empty<byte>();
        width = _width;
        height = _height;
        return pixels.Length > 0;
    }

    private static byte[] Bake()
    {
        string style = "Vector";
        try
        {
            style = ClientCore.UserINISettings.Instance.GlobeStyle.Value;
        }
        catch (InvalidOperationException)
        {
            // Settings not initialized (unit tests) — default to vector.
        }

        if (style.Equals("Art", StringComparison.OrdinalIgnoreCase))
        {
            byte[]? art = TryLoadWorldMap();
            if (art != null)
            {
                Logger.Log($"GlobeTextureBaker: using AI art map ({_width}x{_height}).");
                return art;
            }

            Logger.Log("GlobeTextureBaker: AI art map missing; falling back to vector.");
        }

        if (GlobeVectorBaker.TryGetPixels(out byte[] vector, out int vw, out int vh))
        {
            _width = vw;
            _height = vh;
            Logger.Log($"GlobeTextureBaker: vector bake ready ({vw}x{vh}).");
            return vector;
        }

        Logger.Log("GlobeTextureBaker: vector blob unavailable; falling back to AI art.");
        byte[]? fallbackArt = TryLoadWorldMap();
        if (fallbackArt != null)
            return fallbackArt;

        return BakeProceduralFallback();
    }

    private static byte[]? TryLoadWorldMap()
    {
        Bitmap? bmp = GlmAssets.WorldMap;
        if (bmp is null)
            return null;

        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;
        if (w < 64 || h < 32)
            return null;

        // CopyPixels yields B,G,R,A per pixel; GL wants R,G,B,A.
        var bgra = new byte[w * h * 4];
        try
        {
            unsafe
            {
                fixed (byte* p = bgra)
                {
                    bmp.CopyPixels(new PixelRect(0, 0, w, h), (nint)p, bgra.Length, w * 4);
                }
            }
        }
        catch
        {
            return null;
        }

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        _width = w;
        _height = h;
        return rgba;
    }

    /// <summary>
    /// Placeholder planet (deep ocean gradient + polar caps) used only when
    /// both the vector blob and the art asset are unavailable.
    /// </summary>
    private static byte[] BakeProceduralFallback()
    {
        const int w = 512;
        const int h = 256;
        _width = w;
        _height = h;

        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            double lat = 90.0 - (y + 0.5) * 180.0 / h;
            double band = Math.Cos(lat * Math.PI / 180.0);
            double ice = Math.Clamp((Math.Abs(lat) - 62.0) / 14.0, 0.0, 1.0);
            double iceCurve = ice * ice * (3.0 - 2.0 * ice);

            byte r = (byte)Math.Clamp(10 + 26 * band + iceCurve * 150, 0, 255);
            byte g = (byte)Math.Clamp(20 + 38 * band + iceCurve * 158, 0, 255);
            byte b = (byte)Math.Clamp(32 + 46 * band + iceCurve * 160, 0, 255);

            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = row + x * 4;
                px[o] = r;
                px[o + 1] = g;
                px[o + 2] = b;
                px[o + 3] = 255;
            }
        }

        Logger.Log($"GlobeTextureBaker: procedural fallback ({w}x{h}).");
        return px;
    }
}
