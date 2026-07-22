using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Protocol;
using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Locks <see cref="PlayerOptionsCodec"/> semantics:
///   - ToDto / ApplyDto are inverses (round-trip preserves all fields).
///   - Human rows appear before AI rows.
///   - Host name is matched case-insensitively; Ready flag flips on host.
///   - ApplyDto clears unused trailing slots.
///   - AreEquivalent ignores Ping/Port (runtime-variable, not in PO broadcast).
/// </summary>
public sealed class PlayerOptionsCodecTests
{
    private static readonly IReadOnlyList<string> AiNames = new[] { "Easy AI", "Medium AI", "Hard AI" };

    private static ICnCNetPlayerSlot[] MakeSlots(params (string Name, bool IsAi, int AiLevel)[] defs)
    {
        var slots = new ICnCNetPlayerSlot[LobbyPlayerSlot.MaxSlots];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = new LobbyPlayerSlot();

        for (int i = 0; i < defs.Length; i++)
        {
            LobbyPlayerSlot s = (LobbyPlayerSlot)slots[i];
            s.Name = defs[i].Name;
            s.IsAi = defs[i].IsAi;
            s.AiLevel = defs[i].AiLevel;
            s.SideIndex = i;
            s.ColorIndex = i + 1;
            s.TeamIndex = i;
            s.StartIndex = i + 1;
        }
        return slots;
    }

    // ---- ToDto ----

    [Fact]
    public void ToDto_Empty_Slots_Returns_Empty()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots();
        PlayerOptionsCodec.ToDto(slots, hostName: "Host", aiNames: AiNames).Should().BeEmpty();
    }

    [Fact]
    public void ToDto_Humans_Before_Ai()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(
            ("Alice", IsAi: false, 0),
            ("AI1", IsAi: true, 0),
            ("Bob", IsAi: false, 0),
            ("AI2", IsAi: true, 2));

        List<CnCNetGameRoomPlayer> dto = PlayerOptionsCodec
            .ToDto(slots, hostName: "Alice", aiNames: AiNames)
            .ToList();

        dto.Should().HaveCount(4);
        dto.Take(2).Should().OnlyContain(p => !p.IsAi);
        dto.Skip(2).Should().OnlyContain(p => p.IsAi);
        dto[0].Name.Should().Be("Alice");
        dto[1].Name.Should().Be("Bob");
    }

    [Fact]
    public void ToDto_Host_Name_Case_Insensitive()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(("alice", IsAi: false, 0));

        CnCNetGameRoomPlayer dto = PlayerOptionsCodec
            .ToDto(slots, hostName: "ALICE", aiNames: AiNames)
            .Single();

        dto.IsHost.Should().BeTrue();
        dto.Ready.Should().BeTrue("host is implicitly ready");
    }

    [Fact]
    public void ToDto_NonHost_Human_Keeps_Ready_From_Slot()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(("Bob", IsAi: false, 0));
        ((LobbyPlayerSlot)slots[0]).Ready = true;

        CnCNetGameRoomPlayer dto = PlayerOptionsCodec
            .ToDto(slots, hostName: "Host", aiNames: AiNames)
            .Single();

        dto.IsHost.Should().BeFalse();
        dto.Ready.Should().BeTrue();
    }

    [Fact]
    public void ToDto_Ai_Uses_Resolved_Name_From_Catalog()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(("AI", IsAi: true, AiLevel: 2));

        CnCNetGameRoomPlayer dto = PlayerOptionsCodec
            .ToDto(slots, hostName: "Host", aiNames: AiNames)
            .Single();

        dto.IsAi.Should().BeTrue();
        dto.AiLevel.Should().Be(2);
        dto.Name.Should().Be("Hard AI", "name is resolved from AiNames by AiLevel");
        dto.Ready.Should().BeTrue("AI is always ready");
    }

    [Fact]
    public void ToDto_Ai_Level_Out_Of_Range_Falls_Back_To_First()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(("AI", IsAi: true, AiLevel: 99));

        CnCNetGameRoomPlayer dto = PlayerOptionsCodec
            .ToDto(slots, hostName: "Host", aiNames: AiNames)
            .Single();

        dto.Name.Should().Be("Easy AI");
    }

    // ---- ApplyDto ----

    [Fact]
    public void ApplyDto_Round_Trip_Preserves_All_Fields()
    {
        ICnCNetPlayerSlot[] original = MakeSlots(
            ("Host", IsAi: false, 0),
            ("Joiner", IsAi: false, 0),
            ("AI", IsAi: true, 1));
        ((LobbyPlayerSlot)original[0]).IsHost = true;
        ((LobbyPlayerSlot)original[0]).Ready = true;
        ((LobbyPlayerSlot)original[1]).Ready = true;

        IReadOnlyList<CnCNetGameRoomPlayer> dto = PlayerOptionsCodec
            .ToDto(original, hostName: "Host", aiNames: AiNames);

        ICnCNetPlayerSlot[] roundTripped = MakeSlots();
        PlayerOptionsCodec.ApplyDto(dto, roundTripped, localNick: "Joiner");

        roundTripped[0].Name.Should().Be("Host");
        roundTripped[0].IsHost.Should().BeTrue();
        roundTripped[0].Ready.Should().BeTrue();
        roundTripped[0].IsAi.Should().BeFalse();
        roundTripped[0].IsHumanLocal.Should().BeFalse("Host != localNick (Joiner)");

        roundTripped[1].Name.Should().Be("Joiner");
        roundTripped[1].IsHumanLocal.Should().BeTrue("matches localNick");
        roundTripped[1].Ready.Should().BeTrue();

        roundTripped[2].IsAi.Should().BeTrue();
        roundTripped[2].AiLevel.Should().Be(1);

        // Trailing slots cleared
        roundTripped[3].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void ApplyDto_Clears_Unused_Trailing_Slots()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots(
            ("Alice", IsAi: false, 0),
            ("Bob", IsAi: false, 0),
            ("Carl", IsAi: false, 0));
        IReadOnlyList<CnCNetGameRoomPlayer> dto = new[]
        {
            new CnCNetGameRoomPlayer { Name = "Solo", IsAi = false }
        };

        PlayerOptionsCodec.ApplyDto(dto, slots, localNick: "Solo");

        slots[0].Name.Should().Be("Solo");
        slots[1].IsOccupied.Should().BeFalse("trailing slot must be cleared");
        slots[2].IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void ApplyDto_Truncates_When_Dto_Exceeds_Slot_Capacity()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots();
        var dto = Enumerable.Range(0, 20)
            .Select(i => new CnCNetGameRoomPlayer { Name = $"P{i}", IsAi = false })
            .ToList();

        PlayerOptionsCodec.ApplyDto(dto, slots, localNick: "P0");

        slots.All(s => s.IsOccupied).Should().BeTrue();
        slots.Length.Should().Be(LobbyPlayerSlot.MaxSlots, "extra DTO entries dropped, no overflow");
    }

    [Fact]
    public void ApplyDto_Ai_Entry_Sets_Ready_True()
    {
        ICnCNetPlayerSlot[] slots = MakeSlots();
        IReadOnlyList<CnCNetGameRoomPlayer> dto = new[]
        {
            new CnCNetGameRoomPlayer { Name = "AI", IsAi = true, AiLevel = 0, SideId = 1 }
        };

        PlayerOptionsCodec.ApplyDto(dto, slots, localNick: "Host");

        slots[0].IsAi.Should().BeTrue();
        slots[0].Ready.Should().BeTrue();
        slots[0].SideIndex.Should().Be(1);
    }

    // ---- AreEquivalent ----

    [Fact]
    public void AreEquivalent_Null_Both_Sides_True()
    {
        PlayerOptionsCodec.AreEquivalent(null, null).Should().BeTrue();
    }

    [Fact]
    public void AreEquivalent_Different_Count_False()
    {
        var a = new[] { new CnCNetGameRoomPlayer { Name = "A" } };
        var b = new[]
        {
            new CnCNetGameRoomPlayer { Name = "A" },
            new CnCNetGameRoomPlayer { Name = "B" }
        };
        PlayerOptionsCodec.AreEquivalent(a, b).Should().BeFalse();
    }

    [Fact]
    public void AreEquivalent_Name_Case_Insensitive()
    {
        var a = new[] { new CnCNetGameRoomPlayer { Name = "alice" } };
        var b = new[] { new CnCNetGameRoomPlayer { Name = "ALICE" } };
        PlayerOptionsCodec.AreEquivalent(a, b).Should().BeTrue();
    }

    [Fact]
    public void AreEquivalent_Ignores_Ping_And_Port()
    {
        var a = new[] { new CnCNetGameRoomPlayer { Name = "A", Ping = 10, Port = 1234 } };
        var b = new[] { new CnCNetGameRoomPlayer { Name = "A", Ping = 200, Port = 5678 } };
        PlayerOptionsCodec.AreEquivalent(a, b).Should().BeTrue("Ping/Port are runtime-only, not in PO broadcast");
    }

    [Fact]
    public void AreEquivalent_Detects_SideId_Difference()
    {
        var a = new[] { new CnCNetGameRoomPlayer { Name = "A", SideId = 0 } };
        var b = new[] { new CnCNetGameRoomPlayer { Name = "A", SideId = 1 } };
        PlayerOptionsCodec.AreEquivalent(a, b).Should().BeFalse();
    }

    [Fact]
    public void AreEquivalent_Detects_Ready_Difference()
    {
        var a = new[] { new CnCNetGameRoomPlayer { Name = "A", Ready = false } };
        var b = new[] { new CnCNetGameRoomPlayer { Name = "A", Ready = true } };
        PlayerOptionsCodec.AreEquivalent(a, b).Should().BeFalse();
    }
}
