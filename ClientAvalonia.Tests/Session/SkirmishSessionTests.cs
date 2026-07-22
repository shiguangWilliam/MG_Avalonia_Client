using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

public sealed class SkirmishSessionTests
{
    [Fact]
    public void PlayerSlots_Are_LobbyPlayerSlot_Instances()
    {
        var session = new SkirmishSession();
        session.PlayerSlots.Should().HaveCount(LobbyPlayerSlot.MaxSlots);
        session.PlayerSlots[0].Should().BeOfType<LobbyPlayerSlot>();
    }

    [Fact]
    public void Setting_Map_Raises_StateChanged()
    {
        var session = new SkirmishSession();
        int raised = 0;
        session.StateChanged += () => raised++;

        session.Map = new MapEntry
        {
            BaseFilePath = "a.map",
            DisplayName = "A",
            UntranslatedName = "A",
            GameModes = ["Standard"],
        };

        raised.Should().Be(1);
        session.Map!.DisplayName.Should().Be("A");
    }

    [Fact]
    public void Options_Are_Mutable_GameOptionsState()
    {
        var session = new SkirmishSession();
        session.Options.MapSha1 = "deadbeef";
        ((IGameSession)session).Options.MapSha1.Should().Be("deadbeef");
    }
}
