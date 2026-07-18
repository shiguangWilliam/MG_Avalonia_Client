using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Core;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// Exhaustive pre-startup picker interaction outcomes (register / browse / launch / clear / close).
/// </summary>
public sealed class WorkspacePickerInteractionTests
{
    private static ModRegistryEntry Ready(string mod, string path, ModRegistryEntrySource source = ModRegistryEntrySource.AvaloniaRegistry)
        => new(mod, path, ModRegistryEntryState.Ready, source, displayName: mod);

    private static ModRegistryEntry Stale(string mod, string path)
        => new(mod, path, ModRegistryEntryState.Stale, ModRegistryEntrySource.AvaloniaRegistry);

    [Fact]
    public void BeginRegister_WithNoSelection_RequestsFolderBrowse_DoesNotCallRegister()
    {
        bool registered = false;
        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                registered = true;
                error = null;
                return true;
            });

        controller.Refresh();
        WorkspacePickerCommandResult result = controller.BeginRegister();

        result.UiRequest.Should().Be(WorkspacePickerUiRequest.BrowseFolderForRegister);
        result.Succeeded.Should().BeFalse();
        result.StatusText.Should().Contain("选择游戏根目录");
        registered.Should().BeFalse("must not write registry before a path is chosen");
        controller.AllowCloseAfterBind.Should().BeFalse();
    }

    [Fact]
    public void BeginRegister_WithReadySelection_RegistersImmediately()
    {
        var calls = new List<(string Mod, string Path, string Type)>();
        var live = new List<ModRegistryEntry> { Ready("MG", @"D:\MG", ModRegistryEntrySource.Manual) };

        var controller = new WorkspacePickerController(
            enumerate: () => live.ToList(),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                calls.Add((mod, path, type));
                error = null;
                live[0] = Ready(mod, path);
                return true;
            });

        controller.Refresh();
        controller.ClientGameTypeText = "TS";
        WorkspacePickerCommandResult result = controller.BeginRegister();

        result.Succeeded.Should().BeTrue();
        result.UiRequest.Should().Be(WorkspacePickerUiRequest.None);
        calls.Should().ContainSingle()
            .Which.Should().Be(("MG", @"D:\MG", "TS"));
        result.StatusText.Should().Contain("Avalonia 注册表").And.Contain("ClientGameType=TS");
    }

    [Fact]
    public void CompleteRegisterFromFolder_AddsThenRegisters_InOrder()
    {
        var trace = new List<string>();
        var live = new List<ModRegistryEntry>();

        var controller = new WorkspacePickerController(
            enumerate: () => live.ToList(),
            suggestModName: _ => "FromFolder",
            createManual: (mod, path) => Ready(mod, path, ModRegistryEntrySource.Manual),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                trace.Add($"register:{mod}:{path}:{type}");
                error = null;
                live.Clear();
                live.Add(Ready(mod, path));
                return true;
            },
            resolveGameTypeHint: (_, _) => null);

        controller.ClientGameTypeText = "Ares";
        WorkspacePickerCommandResult result = controller.CompleteRegisterFromFolder(@"D:\Mods\New");

        result.Succeeded.Should().BeTrue();
        result.UiRequest.Should().Be(WorkspacePickerUiRequest.None);
        trace.Should().Equal(@"register:FromFolder:D:\Mods\New:Ares");
        controller.Selected!.Source.Should().Be(ModRegistryEntrySource.AvaloniaRegistry);
        controller.ModNameText.Should().Be("FromFolder");
    }

    [Fact]
    public void CompleteRegisterFromFolder_EmptyPath_FailsWithoutRegister()
    {
        bool registered = false;
        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                registered = true;
                error = null;
                return true;
            });

        WorkspacePickerCommandResult result = controller.CompleteRegisterFromFolder("  ");
        result.Succeeded.Should().BeFalse();
        result.StatusText.Should().Contain("取消");
        registered.Should().BeFalse();
    }

    [Fact]
    public void CompleteRegisterFromFolder_InvalidFolder_DoesNotRegister()
    {
        bool registered = false;
        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            suggestModName: _ => "Bad",
            createManual: (mod, path) => Stale(mod, path),
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                registered = true;
                error = null;
                return true;
            });

        WorkspacePickerCommandResult result = controller.CompleteRegisterFromFolder(@"D:\Missing");
        result.Succeeded.Should().BeFalse();
        result.StatusText.Should().Contain("ClientDefinitions");
        registered.Should().BeFalse();
    }

    [Fact]
    public void BeginRegister_WhenRegisterFails_SurfacesError_KeepsGateClosed()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("MG", @"D:\MG") },
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                error = "模拟注册失败";
                return false;
            });

        controller.Refresh();
        WorkspacePickerCommandResult result = controller.BeginRegister();
        result.Succeeded.Should().BeFalse();
        result.UiRequest.Should().Be(WorkspacePickerUiRequest.None);
        result.StatusText.Should().Be("模拟注册失败");
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();
    }

    [Fact]
    public void TryLaunchSelected_PassesGameType_AndSetsAllowCloseOnlyAfterBind()
    {
        var order = new List<string>();
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("MG", @"D:\MG") },
            tryBind: (string mod, string path, string type, out string? error) =>
            {
                order.Add("bind:" + type);
                error = null;
                return true;
            });

        controller.Refresh();
        controller.AllowCloseAfterBind.Should().BeFalse();
        controller.ShouldCancelClose(false).Should().BeTrue();

        controller.ClientGameTypeText = "RA";
        controller.TryLaunchSelected().Should().BeTrue();
        order.Should().Equal("bind:RA");
        controller.AllowCloseAfterBind.Should().BeTrue();
        controller.ShouldCancelClose(false).Should().BeFalse();
    }

    [Fact]
    public void TryLaunchSelected_DefaultsInvalidGameType_ToYR()
    {
        string? seen = null;
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("MG", @"D:\MG") },
            tryBind: (string mod, string path, string type, out string? error) =>
            {
                seen = type;
                error = null;
                return true;
            });

        controller.Refresh();
        controller.ClientGameTypeText = "Nope";
        controller.TryLaunchSelected().Should().BeTrue();
        seen.Should().Be("YR");
    }

    [Fact]
    public void ApplySelectionFields_PrefersHint_OverPreviousType()
    {
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("A", @"D:\A"), Ready("B", @"D:\B") },
            resolveGameTypeHint: (mod, _) => mod == "B" ? "TS" : "YR");

        controller.Refresh();
        controller.ClientGameTypeText.Should().Be("YR"); // first selected A
        controller.Selected = controller.Entries[1];
        controller.ApplySelectionFields(controller.Selected);
        controller.ClientGameTypeText.Should().Be("TS");
    }

    [Fact]
    public void Refresh_WithEmptyCatalog_DoesNotThrow_AndBlocksRegisterWithoutBrowse()
    {
        var controller = new WorkspacePickerController(enumerate: () => Array.Empty<ModRegistryEntry>());
        Action act = () => controller.Refresh();
        act.Should().NotThrow();
        controller.Selected.Should().BeNull();
        controller.BeginRegister().UiRequest.Should().Be(WorkspacePickerUiRequest.BrowseFolderForRegister);
    }

    [Fact]
    public void LegacyDxHint_CanBeRegistered_ViaBeginRegister()
    {
        bool registered = false;
        var controller = new WorkspacePickerController(
            enumerate: () => new[] { Ready("YR", @"D:\MG", ModRegistryEntrySource.LegacyDxHint) },
            tryRegister: (string mod, string path, string type, out string? error) =>
            {
                registered = true;
                mod.Should().Be("YR");
                error = null;
                return true;
            });

        controller.Refresh();
        controller.BeginRegister().Succeeded.Should().BeTrue();
        registered.Should().BeTrue();
    }
}
