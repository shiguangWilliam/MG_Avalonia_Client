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
/// Phase 5 / Phase 6：LobbyPlayerSlotUiRules Session-aware API（已删除 LobbyPlayerState 重载）。
/// </summary>
public sealed class Phase5ProductionMigrationTests
{
    [Fact]
    public void BuildNameItems_Human_Returns_Name()
    {
        var slots = MakeSlots(
            new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true });

        string[] items = LobbyPlayerSlotUiRules.BuildNameItems(
            0, slots, LobbyPlayerMode.Skirmish, true, aiNames: Array.Empty<string>());

        items.Should().ContainSingle(s => s == "Alice");
    }

    [Fact]
    public void BuildNameItems_Ai_Row_Prefixed_With_Placeholder()
    {
        var slots = MakeSlots(
            new LobbyPlayerSlot { Name = "Alice", IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true });
        var aiNames = new[] { "EasyAI", "NormalAI" };

        string[] items = LobbyPlayerSlotUiRules.BuildNameItems(
            1, slots, LobbyPlayerMode.Skirmish, true, aiNames);

        items[0].Should().Be("-");
        items.Should().Contain("EasyAI");
    }

    [Fact]
    public void BuildNameItems_Open_Row_In_Multiplayer()
    {
        var slots = MakeSlots(new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true });
        var aiNames = new[] { "EasyAI" };

        string[] items = LobbyPlayerSlotUiRules.BuildNameItems(
            1, slots, LobbyPlayerMode.Multiplayer, true, aiNames);

        items[0].Should().Be(string.Empty);
        items.Should().Contain("EasyAI");
    }

    [Fact]
    public void BuildNameItems_KickBan_Items_For_Other_Human_In_Host_Multiplayer()
    {
        var slots = MakeSlots(
            new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "Other", IsHumanLocal = false });

        string[] items = LobbyPlayerSlotUiRules.BuildNameItems(
            1, slots, LobbyPlayerMode.Multiplayer, allowHostPlayerOptions: true,
            aiNames: new[] { "EasyAI", "NormalAI" });

        items.Should().Contain(new[] { "Other", string.Empty, "Kick", "Ban" });
    }

    [Theory]
    [InlineData(LobbyPlayerMode.Skirmish, true, 0, false)]
    [InlineData(LobbyPlayerMode.Skirmish, true, 1, true)]
    [InlineData(LobbyPlayerMode.Multiplayer, false, 0, false)]
    [InlineData(LobbyPlayerMode.Multiplayer, true, 0, false)]
    [InlineData(LobbyPlayerMode.Multiplayer, true, 2, true)]
    public void IsNameDropdownEnabled_Matches_Expectation(
        LobbyPlayerMode mode, bool allowHost, int slotIndex, bool expected)
    {
        var slots = MakeSlots(
            new LobbyPlayerSlot { Name = "Host", IsHumanLocal = true },
            new LobbyPlayerSlot { Name = "AI", IsAi = true },
            new LobbyPlayerSlot { Name = string.Empty });

        bool result = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(slotIndex, slots, mode, allowHost);
        result.Should().Be(expected);
    }

    [Fact]
    public void ArePlayerOptionsEnabled_Open_Row_Always_False()
    {
        var slots = MakeSlots(new LobbyPlayerSlot { Name = "Alice" });
        bool result = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(
            1, slots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true);
        result.Should().BeFalse();
    }

    [Fact]
    public void ArePlayerOptionsEnabled_Skirmish_Ai_Row_True()
    {
        var slots = MakeSlots(
            new LobbyPlayerSlot { Name = "Alice" },
            new LobbyPlayerSlot { Name = "EasyAI", IsAi = true });

        bool result = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(
            1, slots, LobbyPlayerMode.Skirmish, allowHostPlayerOptions: true);
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveNameSelectedIndex_Ai_Slot_Uses_Ai_Index()
    {
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
    public void ResolveNameSelectedIndex_Empty_Slot_Returns_Zero()
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
    public void ResolveNameSelectedIndex_NullArguments_Throw()
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

    [Fact]
    public void LobbyPlayerMode_Available_From_Session_Namespace()
    {
        LobbyPlayerMode.Skirmish.Should().Be(LobbyPlayerMode.Skirmish);
        LobbyPlayerMode.Multiplayer.Should().Be(LobbyPlayerMode.Multiplayer);
        Enum.GetValues<LobbyPlayerMode>().Should().HaveCount(2);
    }

    [Fact]
    public void IGameSession_Mode_Still_Returns_LobbyPlayerMode()
    {
        var session = new SkirmishSession();
        session.Mode.Should().Be(LobbyPlayerMode.Skirmish);
    }

    [Fact]
    public void CnCNetGameRoomSession_Mode_Returns_Multiplayer()
    {
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

    private static List<IPlayerSlot> MakeSlots(params LobbyPlayerSlot[] occupied)
    {
        var slots = new List<IPlayerSlot>(occupied);
        while (slots.Count < LobbyPlayerSlot.MaxSlots)
            slots.Add(new LobbyPlayerSlot());
        return slots;
    }
}
