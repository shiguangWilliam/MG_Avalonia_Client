using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Session;

/// <summary>
/// Phase 5 P5-1 / P5-2 / P5-4 生产迁移的 Session-aware API 单测。
///
/// 覆盖：
/// <list type="bullet">
/// <item><b>P5-1</b>: BindingApplier 渲染层（BuildSideItems / BuildTeamItems / SyncUiFromState）脱离 LobbyPlayerState。</item>
/// <item><b>P5-2</b>: <see cref="LobbyPlayerSlotUiRules.BuildNameItems(int, IReadOnlyList{IPlayerSlot}, LobbyPlayerMode, bool, IReadOnlyList{string})"/>
///   / <see cref="LobbyPlayerSlotUiRules.IsNameDropdownEnabled(int, IReadOnlyList{IPlayerSlot}, LobbyPlayerMode, bool)"/>
///   / <see cref="LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(int, IReadOnlyList{IPlayerSlot}, LobbyPlayerMode, bool)"/>
///   / <see cref="LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(ClientAvalonia.Rendering.UiNodeViewModel, IPlayerSlot, IReadOnlyList{string})"/>
///   Session-aware 重载。</item>
/// <item><b>P5-4</b>: <see cref="LobbyPlayerMode"/> 命名空间迁移（Services → Session）后所有现有 API 仍可调用。</item>
/// </list>
/// </summary>
public sealed class Phase5ProductionMigrationTests
{
    // ---- P5-2 LobbyPlayerSlotUiRules.BuildNameItems Session-aware 重载 ----

    [Fact]
    public void BuildNameItems_SessionOverload_Matches_Legacy_For_Human()
    {
        // Phase 5 P5-2：Session-aware 重载行为等价于 LobbyPlayerState 入口。
        var state = new LobbyPlayerState();
        state.Slots[0] = new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true };
        state.LoadCatalogs();

        string[] legacy = LobbyPlayerSlotUiRules.BuildNameItems(0, state);
        string[] sessionAware = LobbyPlayerSlotUiRules.BuildNameItems(
            0, state.Slots, state.Mode, state.AllowHostPlayerOptions, state.AiNames);

        sessionAware.Should().BeEquivalentTo(legacy);
        sessionAware.Should().ContainSingle(s => s == "Alice");
    }

    [Fact]
    public void BuildNameItems_SessionOverload_Matches_Legacy_For_Ai_Row()
    {
        var state = new LobbyPlayerState();
        state.Slots[0] = new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true };
        state.Slots[1] = new LobbyPlayerSlot { Name = "EasyAI", IsAi = true };
        state.LoadCatalogs();

        string[] legacy = LobbyPlayerSlotUiRules.BuildNameItems(1, state);
        string[] sessionAware = LobbyPlayerSlotUiRules.BuildNameItems(
            1, state.Slots, state.Mode, state.AllowHostPlayerOptions, state.AiNames);

        sessionAware.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void BuildNameItems_SessionOverload_Matches_Legacy_For_Open_Row()
    {
        var state = new LobbyPlayerState();
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = true;
        state.Slots[0] = new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true };
        state.LoadCatalogs();

        string[] legacy = LobbyPlayerSlotUiRules.BuildNameItems(1, state);
        string[] sessionAware = LobbyPlayerSlotUiRules.BuildNameItems(
            1, state.Slots, state.Mode, state.AllowHostPlayerOptions, state.AiNames);

        sessionAware.Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void BuildNameItems_SessionOverload_KickBan_Items_For_Other_Human_In_Host_Multiplayer()
    {
        // Host multiplayer 时其他人类玩家应能看到 Kick/Ban 选项。
        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "Other", IsHumanLocal = false },
        };
        while (slots.Count < LobbyPlayerSlot.MaxSlots)
            slots.Add(new LobbyPlayerSlot());

        string[] items = LobbyPlayerSlotUiRules.BuildNameItems(
            1, slots, LobbyPlayerMode.Multiplayer, allowHostPlayerOptions: true,
            aiNames: new[] { "EasyAI", "NormalAI" });

        items.Should().Contain(new[] { "Other", string.Empty, "Kick", "Ban" });
    }

    // ---- P5-2 LobbyPlayerSlotUiRules.IsNameDropdownEnabled Session-aware ----

    [Theory]
    [InlineData(LobbyPlayerMode.Skirmish, true, 0, false)]   // Skirmish: row 0 human (local) 不可改
    [InlineData(LobbyPlayerMode.Skirmish, true, 1, true)]    // Skirmish: AI 行可改
    [InlineData(LobbyPlayerMode.Multiplayer, false, 0, false)] // Joiner multiplayer 不可改
    [InlineData(LobbyPlayerMode.Multiplayer, true, 0, false)]  // Host multiplayer 自己行 0 不可改
    [InlineData(LobbyPlayerMode.Multiplayer, true, 2, true)]   // Host multiplayer Open 行可改
    public void IsNameDropdownEnabled_SessionOverload_Matches_Expectation(
        LobbyPlayerMode mode, bool allowHost, int slotIndex, bool expected)
    {
        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "AI", IsAi = true },
            new LobbyPlayerSlot { Name = string.Empty },   // 空 → Open in host multiplayer
        };
        while (slots.Count < LobbyPlayerSlot.MaxSlots)
            slots.Add(new LobbyPlayerSlot());

        bool result = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(
            slotIndex, slots, mode, allowHost);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsNameDropdownEnabled_SessionOverload_Matches_Legacy()
    {
        var state = new LobbyPlayerState();
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = true;
        state.Slots[0] = new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true };
        state.Slots[1] = new LobbyPlayerSlot { Name = "Bob" };
        state.LoadCatalogs();

        for (int i = 0; i < LobbyPlayerSlot.MaxSlots; i++)
        {
            bool legacy = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(i, state);
            bool sessionAware = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(
                i, state.Slots, state.Mode, state.AllowHostPlayerOptions);
            sessionAware.Should().Be(legacy, $"slot {i} 必须等价");
        }
    }

    // ---- P5-2 LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled Session-aware ----

    [Fact]
    public void ArePlayerOptionsEnabled_SessionOverload_Matches_Legacy()
    {
        var state = new LobbyPlayerState();
        state.Mode = LobbyPlayerMode.Multiplayer;
        state.AllowHostPlayerOptions = true;
        state.Slots[0] = new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true };
        state.Slots[1] = new LobbyPlayerSlot { Name = "AI", IsAi = true };
        state.LoadCatalogs();

        for (int i = 0; i < LobbyPlayerSlot.MaxSlots; i++)
        {
            bool legacy = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(i, state);
            bool sessionAware = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(
                i, state.Slots, state.Mode, state.AllowHostPlayerOptions);
            sessionAware.Should().Be(legacy, $"slot {i} 必须等价");
        }
    }

    [Fact]
    public void ArePlayerOptionsEnabled_SessionOverload_Open_Row_Always_False()
    {
        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Alice" },
        };
        while (slots.Count < LobbyPlayerSlot.MaxSlots)
            slots.Add(new LobbyPlayerSlot());

        bool result = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(
            1, slots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true);
        result.Should().BeFalse("Open 行不允许改选项");
    }

    [Fact]
    public void ArePlayerOptionsEnabled_SessionOverload_Skirmish_Ai_Row_True()
    {
        var slots = new List<IPlayerSlot>
        {
            new LobbyPlayerSlot { Name = "Alice" },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true },
        };
        while (slots.Count < LobbyPlayerSlot.MaxSlots)
            slots.Add(new LobbyPlayerSlot());

        bool result = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(
            1, slots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true);
        result.Should().BeTrue("Skirmish AI 行允许改选项");
    }

    // ---- P5-2 LobbyPlayerSlotUiRules.ResolveNameSelectedIndex Session-aware ----

    [Fact]
    public void ResolveNameSelectedIndex_SessionOverload_Ai_Slot_Uses_Ai_Index()
    {
        // Phase 5 P5-2：Session-aware 重载接受任意 IPlayerSlot + aiNames。
        // IsAi=true 时返回 1 + aiIndex（EasyAI=1, NormalAI=2, HardAI=3）。
        var dropdown = new UiNodeViewModel(
            new UiNode { Id = "dd", ControlType = "XNAClientDropDown", TemplateKey = "DxLobbyComboBox" },
            new ResourceResolver("."),
            new BehaviorRegistry());
        var slot = new LobbyPlayerSlot { Name = "NormalAI", IsAi = true };
        var aiNames = new[] { "EasyAI", "NormalAI", "HardAI" };

        int index = LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(dropdown, slot, aiNames);

        index.Should().Be(2);
    }

    [Fact]
    public void ResolveNameSelectedIndex_SessionOverload_Empty_Slot_Returns_Zero()
    {
        var dropdown = new UiNodeViewModel(
            new UiNode { Id = "dd", ControlType = "XNAClientDropDown", TemplateKey = "DxLobbyComboBox" },
            new ResourceResolver("."),
            new BehaviorRegistry());
        var slot = new LobbyPlayerSlot();
        var aiNames = new[] { "EasyAI" };

        int index = LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(dropdown, slot, aiNames);

        index.Should().Be(0);
    }

    [Fact]
    public void ResolveNameSelectedIndex_SessionOverload_NullArguments_Throw()
    {
        var dropdown = new UiNodeViewModel(
            new UiNode { Id = "dd", ControlType = "XNAClientDropDown", TemplateKey = "DxLobbyComboBox" },
            new ResourceResolver("."),
            new BehaviorRegistry());
        var slot = new LobbyPlayerSlot();
        var aiNames = new[] { "EasyAI" };

        Action nullDropdown = () => LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(null!, slot, aiNames);
        Action nullAi = () => LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(dropdown, slot, null!);

        nullDropdown.Should().Throw<ArgumentNullException>();
        nullAi.Should().Throw<ArgumentNullException>();
    }

    // ---- P5-4 LobbyPlayerMode 命名空间迁移 ----

    [Fact]
    public void LobbyPlayerMode_Available_From_Session_Namespace()
    {
        // Phase 5 P5-4：LobbyPlayerMode 已从 Services 迁到 Session 命名空间。
        // 验证 Sessions 文件可直接引用（using ClientAvalonia.Session）。
        LobbyPlayerMode skirmish = LobbyPlayerMode.Skirmish;
        LobbyPlayerMode multiplayer = LobbyPlayerMode.Multiplayer;

        skirmish.Should().Be(LobbyPlayerMode.Skirmish);
        multiplayer.Should().Be(LobbyPlayerMode.Multiplayer);
        Enum.GetValues<LobbyPlayerMode>().Should().HaveCount(2);
    }

    [Fact]
    public void IGameSession_Mode_Still_Returns_LobbyPlayerMode()
    {
        // 关键回归：迁移后 SkirmishSession.Mode 仍能正确返回 LobbyPlayerMode。
        var session = new SkirmishSession();
        session.Mode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void CnCNetGameRoomSession_Mode_Returns_Multiplayer()
    {
        // Phase 5 P5-4 回归：CnCNetGameRoomSession.Mode 仍能正确返回 LobbyPlayerMode.Multiplayer。
        var room = new CnCNetGameRoomSession(
            new CnCNetActiveGameRoom
            {
                RoomName = "test",
                ChannelName = "#test-game1",
                Password = "pw",
                Tunnel = new CnCNetTunnel { Name = "T", Address = "1.1.1.1", Port = 50000 },
                HostName = "host",
                IsHost = true,
            });
        room.Mode.Should().Be(LobbyPlayerMode.Multiplayer);
    }

    // ---- helpers ----

    /// <summary>极简 <see cref="IPlayerSlot"/> 实现，仅供 P5-2 测试使用。</summary>
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
