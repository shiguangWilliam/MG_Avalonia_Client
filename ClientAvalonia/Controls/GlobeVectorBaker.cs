using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Platform;

namespace ClientAvalonia.Controls;

/// <summary>
/// Bakes a 2048x1024 equirectangular RGBA texture from the embedded Natural
/// Earth vector blob (Assets/Geo/world_geo.bin, version 2): even-odd scanline
/// fill of land rings, coastal edge detection, shelf glow, and country border
/// strokes in the holographic tactical palette. The bake runs once at startup
/// (WarmUp thread); u=(lon+180)/360, v=(90-lat)/180 matches the anchor and GL
/// sphere formulas, so lat/lon alignment is mathematically exact.
/// </summary>
internal static class GlobeVectorBaker
{
    private const int Width = 2048;
    private const int Height = 1024;
    private const uint Magic = 0x42474D47;

    // Holographic tactical palette (aligned with the GLM art direction).
    private const byte LandR = 0x2E, LandG = 0x87, LandB = 0x77;
    private const byte LandEdgeR = 0x5A, LandEdgeG = 0xF0, LandEdgeB = 0xD8;
    private const byte OceanR = 0x0A, OceanG = 0x14, OceanB = 0x20;
    private const byte ShelfR = 0x10, ShelfG = 0x24, ShelfB = 0x36;
    private const byte BorderR = 0x3E, BorderG = 0x8E, BorderB = 0x86;

    private static byte[]? _cached;

    // Issue #29: 并发 ??= 会让两线程同时进入 Bake，且 _rings/_ringVertexCounts/
    // _lines/_lineVertexCounts 四组静态几何字段整体写入无一致性保证（读到新旧混合
    // → 索引越界/陆地错填）。锁内单次烘焙 + 锁内读取，渲染线程只读结果。
    // 锁序约定：GlobeTextureBaker.Bake（持自身锁）会进入本方法——Texture 锁永远
    // 在 Vector 锁外层，单向依赖无死锁。
    private static readonly object BakeGate = new();

    /// <summary>Bakes and caches; safe to call from a background thread.</summary>
    public static bool TryGetPixels(out byte[] pixels, out int width, out int height)
    {
        lock (BakeGate)
        {
            pixels = _cached ??= Bake();
        }

        width = Width;
        height = Height;
        return pixels.Length == Width * Height * 4;
    }

    public static void WarmUp() => _ = TryGetPixels(out _, out _, out _);

    // Raw blob geometry: flat arrays of quantized (qLon, qLat) ushort pairs.
    private static ushort[][]? _rings;
    private static int[]? _ringVertexCounts;
    private static ushort[][]? _lines;
    private static int[]? _lineVertexCounts;

    private static double DecodeLon(ushort q) => q / 65535.0 * 360.0 - 180.0;
    private static double DecodeLat(ushort q) => q / 65535.0 * 180.0 - 90.0;

    private static byte[] Bake()
    {
        if (!TryLoadBlob())
            return Array.Empty<byte>();

        var px = new byte[Width * Height * 4];

        // ---- Pass 1: ocean base with subtle latitude banding ----
        for (int y = 0; y < Height; y++)
        {
            double lat = 90.0 - (y + 0.5) * 180.0 / Height;
            double band = 0.85 + 0.15 * Math.Cos(lat * Math.PI / 180.0);
            byte r = (byte)(OceanR * band);
            byte g = (byte)(OceanG * band);
            byte b = (byte)(OceanB * band);

            int row = y * Width * 4;
            for (int x = 0; x < Width; x++)
            {
                px[row + x * 4] = r;
                px[row + x * 4 + 1] = g;
                px[row + x * 4 + 2] = b;
                px[row + x * 4 + 3] = 255;
            }
        }

        // ---- Pass 2: land mask via even-odd scanline across all rings ----
        var land = new bool[Width, Height];
        var xs = new int[1024];
        for (int y = 0; y < Height; y++)
        {
            double lat = 90.0 - (y + 0.5) * 180.0 / Height;
            int count = 0;

            for (int ri = 0; ri < _rings!.Length; ri++)
            {
                int n = _ringVertexCounts![ri];
                if (n < 3)
                    continue;

                ushort[] ring = _rings[ri];
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double lat0 = DecodeLat(ring[2 * j + 1]);
                    double lat1 = DecodeLat(ring[2 * i + 1]);
                    // Skip horizontal edges; they never contribute a unique crossing
                    // and amplify vertex-on-scanline double-counts.
                    if (lat0 == lat1)
                        continue;
                    if (lat0 > lat == lat1 > lat)
                        continue;

                    double lon0 = DecodeLon(ring[2 * j]);
                    double lon1 = DecodeLon(ring[2 * i]);
                    // Short-arc unwrap so edges near ±180 do not lerp the long way.
                    double dlon = lon1 - lon0;
                    if (dlon > 180.0)
                        lon1 -= 360.0;
                    else if (dlon < -180.0)
                        lon1 += 360.0;

                    double lonAt = (lon1 - lon0) * (lat - lat0) / (lat1 - lat0) + lon0;
                    while (lonAt < -180.0)
                        lonAt += 360.0;
                    while (lonAt > 180.0)
                        lonAt -= 360.0;

                    // lon=+180 maps to exactly Width; clamp instead of dropping —
                    // dropping antimeridian crossings yields an odd count and
                    // shifts even-odd pairing, which carved the Arctic latitude gap.
                    int col = (int)Math.Floor((lonAt + 180.0) * Width / 360.0);
                    if (col < 0)
                        col = 0;
                    else if (col >= Width)
                        col = Width - 1;

                    if (count == xs.Length)
                        Array.Resize(ref xs, count * 2);
                    xs[count++] = col;
                }
            }

            Array.Sort(xs, 0, count);
            for (int k = 0; k + 1 < count; k += 2)
            {
                for (int x = xs[k]; x <= xs[k + 1]; x++)
                    land[x, y] = true;
            }
        }

        // ---- Pass 3: land coloring + coastal edge (1px erosion) ----
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (!land[x, y])
                    continue;

                bool edge = IsNearOcean(land, x, y, 1);
                int o = (row + x) * 4;
                if (edge)
                {
                    px[o] = LandEdgeR;
                    px[o + 1] = LandEdgeG;
                    px[o + 2] = LandEdgeB;
                }
                else
                {
                    px[o] = LandR;
                    px[o + 1] = LandG;
                    px[o + 2] = LandB;
                }
            }
        }

        // ---- Pass 4: shelf glow (3px proximity halo around land) ----
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (land[x, y])
                    continue;

                if (IsNearLand(land, x, y, 3))
                {
                    int o = (row + x) * 4;
                    px[o] = Math.Max(px[o], ShelfR);
                    px[o + 1] = Math.Max(px[o + 1], ShelfG);
                    px[o + 2] = Math.Max(px[o + 2], ShelfB);
                }
            }
        }

        // ---- Pass 5: country border strokes ----
        // Disabled with GlobeBorderLibrary.CountryBordersEnabled = false: the
        // current border geometry is wrong (see that constant for details).
        if (GlobeBorderLibrary.CountryBordersEnabled)
        {
            for (int li = 0; li < _lines!.Length; li++)
            {
                int n = _lineVertexCounts![li];
                ushort[] line = _lines[li];
                for (int i = 0; i + 1 < n; i++)
                {
                    StrokeSegment(
                        px,
                        DecodeLon(line[2 * i]), DecodeLat(line[2 * i + 1]),
                        DecodeLon(line[2 * (i + 1)]), DecodeLat(line[2 * (i + 1) + 1]));
                }
            }
        }

        return px;
    }

    private static bool IsNearOcean(bool[,] land, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= Height)
                continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int xx = x + dx;
                if (xx < 0 || xx >= Width)
                    return true; // texture edge counts as ocean side
                if (!land[xx, yy])
                    return true;
            }
        }

        return false;
    }

    private static bool IsNearLand(bool[,] land, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= Height)
                continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int xx = x + dx;
                if (xx < 0 || xx >= Width)
                    continue;
                if (land[xx, yy])
                    return true;
            }
        }

        return false;
    }

    private static void StrokeSegment(byte[] px, double lon0, double lat0, double lon1, double lat1)
    {
        int x0 = (int)Math.Round((lon0 + 180.0) * Width / 360.0);
        int y0 = (int)Math.Round((90.0 - lat0) * Height / 180.0);
        int x1 = (int)Math.Round((lon1 + 180.0) * Width / 360.0);
        int y1 = (int)Math.Round((90.0 - lat1) * Height / 180.0);
        x0 = Math.Clamp(x0, 0, Width - 1);
        x1 = Math.Clamp(x1, 0, Width - 1);
        y0 = Math.Clamp(y0, 0, Height - 1);
        y1 = Math.Clamp(y1, 0, Height - 1);

        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            Plot(px, x0, y0);
            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void Plot(byte[] px, int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        int o = (y * Width + x) * 4;
        px[o] = BorderR;
        px[o + 1] = BorderG;
        px[o + 2] = BorderB;
    }

    private static bool TryLoadBlob()
    {
        try
        {
            var uri = new Uri("avares://ClientAvalonia/Assets/Geo/world_geo.bin");
            if (!AssetLoader.Exists(uri))
                return false;

            using var stream = AssetLoader.Open(uri);
            using var br = new BinaryReader(stream);

            if (br.ReadUInt32() != Magic || br.ReadUInt32() != 2)
                return false;

            int ringTotal = br.ReadInt32();
            _rings = new ushort[ringTotal][];
            _ringVertexCounts = new int[ringTotal];
            for (int i = 0; i < ringTotal; i++)
            {
                int n = br.ReadInt32();
                var flat = new ushort[n * 2];
                Buffer.BlockCopy(br.ReadBytes(n * 4), 0, flat, 0, n * 4);
                _rings[i] = flat;
                _ringVertexCounts[i] = n;
            }

            int lineTotal = br.ReadInt32();
            _lines = new ushort[lineTotal][];
            _lineVertexCounts = new int[lineTotal];
            for (int i = 0; i < lineTotal; i++)
            {
                int n = br.ReadInt32();
                var flat = new ushort[n * 2];
                Buffer.BlockCopy(br.ReadBytes(n * 4), 0, flat, 0, n * 4);
                _lines[i] = flat;
                _lineVertexCounts[i] = n;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
