using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Platform;
using Rampastring.Tools;

namespace ClientAvalonia.Controls;

/// <summary>
/// Parses the embedded GBCB country-outline blob (Assets/Geo/country_borders.bin)
/// into per-country polyline rings for the F2 border-highlight GL layer. Codes
/// are ISO 3166-1 alpha-2 when Natural Earth carries one ("IT"), alpha-3
/// otherwise ("KOS"); storage is 3 chars space-padded. Parsing runs once on a
/// background thread (PreloadTacticalAssets); any failure disables F2 silently.
/// </summary>
internal static class GlobeBorderLibrary
{
    private const uint Magic = 0x42434247; // 'G','B','C','B'
    private const uint Version = 1;

    /// <summary>
    /// F2 master switch, disabled 2026-08-16: the current border geometry is
    /// visually wrong, so neither the GL highlight layer nor the texture-bake
    /// border pass may draw. The whole pipeline (blob, shaders, INI bindings)
    /// stays in place; flip to true once the data is fixed to re-enable.
    /// </summary>
    internal const bool CountryBordersEnabled = false;

    private static Dictionary<string, ushort[][]>? _byCountry;
    private static readonly object Gate = new();

    /// <summary>True when the blob parsed and at least one country is available.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _byCountry is { Count: > 0 };
        }
    }

    public static void WarmUp() => _ = IsAvailable;

    /// <summary>
    /// Outline rings for a country code (2- or 3-letter, case-insensitive).
    /// Null when the library or the specific country is unavailable.
    /// Each ring is a flat (qLon, qLat) ushort array.
    /// </summary>
    public static ushort[][]? TryGetRings(string? code)
    {
        if (!CountryBordersEnabled || string.IsNullOrWhiteSpace(code))
            return null;

        EnsureLoaded();
        if (_byCountry is null || _byCountry.Count == 0)
            return null;

        string key = code.Trim().ToUpperInvariant().PadRight(3);
        return _byCountry.TryGetValue(key, out ushort[][]? rings) ? rings : null;
    }

    private static void EnsureLoaded()
    {
        if (_byCountry != null)
            return;

        lock (Gate)
        {
            if (_byCountry != null)
                return;

            _byCountry = Parse() ?? new Dictionary<string, ushort[][]>();
        }
    }

    private static Dictionary<string, ushort[][]>? Parse()
    {
        try
        {
            var uri = new Uri("avares://ClientAvalonia/Assets/Geo/country_borders.bin");
            if (!AssetLoader.Exists(uri))
            {
                Logger.Log("GlobeBorderLibrary: country_borders.bin missing; F2 disabled.");
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            return ParseStream(stream);
        }
        catch (Exception ex)
        {
            Logger.Log($"GlobeBorderLibrary: parse failed ({ex.Message}); F2 disabled.");
            return null;
        }
    }

    /// <summary>Parses a GBCB stream; returns null on any malformed input.</summary>
    internal static Dictionary<string, ushort[][]>? ParseStream(Stream stream)
    {
        try
        {
            using var br = new BinaryReader(stream);

            if (br.ReadUInt32() != Magic || br.ReadUInt32() != Version)
            {
                Logger.Log("GlobeBorderLibrary: bad magic/version; F2 disabled.");
                return null;
            }

            int countryCount = br.ReadInt32();
            if (countryCount is < 1 or > 512)
            {
                Logger.Log($"GlobeBorderLibrary: implausible country count {countryCount}; F2 disabled.");
                return null;
            }

            var map = new Dictionary<string, ushort[][]>(countryCount);
            for (int c = 0; c < countryCount; c++)
            {
                string code = new string(br.ReadChars(3));
                int ringCount = br.ReadInt32();
                if (ringCount < 1 || ringCount > 4096)
                    throw new InvalidDataException($"country {code.Trim()} ring count {ringCount}");

                var rings = new ushort[ringCount][];
                for (int r = 0; r < ringCount; r++)
                {
                    int n = br.ReadInt32();
                    if (n < 3 || n > 65536)
                        throw new InvalidDataException($"country {code.Trim()} ring {r} vertex count {n}");

                    rings[r] = ReadQuantizedRing(br, n);
                }

                map[code] = rings;
            }

            Logger.Log($"GlobeBorderLibrary: {map.Count} countries loaded.");
            return map;
        }
        catch (Exception ex)
        {
            Logger.Log($"GlobeBorderLibrary: parse failed ({ex.Message}); F2 disabled.");
            return null;
        }
    }

    /// <summary>
    /// BinaryReader.ReadBytes returns short arrays instead of throwing on a
    /// truncated stream; an exact-length check keeps BlockCopy from corrupting.
    /// </summary>
    private static ushort[] ReadQuantizedRing(BinaryReader br, int vertexCount)
    {
        int bytes = vertexCount * 4;
        byte[] raw = br.ReadBytes(bytes);
        if (raw.Length != bytes)
            throw new EndOfStreamException($"ring needed {bytes} bytes, got {raw.Length}");

        var flat = new ushort[vertexCount * 2];
        Buffer.BlockCopy(raw, 0, flat, 0, bytes);
        return flat;
    }
}
