using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using ClientAvalonia.Core;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// InstallationRegistry self-healing closed loop (MG):
///   read → validate (directory + gamemd.exe) → repair (rewrite or clear).
///
/// Tests use unique random keys under HKCU\SOFTWARE so they never touch the real MG key
/// (MomentOfGenesis). The internal seam
/// <see cref="InstallationRegistry.TryRepairAllCandidates(string[], string?)"/> takes the
/// candidate list as a parameter, so we feed in test-only keys.
///
/// Each test cleans up its keys in finally. Marked serial (registry is process-wide state).
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class InstallationRegistryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempGameRoot _root = new();
    private readonly string _testKeyName = "ClientAvaloniaTest_" + Guid.NewGuid().ToString("N");
    private readonly List<string> _createdKeys = new();

    public InstallationRegistryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (string key in _createdKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\\" + key, throwOnMissingSubKey: false); }
            catch (Exception ex) { _output.WriteLine($"Cleanup failed for {key}: {ex.Message}"); }
        }
        _root.Dispose();
    }

    [Fact]
    public void IsInstallPathValid_True_WhenClientDefinitionsExists()
    {
        InstallationRegistry.IsInstallPathValid(_root.RootPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]                       // empty
    [InlineData(null)]                     // null
    [InlineData("relative/path")]          // not absolute
    public void IsInstallPathValid_False_ForBadPaths(string? path)
    {
        InstallationRegistry.IsInstallPathValid(path).Should().BeFalse();
    }

    [Fact]
    public void IsInstallPathValid_False_WhenClientDefinitionsMissing()
    {
        // Path is absolute but Resources/ClientDefinitions.ini doesn't exist.
        string empty = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_Empty_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(empty);
            InstallationRegistry.IsInstallPathValid(empty).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(empty, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsInstallPathValid_ToleratesTrailingSlash()
    {
        InstallationRegistry.IsInstallPathValid(_root.RootPath + Path.DirectorySeparatorChar).Should().BeTrue();
    }

    [SkippableFact]
    public void TryRepairAllCandidates_RewritesStaleEntry_WhenKnownGoodProvided()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // Pre-seed a stale InstallPath pointing at a non-existent directory.
        WriteInstallPath(_testKeyName, @"C:\Definitely\Does\Not\Exist");

        int repaired = InstallationRegistry.TryRepairAllCandidates(new[] { _testKeyName }, _root.RootPath);

        repaired.Should().Be(1);
        string? recorded = ReadInstallPath(_testKeyName);
        recorded.Should().Be(_root.RootPath.TrimEnd('\\', '/'));
    }

    [SkippableFact]
    public void TryRepairAllCandidates_ClearsStaleEntry_WhenNoKnownGood()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        WriteInstallPath(_testKeyName, @"C:\Another\Bad\Path");

        int repaired = InstallationRegistry.TryRepairAllCandidates(new[] { _testKeyName }, knownGoodRoot: null);

        repaired.Should().Be(1);
        ReadInstallPath(_testKeyName).Should().BeNull();
    }

    [SkippableFact]
    public void TryRepairAllCandidates_LeavesValidEntries_Untouched()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // Valid entry already points at our temp game root.
        WriteInstallPath(_testKeyName, _root.RootPath);

        int repaired = InstallationRegistry.TryRepairAllCandidates(new[] { _testKeyName }, _root.RootPath);

        repaired.Should().Be(0, "no repair needed when entry already valid");
        ReadInstallPath(_testKeyName).Should().Be(_root.RootPath);
    }

    [SkippableFact]
    public void TryRepairAllCandidates_WritesMissingKey_WhenFirstLaunch()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // Key doesn't exist yet — TryRepair writes it on first launch.
        ReadInstallPath(_testKeyName).Should().BeNull();

        int repaired = InstallationRegistry.TryRepairAllCandidates(new[] { _testKeyName }, _root.RootPath);

        repaired.Should().Be(1);
        ReadInstallPath(_testKeyName).Should().Be(_root.RootPath);
    }

    [SkippableFact]
    public void TryRepairAllCandidates_HandlesMixedValidAndStale()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        string validKey = _testKeyName + "_valid";
        string staleKey = _testKeyName + "_stale";
        string missingKey = _testKeyName + "_missing";

        WriteInstallPath(validKey, _root.RootPath);              // valid → untouched
        WriteInstallPath(staleKey, @"C:\Bad\Path");              // stale → rewritten

        int repaired = InstallationRegistry.TryRepairAllCandidates(
            new[] { validKey, staleKey, missingKey }, _root.RootPath);

        // stale + missing both changed = 2.
        repaired.Should().Be(2);
        ReadInstallPath(validKey).Should().Be(_root.RootPath);
        ReadInstallPath(staleKey).Should().Be(_root.RootPath);
        ReadInstallPath(missingKey).Should().Be(_root.RootPath);
    }

    [SkippableFact]
    public void TryReadEarlyBoundInstallPath_ReturnsValidEntry_AndSkipsInvalid()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        string validKey = _testKeyName + "_v";
        string invalidKey = _testKeyName + "_i";
        WriteInstallPath(invalidKey, @"C:\Nope");   // invalid (no gamemd.exe)
        WriteInstallPath(validKey, _root.RootPath); // valid

        // validateFilePresence=true → invalid entry is skipped, valid one returned.
        string? result = InstallationRegistry.TryReadEarlyBoundInstallPath(
            new[] { invalidKey, validKey }, validateFilePresence: true);
        result.Should().Be(_root.RootPath);
    }

    [SkippableFact]
    public void TryReadEarlyBoundInstallPath_WithoutFilePresenceCheck_ReturnsFirstHit()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        WriteInstallPath(_testKeyName, @"C:\Some\Path"); // would fail file-presence check

        string? result = InstallationRegistry.TryReadEarlyBoundInstallPath(
            new[] { _testKeyName }, validateFilePresence: false);
        result.Should().Be(@"C:\Some\Path");
    }

    [SkippableFact]
    [Trait("DXContract", "DX-REGISTRY-WRITE-GATE")]
    public void EarlyBoundRepair_BypassesWritePathToRegistryToggle()
    {
        // DX Startup.cs:432-436 only writes the registry when WritePathToRegistry=true.
        // Avalonia's early-bound repair (TryRepairAllCandidates) is called BEFORE
        // ClientConfiguration is even initialized — it cannot honor that toggle.
        // This test confirms the early-bound path writes regardless.
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // No ClientConfiguration involved — repair happens with raw registry access.
        int repaired = InstallationRegistry.TryRepairAllCandidates(new[] { _testKeyName }, _root.RootPath);
        repaired.Should().Be(1);
        ReadInstallPath(_testKeyName).Should().NotBeNull();
    }

    private void WriteInstallPath(string keyName, string path)
    {
        string fullKey = "SOFTWARE\\" + keyName;
        _createdKeys.Add(keyName);
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(fullKey);
        key.SetValue("InstallPath", path);
    }

    private static string? ReadInstallPath(string keyName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\" + keyName);
        return key?.GetValue("InstallPath")?.ToString();
    }
}
