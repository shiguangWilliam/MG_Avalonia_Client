using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

public sealed class CnCNetGameRoomSessionInterfaceTests
{
    [Fact]
    public void Implements_ICnCNetGameSession_With_Room_Metadata()
    {
        var tunnel = new CnCNetTunnel
        {
            Address = "1.2.3.4",
            Port = 50000,
            Name = "test",
            Country = "CN",
        };

        var room = new CnCNetActiveGameRoom
        {
            RoomName = "Room",
            ChannelName = "#test-game1",
            Password = "secret",
            Tunnel = tunnel,
            HostName = "Host",
            IsHost = true,
            MaxPlayers = 4,
            SkillLevel = 2,
            Passworded = true,
        };

        var session = new CnCNetGameRoomSession(room);

        session.Should().BeAssignableTo<ICnCNetGameSession>();
        ICnCNetGameSession game = session;
        game.IsHost.Should().BeTrue();
        game.ChannelName.Should().Be("#test-game1");
        game.MaxPlayers.Should().Be(4);
        game.SkillLevel.Should().Be(2);
        game.Passworded.Should().BeTrue();
        game.Tunnel.Should().BeSameAs(tunnel);
        game.PlayerSlots.Should().HaveCount(8);
        game.State.Should().Be(GameSessionState.Lobby);
    }
}
