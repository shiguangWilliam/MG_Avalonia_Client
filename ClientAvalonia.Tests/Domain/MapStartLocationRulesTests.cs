using ClientAvalonia.Domain;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Domain;

public sealed class MapStartLocationRulesTests
{
    [Fact]
    public void JoinerSelect_Blocked_WhenEnforceMaxPlayers_AndOccupied()
    {
        LobbyPlayerSlot[] slots =
        [
            new() { Name = "Alice", StartIndex = 2, IsHumanLocal = false },
            new() { Name = "Bob", StartIndex = 0, IsHumanLocal = true },
        ];

        MapStartLocationRules.CanJoinerSelect(slots, startLocation1Based: 2, enforceMaxPlayers: true)
            .Should().BeFalse();

        MapStartLocationRules.TryApplyJoinerSelection(slots, "Bob", 2, enforceMaxPlayers: true)
            .Should().BeFalse();
        slots[1].StartIndex.Should().Be(0);
    }

    [Fact]
    public void JoinerSelect_Allowed_WhenNotEnforced_EvenIfOccupied()
    {
        LobbyPlayerSlot[] slots =
        [
            new() { Name = "Alice", StartIndex = 2 },
            new() { Name = "Bob", StartIndex = 0, IsHumanLocal = true },
        ];

        MapStartLocationRules.TryApplyJoinerSelection(slots, "Bob", 2, enforceMaxPlayers: false)
            .Should().BeTrue();
        slots[1].StartIndex.Should().Be(2);
    }

    [Fact]
    public void HostAssign_ClearsPreviousOccupant_WhenEnforced()
    {
        LobbyPlayerSlot[] slots =
        [
            new() { Name = "Alice", StartIndex = 3 },
            new() { Name = "Bob", StartIndex = 0 },
        ];

        MapStartLocationRules.TryApplyHostAssignment(slots, targetSlotIndex: 1, startLocation1Based: 3, enforceMaxPlayers: true)
            .Should().BeTrue();

        slots[0].StartIndex.Should().Be(0);
        slots[1].StartIndex.Should().Be(3);
    }

    [Fact]
    public void HostClear_ZeroesAllOccupantsOfSpot()
    {
        LobbyPlayerSlot[] slots =
        [
            new() { Name = "Alice", StartIndex = 1 },
            new() { Name = "AI", StartIndex = 1, IsAi = true },
            new() { Name = "Bob", StartIndex = 2 },
        ];

        MapStartLocationRules.ClearOccupantsOf(slots, 1);

        slots[0].StartIndex.Should().Be(0);
        slots[1].StartIndex.Should().Be(0);
        slots[2].StartIndex.Should().Be(2);
    }

    [Fact]
    public void JoinerRightClick_ClearsOnlyOwnSpot()
    {
        LobbyPlayerSlot[] slots =
        [
            new() { Name = "Alice", StartIndex = 1 },
            new() { Name = "Bob", StartIndex = 2, IsHumanLocal = true },
        ];

        MapStartLocationRules.TryClearLocalIfOwn(slots, "Bob", 1).Should().BeFalse();
        slots[1].StartIndex.Should().Be(2);

        MapStartLocationRules.TryClearLocalIfOwn(slots, "Bob", 2).Should().BeTrue();
        slots[1].StartIndex.Should().Be(0);
    }
}
