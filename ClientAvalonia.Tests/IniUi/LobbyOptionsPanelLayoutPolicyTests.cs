using System;
using System.IO;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

public sealed class LobbyOptionsPanelLayoutPolicyTests
{
    [Fact]
    public void Audit_InternalOverflow_WhenContentExceedsPanelHeight()
    {
        var root = Panel("SkirmishLobby", 1230, 750);
        var panel = Panel("GameOptionsPanel", 400, 200, 812, 380);
        root.Children.Add(panel);
        panel.Parent = root;

        panel.Children.Add(Control("chkA", 10, 10, 120, 24, "DxCheckBox"));
        panel.Children.Add(Control("chkB", 10, 370, 120, 24, "DxCheckBox"));

        LobbyOptionsPanelLayoutPolicy.Audit(panel, root).ScrollNeeded.Should().BeTrue();
    }

    [Fact]
    public void Audit_ExternalOverlap_WhenSiblingIntersectsPanel()
    {
        var root = Panel("SkirmishLobby", 1230, 750);
        var panel = Panel("GameOptionsPanel", 400, 200, 812, 380);
        var preview = Panel("MapPreviewBox", 500, 250, 330, 270);
        root.Children.Add(panel);
        root.Children.Add(preview);
        panel.Parent = root;
        preview.Parent = root;

        panel.Children.Add(Control("chkA", 10, 10, 120, 24, "DxCheckBox"));

        LobbyOptionsPanelLayoutPolicy.Audit(panel, root).ScrollNeeded.Should().BeTrue();
    }

    [Fact]
    public void Audit_Fits_WhenContentInsidePanelAndNoSiblingOverlap()
    {
        var root = Panel("SkirmishLobby", 1230, 750);
        var panel = Panel("GameOptionsPanel", 400, 200, 812, 380);
        var preview = Panel("MapPreviewBox", 900, 12, 330, 270);
        root.Children.Add(panel);
        root.Children.Add(preview);
        panel.Parent = root;
        preview.Parent = root;

        panel.Children.Add(Control("chkA", 10, 10, 120, 24, "DxCheckBox"));
        panel.Children.Add(Control("cmbCredits", 600, 40, 152, 22, "DxComboBox"));

        LobbyOptionsPanelLayoutPolicy.Audit(panel, root).ScrollNeeded.Should().BeFalse();
    }

    [Fact]
    public void Apply_UpgradesTemplate_WhenAuditFails()
    {
        var tree = new UiNodeTree { Root = Panel("SkirmishLobby", 1230, 750), SourcePath = "test.ini" };
        var panel = Panel("GameOptionsPanel", 400, 200, 812, 380);
        panel.Parent = tree.Root;
        tree.Root.Children.Add(panel);
        panel.Children.Add(Control("chkA", 10, 10, 120, 24, "DxCheckBox"));
        panel.Children.Add(Control("chkB", 10, 370, 120, 24, "DxCheckBox"));

        LobbyOptionsPanelLayoutPolicy.Apply(tree, "SkirmishLobby");

        panel.TemplateKey.Should().Be(LobbyOptionsPanelLayoutPolicy.ScrollTemplateKey);
        panel.Props.Should().ContainKey("ScrollContentHeight");
        panel.Props.Should().ContainKey("LobbyOptionsScrollReason");
    }

    [SkippableFact]
    public void DtaSkirmishLobby_GameOptionsPanel_FitsOrScrollsDeterministically()
    {
        string repoRoot = LocateRepoRoot();
        string iniPath = Path.Combine(repoRoot, "DXMainClient", "Resources", "DTA", "SkirmishLobby.ini");
        Skip.IfNot(File.Exists(iniPath), "DTA SkirmishLobby.ini missing.");

        var env = ClientEnvironment.Discover(Path.Combine(repoRoot, "DXMainClient"));
        var engine = LayoutEngine.CreateForWindow(env, iniPath, "SkirmishLobby");
        UiNodeTree tree = engine.LoadWindow(iniPath, "SkirmishLobby");

        UiNode? panel = tree.FindNode("GameOptionsPanel");
        panel.Should().NotBeNull();

        int contentBottom = LobbyOptionsPanelLayoutPolicy.MeasureContentBottom(panel!);
        int panelHeight = panel!.GetIntProp("Height");
        bool scroll = panel.TemplateKey == LobbyOptionsPanelLayoutPolicy.ScrollTemplateKey;

        if (contentBottom + 8 > panelHeight)
            scroll.Should().BeTrue("internal overflow must compile to scroll");
        else
            panel.Props.Should().NotContainKey("LobbyOptionsScrollReason",
                "fixed panel should not record scroll reason when content fits");
    }

    private static string LocateRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(current, "DXMainClient")))
                return current;
            current = Path.GetFullPath(Path.Combine(current, ".."));
        }

        throw new InvalidOperationException("Could not locate repo root from test bin directory.");
    }

    private static UiNode Panel(string id, int width, int height, int left = 0, int top = 0)
    {
        return new UiNode
        {
            Id = id,
            ControlType = "XNAPanel",
            TemplateKey = "DxPanel",
            WindowName = "SkirmishLobby",
            Props =
            {
                ["Width"] = (double)width,
                ["Height"] = (double)height,
                ["CanvasLeft"] = (double)left,
                ["CanvasTop"] = (double)top,
            },
        };
    }

    private static UiNode Control(string id, int left, int top, int width, int height, string templateKey)
        => new()
        {
            Id = id,
            ControlType = templateKey == "DxComboBox" ? "GameLobbyDropDown" : "GameLobbyCheckBox",
            TemplateKey = templateKey,
            WindowName = "SkirmishLobby",
            Props =
            {
                ["CanvasLeft"] = (double)left,
                ["CanvasTop"] = (double)top,
                ["Width"] = (double)width,
                ["Height"] = (double)height,
            },
        };
}
