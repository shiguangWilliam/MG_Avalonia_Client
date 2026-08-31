using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 6：UI 输入态只留在 <see cref="LobbySessionState"/>（不再经 LobbyPlayerState 转发）。
/// </summary>
public sealed class LobbySessionStateOwnerForwardingTests
{
    [Fact]
    public void UIMode_RoundTrips()
    {
        var sut = new LobbySessionState { UIMode = LobbyPlayerMode.Multiplayer };
        sut.UIMode.Should().Be(LobbyPlayerMode.Multiplayer);
        sut.UIMode = LobbyPlayerMode.Skirmish;
        sut.UIMode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void AllowHostPlayerOptions_RoundTrips()
    {
        var sut = new LobbySessionState { AllowHostPlayerOptions = false };
        sut.AllowHostPlayerOptions.Should().BeFalse();
        sut.AllowHostPlayerOptions = true;
        sut.AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void LocalPlayerName_RoundTrips()
    {
        var sut = new LobbySessionState { LocalPlayerName = "Alice" };
        sut.LocalPlayerName.Should().Be("Alice");
        sut.LocalPlayerName = "Bob";
        sut.LocalPlayerName.Should().Be("Bob");
    }

    [Fact]
    public void HostPlayerName_RoundTrips()
    {
        var sut = new LobbySessionState { HostPlayerName = "Host" };
        sut.HostPlayerName.Should().Be("Host");
    }
}
