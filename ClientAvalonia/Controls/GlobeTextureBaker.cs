using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using ClientAvalonia.Assets;

namespace ClientAvalonia.Controls;

/// <summary>
/// Equirectangular albedo source for TacticalGlobeView. Prefers the GLM-Image
/// world_map asset (real continent shapes) and falls back to a procedural bake
/// from ContinentOutlines when the asset is missing. Pure managed BGRA array —
/// no thread affinity; WarmUp is safe on a background thread.
/// </summary>
internal static class GlobeTextureBaker
{
    // Defaults used by the procedural fallback; overwritten when an AI map loads.
    public static int Width { get; private set; } = 1024;
    public static int Height { get; private set; } = 512;

    private static uint[]? _albedo;

    public static uint[] Albedo => _albedo ??= Bake();

    public static void WarmUp() => _ = Albedo;

    private static uint[] Bake()
    {
        uint[]? fromAsset = TryLoadWorldMap();
        if (fromAsset != null)
            return fromAsset;

        return BakeProcedural();
    }

    private static uint[]? TryLoadWorldMap()
    {
        Bitmap? bmp = GlmAssets.WorldMap;
        if (bmp is null)
            return null;

        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;
        if (w < 64 || h < 32)
            return null;

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

        var pixels = new uint[w * h];
        for (int i = 0; i < pixels.Length; i++)
        {
            int o = i * 4;
            // CopyPixels returns B,G,R,A — pack to match PackBgra (A B G R as uint).
            byte b = bgra[o], g = bgra[o + 1], r = bgra[o + 2], a = bgra[o + 3];
            pixels[i] = (uint)(a << 24 | b << 16 | g << 8 | r);
        }

        Width = w;
        Height = h;
        return pixels;
    }

    private static uint[] BakeProcedural()
    {
        Width = 1024;
        Height = 512;
        bool[,] land = BuildLandMask();
        float[,] coast = ComputeCoastDistance(land);

        var pixels = new uint[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            double lat = 90.0 - (y + 0.5) * 180.0 / Height;
            int rowBase = y * Width;
            for (int x = 0; x < Width; x++)
            {
                pixels[rowBase + x] = land[x, y]
                    ? LandPixel(lat, -180.0 + (x + 0.5) * 360.0 / Width, coast[x, y], x, y)
                    : OceanPixel(lat, coast[x, y]);
            }
        }

        return pixels;
    }

    // ---- Mask ----

    private static bool[,] BuildLandMask()
    {
        var land = new bool[Width, Height];
        foreach (double[] outline in ContinentOutlines.All)
        {
            for (int y = 0; y < Height; y++)
            {
                double lat = 90.0 - (y + 0.5) * 180.0 / Height;
                foreach (int lonIdx in ScanlineCrossings(lat, outline))
                    land[lonIdx, y] = true;
            }
        }

        return land;
    }

    private static IEnumerable<int> ScanlineCrossings(double lat, double[] poly)
    {
        var xs = new List<int>(16);
        int n = poly.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double y1 = poly[2 * i], x1 = poly[2 * i + 1];
            double y2 = poly[2 * j], x2 = poly[2 * j + 1];
            if ((y1 > lat) != (y2 > lat))
            {
                double lonAt = (x2 - x1) * (lat - y1) / (y2 - y1) + x1;
                int col = (int)Math.Floor((lonAt + 180.0) * Width / 360.0);
                if (col >= 0 && col < Width)
                    xs.Add(col);
            }
        }

        xs.Sort();
        for (int i = 0; i + 1 < xs.Count; i += 2)
        {
            for (int c = xs[i]; c <= xs[i + 1]; c++)
                yield return c;
        }
    }

    private static float[,] ComputeCoastDistance(bool[,] land)
    {
        int w = Width, h = Height;
        var d = new float[w, h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            d[x, y] = land[x, y] ? float.MaxValue : 0f;

        const float orth = 1f, diag = 1.414f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float m = d[x, y];
            if (x > 0) m = Math.Min(m, d[x - 1, y] + orth);
            if (y > 0) m = Math.Min(m, d[x, y - 1] + orth);
            if (x > 0 && y > 0) m = Math.Min(m, d[x - 1, y - 1] + diag);
            if (x < w - 1 && y > 0) m = Math.Min(m, d[x + 1, y - 1] + diag);
            d[x, y] = m;
        }
        for (int y = h - 1; y >= 0; y--)
        for (int x = w - 1; x >= 0; x--)
        {
            float m = d[x, y];
            if (x < w - 1) m = Math.Min(m, d[x + 1, y] + orth);
            if (y < h - 1) m = Math.Min(m, d[x, y + 1] + orth);
            if (x < w - 1 && y < h - 1) m = Math.Min(m, d[x + 1, y + 1] + diag);
            if (x > 0 && y < h - 1) m = Math.Min(m, d[x - 1, y + 1] + diag);
            d[x, y] = m;
        }
        return d;
    }

    // ---- Palette (cold-steel tactical fallback) ----

    private static uint LandPixel(double lat, double lon, float coast, int x, int y)
    {
        double relief = (Fbm(x * 0.018, y * 0.018) - 0.5) * 26.0;
        relief += MountainBelt(lat, lon) * 18.0;

        double ice = Math.Clamp((Math.Abs(lat) - 60.0) / 18.0, 0.0, 1.0);
        double iceCurve = ice * ice * (3 - 2 * ice);

        double r = 44 + relief + iceCurve * 168;
        double g = 54 + relief + iceCurve * 172;
        double b = 50 + relief + iceCurve * 175;

        if (coast < 3)
        {
            r *= 0.72; g *= 0.72; b *= 0.72;
        }

        return PackBgra(r, g, b);
    }

    private static uint OceanPixel(double lat, float coast)
    {
        double shelf = Math.Clamp(coast / 16.0, 0.0, 1.0);
        double polarDim = 1.0 - 0.25 * Math.Clamp((Math.Abs(lat) - 55) / 35.0, 0, 1);

        double r = (12 + 24 * (1 - shelf)) * polarDim;
        double g = (22 + 36 * (1 - shelf)) * polarDim;
        double b = (34 + 42 * (1 - shelf)) * polarDim;
        return PackBgra(r, g, b);
    }

    private static uint PackBgra(double r, double g, double b)
        => 0xFFu << 24
           | (uint)(Math.Clamp((int)b, 0, 255) << 16)
           | (uint)(Math.Clamp((int)g, 0, 255) << 8)
           | (uint)Math.Clamp((int)r, 0, 255);

    private static double MountainBelt(double lat, double lon)
    {
        double himalaya = Gauss(lat, 32, 9) * Band(lon, 70, 100);
        double andes = Gauss(lat, -18, 16) * Band(lon, -75, -66);
        double rockies = Gauss(lat, 44, 8) * Band(lon, -118, -104);
        double alps = Gauss(lat, 46, 4) * Band(lon, 6, 16);
        return Math.Min(1.0, himalaya + andes + rockies + alps);
    }

    private static double Band(double v, double min, double max)
        => v >= min && v <= max ? 1.0 : 0.0;

    private static double Gauss(double v, double mean, double sigma)
        => Math.Exp(-((v - mean) * (v - mean)) / (2 * sigma * sigma));

    private static double Fbm(double x, double y)
    {
        double sum = 0, amp = 0.5, f = 1;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * ValueNoise(x * f, y * f);
            amp *= 0.5;
            f *= 2;
        }
        return sum;
    }

    private static double ValueNoise(double x, double y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double tx = x - xi, ty = y - yi;
        double a = Hash2(xi, yi), b = Hash2(xi + 1, yi);
        double c = Hash2(xi, yi + 1), d = Hash2(xi + 1, yi + 1);
        double ux = tx * tx * (3 - 2 * tx), uy = ty * ty * (3 - 2 * ty);
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Hash2(int x, int y)
    {
        uint h = Hash(x, y);
        return (h & 0xFFFFFF) / 16777216.0;
    }

    private static uint Hash(int x, int y)
    {
        uint n = (uint)(x * 374761393 + y * 668265263);
        n = (n ^ (n >> 13)) * 1274126177;
        return n ^ (n >> 16);
    }
}
