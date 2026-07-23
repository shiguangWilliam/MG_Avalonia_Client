using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks <see cref="PanelLayoutPass"/> resolution semantics:
///   - Same-column overlaps push the later child down by Gap (6px) below the previous bottom.
///   - Same-row overlaps push the later child right by Gap past the previous right edge.
///   - Off-axis overlaps fall back to vertical displacement.
///   - Non-content panels (no Panel suffix in lobby parent, etc.) are skipped.
///   - Hidden children (IsVisible = false) do not block layout.
///   - Iteration converges (MaxIterations) for stacked overlaps.
/// </summary>
public sealed class PanelLayoutPassTests
{
    private const int Gap = 6;

    private static UiNode MakePanel(string id, int w = 800, int h = 600)
    {
        var panel = new UiNode { Id = id, ControlType = "XNAPanel", TemplateKey = "Panel" };
        panel.Props["Width"] = (double)w;
        panel.Props["Height"] = (double)h;
        return panel;
    }

    private static UiNode AddChild(UiNode parent, string id, int x, int y, int w, int h,
        bool visible = true)
    {
        var n = new UiNode { Id = id, ControlType = "XNAControl", TemplateKey = "Control" };
        n.Props["CanvasLeft"] = (double)x;
        n.Props["CanvasTop"] = (double)y;
        n.Props["Width"] = (double)w;
        n.Props["Height"] = (double)h;
        n.Props["IsVisible"] = visible;
        n.Parent = parent;
        parent.Children.Add(n);
        return n;
    }

    private static UiNodeTree TreeWith(params UiNode[] rootChildren)
    {
        var root = new UiNode { Id = "Root", ControlType = "XNAWindow", TemplateKey = "Window" };
        root.Props["Width"] = 1280.0;
        root.Props["Height"] = 720.0;
        foreach (UiNode c in rootChildren)
        {
            c.Parent = root;
            root.Children.Add(c);
        }
        return new UiNodeTree { Root = root, SourcePath = "<test>" };
    }

    [Fact]
    public void Same_Column_Overlap_Pushes_Later_Child_Down()
    {
        // GameOptionsPanel: two children at the same x, second overlaps first.
        UiNode panel = MakePanel("GameOptionsPanel", w: 400, h: 600);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        AddChild(panel, "chkB", x: 10, y: 5, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        UiNode b = panel.Children[1];
        // chkA bottom = 0 + 20 = 20; chkB.top becomes 20 + Gap = 26.
        b.GetIntProp("CanvasTop").Should().Be(20 + Gap);
    }

    [Fact]
    public void Same_Row_Overlap_Pushes_Later_Child_Right()
    {
        UiNode panel = MakePanel("GameOptionsPanel", w: 800, h: 600);
        AddChild(panel, "btnA", x: 0, y: 0, w: 100, h: 30);
        // btnB overlaps btnA horizontally, almost same row. x-distance > ColumnTolerance(56)
        // so the row branch (not column) kicks in.
        AddChild(panel, "btnB", x: 60, y: 1, w: 60, h: 30);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        UiNode b = panel.Children[1];
        // btnA right = 0 + 100 = 100; btnB.left becomes 100 + Gap = 106.
        b.GetIntProp("CanvasLeft").Should().Be(100 + Gap);
    }

    [Fact]
    public void Hidden_Child_Does_Not_Block_Layout()
    {
        UiNode panel = MakePanel("GameOptionsPanel", w: 400, h: 600);
        AddChild(panel, "chkHidden", x: 10, y: 0, w: 100, h: 20, visible: false);
        AddChild(panel, "chkB", x: 10, y: 0, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        // chkB at the same coordinates as the hidden one — must stay in place because
        // hidden nodes do not participate in overlap resolution.
        panel.Children[1].GetIntProp("CanvasTop").Should().Be(0);
    }

    [Fact]
    public void Non_Content_Panel_Is_Skipped()
    {
        // "RandomContainer" does not satisfy IsContentPanel (no OptionsPanel suffix, not in the
        // known-name list, not a *Panel under a *Lobby). Overlaps remain unresolved.
        UiNode panel = MakePanel("RandomContainer", w: 400, h: 600);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        AddChild(panel, "chkB", x: 10, y: 5, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        panel.Children[1].GetIntProp("CanvasTop").Should().Be(5, "non-content panel is not processed");
    }

    [Fact]
    public void Panel_Under_Lobby_Is_Content_Panel()
    {
        // *Panel whose parent contains "Lobby" is treated as content panel.
        UiNode lobby = new UiNode { Id = "CnCNetGameLobby", ControlType = "XNAWindow", TemplateKey = "Window" };
        lobby.Props["Width"] = 1280.0;
        lobby.Props["Height"] = 720.0;
        UiNode panel = MakePanel("ChatPanel", w: 400, h: 600);
        panel.Parent = lobby;
        lobby.Children.Add(panel);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        AddChild(panel, "chkB", x: 10, y: 5, w: 100, h: 20);

        UiNodeTree tree = new() { Root = lobby, SourcePath = "<test>" };

        new PanelLayoutPass().Apply(tree);

        panel.Children[1].GetIntProp("CanvasTop").Should().Be(20 + Gap);
    }

    [Fact]
    public void Cascading_Overlaps_Converge()
    {
        // Three stacked children at the same x. After Apply, the top-order should be
        // 0 -> 20+Gap -> (20+Gap)+20+Gap.
        UiNode panel = MakePanel("GameOptionsPanel", w: 400, h: 600);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        AddChild(panel, "chkB", x: 10, y: 1, w: 100, h: 20);
        AddChild(panel, "chkC", x: 10, y: 2, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        int topA = panel.Children[0].GetIntProp("CanvasTop");
        int topB = panel.Children[1].GetIntProp("CanvasTop");
        int topC = panel.Children[2].GetIntProp("CanvasTop");
        topA.Should().Be(0);
        topB.Should().Be(20 + Gap);
        topC.Should().Be(20 + Gap + 20 + Gap);
    }

    [Fact]
    public void Already_Separated_Children_Are_Not_Moved()
    {
        UiNode panel = MakePanel("GameOptionsPanel", w: 400, h: 600);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        AddChild(panel, "chkB", x: 10, y: 100, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        new PanelLayoutPass().Apply(tree);

        panel.Children[0].GetIntProp("CanvasTop").Should().Be(0);
        panel.Children[1].GetIntProp("CanvasTop").Should().Be(100);
    }

    [Fact]
    public void Panel_With_Single_Child_Is_Not_Processed()
    {
        UiNode panel = MakePanel("GameOptionsPanel", w: 400, h: 600);
        AddChild(panel, "chkA", x: 10, y: 0, w: 100, h: 20);
        UiNodeTree tree = TreeWith(panel);

        var act = () => new PanelLayoutPass().Apply(tree);
        act.Should().NotThrow("single-child panels have nothing to resolve");
    }
}
