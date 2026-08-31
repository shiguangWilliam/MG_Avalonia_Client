using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// <see cref="GameSessionExtensions"/> 单测——见 layered-architecture-progress-report.md §9.5 Slice 1。
/// </summary>
public sealed class GameSessionExtensionsTests
{
    private static LobbyPlayerSlot[] MakeSlots(int count = LobbyPlayerSlot.MaxSlots)
        => Enumerable.Range(0, count).Select(_ => new LobbyPlayerSlot()).ToArray();

    private static LobbyPlayerSlot Human(string name) => new() { Name = name, IsAi = false };

    private static LobbyPlayerSlot Ai(string name, int level = 0)
        => new() { Name = name, IsAi = true, AiLevel = level };

    // ---- HumanRowCount ----

    [Fact]
    public void HumanRowCount_Zero_When_All_Empty()
    {
        var slots = MakeSlots();
        slots.HumanRowCount().Should().Be(0);
    }

    [Fact]
    public void HumanRowCount_Counts_Contiguous_Humans_From_Index_0()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Human("B");
        slots.HumanRowCount().Should().Be(2);
    }

    [Fact]
    public void HumanRowCount_Stops_At_First_AI()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Ai("AI1");
        slots[2] = Human("B");
        slots.HumanRowCount().Should().Be(1);
    }

    // ---- AiRowCount ----

    [Fact]
    public void AiRowCount_Counts_Contiguous_AIs_After_Humans()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Ai("X");
        slots[2] = Ai("Y");
        slots[3] = Ai("Z");
        slots.AiRowCount().Should().Be(3);
    }

    [Fact]
    public void AiRowCount_Zero_When_No_Continuous_AIs()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots.AiRowCount().Should().Be(0);
    }

    // ---- OccupiedRowCount / OccupiedSlotCount ----

    [Fact]
    public void OccupiedRowCount_Is_Human_Plus_AI()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Human("B");
        slots[2] = Ai("X");
        slots.OccupiedRowCount().Should().Be(3);
    }

    [Fact]
    public void OccupiedSlotCount_Scans_All_Slots()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[2] = Ai("X");
        slots[5] = Human("C");
        slots.OccupiedSlotCount().Should().Be(3);
        slots.OccupiedRowCount().Should().Be(1);
    }

    // ---- GetRowKind ----

    [Theory]
    [InlineData(-1, LobbyPlayerRowKind.Closed)]
    [InlineData(99, LobbyPlayerRowKind.Closed)]
    public void GetRowKind_Out_Of_Range(int idx, LobbyPlayerRowKind expected)
    {
        var slots = MakeSlots();
        slots.GetRowKind(idx).Should().Be(expected);
    }

    [Fact]
    public void GetRowKind_Returns_Human_AI_Open_Closed_In_Order()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Ai("X");

        slots.GetRowKind(0).Should().Be(LobbyPlayerRowKind.Human);
        slots.GetRowKind(1).Should().Be(LobbyPlayerRowKind.Ai);
        slots.GetRowKind(2).Should().Be(LobbyPlayerRowKind.Open);
        slots.GetRowKind(3).Should().Be(LobbyPlayerRowKind.Closed);
    }

    // ---- FirstEmptySlotIndex ----

    [Fact]
    public void FirstEmptySlotIndex_Returns_First_Unoccupied()
    {
        var slots = MakeSlots();
        slots[0] = Human("A");
        slots[1] = Human("B");
        slots.FirstEmptySlotIndex().Should().Be(2);
    }

    [Fact]
    public void FirstEmptySlotIndex_Returns_Negative_One_When_Full()
    {
        var slots = MakeSlots();
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Human($"P{i}");
        slots.FirstEmptySlotIndex().Should().Be(-1);
    }

    // ---- Session 重载（验证委托）----

    [Fact]
    public void Session_Overload_Delegates_To_PlayerSlots()
    {
        var session = new SkirmishSession();
        session.SlotSink.WriteSlotSilent(0, new SlotFieldUpdate { Name = "P1" });
        session.SlotSink.WriteSlotSilent(1, new SlotFieldUpdate { Name = "AI1", IsAi = true });

        session.HumanRowCount().Should().Be(1);
        session.AiRowCount().Should().Be(1);
        session.OccupiedRowCount().Should().Be(2);
        session.OccupiedSlotCount().Should().Be(2);
        session.GetRowKind(0).Should().Be(LobbyPlayerRowKind.Human);
        session.GetRowKind(1).Should().Be(LobbyPlayerRowKind.Ai);
        session.GetRowKind(2).Should().Be(LobbyPlayerRowKind.Open);
        session.FirstEmptySlotIndex().Should().Be(2);
    }

    [Fact]
    public void SkirmishSession_Slots_Use_Extensions()
    {
        var session = new SkirmishSession();
        session.Slots[0] = Human("A");
        session.Slots[1] = Ai("X");

        ((IReadOnlyList<IPlayerSlot>)session.Slots).HumanRowCount().Should().Be(1);
        ((IReadOnlyList<IPlayerSlot>)session.Slots).AiRowCount().Should().Be(1);
        session.GetRowKind(2).Should().Be(LobbyPlayerRowKind.Open);
        session.FirstEmptySlotIndex().Should().Be(2);
    }
}
