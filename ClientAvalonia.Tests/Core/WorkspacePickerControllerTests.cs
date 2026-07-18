using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClientAvalonia.Core;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// Covers workspace picker interaction logic and hand-off timing
/// (the bugs that closed the process without showing the picker).
/// </summary>
public sealed class WorkspacePickerControllerTests
{
    private static ModRegistryEntry Ready(string mod, string path, ModRegistryEntrySource source = ModRegistryEntrySource.AvaloniaRegistry)
        => new(mod, path, ModRegistryEntryState.Ready, source, displayName: "显示名-" + mod);

    private static ModRegistryEntry Stale(string mod, string path)
        => new(mod, path, ModRegistryEntryState.Stale, ModRegistryEntrySource.AvaloniaRegistry);

    private static ModRegistryEntry Missing(string mod)
        => new(mod, null, ModRegistryEntryState.Missing);

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
            onClose: () =>
            {
                // Old bug: teardown ran first → Close touched reset ClientCore.
                sessionAliveDuringClose.Should().BeTrue("teardown must not run before close");
            },
            onTeardown: () => sessionAliveDuringClose = false);

        trace.Should().Equal(
            nameof(WorkspaceSessionHandoff.ReturnStep.EnsureExplicitShutdown),
            nameof(WorkspaceSessionHandoff.ReturnStep.ShowPicker),
            nameof(WorkspaceSessionHandoff.ReturnStep.ClosePreviousMainWindow),
            nameof(WorkspaceSessionHandoff.ReturnStep.TeardownSession));

        int showIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.ShowPicker));
        int closeIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.ClosePreviousMainWindow));
        int tearIdx = trace.IndexOf(nameof(WorkspaceSessionHandoff.ReturnStep.TeardownSession));
        showIdx.Should().BeLessThan(closeIdx);
        closeIdx.Should().BeLessThan(tearIdx);
    }

    [Fact]
    public void LaunchHandOff_Order_AllowsCloseBeforeRaiseBound()
    {
        WorkspaceSessionHandoff.LaunchHandOffOrder.Should().Equal(
            WorkspaceSessionHandoff.LaunchHandOffStep.BindWorkspace,
            WorkspaceSessionHandoff.LaunchHandOffStep.AllowPickerClose,
            WorkspaceSessionHandoff.LaunchHandOffStep.RaiseWorkspaceBound,
            WorkspaceSessionHandoff.LaunchHandOffStep.ShowMainWindow,
            WorkspaceSessionHandoff.LaunchHandOffStep.ClosePicker);

        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryBind: (string mod, string path, string gameType, out string? error) =>
            {
                error = null;
                return true;
            });

        controller.Selected = Ready("MomentOfGenesis", @"D:\MG\Game");
        controller.TryLaunchSelected().Should().BeTrue();
        controller.AllowCloseAfterBind.Should().BeTrue(
            "AllowCloseAfterBind must be set before UI raises WorkspaceBound / replaces MainWindow");
        controller.ShouldCancelClose(workspaceIsBound: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, false, true)]  // idle gate → cancel close
    [InlineData(true, false, false, false)] // after bind → allow
    [InlineData(false, true, false, false)] // user exit → allow
    [InlineData(false, false, true, false)] // already bound → allow
    public void ClosePolicy_MatchesStartupGate(
        bool allowClose,
        bool userExit,
        bool bound,
        bool expectCancel)
    {
        WorkspacePickerClosePolicy.ShouldCancelClose(allowClose, userExit, bound)
            .Should().Be(expectCancel);
    }

    [Fact]
    public void Refresh_HighlightsLastSelection_ByPath()
    {
        var entries = new List<ModRegistryEntry>
        {
            Ready("A", @"D:\Mods\A"),
            Ready("B", @"D:\Mods\B"),
        };

        var controller = new WorkspacePickerController(
            enumerate: () => entries,
            loadLast: () => new ModWorkspaceLastSelection.Snapshot("B", @"D:\Mods\B"));

        controller.Refresh();
        controller.Selected!.ModName.Should().Be("B");
        controller.ModNameText.Should().Be("B");
        controller.StatusText.Should().Contain("2 个不同路径");
    }

    [Fact]
    public void TryAddFolder_DedupesSamePath_AndSelectsManual()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("Old", @"D:\Mods\A", ModRegistryEntrySource.LegacyDxHint) },
            suggestModName: _ => "MomentOfGenesis",
            createManual: (mod, path) => Ready(mod, path, ModRegistryEntrySource.Manual));

        controller.Refresh();
        controller.ModNameText = string.Empty; // browse should suggest from folder, not stale selection
        controller.TryAddFolder(@"D:\Mods\A").Should().BeTrue();
        controller.Entries.Count(e =>
                string.Equals(e.InstallPath, @"D:\Mods\A", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        controller.Selected!.Source.Should().Be(ModRegistryEntrySource.Manual);
        controller.ModNameText.Should().Be("MomentOfGenesis");
    }

    [Fact]
    public void TryAddFolder_RejectsEmptyPath()
    {
        var controller = new WorkspacePickerController(enumerate: () => Array.Empty<ModRegistryEntry>());
        controller.TryAddFolder("  ").Should().BeFalse();
        controller.StatusText.Should().Contain("无法解析");
    }

    [Fact]
    public void TryProbeLocal_WhenMissing_SetsStatus()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryProbe: _ => null);

        controller.TryProbeLocal().Should().BeFalse();
        controller.StatusText.Should().Contain("旁路探测未找到");
    }

    [Fact]
    public void TryRegisterSelected_RequiresPath_ThenRefreshes()
    {
        bool registered = false;
        var live = new List<ModRegistryEntry> { Ready("MG", @"D:\MG", ModRegistryEntrySource.Manual) };

        var controller = new WorkspacePickerController(
            enumerate: () => live.ToList(),
            tryRegister: (string mod, string path, string gameType, out string? error) =>
            {
                registered = true;
                gameType.Should().Be("YR");
                error = null;
                live[0] = Ready(mod, path);
                return true;
            });

        controller.Refresh();
        controller.ClientGameTypeText = "YR";
        controller.TryRegisterSelected().Should().BeTrue();
        registered.Should().BeTrue();
        controller.Selected!.Source.Should().Be(ModRegistryEntrySource.AvaloniaRegistry);
        controller.StatusText.Should().Contain("Avalonia 注册表");
    }

    [Fact]
    public void TryClearSelected_BlocksLegacyDxHint()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("YR", @"D:\MG", ModRegistryEntrySource.LegacyDxHint) });

        controller.Refresh();
        controller.TryClearSelected().Should().BeFalse();
        controller.StatusText.Should().Contain("清理无用 DX");
    }

    [Fact]
    public void TryClearSelected_ClearsAvaloniaEntry()
    {
        bool cleared = false;
        var live = new List<ModRegistryEntry> { Ready("MG", @"D:\MG") };

        var controller = new WorkspacePickerController(
            enumerate: () => live.ToList(),
            tryClear: (string mod, out string? error) =>
            {
                cleared = true;
                error = null;
                live.Clear();
                return true;
            });

        controller.Refresh();
        controller.TryClearSelected().Should().BeTrue();
        cleared.Should().BeTrue();
        controller.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TryCleanupOrphanDx_ReportsCount()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryCleanupDx: (_, cleared) =>
            {
                cleared!.AddRange(new[] { "YR", "CnCNet" });
                return 2;
            });

        controller.TryCleanupOrphanDx().Should().BeTrue();
        controller.StatusText.Should().Contain("已清理 2").And.Contain("YR");
    }

    [Fact]
    public void TryLaunchSelected_RejectsStale_AndDoesNotAllowClose()
    {
        bool bound = false;
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Stale("MG", @"D:\Missing") },
            tryBind: (string mod, string path, string gameType, out string? error) =>
            {
                bound = true;
                error = null;
                return true;
            });

        controller.Refresh();
        controller.TryLaunchSelected().Should().BeFalse();
        bound.Should().BeFalse();
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();
        controller.StatusText.Should().Contain("无效");
    }

    [Fact]
    public void TryLaunchSelected_RejectsMissingSelection()
    {
        var controller = new WorkspacePickerController(enumerate: () => Array.Empty<ModRegistryEntry>());
        controller.Refresh();
        controller.TryLaunchSelected().Should().BeFalse();
        controller.StatusText.Should().Contain("Ready");
    }

    [Fact]
    public void TryLaunchSelected_OnBindFailure_KeepsGateClosed()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("MG", @"D:\MG") },
            tryBind: (string mod, string path, string gameType, out string? error) =>
            {
                error = "模拟绑定失败";
                return false;
            });

        controller.Refresh();
        controller.TryLaunchSelected().Should().BeFalse();
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();
        controller.StatusText.Should().Be("模拟绑定失败");
    }

    [Fact]
    public void MarkUserRequestedExit_AllowsClose()
    {
        var controller = new WorkspacePickerController(enumerate: () => Array.Empty<ModRegistryEntry>());
        controller.ShouldCancelClose(false).Should().BeTrue();
        controller.MarkUserRequestedExit();
        controller.ShouldCancelClose(false).Should().BeFalse();
    }

    [Fact]
    public void ExecuteReturnToPicker_InvokesCallbacksInOrder_EvenIfTeardownThrows()
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

    [Fact]
    public void ApplySelectionFields_UsesResolvedClientGameTypeHint()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("MG", @"D:\MG") },
            resolveGameTypeHint: (_, _) => "TS");

        controller.Refresh();
        controller.ClientGameTypeText.Should().Be("TS");
        controller.ModNameText.Should().Be("MG");
    }

    [Fact]
    public void Refresh_Merge_DoesNotClearCollectionReference()
    {
        var live = new List<ModRegistryEntry>
        {
            Ready("A", @"D:\A"),
            Ready("B", @"D:\B"),
        };

        var controller = new WorkspacePickerController(enumerate: () => live.ToList());
        ObservableCollection<ModRegistryEntry> entriesRef = controller.Entries;

        controller.Refresh();
        controller.Entries.Should().BeSameAs(entriesRef);
        controller.Entries.Count.Should().Be(2);

        live[0] = Ready("A", @"D:\A", ModRegistryEntrySource.AvaloniaRegistry);
        controller.Refresh(preserveInstallPath: @"D:\A", preserveModName: "A");
        controller.Entries.Should().BeSameAs(entriesRef);
        controller.Entries.Count.Should().Be(2);
        controller.Entries[0].Source.Should().Be(ModRegistryEntrySource.AvaloniaRegistry);
    }

    [Fact]
    public void TryRegisterSelected_RefreshPreservesRegisteredModName()
    {
        var live = new List<ModRegistryEntry> { Ready("Old", @"D:\MG", ModRegistryEntrySource.Manual) };

        var controller = new WorkspacePickerController(
            enumerate: () => live.ToList(),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                error = null;
                live.Clear();
                live.Add(Ready(mod, path, ModRegistryEntrySource.AvaloniaRegistry));
                return true;
            });

        controller.Refresh();
        controller.ModNameText = "shutu";
        controller.TryRegisterSelected().Should().BeTrue();
        controller.Selected!.ModName.Should().Be("shutu");
        controller.ModNameText.Should().Be("shutu");
    }
}
