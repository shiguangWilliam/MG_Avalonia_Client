using System;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// <see cref="DefaultServiceHub"/> 单测。
/// 见 docs/design/layered-architecture.md §4.1。
/// 用 EnvironmentServicesSerial collection 保证 EnvironmentServices.Reset 与其他测试串行。
/// </summary>
[Collection("EnvironmentServicesSerial")]
public sealed class DefaultServiceHubTests
{
    [Fact]
    public void Get_Throws_When_Not_Registered()
    {
        EnvironmentServices.Reset();
        var hub = DefaultServiceHub.Instance;
        Action act = () => hub.Get<ISkirmishSession>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryGet_Returns_False_When_Not_Registered()
    {
        EnvironmentServices.Reset();
        var hub = DefaultServiceHub.Instance;
        bool ok = hub.TryGet<ISkirmishSession>(out var session);
        ok.Should().BeFalse();
        session.Should().BeNull();
    }

    [Fact]
    public void Get_Returns_Registered_Instance()
    {
        EnvironmentServices.Reset();
        var session = new SkirmishSession();
        EnvironmentServices.Register<ISkirmishSession>(() => session);

        var hub = DefaultServiceHub.Instance;
        hub.Get<ISkirmishSession>().Should().BeSameAs(session);
    }

    [Fact]
    public void TryGet_Returns_True_When_Registered()
    {
        EnvironmentServices.Reset();
        var session = new SkirmishSession();
        EnvironmentServices.Register<ISkirmishSession>(() => session);

        var hub = DefaultServiceHub.Instance;
        bool ok = hub.TryGet<ISkirmishSession>(out var resolved);
        ok.Should().BeTrue();
        resolved.Should().BeSameAs(session);
    }
}

/// <summary>
/// Session 集成测试：通过 Sink 写入应触发 StateChanged，
/// 且写入对 PlayerSlots 可见。
/// </summary>
public sealed class SessionSinkIntegrationTests
{
    [Fact]
    public void SkirmishSession_Sink_Write_Triggers_StateChanged()
    {
        var session = new SkirmishSession();
        int changes = 0;
        session.StateChanged += () => changes++;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "Alice", ColorIndex = 3 });

        changes.Should().BeGreaterThanOrEqualTo(1);
        session.PlayerSlots[0].Name.Should().Be("Alice");
        session.PlayerSlots[0].ColorIndex.Should().Be(3);
    }

    [Fact]
    public void SkirmishSession_Sink_Silent_Does_Not_Trigger_StateChanged()
    {
        var session = new SkirmishSession();
        int changes = 0;
        session.StateChanged += () => changes++;

        session.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Bob" });

        changes.Should().Be(0);
        session.PlayerSlots[0].Name.Should().Be("Bob");
    }

    [Fact]
    public void SkirmishSession_Sink_ClearAll_Triggers_Once()
    {
        var session = new SkirmishSession();
        for (int i = 0; i < 4; i++)
            session.SlotSink.WriteSlotSilent(i, new SlotFieldUpdate { Name = $"P{i}" });

        int changes = 0;
        session.StateChanged += () => changes++;

        session.SlotSink.ClearAll();

        changes.Should().Be(1, "ClearAll 内部静默，末尾发一次");
        foreach (IPlayerSlot slot in session.PlayerSlots)
            slot.Name.Should().BeEmpty();
    }

    [Fact]
    public void SkirmishSession_SlotSink_Is_NotNull()
    {
        var session = new SkirmishSession();
        session.SlotSink.Should().NotBeNull();
        session.SlotSink.Should().BeOfType<LobbyPlayerSlotSink>();
    }
}
