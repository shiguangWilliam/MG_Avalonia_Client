using System;
using System.IO;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// CnCNetGameLobby INI section resolution — regression for the black room lobby
/// (logical window name ≠ [MultiplayerGameLobby] section that carries Size/$CC).
/// </summary>
public sealed class CnCNetGameLobbySectionResolutionTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Category", "Usability")]
    public void ResolveWindowLoadTarget_CnCNetGameLobby_UsesMultiplayerGameLobbySection()
    {
        WriteMgLobbyIniChain();

        ClientEnvironment env = ClientEnvironment.Discover(_root.RootPath);
        (string iniPath, string sectionName)? target = env.ResolveWindowLoadTarget("CnCNetGameLobby");

        target.Should().NotBeNull();
        Path.GetFileName(target!.Value.iniPath).Should().Be("CnCNetGameLobby.ini");
        target.Value.sectionName.Should().Be(
            "MultiplayerGameLobby",
            "loading as [CnCNetGameLobby] falls back to GenericWindow and skips [MultiplayerGameLobby] as a foreign lobby");

        var engine = LayoutEngine.CreateForWindow(env, target.Value.iniPath, target.Value.sectionName);
        UiNodeTree tree = engine.LoadWindow(target.Value.iniPath, target.Value.sectionName);

        tree.Root.GetIntProp("Width").Should().BeGreaterThan(100);
        tree.FindNode("btnLeaveGame").Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void ResolveWindowLoadTarget_LANGameLobby_UsesMultiplayerGameLobbySection()
    {
        WriteMgLobbyIniChain();

        ClientEnvironment env = ClientEnvironment.Discover(_root.RootPath);
        (string iniPath, string sectionName)? target = env.ResolveWindowLoadTarget("LANGameLobby");

        target.Should().NotBeNull();
        Path.GetFileName(target!.Value.iniPath).Should().Be("LANGameLobby.ini");
        target.Value.sectionName.Should().Be("MultiplayerGameLobby");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void TryResolveOverlaySection_ResolvedSectionAlwaysExistsInDocument()
    {
        WriteMgLobbyIniChain();
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "OptionsWindow.ini"), """
            [Unrelated]
            X=1
            """);
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "GenericWindow.ini"), """
            [GenericWindow]
            DrawBorders=true

            [MultiplayerGameLobby]
            Size=1230,750

            [OptionsWindow]
            Size=800,600
            """);

        // Discover may prefer a registry install root over the temp tree; the contract under test is
        // still that the returned section must exist after BasedOn load (no blind name==section).
        ClientEnvironment env = ClientEnvironment.Discover(_root.RootPath);
        (string IniPath, string Section)? resolved = env.TryResolveOverlaySection("OptionsWindow", "OptionsWindow");

        resolved.Should().NotBeNull();
        IniDocument.Load(resolved!.Value.IniPath)
            .GetSection(resolved.Value.Section)
            .Should().NotBeNull(
                "overlay resolution must not return a logical name that is missing from the INI");
    }

    private void WriteMgLobbyIniChain()
    {
        Directory.CreateDirectory(Path.Combine(_root.RootPath, "Resources"));
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "GenericWindow.ini"), """
            [GenericWindow]
            DrawBorders=true

            [MultiplayerGameLobby]
            BackgroundTexture=gamelobbybg.png
            Size=1230,750
            """);
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "MultiplayerGameLobby.ini"), """
            [INISystem]
            BasedOn=GenericWindow.ini

            [MultiplayerGameLobby]
            $BaseSection=GenericWindow
            """);
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "LANGameLobby.ini"), """
            [INISystem]
            BasedOn=MultiplayerGameLobby.ini
            """);
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "CnCNetGameLobby.ini"), """
            [INISystem]
            BasedOn=LANGameLobby.ini

            [MultiplayerGameLobby]
            $CCMP99=btnLeaveGame:XNAClientButton
            """);
        File.WriteAllText(Path.Combine(_root.RootPath, "Resources", "ClientDefinitions.ini"), """
            [Settings]
            LocalGame=MG
            """);
    }
}
