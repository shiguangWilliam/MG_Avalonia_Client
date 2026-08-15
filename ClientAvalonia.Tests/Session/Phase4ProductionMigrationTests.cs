using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 4 / Phase 6：Session-aware sink / start flags / Revision 单测（已脱离 LobbyPlayerState）。
/// </summary>
public sealed class Phase4ProductionMigrationTests
{
    [Fact]
    public void Sink_WriteSlot_Bumps_Revision_And_Fires_StateChanged()
    {
        var session = new SkirmishSession();
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        long before = session.Revision;
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "Alice", SideIndex = 1 });

        session.Revision.Should().BeGreaterThan(before);
        stateChangedCount.Should().BeGreaterThan(0);
        session.Slots[0].Name.Should().Be("Alice");
        session.Slots[0].SideIndex.Should().Be(1);
    }

    [Fact]
    public void Sink_WriteSlotSilent_Does_Not_Bump_Revision()
    {
        var session = new SkirmishSession();
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        long before = session.Revision;
        session.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "Bob", SideIndex = 2 });

        session.Revision.Should().Be(before);
        stateChangedCount.Should().Be(0);
        session.Slots[0].Name.Should().Be("Bob");
    }

    [Fact]
    public void Sink_ClearSlot_Bumps_Revision_And_Zeroes_Fields()
    {
        var session = new SkirmishSession();
        session.Slots[0].Name = "Alice";
        session.Slots[0].SideIndex = 3;
        session.Slots[0].IsAi = true;

        long before = session.Revision;
        session.SlotSink.ClearSlot(0);

        session.Revision.Should().BeGreaterThan(before);
        session.Slots[0].Name.Should().BeEmpty();
        session.Slots[0].SideIndex.Should().Be(0);
        session.Slots[0].IsAi.Should().BeFalse();
    }

    [Fact]
    public void Sink_OverwriteSlot_Copies_All_Fields()
    {
        var session = new SkirmishSession();
        var source = new LobbyPlayerSlot
        {
            Name = "Alice",
            SideIndex = 2,
            ColorIndex = 3,
            TeamIndex = 1,
            StartIndex = 4,
            AiLevel = 0,
            IsAi = false,
            IsHumanLocal = true,
        };

        session.SlotSink.OverwriteSlot(0, source);

        session.Slots[0].Name.Should().Be("Alice");
        session.Slots[0].SideIndex.Should().Be(2);
        session.Slots[0].ColorIndex.Should().Be(3);
        session.Slots[0].TeamIndex.Should().Be(1);
        session.Slots[0].StartIndex.Should().Be(4);
        session.Slots[0].IsHumanLocal.Should().BeTrue();
    }

    [Fact]
    public void Sink_CopyFrom_Bulk_Applies_And_Bumps_Revision_Once()
    {
        var session = new SkirmishSession();
        int stateChangedCount = 0;
        session.StateChanged += () => stateChangedCount++;

        var source = new IPlayerSlot[]
        {
            new LobbyPlayerSlot { Name = "Alice", SideIndex = 1 },
            new LobbyPlayerSlot { Name = "AI", IsAi = true, AiLevel = 0 },
        };

        session.SlotSink.CopyFrom(source);

        stateChangedCount.Should().Be(1);
        session.PlayerSlots[0].Name.Should().Be("Alice");
        session.PlayerSlots[1].Name.Should().Be("AI");
        session.PlayerSlots[1].IsAi.Should().BeTrue();
    }

    [Fact]
    public void Sink_OutOfRange_Index_Noops_Silently()
    {
        var session = new SkirmishSession();
        long before = session.Revision;

        Action act1 = () => session.SlotSink.WriteSlot(-1, new SlotFieldUpdate { Name = "X" });
        Action act2 = () => session.SlotSink.WriteSlot(999, new SlotFieldUpdate { Name = "X" });
        Action act3 = () => session.SlotSink.ClearSlot(-1);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        session.Revision.Should().Be(before);
    }

    [Fact]
    public void SlotFieldUpdate_IsEmpty_ShortCircuits()
    {
        var session = new SkirmishSession();
        long before = session.Revision;
        session.SlotSink.WriteSlot(0, default(SlotFieldUpdate));
        session.Revision.Should().Be(before);
    }

    [Fact]
    public void SlotFieldUpdate_Partial_Update_Preserves_Other_Fields()
    {
        var session = new SkirmishSession();
        session.Slots[0].Name = "Alice";
        session.Slots[0].SideIndex = 1;
        session.Slots[0].ColorIndex = 2;
        session.Slots[0].TeamIndex = 3;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { SideIndex = 5 });

        session.Slots[0].Name.Should().Be("Alice");
        session.Slots[0].SideIndex.Should().Be(5);
        session.Slots[0].ColorIndex.Should().Be(2);
        session.Slots[0].TeamIndex.Should().Be(3);
    }

    [Fact]
    public void SlotFieldUpdate_Options_Factory_Builds_Correct_Update()
    {
        SlotFieldUpdate u = SlotFieldUpdate.Options(side: 1, color: 2, team: 3, start: 4);
        u.SideIndex.Should().Be(1);
        u.ColorIndex.Should().Be(2);
        u.TeamIndex.Should().Be(3);
        u.StartIndex.Should().Be(4);
        u.Name.Should().BeNull();
        u.IsAi.Should().BeNull();
        u.IsEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData(LobbyPlayerMode.Skirmish, true, true, false)]
    [InlineData(LobbyPlayerMode.Skirmish, false, true, false)]
    [InlineData(LobbyPlayerMode.Multiplayer, true, true, false)]
    [InlineData(LobbyPlayerMode.Multiplayer, false, false, true)]
    public void ResolveStartInteractionFlags_SessionOverload_Matches_Expectation(
        LobbyPlayerMode mode, bool allowHost, bool expectedCanAssign, bool expectedCanSelectLocal)
    {
        GameDataBindingApplier.ResolveStartInteractionFlags(
            mode, allowHost, out bool canAssign, out bool canSelectLocal);

        canAssign.Should().Be(expectedCanAssign);
        canSelectLocal.Should().Be(expectedCanSelectLocal);
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Blocked_When_Enforced_And_Occupied()
    {
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 2 },
            new FakeSlot { Name = "Bob", StartIndex = 0 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 2, enforceMaxPlayers: true);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Allowed_When_Not_Enforced()
    {
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 2 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 2, enforceMaxPlayers: false);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Overload_Allowed_For_Unoccupied_Spot()
    {
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", StartIndex = 1 },
            new FakeSlot { Name = "Bob", StartIndex = 2 },
        };

        bool result = MapStartLocationRules.CanJoinerSelect(
            slots, startLocation1Based: 3, enforceMaxPlayers: true);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanJoinerSelect_IPlayerSlot_Allows_Zero_Start()
    {
        var slots = new List<IPlayerSlot> { new FakeSlot { Name = "A", StartIndex = 1 } };

        MapStartLocationRules.CanJoinerSelect(slots, 0, enforceMaxPlayers: true).Should().BeTrue();
        MapStartLocationRules.CanJoinerSelect(slots, -1, enforceMaxPlayers: true).Should().BeTrue();
    }

    [Fact]
    public void Revision_Monotonically_Increases_On_Each_Mutation()
    {
        var session = new SkirmishSession();

        long r0 = session.Revision;
        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "A" });
        long r1 = session.Revision;
        session.SlotSink.WriteSlot(1, new SlotFieldUpdate { Name = "B" });
        long r2 = session.Revision;
        session.SlotSink.ClearSlot(0);
        long r3 = session.Revision;

        r1.Should().BeGreaterThan(r0);
        r2.Should().BeGreaterThan(r1);
        r3.Should().BeGreaterThan(r2);
    }

    [Fact]
    public void Revision_Detects_Self_Triggered_StateChanged()
    {
        var session = new SkirmishSession();

        long subscribedAt = session.Revision;
        bool firedFromExternal = false;

        session.StateChanged += () =>
        {
            if (session.Revision != subscribedAt)
                firedFromExternal = true;
        };

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "Alice" });
        firedFromExternal.Should().BeTrue();
    }

    [Fact]
    public void Revision_Detects_Echo_After_Subscribe()
    {
        var session = new SkirmishSession();

        long capturedDuringCallback = -1;
        session.StateChanged += () => capturedDuringCallback = session.Revision;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "A" });
        long r1 = capturedDuringCallback;

        session.SlotSink.WriteSlot(0, new SlotFieldUpdate { Name = "B" });
        long r2 = capturedDuringCallback;

        r2.Should().BeGreaterThan(r1);
    }

    private sealed class FakeSlot : IPlayerSlot
    {
        public string Name { get; set; } = string.Empty;
        public int SideIndex { get; set; }
        public int ColorIndex { get; set; }
        public int TeamIndex { get; set; }
        public int StartIndex { get; set; }
        public int AiLevel { get; set; }
        public bool IsAi { get; set; }
        public bool IsHumanLocal { get; set; }
        public bool IsOccupied => !string.IsNullOrEmpty(Name);
    }
}
