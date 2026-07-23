using System.IO;
using System.Linq;
using ClientAvalonia.IniUi.Loading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Diagnostic: load QEC's real MultiplayerGameLobby.ini (with BasedOn=GenericWindow.ini)
/// and dump what sections are visible after the BasedOn merge.
/// </summary>
public sealed class QecCompatibilityDiagnosticTests
{
    private readonly ITestOutputHelper _output;
    public QecCompatibilityDiagnosticTests(ITestOutputHelper output) => _output = output;

    private const string QecRoot = @"D:\MG\MG_Enc\QEC";
    private static string QecMultiplayerIni => Path.Combine(QecRoot, "Resources", "MultiplayerGameLobby.ini");
    private static string QecGenericIni => Path.Combine(QecRoot, "Resources", "GenericWindow.ini");

    [SkippableFact]
    public void QEC_MultiplayerGameLobby_AfterBasedOnMerge_HasExpectedSections()
    {
        Skip.IfNot(File.Exists(QecMultiplayerIni), "QEC install not present at D:\\MG\\MG_Enc\\QEC");

        IniDocument doc = IniDocument.Load(QecMultiplayerIni);

        _output.WriteLine($"=== Sections after BasedOn merge ({doc.Sections.Count} total) ===");
        foreach (var s in doc.Sections)
            _output.WriteLine($"  [{s.SectionName}]  keys={s.Keys.Count}");

        // The root window section MUST be reachable after merge — either [MultiplayerGameLobby]
        // from GenericWindow.ini or [GenericWindow] from GenericWindow.ini.
        bool hasMpSection = doc.GetSection("MultiplayerGameLobby") != null;
        bool hasGenericSection = doc.GetSection("GenericWindow") != null;
        hasMpSection.Should().BeTrue("[MultiplayerGameLobby] should be merged from GenericWindow.ini");
        hasGenericSection.Should().BeTrue("[GenericWindow] should be merged from GenericWindow.ini");

        // Spot-check a known control section in the overlay file.
        doc.GetSection("btnLaunchGame").Should().NotBeNull();
        doc.GetSection("lbMapList").Should().NotBeNull();
        doc.GetSection("MapPreviewBox").Should().NotBeNull();
    }

    [SkippableFact]
    public void QEC_GenericWindow_Has_Size_For_MultiplayerGameLobby_Section()
    {
        Skip.IfNot(File.Exists(QecGenericIni), "QEC install not present");

        IniDocument generic = IniDocument.Load(QecGenericIni);
        var mp = generic.GetSection("MultiplayerGameLobby");
        mp.Should().NotBeNull();
        mp!.GetStringValue("Size", "").Should().Be("1230,750",
            "QEC declares MultiplayerGameLobby window size inside GenericWindow.ini");
    }
}
