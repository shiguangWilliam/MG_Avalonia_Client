using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Core;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// Timing / ordering contracts for pre-startup and return-to-picker hand-off.
/// These bugs previously closed the process without showing the picker.
/// </summary>
public sealed class WorkspaceSessionHandoffTimingTests
{
    [Fact]
    public void ReturnToPicker_Order_IsShowThenCloseThenTeardown()
    {
        WorkspaceSessionHandoff.ReturnToPickerOrder.Should().Equal(
            WorkspaceSessionHandoff.ReturnStep.EnsureExplicitShutdown,
            WorkspaceSessionHandoff.ReturnStep.ShowPicker,
            WorkspaceSessionHandoff.ReturnStep.ClosePreviousMainWindow,
            WorkspaceSessionHandoff.ReturnStep.TeardownSession);

        bool sessionAliveDuringClose = true;
        var trace = WorkspaceSessionHandoff.TraceReturnToPicker(
            onClose: () => sessionAliveDuringClose.Should().BeTrue(),
            onTeardown: () => sessionAliveDuringClose = false);

        int showIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.ShowPicker));
        int closeIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.ClosePreviousMainWindow));
        int tearIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.TeardownSession));
        showIdx.Should().BeLessThan(closeIdx);
        closeIdx.Should().BeLessThan(tearIdx);
    }

    [Fact]
    public void LaunchHandOff_Order_Contract()
    {
        WorkspaceSessionHandoff.LaunchHandOffOrder.Should().Equal(
            WorkspaceSessionHandoff.LaunchHandOffStep.BindWorkspace,
            WorkspaceSessionHandoff.LaunchHandOffStep.AllowPickerClose,
            WorkspaceSessionHandoff.LaunchHandOffStep.RaiseWorkspaceBound,
            WorkspaceSessionHandoff.LaunchHandOffStep.ShowMainWindow,
            WorkspaceSessionHandoff.LaunchHandOffStep.ClosePicker);
    }

    [Fact]
    public void Launch_Sequence_BindThenAllowCloseBeforeRaiseBound()
    {
        var order = new List<string>();
        var controller = new WorkspacePickerController(
            enumerate: () => new[]
            {
                new ModRegistryEntry(
                    "MG",
                    @"D:\MG",
                    ModRegistryEntryState.Ready,
                    ModRegistryEntrySource.AvaloniaRegistry),
            },
            tryBind: (string mod, string path, string type, out string? error) =>
            {
                order.Add(nameof(WorkspaceSessionHandoff.LaunchHandOffStep.BindWorkspace));
                error = null;
                return true;
            });

        controller.Refresh();
        controller.TryLaunchSelected().Should().BeTrue();
        order.Add(nameof(WorkspaceSessionHandoff.LaunchHandOffStep.AllowPickerClose));
        controller.AllowCloseAfterBind.Should().BeTrue();

        // Mimic App: raise WorkspaceBound only after AllowCloseAfterBind.
        order.Add(nameof(WorkspaceSessionHandoff.LaunchHandOffStep.RaiseWorkspaceBound));
        order.Add(nameof(WorkspaceSessionHandoff.LaunchHandOffStep.ShowMainWindow));
        order.Add(nameof(WorkspaceSessionHandoff.LaunchHandOffStep.ClosePicker));

        order.Should().Equal(WorkspaceSessionHandoff.LaunchHandOffOrder.Select(s => s.ToString()));
        controller.ShouldCancelClose(workspaceIsBound: false).Should().BeFalse();
    }

    [Fact]
    public void RegisterThenLaunch_DoesNotAllowCloseUntilBindSucceeds()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[]
            {
                new ModRegistryEntry(
                    "MG",
                    @"D:\MG",
                    ModRegistryEntryState.Ready,
                    ModRegistryEntrySource.Manual),
            },
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                error = null;
                return true;
            },
            tryBind: (string mod, string path, string type, out string? error) =>
            {
                error = "bind later";
                return false;
            });

        controller.Refresh();
        controller.BeginRegister().Succeeded.Should().BeTrue();
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();

        controller.TryLaunchSelected().Should().BeFalse();
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();
    }

    [Fact]
    public void ClosePolicy_Matrix()
    {
        WorkspacePickerClosePolicy.ShouldCancelClose(false, false, false).Should().BeTrue();
        WorkspacePickerClosePolicy.ShouldCancelClose(true, false, false).Should().BeFalse();
        WorkspacePickerClosePolicy.ShouldCancelClose(false, true, false).Should().BeFalse();
        WorkspacePickerClosePolicy.ShouldCancelClose(false, false, true).Should().BeFalse();
    }

    [Fact]
    public void ExecuteReturnToPicker_RunsAllSteps_EvenIfLaterThrows()
    {
        var trace = new List<string>();
        Action act = () => WorkspaceSessionHandoff.ExecuteReturnToPicker(
            () => trace.Add("shutdown"),
            () => trace.Add("show"),
            () => trace.Add("close"),
            () =>
            {
                trace.Add("teardown");
                throw new InvalidOperationException("boom");
            });

        act.Should().Throw<InvalidOperationException>();
        trace.Should().Equal("shutdown", "show", "close", "teardown");
    }
}
