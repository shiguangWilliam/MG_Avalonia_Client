using System;
using System.Collections.Generic;
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
/// Three-mod regression suite: load real INI files from the user's actual MG / LNOD / QEC
/// installations and assert that every gameplay-critical control is present in the resulting
/// UiNodeTree with the correct type. This guards against regressions in TreeBuilder semantics
/// or type inference (the 2026-07 QEC fix where lbl*/lb*/tb* prefix handling was added).
///
/// Skip automatically when the mod install directory is not present (CI / foreign checkout).
/// </summary>
public sealed class ThreeModCompatibilityTests
{
    private readonly ITestOutputHelper _output;
    public ThreeModCompatibilityTests(ITestOutputHelper output) => _output = output;

    private const string MgRoot = @"D:\MG\MG-Avalonia测试区3";
    private const string LnodRoot = @"D:\MG\LNod5.15";
    private const string QecRoot = @"D:\MG\MG_Enc\QEC";

    public static IEnumerable<object[]> ModRoots => new[]
    {
        new object[] { "MG", MgRoot },
        new object[] { "LNOD", LnodRoot },
        new object[] { "QEC", QecRoot },
    };

    // ---------------------------------------------------------------------
    // MainMenu.ini — every mod must load all 7+ core navigation buttons.
    // ---------------------------------------------------------------------

    [Theory, MemberData(nameof(ModRoots))]
    public void MainMenu_Loads_CoreNavigationButtons(string modName, string modRoot)
    {
        string iniPath = Path.Combine(modRoot, "Resources", "MainMenu.ini");
        Skip.IfNot(File.Exists(iniPath), $"{modName} MainMenu.ini not present at {modRoot}");

        var env = ClientEnvironment.Discover(modRoot);
        var engine = LayoutEngine.CreateForWindow(env, iniPath, "MainMenu");
        UiNodeTree tree = engine.LoadWindow(iniPath, "MainMenu");

        Dump(modName, "MainMenu", tree);

        // DX MainMenu.cs code-behind creates these via AddChild; every mod's INI must surface
        // them in the tree for the navigation behaviors (MainMenuBehaviors) to bind.
        tree.FindNode("btnNewCampaign").Should().NotBeNull($"{modName} must surface btnNewCampaign");
        tree.FindNode("btnLoadGame").Should().NotBeNull($"{modName} must surface btnLoadGame");
        tree.FindNode("btnSkirmish").Should().NotBeNull($"{modName} must surface btnSkirmish");
        tree.FindNode("btnCnCNet").Should().NotBeNull($"{modName} must surface btnCnCNet");
        tree.FindNode("btnOptions").Should().NotBeNull($"{modName} must surface btnOptions");
        tree.FindNode("btnExit").Should().NotBeNull($"{modName} must surface btnExit");

        // Labels must be XNALabel (regression: prior fix needed prefix-match for lbl*).
        UiNode? lblVersion = tree.FindNode("lblVersion");
        lblVersion.Should().NotBeNull();
        lblVersion!.ControlType.Should().Be("XNALabel",
            $"{modName} lblVersion must resolve to XNALabel (prefix inference for lbl*)");

        UiNode? lblCnCNetStatus = tree.FindNode("lblCnCNetStatus");
        lblCnCNetStatus.Should().NotBeNull();
        lblCnCNetStatus!.ControlType.Should().Be("XNALabel");
    }

    // ---------------------------------------------------------------------
    // MultiplayerGameLobby.ini — every mod must load map list, map preview,
    // chat boxes, and launch button with the correct types.
    // ---------------------------------------------------------------------

    [Theory, MemberData(nameof(ModRoots))]
    public void MultiplayerLobby_Loads_MapAndChatControls(string modName, string modRoot)
    {
        string iniPath = Path.Combine(modRoot, "Resources", "MultiplayerGameLobby.ini");
        Skip.IfNot(File.Exists(iniPath), $"{modName} MultiplayerGameLobby.ini not present");

        var env = ClientEnvironment.Discover(modRoot);
        var engine = LayoutEngine.CreateForWindow(env, iniPath, "MultiplayerGameLobby");
        UiNodeTree tree = engine.LoadWindow(iniPath, "MultiplayerGameLobby");

        Dump(modName, "MultiplayerGameLobby", tree);

        // Map binding (GameDataBindingApplier relies on these names).
        UiNode? lbMapList = tree.FindNode("lbMapList");
        lbMapList.Should().NotBeNull($"{modName} must surface lbMapList for map binding");
        lbMapList!.ControlType.Should().Be("XNAMultiColumnListBox",
            $"{modName} lbMapList must be XNAMultiColumnListBox");

        UiNode? ddGameMode = tree.FindNode("ddGameMode");
        ddGameMode.Should().NotBeNull();
        ddGameMode!.ControlType.Should().Be("XNAClientDropDown");

        UiNode? tbMapSearch = tree.FindNode("tbMapSearch");
        tbMapSearch.Should().NotBeNull();
        tbMapSearch!.ControlType.Should().Be("XNASuggestionTextBox");

        // Launch button is a GameLaunchButton in DX (handles state machine). Critical for gameplay.
        UiNode? btnLaunchGame = tree.FindNode("btnLaunchGame");
        btnLaunchGame.Should().NotBeNull();
        btnLaunchGame!.ControlType.Should().Be("GameLaunchButton",
            $"{modName} btnLaunchGame must be GameLaunchButton (exact-name inference)");

        // Chat (LNOD/MG/QEC all use _Host and _Player variants).
        UiNode? host = tree.FindNode("lbChatMessages_Host");
        UiNode? player = tree.FindNode("lbChatMessages_Player");
        host.Should().NotBeNull($"{modName} must surface lbChatMessages_Host");
        player.Should().NotBeNull($"{modName} must surface lbChatMessages_Player");
        host!.ControlType.Should().Be("ChatListBox",
            $"{modName} lbChatMessages_Host must be ChatListBox (prefix-with-suffix inference)");
        player!.ControlType.Should().Be("ChatListBox");

        UiNode? inputHost = tree.FindNode("tbChatInput_Host");
        UiNode? inputPlayer = tree.FindNode("tbChatInput_Player");
        inputHost.Should().NotBeNull();
        inputPlayer.Should().NotBeNull();
        inputHost!.ControlType.Should().Be("XNAChatTextBox");
        inputPlayer!.ControlType.Should().Be("XNAChatTextBox");
    }

    // ---------------------------------------------------------------------
    // SkirmishLobby.ini — DX-based mods (MG/LNOD) usually have minimal
    // overlay INIs; QEC may inherit entirely from MultiplayerGameLobby.ini.
    // ---------------------------------------------------------------------

    [Theory, MemberData(nameof(ModRoots))]
    public void SkirmishLobby_Loads_LaunchButton(string modName, string modRoot)
    {
        string iniPath = Path.Combine(modRoot, "Resources", "SkirmishLobby.ini");
        Skip.IfNot(File.Exists(iniPath), $"{modName} SkirmishLobby.ini not present");

        var env = ClientEnvironment.Discover(modRoot);
        var engine = LayoutEngine.CreateForWindow(env, iniPath, "SkirmishLobby");
        UiNodeTree tree = engine.LoadWindow(iniPath, "SkirmishLobby");

        Dump(modName, "SkirmishLobby", tree);

        // btnLaunchGame should be reachable through $BaseSection=MultiplayerGameLobby or directly.
        UiNode? btnLaunchGame = tree.FindNode("btnLaunchGame");
        btnLaunchGame.Should().NotBeNull($"{modName} SkirmishLobby must surface btnLaunchGame via inheritance");
        btnLaunchGame!.ControlType.Should().Be("GameLaunchButton");
    }

    // ---------------------------------------------------------------------
    // GenericWindow.ini — every mod must define window-level defaults for
    // every top-level window referenced in code.
    // ---------------------------------------------------------------------

    [Theory, MemberData(nameof(ModRoots))]
    public void GenericWindow_Defines_LobbyWindowSizes(string modName, string modRoot)
    {
        string iniPath = Path.Combine(modRoot, "Resources", "GenericWindow.ini");
        Skip.IfNot(File.Exists(iniPath), $"{modName} GenericWindow.ini not present");

        IniDocument doc = IniDocument.Load(iniPath);

        // GameLobbyBase-equivalent sections (SkirmishLobby / MultiplayerGameLobby) must
        // declare a Size so the window is created at the correct resolution.
        IniSection? skirmish = doc.GetSection("SkirmishLobby");
        skirmish.Should().NotBeNull($"{modName} GenericWindow.ini must define [SkirmishLobby]");
        skirmish!.GetStringValue("Size", "").Should().NotBeEmpty(
            $"{modName} [SkirmishLobby] must declare Size");

        IniSection? multi = doc.GetSection("MultiplayerGameLobby");
        multi.Should().NotBeNull($"{modName} GenericWindow.ini must define [MultiplayerGameLobby]");
        multi!.GetStringValue("Size", "").Should().NotBeEmpty(
            $"{modName} [MultiplayerGameLobby] must declare Size");
    }

    private void Dump(string modName, string windowName, UiNodeTree tree)
    {
        _output.WriteLine($"=== {modName} / {windowName} ({tree.AllNodes().Count()} nodes) ===");
        foreach (UiNode node in tree.AllNodes())
            _output.WriteLine($"  {node.Id} [{node.ControlType}] parent={node.Parent?.Id}");
    }
}
