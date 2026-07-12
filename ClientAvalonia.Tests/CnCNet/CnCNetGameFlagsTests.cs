using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// DX-aligned: CnCNetLobby.cs:1547-1551 — 5-char flags string with index-ordered booleans and
/// fixed defaults when the field is empty/truncated.
/// </summary>
public sealed class CnCNetGameFlagsTests
{
    [Fact]
    [Trait("DXContract", "DX-GAME-FLAGS-DEFAULTS")]
    public void Build_Roundtrips_AllFiveFlags()
    {
        // Order: locked, passworded, closed, loaded, ladder
        string built = CnCNetGameFlags.Build(locked: true, passworded: false, closed: true, loadedGame: false, ladder: true);
        built.Should().Be("10101");
        built.Should().HaveLength(DxAliases.FlagsFieldLength);
    }

    [Fact]
    public void Build_AllFalse_ProducesAllZeros()
    {
        CnCNetGameFlags.Build(false, false, false).Should().Be("00000");
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FLAGS-DEFAULTS")]
    public void ParsePassworded_FlagIndex1_OnlyChar1MeansPassworded()
    {
        // DX isCustomPassword: index 1, only '1' is true.
        CnCNetGameFlags.ParsePassworded("01000").Should().BeTrue();
        CnCNetGameFlags.ParsePassworded("00000").Should().BeFalse();
        // DX BooleanFromString treats anything non-"1" as default (false here).
        CnCNetGameFlags.ParsePassworded("02000").Should().BeFalse();
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FLAGS-DEFAULTS")]
    public void ParseLocked_DefaultsTrue_WhenEmpty()
    {
        // DX BooleanSubstring(flags, 0, defaultValue=true) — empty/short → true.
        CnCNetGameFlags.ParseLocked("").Should().Be(DxAliases.DefaultLocked);
        CnCNetGameFlags.ParseLocked(null).Should().BeTrue();
        CnCNetGameFlags.ParseLocked("1").Should().BeTrue();
        CnCNetGameFlags.ParseLocked("0").Should().BeFalse();
    }

    [Fact]
    [Trait("DXContract", "DX-GAME-FLAGS-DEFAULTS")]
    public void ParseClosed_DefaultsTrue_WhenEmpty()
    {
        CnCNetGameFlags.ParseClosed("").Should().Be(DxAliases.DefaultClosed);
        CnCNetGameFlags.ParseClosed(null).Should().BeTrue();
        CnCNetGameFlags.ParseClosed("00100").Should().BeTrue();
        CnCNetGameFlags.ParseClosed("00000").Should().BeFalse();
    }

    [Fact]
    public void ParseLoadedGame_DefaultsFalse_WhenEmpty()
    {
        CnCNetGameFlags.ParseLoadedGame("").Should().BeFalse();
        CnCNetGameFlags.ParseLoadedGame("00010").Should().BeTrue();
    }

    [Fact]
    public void ParseLadder_DefaultsFalse_WhenEmpty()
    {
        CnCNetGameFlags.ParseLadder("").Should().BeFalse();
        CnCNetGameFlags.ParseLadder("00001").Should().BeTrue();
    }

    [Fact]
    public void Normalize_PadsShortFlags_WithZeros()
    {
        CnCNetGameFlags.Normalize("1").Should().Be("10000");
        CnCNetGameFlags.Normalize("").Should().Be("00000");
        CnCNetGameFlags.Normalize(null).Should().Be("00000");
    }

    [Fact]
    public void Normalize_TruncatesLongFlags_AtFive()
    {
        CnCNetGameFlags.Normalize("123456").Should().Be("12345");
    }

    [Fact]
    public void Normalize_KeepsExactLengthString()
    {
        CnCNetGameFlags.Normalize("01100").Should().Be("01100");
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("1", 1)]
    [InlineData("2", null)] // Only "1" counts as passworded in settings parsing.
    public void ParseSettingsPassworded_OnlyIntegerOneMeansPassworded(string value, int? expected)
    {
        CnCNetGameFlags.ParseSettingsPassworded(value).Should().Be(expected == 1);
    }
}
