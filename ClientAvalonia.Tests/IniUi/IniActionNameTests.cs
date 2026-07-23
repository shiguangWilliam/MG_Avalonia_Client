using ClientAvalonia.IniUi.Actions;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks <see cref="IniActionName"/> parsing semantics:
///   - Bare name → no args
///   - Name:Args → name + everything after first colon
///   - DISABLE recognized as special
/// </summary>
public sealed class IniActionNameTests
{
    [Theory]
    [InlineData("ExitApplication", "ExitApplication", "")]
    [InlineData("NavigateTo:SkirmishLobby", "NavigateTo", "SkirmishLobby")]
    [InlineData("Foo:a:b:c", "Foo", "a:b:c")]
    [InlineData("  Trim  :  Arg  ", "Trim", "  Arg  ")]
    public void Parse_Splits_Name_And_Args(string raw, string expectedName, string expectedArgs)
    {
        IniActionName.ParseName(raw).Should().Be(expectedName);
        IniActionName.ParseArgs(raw).Should().Be(expectedArgs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Null_Or_Whitespace_Returns_Empty(string? raw)
    {
        IniActionName.ParseName(raw!).Should().BeEmpty();
        IniActionName.ParseArgs(raw!).Should().BeEmpty();
    }

    [Theory]
    [InlineData("DISABLE", true)]
    [InlineData("disable", true)]
    [InlineData("Disable", true)]
    [InlineData("DISABLED", false)]
    [InlineData("NavigateTo:DISABLE", false)]
    [InlineData("", false)]
    public void IsDisable_Matches_Name_Part_Only_Case_Insensitive(string raw, bool expected)
    {
        IniActionName.IsDisable(raw).Should().Be(expected);
    }
}
