using System;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// StateBindingApplier walks a UiNodeViewModel tree and binds known control ids
/// (lblVersion, lblUpdateStatus, lblCnCNetPlayerCount, btnLaunchGame) to UiStateService values.
/// Tests construct minimal VMs with the target ids and verify the binding fires.
/// </summary>
public sealed class StateBindingApplierTests
{
    private static UiNodeViewModel MakeVm(string id, params UiNodeViewModel[] children)
    {
        var node = new UiNode
        {
            Id = id,
            ControlType = "XNAControl",
            TemplateKey = "Blank",
        };
        var behaviors = new BehaviorRegistry();
        var resources = new ResourceResolver();
        return new UiNodeViewModel(node, resources, behaviors, children);
    }

    [Fact]
    public void Apply_LblVersion_SetsDisplayText_FromGameVersion()
    {
        UiNodeViewModel root = MakeVm("root", MakeVm("lblVersion"));
        var state = new Mock<IUiStateService>();
        state.SetupGet(s => s.GameVersion).Returns("1.0.5");

        StateBindingApplier.Apply(root, state.Object, "MainMenu");

        var lbl = Find(root, "lblVersion");
        lbl!.Text.Should().Be("1.0.5");
    }

    [Fact]
    public void Apply_LblUpdateStatus_SetsDisplayText()
    {
        UiNodeViewModel root = MakeVm("root", MakeVm("lblUpdateStatus"));
        var state = new Mock<IUiStateService>();
        state.SetupGet(s => s.UpdateStatusText).Returns("Up to date");

        StateBindingApplier.Apply(root, state.Object, "MainMenu");

        Find(root, "lblUpdateStatus")!.Text.Should().Be("Up to date");
    }

    [Fact]
    public void Apply_BtnLaunchGame_SetsIsEnabled_FromCanLaunchGame()
    {
        UiNodeViewModel root = MakeVm("root", MakeVm("btnLaunchGame"));
        var state = new Mock<IUiStateService>();
        state.SetupGet(s => s.CanLaunchGame).Returns(true);

        StateBindingApplier.Apply(root, state.Object, "MainMenu");

        Find(root, "btnLaunchGame")!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_MainMenuWindow_RefreshesMainMenuState()
    {
        UiNodeViewModel root = MakeVm("root");
        var state = new Mock<IUiStateService>();

        StateBindingApplier.Apply(root, state.Object, "MainMenu");

        state.Verify(s => s.RefreshMainMenuState(), Times.Once);
    }

    [Fact]
    public void Apply_NonMainMenuWindow_DoesNotRefreshMainMenuState()
    {
        UiNodeViewModel root = MakeVm("root");
        var state = new Mock<IUiStateService>();

        StateBindingApplier.Apply(root, state.Object, "OptionsWindow");

        state.Verify(s => s.RefreshMainMenuState(), Times.Never);
    }

    [Fact]
    public void Apply_DescendsIntoNestedChildren()
    {
        UiNodeViewModel root = MakeVm("root",
            MakeVm("panel", MakeVm("lblVersion")));

        var state = new Mock<IUiStateService>();
        state.SetupGet(s => s.GameVersion).Returns("9.9");

        StateBindingApplier.Apply(root, state.Object, "MainMenu");

        Find(root, "lblVersion")!.Text.Should().Be("9.9");
    }

    private static UiNodeViewModel? Find(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
