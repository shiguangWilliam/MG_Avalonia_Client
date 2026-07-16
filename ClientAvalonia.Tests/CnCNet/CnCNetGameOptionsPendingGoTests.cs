using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class CnCNetGameOptionsCatalogAndPendingTests
{
    [Fact]
    public void Enumerate_IncludesMapCodeDropDowns_WithoutSpawnIniOption()
    {
        var root = new UiNode
        {
            Id = "root",
            ControlType = "XNAWindow",
            TemplateKey = "Blank",
        };
        var cmb = new UiNode
        {
            Id = "cmbSuperWeapon",
            ControlType = "GameLobbyDropDown",
            TemplateKey = "DxComboBox",
            Props =
            {
                ["Items"] = "a.ini,b.ini",
                ["ItemLabels"] = "禁止,允许",
                ["DataWriteMode"] = "MapCode",
                ["OptionName"] = "SuperWeaponrules",
                ["DefaultIndex"] = 0,
            },
        };
        root.Children.Add(cmb);

        var rootVm = new UiNodeViewModel(root, new ResourceResolver("."), new BehaviorRegistry(),
        [
            new UiNodeViewModel(cmb, new ResourceResolver("."), new BehaviorRegistry()),
        ]);

        (var checks, var drops) = CnCNetGameOptionsCatalog.Enumerate(rootVm);
        drops.Should().ContainSingle(d => d.Id == "cmbSuperWeapon");
        checks.Should().BeEmpty();
    }

    [Fact]
    public void ApplyDropDownIndices_UsesSilentSet_DoesNotRaiseSelectionChanged()
    {
        var node = new UiNode
        {
            Id = "cmbStolenTech",
            ControlType = "GameLobbyDropDown",
            TemplateKey = "DxComboBox",
            Props =
            {
                ["Items"] = "a.ini,b.ini,c.ini",
                ["ItemLabels"] = "A,B,C",
                ["DataWriteMode"] = "MapCode",
                ["DefaultIndex"] = 0,
            },
        };
        var vm = new UiNodeViewModel(node, new ResourceResolver("."), new BehaviorRegistry());
        int fired = 0;
        vm.SelectionChanged += () => fired++;

        CnCNetGameOptionsCatalog.ApplyDropDownIndices([vm], [2]);

        vm.SelectedIndex.Should().Be(2);
        fired.Should().Be(0);
    }

    [Fact]
    public void ApplyCheckBoxValues_UsesSilentSet_DoesNotRaiseCheckedChanged()
    {
        var node = new UiNode
        {
            Id = "chkSomething",
            ControlType = "GameLobbyCheckBox",
            TemplateKey = "DxCheckBox",
            Props =
            {
                ["SpawnIniOption"] = "Something",
                ["DefaultChecked"] = false,
            },
        };
        var vm = new UiNodeViewModel(node, new ResourceResolver("."), new BehaviorRegistry());
        int fired = 0;
        vm.CheckedChanged += () => fired++;

        CnCNetGameOptionsCatalog.ApplyCheckBoxValues([vm], [true]);

        vm.IsChecked.Should().BeTrue();
        fired.Should().Be(0);
    }
}
