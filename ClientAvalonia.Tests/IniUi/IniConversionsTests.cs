using ClientAvalonia.IniUi.Loading;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks DX/INI scalar conversion semantics — <see cref="IniConversions"/>.
///
/// Background: DX (ClientCore) IniFile.GetBooleanValue accepts Yes/No, True/False, 1/0
/// (case-insensitive). Anything else falls back to default. This suite pins that contract
/// because lobby INIs (Spawn.ini, Settings.ini) and UI INIs both rely on the same parser.
/// </summary>
public sealed class IniConversionsTests
{
    [Theory]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    [InlineData("YES", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("  yes  ", true)]
    public void BooleanFromString_Accepts_Canonical_TrueTokens(string input, bool expected)
    {
        IniConversions.BooleanFromString(input, defaultValue: false).Should().Be(expected);
    }

    [Theory]
    [InlineData("no", false)]
    [InlineData("NO", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("  no  ", false)]
    public void BooleanFromString_Accepts_Canonical_FalseTokens(string input, bool expected)
    {
        IniConversions.BooleanFromString(input, defaultValue: true).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("yep")]
    [InlineData("2")]
    [InlineData("on")]
    [InlineData("off")]
    public void BooleanFromString_FallsBack_To_Default_For_Unknown(string? input)
    {
        IniConversions.BooleanFromString(input ?? string.Empty, defaultValue: true).Should().BeTrue("unknown token keeps default");
        IniConversions.BooleanFromString(input ?? string.Empty, defaultValue: false).Should().BeFalse("unknown token keeps default");
    }
}
