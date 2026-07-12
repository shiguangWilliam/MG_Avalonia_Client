using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.Integration;

/// <summary>
/// Subprocess smoke tests for the headless validate-modes in Program.cs
/// (--validate-ini, --validate-bindings, --validate-resources, --dump-tree).
///
/// These mirror what CI would run as a sanity gate after building ClientAvalonia.exe.
/// Marked Category=Integration so CI can opt-out via --filter Category!=Integration.
///
/// Skipped if ClientAvalonia.exe isn't built (developer-only checkouts without a prior build).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ValidateModesTests
{
    private static readonly string RepoRoot = LocateRepoRoot();
    private readonly ITestOutputHelper _output;

    public ValidateModesTests(ITestOutputHelper output) => _output = output;

    private static string ExePath => Path.Combine(RepoRoot, "ClientAvalonia", "bin", "Debug", "net8.0", "ClientAvalonia.exe");
    private static string MainMenuIni => Path.Combine(RepoRoot, "DXMainClient", "Resources", "DTA", "MainMenu.ini");

    [SkippableFact]
    public void ValidateIni_ExitsZero_OnMainMenuIni()
    {
        Skip.IfNot(File.Exists(ExePath), "ClientAvalonia.exe not built; run 'dotnet build ClientAvalonia' first.");
        Skip.IfNot(File.Exists(MainMenuIni), "MainMenu.ini fixture missing.");

        (int exitCode, string stdout, string stderr) = RunExe("--validate-ini", MainMenuIni);

        _output.WriteLine($"exit={exitCode}");
        _output.WriteLine($"stdout={stdout}");
        _output.WriteLine($"stderr={stderr}");

        exitCode.Should().Be(0, "validate-ini should succeed on a well-formed MainMenu.ini");
        stdout.Should().Contain("OK:", "validate-ini emits an OK line on success");
        stdout.Should().Contain("btnSkirmish", "summary line names btnSkirmish geometry");
    }

    [SkippableFact]
    public void DumpTree_ExitsZero_OnMainMenuIni()
    {
        Skip.IfNot(File.Exists(ExePath), "ClientAvalonia.exe not built.");
        Skip.IfNot(File.Exists(MainMenuIni), "MainMenu.ini fixture missing.");

        (int exitCode, string stdout, _) = RunExe("--dump-tree", MainMenuIni, "MainMenu");

        exitCode.Should().Be(0);
        stdout.Should().Contain("Tree:");
        stdout.Should().Contain("btnSkirmish");
    }

    [SkippableFact]
    public void ValidateBindings_ExitsZero_WithMainMenuAsBindingSource()
    {
        // --validate-bindings needs a window section. DX's OptionsWindow.ini is panel-structured
        // (no [OptionsWindow] section), so we reuse MainMenu.ini which has [MainMenu] + lblVersion
        // (the canonical binding target used by Program.cs's own sample output).
        Skip.IfNot(File.Exists(ExePath), "ClientAvalonia.exe not built.");
        Skip.IfNot(File.Exists(MainMenuIni), "MainMenu.ini fixture missing.");

        (int exitCode, string stdout, string stderr) = RunExe("--validate-bindings", MainMenuIni, "MainMenu");

        _output.WriteLine($"exit={exitCode}");
        _output.WriteLine($"stdout={stdout}");
        _output.WriteLine($"stderr={stderr}");

        exitCode.Should().Be(0);
        stdout.Should().Contain("OK:");
    }

    [SkippableFact]
    public void ValidateResources_ExitsZero_OrFails_WithClearMessage()
    {
        // --validate-resources runs the full ClientStartupService + GameResourceCatalog.
        // In a developer checkout it may succeed or fail depending on whether Resources/ is
        // populated; we just require it doesn't crash silently (exit code 0 or 1, not -1).
        Skip.IfNot(File.Exists(ExePath), "ClientAvalonia.exe not built.");

        (int exitCode, string stdout, string stderr) = RunExe("--validate-resources");

        _output.WriteLine($"exit={exitCode}");
        _output.WriteLine($"stdout={stdout}");
        _output.WriteLine($"stderr={stderr}");

        // Either OK: maps=... or FAIL: ... — both are valid outcomes; a hang/crash would manifest
        // as a timeout or exit code outside [0,1].
        (exitCode == 0 || exitCode == 1).Should().BeTrue($"expected clean exit, got {exitCode}");
        (stdout + stderr).Should().NotBeEmpty("validate-resources always produces output");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunExe(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        bool exited = proc.WaitForExit(TimeSpan.FromSeconds(60));
        if (!exited)
        {
            try { proc.Kill(); } catch { }
            return (-1, stdout, stderr + "\n[TIMEOUT after 60s]");
        }
        return (proc.ExitCode, stdout, stderr);
    }

    private static string LocateRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(current, "ClientAvalonia")) && Directory.Exists(Path.Combine(current, "DXMainClient")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return AppContext.BaseDirectory;
    }
}
