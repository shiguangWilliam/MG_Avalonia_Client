using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientAvalonia.Core;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.Core;

/// <summary>Avalonia ModWorkspaceRegistry is isolated from DX SOFTWARE\{Mod} keys.</summary>
public sealed class ModWorkspaceRegistryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testModName;
    private readonly string _avaloniaKeyPath;
    private readonly string _dxKeyPath;
    private readonly string _tempRoot;

    public ModWorkspaceRegistryTests(ITestOutputHelper output)
    {
        _output = output;
        _testModName = "AvaloniaTestMod_" + Guid.NewGuid().ToString("N")[..8];
        _avaloniaKeyPath = ModWorkspaceRegistry.KeyPathFor(_testModName);
        _dxKeyPath = "SOFTWARE\\" + _testModName;
        _tempRoot = Path.Combine(Path.GetTempPath(), "ClientAvaloniaModWs_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Resources"));
        File.WriteAllText(Path.Combine(_tempRoot, "Resources", "ClientDefinitions.ini"), "[Settings]\nLocalGame=TEST\n");
    }

    public void Dispose()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Registry.CurrentUser.DeleteSubKeyTree(_avaloniaKeyPath, throwOnMissingSubKey: false);
                // Never leave DX pollution from tests.
                using RegistryKey? dx = Registry.CurrentUser.OpenSubKey(_dxKeyPath, writable: true);
                dx?.DeleteValue(ModWorkspaceRegistry.InstallPathValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }

        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void TryWrite_WritesAvaloniaKey_NotDxKey()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ModWorkspaceRegistry.TryWriteInstallPath(_testModName, _tempRoot, out string? error)
            .Should().BeTrue(error);

        ModWorkspaceRegistry.TryReadInstallPath(_testModName).Should().Be(_tempRoot.TrimEnd('\\', '/'));

        using RegistryKey? dx = Registry.CurrentUser.OpenSubKey(_dxKeyPath);
        dx?.GetValue(ModWorkspaceRegistry.InstallPathValueName).Should().BeNull(
            "Avalonia registry must not write DX SOFTWARE\\{ModName} keys");
        _output.WriteLine($"Avalonia key OK: {_avaloniaKeyPath}");
    }

    [Fact]
    public void IsInstallPathValid_RequiresClientDefinitions()
    {
        ModWorkspaceRegistry.IsInstallPathValid(_tempRoot).Should().BeTrue();
        ModWorkspaceRegistry.IsInstallPathValid(Path.GetTempPath()).Should().BeFalse();
    }

    [Fact]
    public void Catalog_Enumerate_ClassifiesReady()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ModWorkspaceRegistry.TryWriteInstallPath(_testModName, _tempRoot, out _).Should().BeTrue();
        var entries = ModRegistryCatalog.Enumerate(new[] { _testModName }, includeLegacyDxHints: false);
        entries.Should().ContainSingle(e => e.ModName == _testModName && e.IsReady
            && e.Source == ModRegistryEntrySource.AvaloniaRegistry);
    }

    [Fact]
    public void Catalog_Enumerate_DedupesSamePathAcrossDxKeys()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Simulate polluted DX keys: several ModNames → same install path.
        string otherKey = "AvaloniaDxPollute_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + _testModName))
                key.SetValue(ModWorkspaceRegistry.InstallPathValueName, _tempRoot);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + otherKey))
                key.SetValue(ModWorkspaceRegistry.InstallPathValueName, _tempRoot);

            var entries = ModRegistryCatalog.Enumerate(
                new[] { _testModName, otherKey },
                includeLegacyDxHints: true,
                includeMissingSlots: false);

            entries.Count(e =>
                    string.Equals(e.InstallPath, _tempRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, "same InstallPath must appear once even if many DX keys point at it");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\\" + otherKey, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void LegacyDxHint_IsReadOnly_DoesNotWriteDx()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_dxKeyPath))
            key.SetValue(ModWorkspaceRegistry.InstallPathValueName, _tempRoot);

        string? hint = ModWorkspaceRegistry.TryReadLegacyDxInstallPath(_testModName);
        hint.Should().Be(_tempRoot.TrimEnd('\\', '/'));

        // Catalog may surface as LegacyDxHint when Avalonia key missing.
        var entries = ModRegistryCatalog.Enumerate(new[] { _testModName }, includeLegacyDxHints: true);
        entries.Should().Contain(e =>
            e.Source == ModRegistryEntrySource.LegacyDxHint
            && e.IsReady
            && string.Equals(e.InstallPath, _tempRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));

        // Registering goes to Avalonia only.
        ModRegistrar.TryRegister(_testModName, _tempRoot, "YR", out _).Should().BeTrue();
        ModWorkspaceRegistry.TryReadInstallPath(_testModName).Should().NotBeNullOrEmpty();
        ModWorkspaceRegistry.TryReadClientGameType(_testModName).Should().Be("YR");
    }

    [Fact]
    public void CleanupOrphanLegacyDx_ClearsMismatchedKey_KeepsMatchingKey()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string ownedKey = "AvaloniaOwned_" + Guid.NewGuid().ToString("N")[..8];
        string orphanKey = "AvaloniaOrphan_" + Guid.NewGuid().ToString("N")[..8];
        string ownedRoot = Path.Combine(Path.GetTempPath(), "ClientAvaloniaOwned_" + Guid.NewGuid().ToString("N")[..8]);
        string orphanRoot = _tempRoot; // LocalGame=TEST → SuggestModName=TEST ≠ orphanKey

        try
        {
            Directory.CreateDirectory(Path.Combine(ownedRoot, "Resources"));
            File.WriteAllText(
                Path.Combine(ownedRoot, "Resources", "ClientDefinitions.ini"),
                $"[Settings]\nRegistryInstallPath={ownedKey}\nLocalGame={ownedKey}\n");

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + ownedKey))
                key.SetValue(ModWorkspaceRegistry.InstallPathValueName, ownedRoot);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + orphanKey))
                key.SetValue(ModWorkspaceRegistry.InstallPathValueName, orphanRoot);

            var cleared = new List<string>();
            int n = ModRegistrar.TryCleanupOrphanLegacyDxKeys(
                new[] { ownedKey, orphanKey },
                cleared);

            n.Should().Be(1);
            cleared.Should().Contain(orphanKey);
            ModWorkspaceRegistry.TryReadLegacyDxInstallPath(ownedKey).Should().NotBeNullOrEmpty(
                "DX key matching RegistryInstallPath must be kept");
            ModWorkspaceRegistry.TryReadLegacyDxInstallPath(orphanKey).Should().BeNull(
                "mismatched DX pollution key must be cleared");
            ModWorkspaceRegistry.TryReadInstallPath(ownedKey).Should().BeNull(
                "cleanup must not touch Avalonia store");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\\" + ownedKey, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\\" + orphanKey, throwOnMissingSubKey: false);
            try
            {
                if (Directory.Exists(ownedRoot))
                    Directory.Delete(ownedRoot, recursive: true);
            }
            catch
            {
            }
        }
    }
}
