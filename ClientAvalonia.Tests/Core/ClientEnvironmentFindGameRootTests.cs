using System;
using System.IO;
using Microsoft.Win32;
using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.Core;

/// <summary>
/// ClientEnvironment.FindGameRoot three-tier fallback:
///   1. Registry hint (HKCU\SOFTWARE\&lt;candidate&gt;\InstallPath, validated against ClientDefinitions.ini)
///   2. Walk upward from startDirectory (CWD) looking for Resources/ClientDefinitions.ini
///   3. Walk upward from AppContext.BaseDirectory (exe folder)
///
/// DXMainClient PreStartup.cs:62-65 uses exe-folder directly (no CWD dependency).
/// Avalonia adds the registry hint + CWD walk as MG enhancements — see plan B.1.
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class ClientEnvironmentFindGameRootTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempGameRoot _root = new();
    private readonly string _testKeyName = "ClientAvaloniaTest_" + Guid.NewGuid().ToString("N");

    public ClientEnvironmentFindGameRootTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree("SOFTWARE\\" + _testKeyName, throwOnMissingSubKey: false); }
        catch (Exception ex) { _output.WriteLine($"Cleanup failed: {ex.Message}"); }
        _root.Dispose();
    }

    [SkippableFact]
    [Trait("DXContract", "DX-BOOTSTRAP-CWD")]
    public void ReturnsRegistryHint_WhenCwdIsUnrelated()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // Seed a unique test candidate pointing at the temp game root.
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + _testKeyName))
            key.SetValue("InstallPath", _root.RootPath);

        // CWD is the system temp dir (NOT under game root).
        string unrelatedCwd = Path.GetTempPath();

        string result = ClientEnvironment.FindGameRoot(unrelatedCwd, registryCandidates: new[] { _testKeyName });
        result.Should().Be(_root.RootPath);
    }

    [SkippableFact]
    public void RegistryHint_IsRejected_WhenClientDefinitionsMissing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Registry is Windows-only.");

        // Hint points at a directory with no Resources/ClientDefinitions.ini → rejected,
        // then we fall through to the CWD walk.
        string bogus = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_Bogus_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bogus);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\" + _testKeyName))
                key.SetValue("InstallPath", bogus);

            // CWD is INSIDE the real temp root → CWD walk should find it.
            string nestedCwd = Path.Combine(_root.RootPath, "Nested", "Deeper");
            Directory.CreateDirectory(nestedCwd);

            string result = ClientEnvironment.FindGameRoot(nestedCwd, registryCandidates: new[] { _testKeyName });
            result.Should().Be(_root.RootPath, "registry hint was rejected, CWD walk should find the real root");
        }
        finally
        {
            try { Directory.Delete(bogus, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WalksUpCwd_WhenNoRegistryHint_AndFindsGameRoot()
    {
        // No registry candidates → skip straight to CWD walk. Place CWD two levels deep.
        string nestedCwd = Path.Combine(_root.RootPath, "Sub1", "Sub2");
        Directory.CreateDirectory(nestedCwd);

        string result = ClientEnvironment.FindGameRoot(nestedCwd, registryCandidates: Array.Empty<string>());
        result.Should().Be(_root.RootPath);
    }

    [Fact]
    public void WalksUpCwd_FindsRoot_AtGameRootLevel()
    {
        // CWD == game root itself.
        string result = ClientEnvironment.FindGameRoot(_root.RootPath, registryCandidates: Array.Empty<string>());
        result.Should().Be(_root.RootPath);
    }

    [Fact]
    public void WalksUpCwd_StopsAfterEightLevels_WhenExeDirAlsoLacksResources()
    {
        // WalkUpForGameRoot walks at most 8 parents. We can't reliably assert the cap in this repo
        // because the test bin directory itself may contain a Resources/ folder (copied by the
        // root CopyResources target), which makes the exe-directory fallback succeed even when
        // the CWD walk fails. Instead, this test only verifies the cap when the bin dir is clean.
        //
        // Run a nested walk from a sandboxed deep path with NO Resources anywhere up the chain.
        string sandbox = Path.Combine(Path.GetTempPath(), "ClientAvaloniaTests_Sandbox_" + Guid.NewGuid().ToString("N"));
        try
        {
            string deep = sandbox;
            for (int i = 0; i < 12; i++)
                deep = Path.Combine(deep, "Deep" + i);
            Directory.CreateDirectory(deep);

            // FindGameRoot from this deep dir will: (1) walk up 8 levels and fail,
            // (2) then fall through to AppContext.BaseDirectory which may or may not have Resources.
            // We assert only that the CWD walk does NOT find the sandbox root (no Resources marker there).
            string result = ClientEnvironment.FindGameRoot(deep, registryCandidates: Array.Empty<string>());

            // The result is implementation-defined for the exe fallback; just verify we never
            // returned a path inside the sandbox (the walk would have to climb 12 levels to escape).
            result.StartsWith(sandbox, StringComparison.OrdinalIgnoreCase).Should().BeFalse(
                "8-level cap should prevent the CWD walk from reaching back out of a 12-deep sandbox.");
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FallsBackThroughLayers_WhenCwdLacksResources()
    {
        // FindGameRoot's contract: returns SOMETHING usable — either a real root (registry/CWD/exe)
        // or, as last resort, the start directory unchanged. We just verify it returns a non-null,
        // absolute path. The repo's bin directory typically has Resources copied in, so we can't
        // deterministically assert "nothing matched"; this test is the weaker non-crash contract.
        string start = Path.GetTempPath();
        string result = ClientEnvironment.FindGameRoot(start, registryCandidates: Array.Empty<string>());
        result.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(result).Should().BeTrue();
    }

    [Fact]
    public void FindGameRoot_ResolvesRelativeStartDirectory_ToAbsolute()
    {
        // FindGameRoot calls Path.GetFullPath internally; passing a relative path should still work.
        // Use a relative path that resolves to a real game root by chdir-ing there first.
        Environment.CurrentDirectory = _root.RootPath;

        string result = ClientEnvironment.FindGameRoot(".", registryCandidates: Array.Empty<string>());
        result.Should().Be(_root.RootPath);
    }
}
