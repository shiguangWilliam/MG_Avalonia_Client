using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// DX Channel.cs contract: JOIN preserves create/join casing, comparisons use lower-case.
/// <see cref="CnCNetIrcChannelNames"/> mirrors that with Preserve (case-preserving) and
/// Normalize (lower-case for keys).
/// </summary>
public sealed class CnCNetIrcChannelNamesTests
{
    [Fact]
    [Trait("DXContract", "DX-IRC-CHANNEL-CASING")]
    public void Preserve_AddsHashPrefix_WhenMissing()
    {
        CnCNetIrcChannelNames.Preserve("game-1").Should().Be("#game-1");
    }

    [Fact]
    public void Preserve_KeepsExistingHashPrefix()
    {
        CnCNetIrcChannelNames.Preserve("#game-1").Should().Be("#game-1");
    }

    [Fact]
    public void Preserve_DoesNotChangeCase()
    {
        // Mixed-case channel names keep their casing on the wire.
        CnCNetIrcChannelNames.Preserve("GameRoom-ABC").Should().Be("#GameRoom-ABC");
    }

    [Fact]
    public void Preserve_TrimsWhitespace()
    {
        CnCNetIrcChannelNames.Preserve("  #game-1  ").Should().Be("#game-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Preserve_ReturnsEmpty_ForBlankInput(string? input)
    {
        CnCNetIrcChannelNames.Preserve(input!).Should().Be(string.Empty);
    }

    [Fact]
    [Trait("DXContract", "DX-IRC-CHANNEL-CASING")]
    public void Normalize_Lowercases_ForComparisonKeys()
    {
        CnCNetIrcChannelNames.Normalize("GameRoom-ABC").Should().Be("#gameroom-abc");
    }

    [Fact]
    public void Normalize_ReturnsEmpty_ForBlankInput()
    {
        CnCNetIrcNamesTests_NormEmpty("");
        CnCNetIrcNamesTests_NormEmpty(null!);
        CnCNetIrcNamesTests_NormEmpty("   ");
    }

    // Helper just to dedupe the blank-input assertion (kept inline to keep tests self-contained).
    private static void CnCNetIrcNamesTests_NormEmpty(string input)
        => CnCNetIrcChannelNames.Normalize(input).Should().Be(string.Empty);
}
