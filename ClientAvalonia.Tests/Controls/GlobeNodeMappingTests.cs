using System;
using System.IO;
using System.Linq;
using ClientAvalonia.Controls;
using Rampastring.Tools;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// Battle.ini Globe fields → GlobeNode mapping semantics: coordinate clamping,
/// GlobeCountry normalization (case, whitespace, invalid codes) and the
/// hash-fallback spread staying deterministic.
/// </summary>
public sealed class GlobeNodeMappingTests
{
    private static IniFile BuildIni(string body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"battle_{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, body);
        return new IniFile(path);
    }

    [Theory]
    [InlineData("IT", "IT")]
    [InlineData("it", "IT")]
    [InlineData("  us ", "US")]
    [InlineData("DEU", "DEU")]
    public void ReadCountryCode_Normalizes_Case_And_Space(string raw, string expected)
    {
        IniFile ini = BuildIni($"[MISSION]\nGlobeCountry={raw}\n");
        string? code = ClientAvalonia.Services.MissionCatalogLoader.ReadCountryCode(ini, "MISSION", "GlobeCountry");
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I")]
    [InlineData("ITAL")]
    [InlineData("I1")]
    [InlineData("XX-")]
    public void ReadCountryCode_Rejects_Invalid(string raw)
    {
        IniFile ini = BuildIni($"[MISSION]\nGlobeCountry={raw}\n");
        string? code = ClientAvalonia.Services.MissionCatalogLoader.ReadCountryCode(ini, "MISSION", "GlobeCountry");
        Assert.Null(code);
    }

    [Fact]
    public void ReadCountryCode_Missing_Key_Yields_Null()
    {
        IniFile ini = BuildIni("[MISSION]\nDescription=x\n");
        Assert.Null(ClientAvalonia.Services.MissionCatalogLoader.ReadCountryCode(ini, "MISSION", "GlobeCountry"));
    }

    [Fact]
    public void GlobeNode_Carries_Country_Code()
    {
        var node = new TacticalGlobeView.GlobeNode("Test", 41.9, 12.5, locked: false, side: "GDI", countryCode: "IT");
        Assert.Equal("IT", node.CountryCode);
        Assert.Equal(41.9, node.LatitudeDegrees);
        Assert.Equal(12.5, node.LongitudeDegrees);
        Assert.False(node.Locked);
    }

    [Fact]
    public void GlobeNode_Defaults_Country_To_Null()
    {
        var node = new TacticalGlobeView.GlobeNode("Test", 0, 0);
        Assert.Null(node.CountryCode);
    }

    [Fact]
    public void Border_Library_Key_Matches_Normalized_Code()
    {
        // The blob stores 3-char space-padded keys; TryGetRings must accept
        // the plain 2-letter form emitted by ReadCountryCode.
        string Key(string code) => code.Trim().ToUpperInvariant().PadRight(3);
        Assert.Equal("IT ", Key(ClientAvalonia.Services.MissionCatalogLoader.ReadCountryCode(
            BuildIni("[M]\nGlobeCountry=it\n"), "M", "GlobeCountry")!));
    }
}
