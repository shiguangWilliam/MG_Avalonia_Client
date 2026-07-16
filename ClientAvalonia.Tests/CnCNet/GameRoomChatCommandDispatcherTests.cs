using System.Collections.Generic;
using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class GameRoomChatCommandDispatcherTests
{
    [Fact]
    public void NonSlash_ReturnsFalse_DoesNotFireNotice()
    {
        var notices = new List<string>();
        var dispatcher = new GameRoomChatCommandDispatcher(() => true, notices.Add);

        dispatcher.TryHandle("hello").Should().BeFalse();
        notices.Should().BeEmpty();
    }

    [Fact]
    public void UnknownCommand_EmitsHelp()
    {
        var notices = new List<string>();
        var dispatcher = new GameRoomChatCommandDispatcher(() => true, notices.Add);

        dispatcher.TryHandle("/nope").Should().BeTrue();
        notices.Should().ContainSingle();
        notices[0].Should().Contain("Possible chat box commands");
        notices[0].Should().Contain("ROLL:");
    }

    [Fact]
    public void HostOnly_Rejected_ForNonHost()
    {
        var notices = new List<string>();
        var dispatcher = new GameRoomChatCommandDispatcher(() => false, notices.Add);

        dispatcher.TryHandle("/hidemaps").Should().BeTrue();
        notices.Should().ContainSingle(n => n.Contains("hosts only", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HostOnly_Allowed_ForHost()
    {
        var notices = new List<string>();
        var dispatcher = new GameRoomChatCommandDispatcher(() => true, notices.Add);

        dispatcher.TryHandle("/hidemaps").Should().BeTrue();
        notices.Should().ContainSingle(n => n.Contains("Map list hide", System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("3d6", 3, 6)]
    [InlineData("1D20", 1, 20)]
    public void DiceRoll_FormatsResults(string parameters, int count, int sides)
    {
        int call = 0;
        string? text = GameRoomChatCommandDispatcher.TryFormatDiceRoll(
            parameters,
            sidesExclusive =>
            {
                // Fixed sequence: 0,1,2,... so rolled values are 1,2,3,...
                int v = call % sidesExclusive;
                call++;
                return v;
            });

        text.Should().NotBeNull();
        text!.Should().StartWith($"Dice roll ({count}d{sides}):");
        text.Should().Contain("=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0d6")]
    [InlineData("3d")]
    public void DiceRoll_RejectsMalformed(string parameters)
    {
        string? text = GameRoomChatCommandDispatcher.TryFormatDiceRoll(parameters, _ => 0);
        text.Should().Be("Invalid dice roll. Example: /roll 3d6");
    }

    [Fact]
    public void RollCommand_WritesNotice()
    {
        var notices = new List<string>();
        var dispatcher = new GameRoomChatCommandDispatcher(() => false, notices.Add);

        dispatcher.TryHandle("/roll 2d6").Should().BeTrue();
        notices.Should().ContainSingle(n => n.StartsWith("Dice roll (2d6):"));
    }
}
