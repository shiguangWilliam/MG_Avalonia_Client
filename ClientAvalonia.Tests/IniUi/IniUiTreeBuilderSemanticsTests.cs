using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientAvalonia.IniUi.Ast;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Schema;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// DX-semantics alignment tests for IniUiTreeBuilder.Build.
///
/// The fork builds UI as INI → AST → UiNode tree → axaml, while DX is INI → XNA control tree.
/// The CHILDREN-TREE CONSTRUCTION RULES must be aligned even though the runtime representation
/// differs. This suite locks in the rules captured from the DX reference implementation
/// (ClientGUI/INItializableWindow.cs, XNAWindowBase.cs, GameLobbyBase.cs):
///
///   R2  [ExtraControls] section          — top-level control declaration list
///   R3  [$ExtraControls] section         — same, with $CC-prefixed keys
///   R4  [$CCnn=name:Type] inside parent  — child declaration within a section
///   R5  [$BaseSection=Name]              — key-merge inheritance (handled by IniDocument)
///   R6  [ChildName] standalone section   — config block for an already-declared child
///   R7  Standalone section without $CC   — adopted by name, type inferred
///   R8  Panel with $CC children          — nested children materialized recursively
/// </summary>
public sealed class IniUiTreeBuilderSemanticsTests
{
    private static UiNodeTree BuildFromText(string ini, string windowName = "TestWindow")
    {
        string path = Path.Combine(Path.GetTempPath(), $"tree-test-{System.Guid.NewGuid():N}.ini");
        File.WriteAllText(path, ini);
        try
        {
            IniFileAst ast = IniAstBuilder.BuildFromFile(path);
            var registry = DefaultControlRegistry.Create();
            var builder = new IniUiTreeBuilder(registry, new PropertyResolver(registry, new PassthroughLocalizationService()));
            return builder.Build(ast, windowName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>R6 + R7: a standalone control section (no $CC declaration) must be adopted.</summary>
    [Fact]
    public void Standalone_Section_Is_Adopted_As_Control()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [btnSave]
            Text=Save
            Location=10,20
            """);

        UiNode? btn = tree.FindNode("btnSave");
        btn.Should().NotBeNull("standalone section must be adopted by name");
        btn!.ControlType.Should().Be("XNAClientButton");
        btn.Parent.Should().Be(tree.Root);
    }

    /// <summary>
    /// Regression: sections that only carry layout keys ($Width/$Height/$Y) were previously
    /// rejected by SectionLooksLikeControl and silently dropped. DX treats them as panels.
    /// </summary>
    [Fact]
    public void Layout_Only_Section_Is_Adopted_As_Panel()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [SomeContainer]
            $Height=300
            $Y=50
            """);

        UiNode? node = tree.FindNode("SomeContainer");
        node.Should().NotBeNull("layout-only section must be adopted");
        node!.ControlType.Should().Be("XNAPanel",
            "no widget-specific keys => plain panel by default");
    }

    /// <summary>
    /// R8 alignment: when an orphan panel is adopted, any $CC children it declares must be
    /// materialized too. This was the #1 cause of "UI group disappears" — the panel was
    /// rendered but its child dropdowns/checkboxes were never created.
    /// </summary>
    [Fact]
    public void Orphan_Panel_With_CC_Children_Materializes_Grandchildren()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [GameOptionsPanel]
            $Width=400
            $Height=300
            $CC_00=cmbTechLevel:GameLobbyDropDown
            $CC_01=chkBases:GameLobbyCheckBox

            [cmbTechLevel]
            Items=10,9,8
            SpawnIniOption=TechLevel

            [chkBases]
            Text=Bases
            SpawnIniOption=Bases
            """);

        UiNode? panel = tree.FindNode("GameOptionsPanel");
        panel.Should().NotBeNull();
        panel!.Children.Should().Contain(c => c.Id == "cmbTechLevel", "$CC child must be built");
        panel.Children.Should().Contain(c => c.Id == "chkBases", "$CC child must be built");

        UiNode? cmb = tree.FindNode("cmbTechLevel");
        cmb!.ControlType.Should().Be("GameLobbyDropDown");
        UiNode? chk = tree.FindNode("chkBases");
        chk!.ControlType.Should().Be("GameLobbyCheckBox");
    }

    /// <summary>R5 alignment: $BaseSection inheritance propagates keys to the derived section.</summary>
    [Fact]
    public void BaseSection_Merges_Keys_Before_Adoption()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [lbChatMessages]
            $X=10
            $Y=20
            $Width=400
            $Height=200

            [lbChatMessages_Player]
            $BaseSection=lbChatMessages
            $Height=100
            """);

        UiNode? player = tree.FindNode("lbChatMessages_Player");
        player.Should().NotBeNull();
        // Inherited $X/$Y/$Width from base, override $Height.
        player!.Props.Should().ContainKey("Width");
        player.Props["Width"].Should().NotBeNull();
    }

    /// <summary>
    /// btnLaunchGame must be inferred as GameLaunchButton (DX typed subclass), not XNAClientButton.
    /// Generic btn prefix matching runs after the explicit-name table.
    /// </summary>
    [Fact]
    public void BtnLaunchGame_Is_GameLaunchButton_Not_GenericButton()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [btnLaunchGame]
            Text=Launch Game
            """);

        UiNode? btn = tree.FindNode("btnLaunchGame");
        btn!.ControlType.Should().Be("GameLaunchButton");
    }

    /// <summary>MapPreviewBox exact-name inference.</summary>
    [Fact]
    public void MapPreviewBox_Is_MapPreviewBox_Type()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [MapPreviewBox]
            $Width=802
            """);

        UiNode? preview = tree.FindNode("MapPreviewBox");
        preview!.ControlType.Should().Be("MapPreviewBox");
    }

    /// <summary>ChatListBox / XNAMultiColumnListBox exact-name inference.</summary>
    [Theory]
    [InlineData("lbChatMessages", "ChatListBox")]
    [InlineData("lbGameList", "ChatListBox")]
    [InlineData("lbPlayerList", "ChatListBox")]
    [InlineData("lbMapList", "XNAMultiColumnListBox")]
    [InlineData("lbCampaignList", "XNAListBox")]
    public void Known_ListBox_Names_Map_To_DX_Types(string sectionName, string expectedType)
    {
        UiNodeTree tree = BuildFromText($"""
            [TestWindow]

            [{sectionName}]
            $Width=100
            """);

        UiNode? node = tree.FindNode(sectionName);
        node!.ControlType.Should().Be(expectedType);
    }

    /// <summary>cmb-prefixed GameLobby option sections infer GameLobbyDropDown, not XNAClientDropDown.</summary>
    [Fact]
    public void Cmb_With_GameOption_Keys_Is_GameLobbyDropDown()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [cmbTechLevel]
            Items=10,9,8
            SpawnIniOption=TechLevel
            """);

        UiNode? cmb = tree.FindNode("cmbTechLevel");
        cmb!.ControlType.Should().Be("GameLobbyDropDown");
    }

    /// <summary>chk-prefixed GameLobby option sections infer GameLobbyCheckBox.</summary>
    [Fact]
    public void Chk_With_GameOption_Keys_Is_GameLobbyCheckBox()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [chkBases]
            Text=Bases
            SpawnIniOption=Bases
            """);

        UiNode? chk = tree.FindNode("chkBases");
        chk!.ControlType.Should().Be("GameLobbyCheckBox");
    }

    /// <summary>INISystem and other meta sections are never adopted as controls.</summary>
    [Theory]
    [InlineData("INISystem")]
    [InlineData("$ExtraControls")]
    public void Meta_Sections_Are_Skipped(string metaName)
    {
        UiNodeTree tree = BuildFromText($"""
            [{metaName}]
            BasedOn=Other.ini

            [TestWindow]
            """);

        tree.FindNode(metaName).Should().BeNull("meta section must not be adopted");
    }

    /// <summary>
    /// [ExtraControls] is meta (a control declaration list, not a control itself) — but with
    /// its own special handling. It must never appear as a child node.
    /// </summary>
    [Fact]
    public void ExtraControls_Section_Is_Meta_Not_Control()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [ExtraControls]
            0=btnHelp:XNAClientButton

            [btnHelp]
            Text=Help
            """);

        tree.FindNode("ExtraControls").Should().BeNull("ExtraControls is a declaration list, not a control");
    }

    /// <summary>R2/R3: ExtraControls top-level declarations create direct children of root.</summary>
    [Fact]
    public void ExtraControls_Section_Creates_Root_Children()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [ExtraControls]
            0=Logo:XNAPanel
            1=btnHelp:XNAClientButton

            [Logo]
            BackgroundTexture=logo.png

            [btnHelp]
            Text=Help
            """);

        tree.Root.Children.Should().Contain(c => c.Id == "Logo");
        tree.Root.Children.Should().Contain(c => c.Id == "btnHelp");
    }

    /// <summary>
    /// Foreign window sections (ending with "Window" or "Lobby" AND having window-level keys)
    /// are skipped to avoid pulling in unrelated window definitions. A bare panel named
    /// "...Window" without window signals is still adopted.
    /// </summary>
    [Fact]
    public void Foreign_Window_Section_With_Window_Signals_Is_Skipped()
    {
        UiNodeTree tree = BuildFromText("""
            [TestWindow]

            [OtherWindow]
            Size=640,480
            DrawMode=Centered

            [btnAdoptMe]
            Text=OK
            """);

        tree.FindNode("OtherWindow").Should().BeNull("foreign window must be skipped");
        tree.FindNode("btnAdoptMe").Should().NotBeNull("sibling control must still be adopted");
    }

    /// <summary>
    /// Build does not throw when the top-level window section is missing entirely.
    /// IniUiTreeBuilder synthesizes an empty root (GenericWindow-style fallback).
    /// </summary>
    [Fact]
    public void Build_Without_Window_Section_Synthesizes_Empty_Root()
    {
        UiNodeTree tree = BuildFromText("""
            [INISystem]
            BasedOn=GenericWindow.ini

            [btnOrphan]
            Text=Orphan
            """, windowName: "MissingWindow");

        tree.Root.Id.Should().Be("MissingWindow");
        tree.FindNode("btnOrphan").Should().NotBeNull();
    }

    private sealed class PassthroughLocalizationService : ILocalizationService
    {
        public string Localize(string? windowName, string? nodeId, string key, string value, bool notify = true)
            => value;
    }
}
