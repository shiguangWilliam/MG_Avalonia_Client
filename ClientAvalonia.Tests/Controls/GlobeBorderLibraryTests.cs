using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// GBCB country-border blob parsing: happy path, bad magic/version, truncated
/// stream, implausible counts and unknown-country lookup — F2 must degrade
/// silently rather than break the globe when the blob is missing or corrupt.
/// </summary>
public sealed class GlobeBorderLibraryTests
{
    private static byte[] BuildBlob(params (string Code, int[][] Rings)[] countries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(0x42434247u); // 'G','B','C','B'
        bw.Write(1u);
        bw.Write(countries.Length);
        foreach (var (code, rings) in countries)
        {
            bw.Write(code.PadRight(3).ToCharArray());
            bw.Write(rings.Length);
            foreach (int[] ring in rings)
            {
                bw.Write(ring.Length / 2);
                for (int i = 0; i < ring.Length; i += 2)
                {
                    bw.Write((ushort)ring[i]);
                    bw.Write((ushort)ring[i + 1]);
                }
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static Dictionary<string, ushort[][]>? Parse(byte[] blob)
    {
        using var ms = new MemoryStream(blob, writable: false);
        return ClientAvalonia.Controls.GlobeBorderLibrary.ParseStream(ms);
    }

    [Fact]
    public void Parses_Countries_And_Rings()
    {
        byte[] blob = BuildBlob(
            ("US", new[] { new[] { 1, 2, 3, 4, 5, 6, 1, 2 } }),
            ("IT", new[]
            {
                new[] { 10, 20, 30, 40, 50, 60, 10, 20 },
                new[] { 7, 8, 9, 10, 11, 12 },
            }));

        Dictionary<string, ushort[][]>? map = Parse(blob);

        Assert.NotNull(map);
        Assert.Equal(2, map!.Count);
        Assert.Single(map["US "]);
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6, 1, 2 }, map["US "][0]);
        Assert.Equal(2, map["IT "].Length);
        Assert.Equal(new ushort[] { 7, 8, 9, 10, 11, 12 }, map["IT "][1]);
    }

    [Theory]
    [InlineData(0x42474D47u, 1u)] // GMGB (wrong magic)
    [InlineData(0x42434247u, 2u)] // right magic, wrong version
    [InlineData(0u, 0u)]
    public void Bad_Magic_Or_Version_Yields_Null(uint magic, uint version)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(magic);
            bw.Write(version);
            bw.Write(1);
        }

        Assert.Null(ClientAvalonia.Controls.GlobeBorderLibrary.ParseStream(
            new MemoryStream(ms.ToArray())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100000)]
    public void Implausible_Country_Count_Yields_Null(int count)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(0x42434247u);
            bw.Write(1u);
            bw.Write(count);
        }

        Assert.Null(ClientAvalonia.Controls.GlobeBorderLibrary.ParseStream(
            new MemoryStream(ms.ToArray())));
    }

    [Fact]
    public void Truncated_Stream_Yields_Null_Not_Corruption()
    {
        byte[] blob = BuildBlob(("US", new[] { new[] { 1, 2, 3, 4, 5, 6, 1, 2 } }));

        // Cut inside the ring payload: ReadBytes would under-deliver silently.
        Dictionary<string, ushort[][]>? map = Parse(blob.Take(blob.Length - 5).ToArray());
        Assert.Null(map);
    }

    [Fact]
    public void Truncated_Header_Yields_Null()
    {
        byte[] blob = BuildBlob(("US", new[] { new[] { 1, 2, 3, 4, 5, 6, 1, 2 } }));
        Assert.Null(Parse(blob.Take(6).ToArray()));
    }

    [Fact]
    public void TryGetRings_Null_Or_Safe_Without_AssetLoader()
    {
        // The unit-test host has no Avalonia AssetLoader; the public surface
        // must yield a clean "unavailable" instead of throwing.
        ushort[][]? rings = ClientAvalonia.Controls.GlobeBorderLibrary.TryGetRings("US");
        Assert.True(rings is null || rings.Length > 0);
        Assert.Null(ClientAvalonia.Controls.GlobeBorderLibrary.TryGetRings(null));
        Assert.Null(ClientAvalonia.Controls.GlobeBorderLibrary.TryGetRings(""));
        Assert.Null(ClientAvalonia.Controls.GlobeBorderLibrary.TryGetRings("   "));
    }
}
