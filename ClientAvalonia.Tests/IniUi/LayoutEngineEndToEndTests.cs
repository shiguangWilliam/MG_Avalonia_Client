using System;
using System.IO;
using System.Linq;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// End-to-end INI → tree → layout via LayoutEngine, using DXMainClient/Resources/DTA/MainMenu.ini
/// as the fixture. Mirrors what Program.cs --validate-ini does (without launching Avalonia).
///
/// The DXMainClient/ directory itself is a valid game root (Resources/ClientDefinitions.ini is
/// checked in), so we point ClientEnvironment at it.
/// </summary>
/// <remarks>
/// Skipped if the DXMainClient fixture directory is missing (e.g. shallow checkout).
/// </remarks>
public sealed class LayoutEngineEndToEndTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    private static string MainMenuIniPath => Path.Combine(RepoRoot, "DXMainClient", "Resources", "DTA", "MainMenu.ini");
    private static string DxMainClientRoot => Path.Combine(RepoRoot, "DXMainClient");

    [SkippableFact]
    public void MainMenuIni_Loads_AllExpectedControls()
    {
        Skip.IfNot(File.Exists(MainMenuIniPath), "DXMainClient/Resources/DTA/MainMenu.ini not present in checkout.");

        var env = ClientEnvironment.Discover(DxMainClientRoot);
        var engine = LayoutEngine.CreateForWindow(env, MainMenuIniPath, "MainMenu");

        UiNodeTree tree = engine.LoadWindow(MainMenuIniPath, "MainMenu");

        tree.Should().NotBeNull();
        tree.AllNodes().Count().Should().BeGreaterThan(5, "MainMenu has many controls");

        // Known DX control ids — locked by MainMenu.ini section names.
        tree.FindNode("btnSkirmish").Should().NotBeNull();
        tree.FindNode("btnCnCNet").Should().NotBeNull();
        tree.FindNode("btnOptions").Should().NotBeNull();
        tree.FindNode("btnExit").Should().NotBeNull();
        tree.FindNode("lblVersion").Should().NotBeNull();
    }

    [SkippableFact]
    public void MainMenuIni_HasRootWindow_WithChildren()
    {
        Skip.IfNot(File.Exists(MainMenuIniPath), "MainMenu.ini not present.");

        var env = ClientEnvironment.Discover(DxMainClientRoot);
        var engine = LayoutEngine.CreateForWindow(env, MainMenuIniPath, "MainMenu");

        UiNodeTree tree = engine.LoadWindow(MainMenuIniPath, "MainMenu");

        tree.Root.Children.Should().NotBeEmpty("MainMenu must have child controls");
        tree.Root.GetIntProp("Width").Should().BeGreaterThan(0);
        tree.Root.GetIntProp("Height").Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public void MainMenuIni_BtnSkirmish_HasGeometryProps()
    {
        Skip.IfNot(File.Exists(MainMenuIniPath), "MainMenu.ini not present.");

        var env = ClientEnvironment.Discover(DxMainClientRoot);
        var engine = LayoutEngine.CreateForWindow(env, MainMenuIniPath, "MainMenu");
        UiNodeTree tree = engine.LoadWindow(MainMenuIniPath, "MainMenu");

        UiNode? btn = tree.FindNode("btnSkirmish");
        btn.Should().NotBeNull();
        btn!.GetIntProp("Width").Should().BeGreaterThan(0, "btnSkirmish must have a width after layout");
        btn.GetIntProp("Height").Should().BeGreaterThan(0, "btnSkirmish must have a height after layout");
    }

    [SkippableFact]
    public void MainMenuIni_BtnSkirmish_TextureResolves()
    {
        // DX idle textures are relative to Resources/DTA/ — verify the resolver finds them.
        Skip.IfNot(File.Exists(MainMenuIniPath), "MainMenu.ini not present.");

        var env = ClientEnvironment.Discover(DxMainClientRoot);
        var engine = LayoutEngine.CreateForWindow(env, MainMenuIniPath, "MainMenu");
        UiNodeTree tree = engine.LoadWindow(MainMenuIniPath, "MainMenu");

        UiNode? btn = tree.FindNode("btnSkirmish");
        btn.Should().NotBeNull();
        btn!.Props.TryGetValue("IdleTexture", out object? tex).Should().BeTrue("btnSkirmish has an IdleTexture in MainMenu.ini");

        string? resolved = engine.Resources.ResolveTexturePath(tex?.ToString());
        resolved.Should().NotBeNull("IdleTexture must resolve to a real file");
        File.Exists(resolved).Should().BeTrue($"resolved texture should exist: {resolved}");
    }

    [SkippableFact]
    public void OptionsWindowIni_PanelSection_LoadsWithoutCrash()
    {
        // DX OptionsWindow.ini is panel-structured — no [OptionsWindow] section. Use a real
        // panel section name as the window to exercise the tree builder against a non-trivial INI.
        string optionsIni = Path.Combine(RepoRoot, "DXMainClient", "Resources", "DTA", "OptionsWindow.ini");
        Skip.IfNot(File.Exists(optionsIni), "OptionsWindow.ini not present.");

        var env = ClientEnvironment.Discover(DxMainClientRoot);
        var engine = LayoutEngine.CreateForWindow(env, optionsIni, "DisplayOptionsPanelExtraControls");

        UiNodeTree tree = engine.LoadWindow(optionsIni, "DisplayOptionsPanelExtraControls");

        tree.AllNodes().Count().Should().BeGreaterThan(0, "panel section has at least one control");
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test bin directory to find the repo root (contains DXMainClient/).
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(current, "DXMainClient")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                break;
            current = parent.FullName;
        }
        return AppContext.BaseDirectory;
    }
}
