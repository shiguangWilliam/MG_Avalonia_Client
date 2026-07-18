using System;
using System.IO;
using System.Linq;
using ClientAvalonia.Core;
using ClientCore.Enums;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// End-to-end register / clear / ClientGameType against a real temp game root + HKCU Avalonia keys.
/// Does not run full Startup.Execute (needs a complete mod tree).
/// </summary>
public sealed class ModWorkspaceRegisterIntegrationTests : IDisposable
{
    private readonly string _modName;
    private readonly string _avaloniaKey;
    private readonly string _dxKey;
    private readonly string _gameRoot;

    public ModWorkspaceRegisterIntegrationTests()
    {
        _modName = "AvaloniaInt_" + Guid.NewGuid().ToString("N")[..8];
        _avaloniaKey = ModWorkspaceRegistry.KeyPathFor(_modName);
        _dxKey = "SOFTWARE\\" + _modName;
        _gameRoot = Path.Combine(Path.GetTempPath(), "ClientAvaloniaInt_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Resources"));
        File.WriteAllText(
            Path.Combine(_gameRoot, "Resources", "ClientDefinitions.ini"),
            "[Settings]\n" +
            $"LocalGame={_modName}\n" +
            // Intentionally omit ClientGameType — picker SessionFallback must cover this.
            "SettingsFile=Settings.ini\n");
    }

    public void Dispose()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Registry.CurrentUser.DeleteSubKeyTree(_avaloniaKey, throwOnMissingSubKey: false);
                using RegistryKey? dx = Registry.CurrentUser.OpenSubKey(_dxKey, writable: true);
                dx?.DeleteValue(ModWorkspaceRegistry.InstallPathValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }

        try
        {
            if (Directory.Exists(_gameRoot))
                Directory.Delete(_gameRoot, recursive: true);
        }
        catch
        {
        }

        ClientTypeHelper.ClearSessionFallback();
    }

    [Fact]
    public void Register_WritesAvaloniaInstallPathAndClientGameType_NotDx()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ModRegistrar.TryRegister(_modName, _gameRoot, "YR", out string? error)
            .Should().BeTrue(error);

        ModWorkspaceRegistry.TryReadInstallPath(_modName)
            .Should().Be(_gameRoot.TrimEnd('\\', '/'));
        ModWorkspaceRegistry.TryReadClientGameType(_modName).Should().Be("YR");

        using RegistryKey? dx = Registry.CurrentUser.OpenSubKey(_dxKey);
        dx?.GetValue(ModWorkspaceRegistry.InstallPathValueName).Should().BeNull(
            "register must not pollute DX SOFTWARE\\{ModName}");
    }

    [Fact]
    public void Controller_CompleteRegisterFromFolder_RoundTripsThroughCatalog()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Real register/clear/catalog — only enumerate scoped to our test key.
        var controller = new WorkspacePickerController(
            enumerate: () => ModRegistryCatalog.Enumerate(new[] { _modName }, includeLegacyDxHints: false),
            tryRegister: ModRegistrar.TryRegister,
            tryClear: ModRegistrar.TryClear,
            suggestModName: ModRegistryCatalog.SuggestModName,
            createManual: ModRegistryCatalog.CreateManualEntry,
            resolveGameTypeHint: ModRegistryCatalog.ResolveClientGameTypeHint,
            loadLast: () => null);

        controller.ClientGameTypeText = "TS";
        WorkspacePickerCommandResult result = controller.CompleteRegisterFromFolder(_gameRoot);

        result.Succeeded.Should().BeTrue(result.StatusText);
        result.UiRequest.Should().Be(WorkspacePickerUiRequest.None);

        ModWorkspaceRegistry.TryReadInstallPath(_modName).Should().NotBeNullOrEmpty();
        ModWorkspaceRegistry.TryReadClientGameType(_modName).Should().Be("TS");

        controller.Refresh();
        controller.Entries.Should().ContainSingle(e =>
            e.ModName == _modName
            && e.IsReady
            && e.Source == ModRegistryEntrySource.AvaloniaRegistry);

        controller.ClientGameTypeText.Should().Be("TS",
            "hint resolution should prefer Avalonia registry ClientGameType when ini omits it");
    }

    [Fact]
    public void BeginRegister_WithoutPath_RequestsBrowse_ThenCompleteWrites()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var controller = new WorkspacePickerController(
            enumerate: () => Array.Empty<ModRegistryEntry>(),
            tryRegister: ModRegistrar.TryRegister,
            suggestModName: ModRegistryCatalog.SuggestModName,
            createManual: ModRegistryCatalog.CreateManualEntry,
            resolveGameTypeHint: ModRegistryCatalog.ResolveClientGameTypeHint,
            loadLast: () => null);

        controller.Refresh();
        WorkspacePickerCommandResult begin = controller.BeginRegister();
        begin.UiRequest.Should().Be(WorkspacePickerUiRequest.BrowseFolderForRegister);
        ModWorkspaceRegistry.TryReadInstallPath(_modName).Should().BeNull();

        controller.ClientGameTypeText = "Ares";
        WorkspacePickerCommandResult done = controller.CompleteRegisterFromFolder(_gameRoot);
        done.Succeeded.Should().BeTrue(done.StatusText);
        ModWorkspaceRegistry.TryReadClientGameType(_modName).Should().Be("Ares");
    }

    [Fact]
    public void Clear_RemovesInstallPathAndClientGameType()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ModRegistrar.TryRegister(_modName, _gameRoot, "RA", out _).Should().BeTrue();
        ModRegistrar.TryClear(_modName, out string? error).Should().BeTrue(error);

        ModWorkspaceRegistry.TryReadInstallPath(_modName).Should().BeNull();
        ModWorkspaceRegistry.TryReadClientGameType(_modName).Should().BeNull();
    }

    [Fact]
    public void SessionFallback_AppliesBeforeIniEmpty_AndClearsOnExplicitClear()
    {
        ClientTypeHelper.ClearSessionFallback();
        ModWorkspaceBinder.TryApplySessionClientGameType("YR", out string? error)
            .Should().BeTrue(error);
        ClientTypeHelper.SessionFallback.Should().Be(ClientType.YR);

        // Empty / missing ini value uses SessionFallback.
        ClientTypeHelper.FromString(string.Empty).Should().Be(ClientType.YR);
        ClientTypeHelper.FromString("   ").Should().Be(ClientType.YR);

        // Explicit ini value still wins.
        ClientTypeHelper.FromString("TS").Should().Be(ClientType.TS);

        ClientTypeHelper.ClearSessionFallback();
        Action missing = () => ClientTypeHelper.FromString(string.Empty);
        missing.Should().Throw<Exception>().WithMessage("*ClientGameType*");
    }

    [Fact]
    public void ResolveClientGameTypeHint_IniWinsOverRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string withIni = Path.Combine(Path.GetTempPath(), "ClientAvaloniaHint_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(withIni, "Resources"));
            File.WriteAllText(
                Path.Combine(withIni, "Resources", "ClientDefinitions.ini"),
                "[Settings]\nClientGameType=RA\nLocalGame=X\n");

            ModWorkspaceRegistry.TryWriteClientGameType(_modName, "YR", out _).Should().BeTrue();
            ModRegistryCatalog.ResolveClientGameTypeHint(_modName, withIni).Should().Be("RA");
            ModRegistryCatalog.ResolveClientGameTypeHint(_modName, _gameRoot).Should().Be("YR",
                "when ini omits type, Avalonia registry hint is used");
        }
        finally
        {
            try
            {
                if (Directory.Exists(withIni))
                    Directory.Delete(withIni, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void InvalidClientGameType_RejectsRegister_WithoutLeavingDxPollution()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ModRegistrar.TryRegister(_modName, _gameRoot, "NotAType", out string? error)
            .Should().BeFalse();
        error.Should().Contain("ClientGameType");
        ModWorkspaceRegistry.TryReadInstallPath(_modName).Should().BeNull();

        using RegistryKey? dx = Registry.CurrentUser.OpenSubKey(_dxKey);
        dx?.GetValue(ModWorkspaceRegistry.InstallPathValueName).Should().BeNull();
    }
}
