using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Slice 4: IGameSession.Mode / Revision + LobbySessionState UI 输入态迁移。
/// 见 layered-architecture-progress-report.md §9.5 Slice 4。
/// </summary>
public sealed class GameSessionModeAndRevisionTests
{
    [Fact]
    public void SkirmishSession_Has_Skirmish_Mode()
    {
        var session = new SkirmishSession();
        session.Mode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void SkirmishSession_Revision_Starts_At_Zero()
    {
        var session = new SkirmishSession();
        session.Revision.Should().Be(0);
    }

    [Fact]
    public void SkirmishSession_Revision_Increments_On_SlotSink_Write()
    {
        var session = new SkirmishSession();
        long initial = session.Revision;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "P1" });

        session.Revision.Should().BeGreaterThan(initial);
    }

    [Fact]
    public void SkirmishSession_Revision_Silent_Write_Does_Not_Increment()
    {
        var session = new SkirmishSession();
        long initial = session.Revision;

        session.SlotSink.WriteSlotSilent(1, new SlotFieldUpdate { Name = "AI1" });

        session.Revision.Should().Be(initial);
    }

    [Fact]
    public void SkirmishSession_Revision_Increments_On_Map_Change()
    {
        var session = new SkirmishSession { Map = null };
        long initial = session.Revision;

        // 用 Moq 构造 IMapResource，避免实现全部 IResource + IMapResource 字段
        var mock = new Moq.Mock<ClientAvalonia.Domain.Resources.IMapResource>();
        session.Map = mock.Object;

        session.Revision.Should().BeGreaterThan(initial);
    }

    [Fact]
    public void SkirmishSession_Revision_Increments_On_State_Change()
    {
        var session = new SkirmishSession();
        long initial = session.Revision;

        session.State = GameSessionState.Launching;

        session.Revision.Should().BeGreaterThan(initial);
    }

    [Fact]
    public void SkirmishSession_Revision_Unchanged_When_State_Set_To_Same_Value()
    {
        var session = new SkirmishSession();
        session.State = GameSessionState.Lobby;
        long initial = session.Revision;

        session.State = GameSessionState.Lobby; // no-op

        session.Revision.Should().Be(initial);
    }

    [Fact]
    public void StateChanged_Fires_On_SlotSink_Write()
    {
        var session = new SkirmishSession();
        int fired = 0;
        session.StateChanged += () => fired++;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "P1" });

        fired.Should().BeGreaterThan(0);
    }
}

/// <summary>
/// Slice 4: LobbySessionState UI 输入态字段（从 LobbyPlayerState 迁移而来）。
/// </summary>
public sealed class LobbySessionStateUiInputTests
{
    [Fact]
    public void UIMode_Defaults_To_Skirmish()
    {
        var sut = new LobbySessionState();
        sut.UIMode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void AllowHostPlayerOptions_Defaults_True()
    {
        new LobbySessionState().AllowHostPlayerOptions.Should().BeTrue();
    }

    [Fact]
    public void LocalPlayerName_Defaults_To_ProgramConstants_PlayerName()
    {
        new LobbySessionState().LocalPlayerName.Should().Be(ClientCore.ProgramConstants.PLAYERNAME);
    }

    [Fact]
    public void HostPlayerName_Defaults_To_ProgramConstants_PlayerName()
    {
        new LobbySessionState().HostPlayerName.Should().Be(ClientCore.ProgramConstants.PLAYERNAME);
    }

    [Fact]
    public void PlayerUpdatingInProgress_Defaults_False_And_Settable()
    {
        var sut = new LobbySessionState();
        sut.PlayerUpdatingInProgress.Should().BeFalse();

        sut.PlayerUpdatingInProgress = true;
        sut.PlayerUpdatingInProgress.Should().BeTrue();
    }

    [Fact]
    public void UIMode_Can_Be_Set_To_Multiplayer()
    {
        var sut = new LobbySessionState { UIMode = LobbyPlayerMode.Multiplayer };
        sut.UIMode.Should().Be(LobbyPlayerMode.Multiplayer);
    }
}
