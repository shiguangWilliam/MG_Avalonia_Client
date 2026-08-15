using System.Collections.Generic;
using ClientAvalonia.Lan;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Lan;

public sealed class LanGameBroadcastCodecTests
{
    [Fact]
    public void Format_Then_Parse_RoundTrips_Ten_Fields()
    {
        string payload = LanGameBroadcastCodec.FormatPayload(
            "1.0",
            "MG",
            "MapA",
            "Standard",
            ["Host", "Guest"],
            locked: false,
            isLoadedGame: true,
            loadedGameId: "42",
            mapSha1: "abc");

        LanGameBroadcastCodec.TryParse(payload, out LanHostedGame game).Should().BeTrue();
        game.ProtocolRevision.Should().Be(LanProtocol.Revision);
        game.MapName.Should().Be("MapA");
        game.GameMode.Should().Be("Standard");
        game.Players.Should().Equal("Host", "Guest");
        game.IsLoadedGame.Should().BeTrue();
        game.LoadedGameId.Should().Be("42");
        game.MapSha1.Should().Be("abc");
        game.Locked.Should().BeFalse();
    }

    [Fact]
    public void Parse_Rejects_Wrong_Field_Count()
    {
        LanGameBroadcastCodec.TryParse("RL8\x01a\x01b", out _).Should().BeFalse();
    }

    [Fact]
    public void PlayerOptions_Codec_RoundTrip()
    {
        var rows = new[]
        {
            new LanPlayerOptionRow("Alice", 1, 2, 3, 4, 1, "127.0.0.1", -1),
            new LanPlayerOptionRow("Easy AI", 0, 1, 0, 0, 0, "", 0),
        };

        string wire = LanPlayerOptionsCodec.Format(rows);
        IReadOnlyList<LanPlayerOptionRow> parsed = LanPlayerOptionsCodec.Parse(wire);
        parsed.Should().HaveCount(2);
        parsed[0].Name.Should().Be("Alice");
        parsed[0].Ready.Should().Be(1);
        parsed[1].AiLevel.Should().Be(0);
    }
}

public sealed class MultiplayerLoadGameSupportTests
{
    [Fact]
    public void Sha1Prefix10_Is_Stable_Length()
    {
        string key = MultiplayerLoadGameSupport.Sha1Prefix10("123456");
        key.Should().HaveLength(10);
        key.Should().MatchRegex("^[0-9a-f]{10}$");
    }

    [Fact]
    public void Sha1Prefix10_Differs_By_GameId()
    {
        MultiplayerLoadGameSupport.Sha1Prefix10("1")
            .Should().NotBe(MultiplayerLoadGameSupport.Sha1Prefix10("2"));
    }
}
