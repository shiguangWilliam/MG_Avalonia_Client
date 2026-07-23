using System;
using System.IO;
using System.Linq;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Diagnostic tests: dump the UiNodeTree shape after loading DX sample SkirmishLobby,
/// and verify map-related controls are reachable where GameDataBindingApplier expects them.
/// </summary>
public sealed class LobbyTreeStructureDiagnosticTests
{
    private readonly ITestOutputHelper _output;
    public LobbyTreeStructureDiagnosticTests(ITestOutputHelper output) => _output = output;

    private static readonly string RepoRoot = LocateRepoRoot();
    private static string DxRoot => Path.Combine(RepoRoot, "DXMainClient");
    private static string SkirmishLobbyIni => Path.Combine(DxRoot, "Resources", "DTA", "SkirmishLobby.ini");

    [SkippableFact]
    public void SkirmishLobby_MapControls_Reachable_FromRoot()
    {
        Skip.IfNot(File.Exists(SkirmishLobbyIni), "DTA SkirmishLobby.ini not present.");

        var env = ClientEnvironment.Discover(DxRoot);
        var engine = LayoutEngine.CreateForWindow(env, SkirmishLobbyIni, "SkirmishLobby");
        UiNodeTree tree = engine.LoadWindow(SkirmishLobbyIni, "SkirmishLobby");

        Dump(tree);

        tree.FindNode("lbMapList").Should().NotBeNull("lbMapList must exist for map list binding");
        tree.FindNode("MapPreviewBox").Should().NotBeNull("MapPreviewBox must exist for preview binding");
        tree.FindNode("ddGameMode").Should().NotBeNull("ddGameMode must exist for game mode filter");
        tree.FindNode("btnPickRandomMap").Should().NotBeNull();
        tree.FindNode("btnSaveLoadGameOptions").Should().NotBeNull();
        tree.FindNode("GameOptionsPanel").Should().NotBeNull();
        tree.FindNode("PlayerOptionsPanel").Should().NotBeNull();
        tree.FindNode("cmbTechLevel").Should().NotBeNull();
        tree.FindNode("chkBases").Should().NotBeNull();
    }

    private void Dump(UiNodeTree tree)
    {
        _output.WriteLine($"Source: {tree.SourcePath}");
        _output.WriteLine($"Root: {tree.Root.Id} [{tree.Root.ControlType}]");
        foreach (UiNode node in tree.AllNodes().Skip(1))
        {
            int depth = GetDepth(tree.Root, node);
            string indent = new string(' ', depth * 2);
            string parent = node.Parent?.Id ?? "(null)";
            _output.WriteLine($"{indent}{node.Id} [{node.ControlType}] parent={parent}");
        }
    }

    private static int GetDepth(UiNode root, UiNode target, int current = 0)
    {
        if (ReferenceEquals(root, target)) return current;
        foreach (UiNode child in root.Children)
        {
            int found = GetDepth(child, target, current + 1);
            if (found >= 0) return found;
        }
        return -1;
    }

    private static string LocateRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(current, "DXMainClient")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return AppContext.BaseDirectory;
    }
}
