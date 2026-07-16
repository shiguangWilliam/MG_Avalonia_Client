using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Verifies the <see cref="CnCNetChatLine"/> data model carries chat-scope metadata
/// while remaining backward compatible with pre-existing callers that never set it.
/// </summary>
public sealed class CnCNetChatLineTests
{
    [Fact]
    public void Scope_DefaultsToLobbyChannel_ForBackwardCompatibility()
    {
        // Pre-Phase-A callers construct CnCNetChatLine { Sender=..., DisplayText=... }
        // without setting Scope. Those must continue to land in the lobby timeline.
        var line = new CnCNetChatLine
        {
            Sender = "player",
            DisplayText = "[00:00] player: hello",
        };

        line.Scope.Should().Be(CnCNetChatScope.LobbyChannel);
    }

    [Fact]
    public void Scope_CanBeSetToGameRoom_ForRoomTimeline()
    {
        var line = new CnCNetChatLine
        {
            Scope = CnCNetChatScope.GameRoom,
            Sender = "player",
            DisplayText = "[00:00] player: room hello",
        };

        line.Scope.Should().Be(CnCNetChatScope.GameRoom);
    }

    [Fact]
    public void IsSystem_DefaultsToFalse_AndCanBeOverridden()
    {
        var user = new CnCNetChatLine { DisplayText = "hi" };
        var notice = new CnCNetChatLine { DisplayText = "notice", IsSystem = true };

        user.IsSystem.Should().BeFalse();
        notice.IsSystem.Should().BeTrue();
    }

    [Theory]
    [InlineData(CnCNetChatScope.LobbyChannel, 0)]
    [InlineData(CnCNetChatScope.GameRoom, 1)]
    public void Scope_HasStableNumericValues_ForSerializationStability(CnCNetChatScope scope, int expected)
    {
        // Lock the numeric values so future enum insertions don't silently shift
        // serialized INI/log state. Mirrors the "explicit values" pattern used for
        // other CnCNet protocol enums.
        ((int)scope).Should().Be(expected);
    }
}
