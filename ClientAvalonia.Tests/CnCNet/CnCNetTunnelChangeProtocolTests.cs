using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class CnCNetTunnelChangeProtocolTests
{
    [Fact]
    public void FormatChtnl_UsesAddressColonPort()
    {
        CnCNetTunnelChangeProtocol.FormatChtnl("1.2.3.4", 5000)
            .Should().Be("CHTNL 1.2.3.4:5000");
    }

    [Theory]
    [InlineData("10.0.0.1:50000", "10.0.0.1", (ushort)50000)]
    [InlineData("example.cncnet.org:5000", "example.cncnet.org", (ushort)5000)]
    public void TryParse_AcceptsValidEndpoints(string payload, string address, ushort port)
    {
        CnCNetTunnelChangeProtocol.TryParse(payload, out string parsedAddress, out ushort parsedPort)
            .Should().BeTrue();
        parsedAddress.Should().Be(address);
        parsedPort.Should().Be(port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData("host:")]
    public void TryParse_RejectsInvalid(string payload)
    {
        CnCNetTunnelChangeProtocol.TryParse(payload, out _, out _).Should().BeFalse();
    }
}

public sealed class GameLobbySettingsRulesTests
{
    [Fact]
    public void CanSetMaxPlayers_Rejects_WhenBelowOccupied()
    {
        GameLobbySettingsRules.CanSetMaxPlayers(2, occupiedPlayerCount: 3, out string? notice)
            .Should().BeFalse();
        notice.Should().Contain("Cannot reduce maximum players to 2");
    }

    [Fact]
    public void CanSetMaxPlayers_Allows_EqualOrHigher()
    {
        GameLobbySettingsRules.CanSetMaxPlayers(3, 3, out _).Should().BeTrue();
        GameLobbySettingsRules.CanSetMaxPlayers(8, 3, out _).Should().BeTrue();
    }
}
