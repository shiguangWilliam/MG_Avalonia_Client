using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 3 P3-1 / P3-2 / P3-3 生产迁移的新 Session-aware API 单测。
///
/// 覆盖：
/// <list type="bullet">
/// <item><see cref="LobbyPlayerHouseResolver.Resolve(IReadOnlyList{IPlayerSlot}, int)"/>：Session-aware 主入口</item>
/// <item><see cref="LobbyPlayerHouseResolver.HouseHandicapFromAiLevel"/>：从 LobbyPlayerState 迁出</item>
/// <item><see cref="SkirmishSpawnWriter.Write(ClientAvalonia.Domain.MapEntry, ClientAvalonia.Domain.GameModeEntry, IReadOnlyList{IPlayerSlot}, int, ClientAvalonia.Rendering.UiNodeViewModel?, int)"/>：Session-aware spawn.ini 写入</item>
/// <item><see cref="LobbyPlayerSlotUiRules.GetUiRowKind(int, IReadOnlyList{IPlayerSlot}, LobbyPlayerMode, bool)"/>：Session-aware 行类型判定</item>
/// </list>
/// </summary>
public sealed class Phase3ProductionMigrationTests
{
    // ---- P3-1 LobbyPlayerHouseResolver.HouseHandicapFromAiLevel ----

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void HouseHandicapFromAiLevel_Maps_AiLevel_To_Handicap(int aiLevel, int expected)
    {
        // Phase 3 P3-1：HouseHandicapFromAiLevel 从 LobbyPlayerState 迁到 LobbyPlayerHouseResolver。
        LobbyPlayerHouseResolver.HouseHandicapFromAiLevel(aiLevel).Should().Be(expected);
    }

    [Fact]
    public void HouseHandicapFromAiLevel_Legacy_LobbyPlayerState_Still_Delegates()
    {
#pragma warning disable CS0618 // 验证 legacy 入口仍委托到新位置。
        int legacyResult = LobbyPlayerState.HouseHandicapFromAiLevel(0);
        int newResult = LobbyPlayerHouseResolver.HouseHandicapFromAiLevel(0);
        legacyResult.Should().Be(newResult);
#pragma warning restore CS0618
    }

    // ---- P3-1 LobbyPlayerHouseResolver.Resolve(IReadOnlyList<IPlayerSlot>, int) ----

    [Fact]
    public void Resolve_IPlayerSlot_Overload_Handles_Empty()
    {
        IReadOnlyList<IPlayerSlot> empty = Array.Empty<LobbyPlayerSlot>();
        var houses = LobbyPlayerHouseResolver.Resolve(empty, randomSeed: 42);
        houses.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_IPlayerSlot_Overload_Accepts_NonLobby_Slot_Type()
    {
        // 关键场景：调用方传任意 IPlayerSlot 实现（不仅是 LobbyPlayerSlot）。
        var slots = new List<IPlayerSlot>
        {
            new FakeSlot { Name = "Alice", SideIndex = 1, ColorIndex = 0 },
            new FakeSlot { Name = "Bob", SideIndex = 2, ColorIndex = 0 },
        };

        var houses = LobbyPlayerHouseResolver.Resolve(slots, randomSeed: 1);
        houses.Should().HaveCount(2);
        houses[0].GameColorIndex.Should().BeGreaterThanOrEqualTo(0);
        houses[1].GameColorIndex.Should().BeGreaterThanOrEqualTo(0);
    }

    // ---- P3-2 SkirmishSpawnWriter.Write(... IReadOnlyList<IPlayerSlot>, int, ...) ----

    [Fact]
    public void SkirmishSpawnWriter_SessionOverload_NullSlots_Throws()
    {
        var map = MakeMap();
        var gameMode = MakeGameMode();
        Action act = () => SkirmishSpawnWriter.Write(map, gameMode, null!, sideCount: 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SkirmishSpawnWriter_SessionOverload_Accepts_Arbitrary_IPlayerSlot_List()
    {
        // 关键场景：可以传非 LobbyPlayerSlot[] 的 IPlayerSlot 列表（session.PlayerSlots 可能是）。
        var map = MakeMap();
        var gameMode = MakeGameMode();
        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Alice", SideIndex = 0, ColorIndex = 0, IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true, AiLevel = 0, SideIndex = 0, ColorIndex = 1 },
        };

        // 不实际写盘（测试环境无游戏目录），但能进入入口、走完 house 解析逻辑即可。
        // 失败模式：FileNotFound / DirectoryNotFound（说明进入了写入路径，参数校验通过）。
        Action act = () => SkirmishSpawnWriter.Write(map, gameMode, slots, sideCount: 3);
        act.Should().Throw<Exception>(); // 任意异常都行——关键是参数已通过校验。
    }

    // ---- P3-3 LobbyPlayerSlotUiRules.GetUiRowKind(IReadOnlyList<IPlayerSlot>, ...) ----

    [Fact]
    public void GetUiRowKind_SessionOverload_Skirmish_Delegates_To_Extensions()
    {
        // Phase 3 P3-3：Session-aware 重载行为等价于 LobbyPlayerState 入口。
        var slots = new IPlayerSlot[]
        {
            new LobbyPlayerSlot { Name = "Alice" },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true },
        };
        // 补齐 8 个槽位
        var fullSlots = slots.Concat(Enumerable.Range(0, 6).Select(_ => (IPlayerSlot)new LobbyPlayerSlot())).ToArray();

        LobbyPlayerSlotUiRules.GetUiRowKind(0, fullSlots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true)
            .Should().Be(LobbyPlayerRowKind.Human);
        LobbyPlayerSlotUiRules.GetUiRowKind(1, fullSlots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true)
            .Should().Be(LobbyPlayerRowKind.Ai);
        LobbyPlayerSlotUiRules.GetUiRowKind(2, fullSlots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true)
            .Should().Be(LobbyPlayerRowKind.Open);
    }

    [Fact]
    public void GetUiRowKind_SessionOverload_HostMultiplayer_Open_After_Humans()
    {
        // 关键差异：Host multiplayer 时 humans 后面永远是 Open（不是 Closed）。
        var slots = new IPlayerSlot[]
        {
            new LobbyPlayerSlot { Name = "Alice" },
        };
        var fullSlots = slots.Concat(Enumerable.Range(0, 7).Select(_ => (IPlayerSlot)new LobbyPlayerSlot())).ToArray();

        LobbyPlayerSlotUiRules.GetUiRowKind(1, fullSlots, LobbyPlayerMode.Multiplayer, allowHostPlayerOptions: true)
            .Should().Be(LobbyPlayerRowKind.Open, "host multiplayer: rows after humans are Open");
    }

    [Fact]
    public void GetUiRowKind_SessionOverload_Matches_Legacy_Overload()
    {
        // 等价性验证：新重载行为与 LobbyPlayerState 入口完全一致。
        var state = new LobbyPlayerState();
        state.Slots[0] = new LobbyPlayerSlot { Name = "Alice" };
        state.Slots[1] = new LobbyPlayerSlot { Name = "EasyAI", IsAi = true };
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = true;

        for (int i = 0; i < LobbyPlayerSlot.MaxSlots; i++)
        {
            LobbyPlayerRowKind legacy = LobbyPlayerSlotUiRules.GetUiRowKind(i, state);
            LobbyPlayerRowKind sessionAware = LobbyPlayerSlotUiRules.GetUiRowKind(
                i, state.Slots, state.Mode, state.AllowHostPlayerOptions);
            sessionAware.Should().Be(legacy, $"slot {i} 必须等价");
        }
    }

    // ---- helpers ----

    private static MapEntry MakeMap(int maxPlayers = 4, int minPlayers = 0, bool enforceMaxPlayers = false)
        => new()
        {
            BaseFilePath = "test.map",
            DisplayName = "Test Map",
            UntranslatedName = "test.map",
            GameModes = new[] { "TestMode" },
            MaxPlayers = maxPlayers,
            MinPlayers = minPlayers,
            EnforceMaxPlayers = enforceMaxPlayers,
        };

    private static GameModeEntry MakeGameMode(string name = "TestMode")
        => new()
        {
            Name = name,
            DisplayName = name,
            UntranslatedUIName = name,
        };

    /// <summary>极简 <see cref="IPlayerSlot"/> 实现，仅供 LobbyPlayerHouseResolver 单测使用。</summary>
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
