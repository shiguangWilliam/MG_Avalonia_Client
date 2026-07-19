using System.IO;
using System.Linq;
using System.Text;
using ClientAvalonia.IniUi.Loading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Targeted diagnostic for QEC's BasedOn=MultiplayerGameLobby.ini SkirmishLobby overlay.
/// QEC ships an older INI layout that relies on transitive BasedOn chains
/// (SkirmishLobby -> MultiplayerGameLobby -> GenericWindow). Verifies that the entire merged
/// section set, including btnLaunchGame and chat controls, is present after the chain merge.
/// </summary>
public sealed class QecBasedOnChainTests
{
    private readonly ITestOutputHelper _output;
    public QecBasedOnChainTests(ITestOutputHelper output) => _output = output;

    private const string QecRoot = @"D:\MG\MG_Enc\QEC";

    [Fact]
    public void QEC_SkirmishLobby_AfterChainMerge_HasAllMultiplayerLobbySections()
    {
        string ini = Path.Combine(QecRoot, "Resources", "SkirmishLobby.ini");
        Skip.IfNot(File.Exists(ini), "QEC SkirmishLobby.ini not present");

        IniDocument doc = IniDocument.Load(ini);

        var sb = new StringBuilder();
        sb.AppendLine($"Sections after merge: {doc.Sections.Count}");
        foreach (var s in doc.Sections)
            sb.AppendLine($"  [{s.SectionName}] keys={s.Keys.Count}");
        _output.WriteLine(sb.ToString());

        // Sections defined in MultiplayerGameLobby.ini must survive the SkirmishLobby overlay merge.
        doc.GetSection("btnLaunchGame").Should().NotBeNull(
            "btnLaunchGame is defined in MultiplayerGameLobby.ini and must survive BasedOn chain merge");
        doc.GetSection("lbChatMessages_Host").Should().NotBeNull();
        doc.GetSection("tbChatInput_Host").Should().NotBeNull();
        doc.GetSection("MapPreviewBox").Should().NotBeNull();
        doc.GetSection("btnPickRandomMap").Should().NotBeNull();
        doc.GetSection("lbMapList").Should().NotBeNull();

        // GenericWindow.ini sections must also be present after transitive merge.
        doc.GetSection("MultiplayerGameLobby").Should().NotBeNull(
            "GenericWindow.ini must define the [MultiplayerGameLobby] window section");

        // SkirmishLobby's own overrides must apply on top.
        IniSection? skirmish = doc.GetSection("SkirmishLobby");
        skirmish.Should().NotBeNull("QEC SkirmishLobby overlay or its base must provide [SkirmishLobby]");
    }
}
