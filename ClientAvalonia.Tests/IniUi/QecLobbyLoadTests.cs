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
/// End-to-end smoke test: load QEC's real MultiplayerGameLobby.ini and assert that
/// the resulting UiNodeTree contains every control section declared in the INI.
/// </summary>
public sealed class QecLobbyLoadTests
{
    private readonly ITestOutputHelper _output;
    public QecLobbyLoadTests(ITestOutputHelper output) => _output = output;

    private const string QecRoot = @"D:\MG\MG_Enc\QEC";
    private static string QecMultiplayerIni => Path.Combine(QecRoot, "Resources", "MultiplayerGameLobby.ini");

    [SkippableFact]
    public void QEC_MultiplayerLobby_Loads_AllDeclaredControls()
    {
        Skip.IfNot(File.Exists(QecMultiplayerIni), "QEC install not present at D:\\MG\\MG_Enc\\QEC");

        var env = ClientEnvironment.Discover(QecRoot);
        var engine = LayoutEngine.CreateForWindow(env, QecMultiplayerIni, "MultiplayerGameLobby");
        UiNodeTree tree = engine.LoadWindow(QecMultiplayerIni, "MultiplayerGameLobby");

        _output.WriteLine($"=== Loaded tree ({tree.AllNodes().Count()} nodes) ===");
        foreach (UiNode node in tree.AllNodes())
            _output.WriteLine($"  {node.Id} [{node.ControlType}] parent={node.Parent?.Id}");

        // Every section declared in the QEC INI must end up in the tree.
        string[] expectedControls =
        [
            "lblName", "lblSide", "lblStart", "lblColor", "lblTeam",
            "ddPlayerStart0", "ddPlayerStart1", "ddPlayerStart2", "ddPlayerStart3",
            "ddPlayerStart4", "ddPlayerStart5", "ddPlayerStart6", "ddPlayerStart7",
            "lbChatMessages_Host", "lbChatMessages_Player",
            "tbChatInput_Host", "tbChatInput_Player",
            "lblGameModeSelect", "ddGameMode", "lbMapList",
            "tbMapSearch", "btnPickRandomMap",
            "PlayerOptionsPanel", "GameOptionsPanel", "MapPreviewBox",
            "lblMapName", "lblMapAuthor", "lblGameMode", "lblMapSize",
            "btnLaunchGame", "btnLockGame", "chkAutoReady",
            "btnChangeTunnel", "btnLeaveGame",
        ];

        foreach (string id in expectedControls)
        {
            tree.FindNode(id).Should().NotBeNull($"QEC INI declares [{id}] — must be adopted into the tree");
        }
    }
}
