using System;
using System.IO;
using System.Linq;
using ClientAvalonia.IniUi.Loading;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Unit tests for IniDocument's BasedOn chain merge logic. These do not require any
/// real mod on disk — they write synthetic INI hierarchies to temp files and assert
/// the post-merge IniDocument contents.
/// </summary>
public sealed class IniDocumentBasedOnChainTests : IDisposable
{
    private readonly string _tempDir;

    public IniDocumentBasedOnChainTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IniDocTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* test cleanup */ }
    }

    private string WriteIni(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // -------------------------------------------------------------------
    // 1-level BasedOn
    // -------------------------------------------------------------------

    [Fact]
    public void SingleLevel_Inherits_AllParentSections()
    {
        WriteIni("base.ini", """
            [Shared]
            Key1=base
            Key2=base

            [OnlyInBase]
            Foo=bar
            """);

        string child = WriteIni("child.ini", """
            [INISystem]
            BasedOn=base.ini

            [Shared]
            Key1=child

            [OnlyInChild]
            Baz=qux
            """);

        IniDocument doc = IniDocument.Load(child);

        IniSection shared = doc.GetSection("Shared")!;
        shared.GetStringValue("Key1", "").Should().Be("child", "child overrides parent");
        shared.GetStringValue("Key2", "").Should().Be("base", "child inherits undeclared keys");

        doc.GetSection("OnlyInBase").Should().NotBeNull("parent-only sections are inherited");
        doc.GetSection("OnlyInChild").Should().NotBeNull("child-only sections are kept");
    }

    // -------------------------------------------------------------------
    // 3-level transitive chain (mirrors QEC's real layout)
    // -------------------------------------------------------------------

    [Fact]
    public void ThreeLevel_TransitiveChain_MergesEndToEnd()
    {
        WriteIni("generic.ini", """
            [GenericWindow]
            Size=800,600

            [btnShared]
            Text=base
            """);

        WriteIni("multiplayer.ini", """
            [INISystem]
            BasedOn=generic.ini

            [MultiplayerGameLobby]
            Size=1280,720

            [btnShared]
            Text=multiplayer

            [btnLaunchGame]
            Location=0,700
            """);

        string skirmish = WriteIni("skirmish.ini", """
            [INISystem]
            BasedOn=multiplayer.ini

            [SkirmishLobby]
            Size=1280,720

            [btnShared]
            Location=0,710
            """);

        IniDocument doc = IniDocument.Load(skirmish);

        // Sections from all three files must be present
        doc.GetSection("GenericWindow").Should().NotBeNull();
        doc.GetSection("MultiplayerGameLobby").Should().NotBeNull();
        doc.GetSection("SkirmishLobby").Should().NotBeNull();
        doc.GetSection("btnShared").Should().NotBeNull();
        doc.GetSection("btnLaunchGame").Should().NotBeNull();

        // Override chain: generic → multiplayer → skirmish.
        // btnShared.Text was last set in multiplayer (skirmish doesn't override it)
        doc.GetSection("btnShared")!.GetStringValue("Text", "").Should().Be("multiplayer");
        // btnShared.Location was set in skirmish
        doc.GetSection("btnShared")!.GetStringValue("Location", "").Should().Be("0,710");
    }

    // -------------------------------------------------------------------
    // Missing BasedOn file (should not throw)
    // -------------------------------------------------------------------

    [Fact]
    public void MissingBasedOnFile_DoesNotThrow_OverlayPreserved()
    {
        string path = WriteIni("orphan.ini", """
            [INISystem]
            BasedOn=nonexistent.ini

            [Window]
            Title=test
            """);

        IniDocument doc = IniDocument.Load(path);

        doc.GetSection("Window").Should().NotBeNull();
        doc.GetSection("Window")!.GetStringValue("Title", "").Should().Be("test");
    }

    // -------------------------------------------------------------------
    // Multi-file BasedOn (comma-separated)
    // -------------------------------------------------------------------

    [Fact]
    public void MultiFileBasedOn_AccumulatesAllParents()
    {
        WriteIni("a.ini", "[SectionA]\nKeyA=valueA\n");
        WriteIni("b.ini", "[SectionB]\nKeyB=valueB\n");

        string child = WriteIni("child.ini", """
            [INISystem]
            BasedOn=a.ini,b.ini

            [SectionC]
            KeyC=valueC
            """);

        IniDocument doc = IniDocument.Load(child);

        doc.GetSection("SectionA").Should().NotBeNull();
        doc.GetSection("SectionB").Should().NotBeNull();
        doc.GetSection("SectionC").Should().NotBeNull();
    }

    // -------------------------------------------------------------------
    // Multiple BasedOn files override earlier ones (left wins on conflict? right wins?)
    // Documented here so future changes can detect regressions in merge order.
    // -------------------------------------------------------------------

    [Fact]
    public void MultiFileBasedOn_FirstListedFile_WinsOnConflict()
    {
        // Documented DX semantics: BasedOn files are processed left-to-right, and
        // Consolidate(base, overlay) treats the *current doc* (which accumulates earlier
        // parents + the child itself) as the overlay. So earlier parents override later
        // parents on conflicting keys, and the child always wins.
        WriteIni("first.ini", """
            [Shared]
            Value=from-first
            """);
        WriteIni("second.ini", """
            [Shared]
            Value=from-second
            """);

        string child = WriteIni("child.ini", """
            [INISystem]
            BasedOn=first.ini,second.ini

            [Shared]
            """);

        IniDocument doc = IniDocument.Load(child);

        doc.GetSection("Shared")!.GetStringValue("Value", "").Should().Be("from-first",
            "first parent's value wins because it is re-merged as overlay on top of second");
    }

    // -------------------------------------------------------------------
    // $BaseSection (per-section inheritance) basic
    // -------------------------------------------------------------------

    [Fact]
    public void BaseSection_Inherits_UndeclaredKeys()
    {
        string path = WriteIni("test.ini", """
            [Base]
            Width=100
            Height=100
            Color=red

            [Child]
            $BaseSection=Base
            Color=blue
            """);

        IniDocument doc = IniDocument.Load(path);

        IniSection child = doc.GetSection("Child")!;
        child.GetStringValue("Width", "").Should().Be("100", "inherited from Base");
        child.GetStringValue("Height", "").Should().Be("100", "inherited from Base");
        child.GetStringValue("Color", "").Should().Be("blue", "child overrides Base");
    }

    [Fact]
    public void BaseSection_MissingBase_DoesNotThrow()
    {
        string path = WriteIni("test.ini", """
            [Standalone]
            $BaseSection=NoSuchBase
            Key=value
            """);

        IniDocument doc = IniDocument.Load(path);

        IniSection section = doc.GetSection("Standalone")!;
        section.GetStringValue("Key", "").Should().Be("value");
    }

    // -------------------------------------------------------------------
    // Overlay section names (used by IniUiTreeBuilder to gate adoption)
    // -------------------------------------------------------------------

    [Fact]
    public void ParseOverlay_OnlyReturnsPhysicallyPresent_Sections()
    {
        WriteIni("base.ini", "[FromBase]\nKey=value\n");
        string childPath = WriteIni("child.ini", """
            [INISystem]
            BasedOn=base.ini

            [FromChild]
            Key=other
            """);

        IniDocument overlay = IniDocument.ParseOverlay(childPath);

        overlay.GetSection("FromChild").Should().NotBeNull();
        overlay.GetSection("FromBase").Should().BeNull("ParseOverlay excludes BasedOn-inherited sections");
    }
}
