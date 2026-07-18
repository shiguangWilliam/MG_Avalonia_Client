using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Pure-logic funnel stages for LocalGame channel resolution (no ProgramConstants).
/// LNOD DX JOIN baseline: <c>#cncnet-lnod</c> / <c>#cncnet-lnod-games</c>.
/// </summary>
public sealed class CnCNetLocalGameChannelResolverTests
{
    [Fact]
    [Trait("Baseline", "LNOD-DX")]
    public void BuildConventionChannels_MatchesLnodDxJoinLog()
    {
        var (chat, broadcast) = CnCNetLocalGameChannelResolver.BuildConventionChannels("lnod");
        chat.Should().Be("#cncnet-lnod");
        broadcast.Should().Be("#cncnet-lnod-games");
    }

    [Fact]
    public void TryResolve_PrefersClientDefinitions_OverConvention()
    {
        bool ok = CnCNetLocalGameChannelResolver.TryResolve(
            "lnod",
            clientDefinitionsChatChannel: "#custom-lobby",
            clientDefinitionsBroadcastChannel: "#custom-games",
            out string chat,
            out string broadcast,
            out CnCNetLocalGameChannelResolver.Source source);

        ok.Should().BeTrue();
        source.Should().Be(CnCNetLocalGameChannelResolver.Source.ClientDefinitions);
        chat.Should().Be("#custom-lobby");
        broadcast.Should().Be("#custom-games");
    }

    [Fact]
    public void TryResolve_MirrorsSingleClientDefinitionsChannel()
    {
        CnCNetLocalGameChannelResolver.TryResolve(
            "lnod", "#only-chat", null, out string chat, out string broadcast, out _)
            .Should().BeTrue();
        chat.Should().Be("#only-chat");
        broadcast.Should().Be("#only-chat");
    }

    [Fact]
    [Trait("Baseline", "LNOD-DX")]
    public void TryResolve_UsesConvention_WhenNoClientDefinitionsKeys()
    {
        bool ok = CnCNetLocalGameChannelResolver.TryResolve(
            "LNOD",
            clientDefinitionsChatChannel: null,
            clientDefinitionsBroadcastChannel: "  ",
            out string chat,
            out string broadcast,
            out CnCNetLocalGameChannelResolver.Source source);

        ok.Should().BeTrue();
        source.Should().Be(CnCNetLocalGameChannelResolver.Source.LocalGameConvention);
        chat.Should().Be("#cncnet-lnod");
        broadcast.Should().Be("#cncnet-lnod-games");
    }

    [Fact]
    public void TryResolve_AddsHashPrefix_ForClientDefinitionsWithoutHash()
    {
        CnCNetLocalGameChannelResolver.TryResolve(
            "mg", "yuanming-games", "yuanming-cg-games",
            out string chat, out string broadcast, out _)
            .Should().BeTrue();
        chat.Should().Be("#yuanming-games");
        broadcast.Should().Be("#yuanming-cg-games");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad name")]
    [InlineData("a,b")]
    public void TryResolve_RejectsInvalidLocalGame(string? localGame)
    {
        CnCNetLocalGameChannelResolver.TryResolve(
            localGame, null, null, out _, out _, out _)
            .Should().BeFalse();
    }
}
