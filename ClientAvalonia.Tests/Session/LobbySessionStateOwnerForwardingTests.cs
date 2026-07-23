using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 2 P2-1：验证 LobbyPlayerState.Owner 双向转发到 LobbySessionState，
/// 消除"双份真相"——读写 PlayerState.Mode 与读写 sessionState.UIMode 等价。
/// </summary>
public sealed class LobbySessionStateOwnerForwardingTests
{
    [Fact]
    public void PlayerState_Mode_Reads_Writes_Owner_UIMode()
    {
        var sut = new LobbySessionState();
        sut.PlayerState.Mode = LobbyPlayerMode.Multiplayer;

        sut.UIMode.Should().Be(LobbyPlayerMode.Multiplayer);
        sut.PlayerState.Mode.Should().Be(sut.UIMode);
    }

    [Fact]
    public void UIMode_Write_Reflects_In_PlayerState()
    {
        var sut = new LobbySessionState { UIMode = LobbyPlayerMode.Multiplayer };
        sut.PlayerState.Mode.Should().Be(LobbyPlayerMode.Multiplayer);
    }

    [Fact]
    public void PlayerState_AllowHostPlayerOptions_Bidirectional()
    {
        var sut = new LobbySessionState();
        sut.PlayerState.AllowHostPlayerOptions = false;

        sut.AllowHostPlayerOptions.Should().BeFalse();
        sut.AllowHostPlayerOptions = true;
        sut.PlayerState.AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void PlayerState_LocalPlayerName_Bidirectional()
    {
        var sut = new LobbySessionState();
        sut.PlayerState.LocalPlayerName = "Alice";

        sut.LocalPlayerName.Should().Be("Alice");
        sut.LocalPlayerName = "Bob";
        sut.PlayerState.LocalPlayerName.Should().Be("Bob");
    }

    [Fact]
    public void PlayerState_HostPlayerName_Bidirectional()
    {
        var sut = new LobbySessionState();
        sut.PlayerState.HostPlayerName = "Host";

        sut.HostPlayerName.Should().Be("Host");
    }

    [Fact]
    public void PlayerState_PlayerUpdatingInProgress_Bidirectional()
    {
        var sut = new LobbySessionState();
        sut.PlayerState.PlayerUpdatingInProgress = true;

        sut.PlayerUpdatingInProgress.Should().BeTrue();
    }

    [Fact]
    public void Standalone_PlayerState_NoOwner_Still_Works()
    {
        // 直接 new LobbyPlayerState()（无 owner）应该用本地后备字段，不抛
        var state = new LobbyPlayerState();
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.Mode.Should().Be(LobbyPlayerMode.Multiplayer);

        state.AllowHostPlayerOptions = false;
        state.AllowHostPlayerOptions.Should().BeFalse();

        state.LocalPlayerName = "X";
        state.LocalPlayerName.Should().Be("X");
    }
}
