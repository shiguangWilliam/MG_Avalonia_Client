using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Rendering;

/// <summary>
/// GameLobbyDropDown ItemLabels (display) vs Items (spawn/map-code paths) — DX GameSessionDropDown parity.
/// </summary>
public sealed class ComboItemLabelsTests
{
    [Fact]
    public void ItemLabels_DriveDisplayText_WhileTagKeepsIniPaths()
    {
        var node = new UiNode
        {
            Id = "cmbSuperWeapon",
            ControlType = "GameLobbyDropDown",
            TemplateKey = "DxComboBox",
            WindowName = "CnCNetGameLobby",
            Props =
            {
                ["Items"] =
                    "INI/Game Options/NOSuperWeapons.ini,INI/Game Options/YESSuperWeapons.ini",
                ["ItemLabels"] = "禁止授权超武建造,允许授权超武建造",
                ["DefaultIndex"] = 0,
            },
        };

        var vm = new UiNodeViewModel(node, new ResourceResolver("."), new BehaviorRegistry());

        vm.UseComboItemIcons.Should().BeTrue();
        vm.ComboItems.Should().Equal("禁止授权超武建造", "允许授权超武建造");
        vm.ComboItemEntries.Should().HaveCount(2);
        vm.ComboItemEntries[0].Tag.Should().Be("INI/Game Options/NOSuperWeapons.ini");
        vm.ComboItemEntries[1].Tag.Should().Be("INI/Game Options/YESSuperWeapons.ini");

        vm.SelectedIndex = 1;
        vm.GetSelectedComboValue().Should().Be("INI/Game Options/YESSuperWeapons.ini");
    }

    [Fact]
    public void ItemsAlone_StayAsDisplayAndValue()
    {
        var node = new UiNode
        {
            Id = "cmbCredits",
            ControlType = "GameLobbyDropDown",
            TemplateKey = "DxComboBox",
            WindowName = "CnCNetGameLobby",
            Props =
            {
                ["Items"] = "5000,10000,20000",
                ["DefaultIndex"] = 0,
            },
        };

        var vm = new UiNodeViewModel(node, new ResourceResolver("."), new BehaviorRegistry());

        vm.UseComboItemIcons.Should().BeFalse();
        vm.ComboItems.Should().Equal("5000", "10000", "20000");
        vm.GetSelectedComboValue().Should().Be("5000");
    }
}
