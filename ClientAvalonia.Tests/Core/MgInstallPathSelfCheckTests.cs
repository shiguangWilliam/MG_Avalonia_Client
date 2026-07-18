using System;
using System.IO;
using Microsoft.Win32;
using ClientAvalonia.Core;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

[Collection("ProgramConstantsSerial")]
public sealed class MgInstallPathSelfCheckTests : IDisposable
{
    private readonly TempGameRoot _root = new();
    private string? _previousInstallPath;
    private bool _hadPrevious;

    public MgInstallPathSelfCheckTests()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InstallationRegistry.MgRegistryKeyPath);
        object? value = key?.GetValue("InstallPath");
        if (value != null)
        {
            _hadPrevious = true;
            _previousInstallPath = value.ToString();
        }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (_hadPrevious && _previousInstallPath != null)
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallationRegistry.MgRegistryKeyPath);
                    key.SetValue("InstallPath", _previousInstallPath);
                }
                else
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                        InstallationRegistry.MgRegistryKeyPath,
                        writable: true);
                    key?.DeleteValue("InstallPath", throwOnMissingValue: false);
                }
            }
            catch
            {
                // best-effort restore
            }
        }

        _root.Dispose();
    }

    [SkippableFact]
    public void ResolveAndHeal_WritesCwd_WhenMgKeyMissing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        ClearMgInstallPath();

        string resolved = InstallationRegistry.ResolveAndHealMgInstallPath(_root.RootPath);

        resolved.Should().Be(_root.RootPath.TrimEnd('\\', '/'));
        ReadMgInstallPath().Should().Be(_root.RootPath.TrimEnd('\\', '/'));
    }

    [SkippableFact]
    public void ResolveAndHeal_KeepsValidMgPath()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        WriteMgInstallPath(_root.RootPath);

        string otherCwd = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_Other_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherCwd);
        try
        {
            string resolved = InstallationRegistry.ResolveAndHealMgInstallPath(otherCwd);
            resolved.Should().Be(_root.RootPath.TrimEnd('\\', '/'));
            ReadMgInstallPath().Should().Be(_root.RootPath.TrimEnd('\\', '/'));
        }
        finally
        {
            try { Directory.Delete(otherCwd, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public void ResolveAndHeal_RewritesCwd_WhenGamemdMissing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        string stale = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_Stale_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        try
        {
            WriteMgInstallPath(stale); // dir exists, no gamemd.exe

            string resolved = InstallationRegistry.ResolveAndHealMgInstallPath(_root.RootPath);

            resolved.Should().Be(_root.RootPath.TrimEnd('\\', '/'));
            ReadMgInstallPath().Should().Be(_root.RootPath.TrimEnd('\\', '/'));
        }
        finally
        {
            try { Directory.Delete(stale, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsMgInstallPathValid_RequiresGamemdExe()
    {
        InstallationRegistry.IsMgInstallPathValid(_root.RootPath).Should().BeTrue();

        File.Delete(Path.Combine(_root.RootPath, InstallationRegistry.MgGameExecutableName));
        InstallationRegistry.IsMgInstallPathValid(_root.RootPath).Should().BeFalse();
    }

    private static void ClearMgInstallPath()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            InstallationRegistry.MgRegistryKeyPath,
            writable: true);
        key?.DeleteValue("InstallPath", throwOnMissingValue: false);
    }

    private static void WriteMgInstallPath(string path)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallationRegistry.MgRegistryKeyPath);
        key.SetValue("InstallPath", path.TrimEnd('\\', '/'));
    }

    private static string? ReadMgInstallPath()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InstallationRegistry.MgRegistryKeyPath);
        return key?.GetValue("InstallPath")?.ToString();
    }
}
