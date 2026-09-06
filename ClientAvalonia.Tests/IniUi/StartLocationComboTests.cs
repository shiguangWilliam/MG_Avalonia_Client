using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Issue #6: StartLocationCombo is the single source of truth for the
/// ddPlayerStart combo semantics. These tests lock the exact contract the
/// spectator-regression fix (per-field transient guard) depends on:
/// 0 = "-" (random), k = start k, invalid → fallback, never a wrong spawn.
/// </summary>
public sealed class StartLocationComboTests
{
    private static UiNodeViewModel NewDropdown(int selectedIndex)
    {
        var node = new UiNode
        {
            Id = "ddPlayerStart0",
            ControlType = "XNAClientDropDown",
            TemplateKey = "DxLobbyComboBox",
        };
        var vm = new UiNodeViewModel(node, new ResourceResolver("."), new BehaviorRegistry());
        vm.SetComboItems(StartLocationCombo.Items(LobbyPlayerSlot.MaxSlots));
        // Constructor defaults SelectedIndex to 0 (a valid pick); a "cleared"
        // dropdown is simulated by explicitly setting -1, exactly what a
        // mid-rebuild transient looks like.
        vm.SetSelectedIndexSilent(selectedIndex);
        return vm;
    }

    [Fact]
    public void Items_Are_Dash_Then_One_To_MaxSlots()
    {
        string[] items = StartLocationCombo.Items(LobbyPlayerSlot.MaxSlots);

        items.Should().HaveCount(LobbyPlayerSlot.MaxSlots + 1);
        items[0].Should().Be("-");
        items[1].Should().Be("1");
        items[^1].Should().Be(LobbyPlayerSlot.MaxSlots.ToString());
    }

    [Theory]
    [InlineData(0, 0)]   // "-" picked → StartIndex 0 (engine assigns random)
    [InlineData(1, 1)]   // "1" picked → StartIndex 1
    [InlineData(9, 9)]   // "9" picked → StartIndex 9
    public void ToStartIndex_Is_Identity_For_Valid_Picks(int selected, int expected)
    {
        StartLocationCombo.ToStartIndex(selected, LobbyPlayerSlot.MaxSlots)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]  // dropdown cleared mid-rebuild
    [InlineData(10)]  // out-of-range glitch
    public void ToStartIndex_Clamps_Invalid_To_Random(int selected)
    {
        // A glitchy dropdown must never produce an invalid StartIndex —
        // falling back to 0 (random) is always game-legal.
        StartLocationCombo.ToStartIndex(selected, LobbyPlayerSlot.MaxSlots)
            .Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(9, 9)]
    public void ToSelectedIndex_Is_Identity_For_Valid_States(int start, int expected)
    {
        StartLocationCombo.ToSelectedIndex(start, LobbyPlayerSlot.MaxSlots)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void ToSelectedIndex_Clears_Selection_For_Invalid_States(int start)
    {
        StartLocationCombo.ToSelectedIndex(start, LobbyPlayerSlot.MaxSlots)
            .Should().Be(-1);
    }

    [Fact]
    public void HasValidSelection_False_For_Null_And_Transient_States()
    {
        StartLocationCombo.HasValidSelection(null).Should().BeFalse();
        StartLocationCombo.HasValidSelection(NewDropdown(-1)).Should().BeFalse();
    }

    [Fact]
    public void HasValidSelection_True_For_Real_Picks()
    {
        StartLocationCombo.HasValidSelection(NewDropdown(0)).Should().BeTrue();
        StartLocationCombo.HasValidSelection(NewDropdown(LobbyPlayerSlot.MaxSlots)).Should().BeTrue();
    }

    [Fact]
    public void Roundtrip_Preserves_Every_Value()
    {
        for (int start = 0; start <= LobbyPlayerSlot.MaxSlots; start++)
        {
            int selected = StartLocationCombo.ToSelectedIndex(start, LobbyPlayerSlot.MaxSlots);
            StartLocationCombo.ToStartIndex(selected, LobbyPlayerSlot.MaxSlots)
                .Should().Be(start, $"roundtrip must preserve StartIndex {start}");
        }
    }
}
