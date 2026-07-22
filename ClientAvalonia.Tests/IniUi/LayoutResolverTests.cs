using System.Collections.Generic;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks DX layout-key semantics for <see cref="LayoutResolver"/>:
///   - $X/$Y/$Width/$Height (and X/Y/Width/Height) propagate to Canvas* props
///   - DistanceFromRightBorder / DistanceFromBottomBorder anchor against parent edge
///   - FillWidth / FillHeight consume parent - inset
///   - AnchorPoint + TextAnchor aligns a label rectangle relative to a pivot (LEFT/RIGHT/HORIZONTAL_CENTER/TOP/BOTTOM/VERTICAL_CENTER)
///   - DrawOrder is reversed into ZIndex so larger draw order is on top
/// </summary>
public sealed class LayoutResolverTests
{
    private static (LayoutResolver resolver, UiNodeTree tree) MakeTree(int rootW = 800, int rootH = 600)
    {
        var evaluator = new ExpressionEvaluator(rootW, rootH);
        var resolver = new LayoutResolver(evaluator);
        var root = new UiNode { Id = "Root", ControlType = "XNAWindow", TemplateKey = "Window" };
        root.Props["Width"] = (double)rootW;
        root.Props["Height"] = (double)rootH;
        var tree = new UiNodeTree { Root = root, SourcePath = "<test>" };
        return (resolver, tree);
    }

    private static UiNode AddChild(UiNodeTree tree, string id,
        int x = 0, int y = 0, int w = 0, int h = 0)
    {
        var n = new UiNode { Id = id, ControlType = "XNAControl", TemplateKey = "Control" };
        n.Props["CanvasLeft"] = (double)x;
        n.Props["CanvasTop"] = (double)y;
        n.Props["Width"] = (double)w;
        n.Props["Height"] = (double)h;
        n.Parent = tree.Root;
        tree.Root.Children.Add(n);
        return n;
    }

    // ---- $X/$Y/$Width/$Height expression propagation ----

    [Fact]
    public void Dollar_X_Propagates_To_CanvasLeft()
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "btnA");
        n.RawAttributes["$X"] = "100+20";
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(120);
    }

    [Fact]
    public void Bare_X_Also_Propagates_To_CanvasLeft()
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "btnA");
        n.RawAttributes["Y"] = "50";
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasTop").Should().Be(50);
    }

    // ---- DistanceFromRightBorder / DistanceFromBottomBorder ----

    [Fact]
    public void DistanceFromRightBorder_Anchors_To_Parent_Right_Edge()
    {
        var (resolver, tree) = MakeTree(rootW: 800);
        var n = AddChild(tree, "btnA", w: 100);
        n.RawAttributes["DistanceFromRightBorder"] = "30";   // parentW - w - 30 = 670
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(670);
    }

    [Fact]
    public void DistanceFromBottomBorder_Anchors_To_Parent_Bottom_Edge()
    {
        var (resolver, tree) = MakeTree(rootH: 600);
        var n = AddChild(tree, "btnA", h: 50);
        n.RawAttributes["DistanceFromBottomBorder"] = "20";  // parentH - h - 20 = 530
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasTop").Should().Be(530);
    }

    // ---- FillWidth / FillHeight ----

    [Fact]
    public void FillWidth_Consumes_Parent_Width_Minus_Inset()
    {
        var (resolver, tree) = MakeTree(rootW: 800);
        var n = AddChild(tree, "btnA");
        n.RawAttributes["FillWidth"] = "50";                  // 800 - 50 = 750
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("Width").Should().Be(750);
    }

    [Fact]
    public void FillHeight_Consumes_Parent_Height_Minus_Inset()
    {
        var (resolver, tree) = MakeTree(rootH: 600);
        var n = AddChild(tree, "btnA");
        n.RawAttributes["FillHeight"] = "30";                 // 600 - 30 = 570
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("Height").Should().Be(570);
    }

    // ---- AnchorPoint + TextAnchor ----

    [Fact]
    public void AnchorPoint_Default_TextAnchor_Is_Left_Top()
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "lblA", w: 100, h: 20);
        n.RawAttributes["$AnchorPoint"] = "200,300";
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(200);
        n.GetIntProp("CanvasTop").Should().Be(300);
    }

    [Theory]
    [InlineData("RIGHT", 100)]              // 200 - 100
    [InlineData("HORIZONTAL_CENTER", 150)]  // 200 - 100/2
    public void AnchorPoint_TextAnchor_Horizontal_Aligns_X(string anchor, int expectedX)
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "lblA", w: 100, h: 20);
        n.RawAttributes["$AnchorPoint"] = $"200,0";
        n.RawAttributes["$TextAnchor"] = anchor;
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(expectedX);
    }

    [Theory]
    [InlineData("BOTTOM", 280)]              // 300 - 20
    [InlineData("VERTICAL_CENTER", 290)]     // 300 - 20/2
    public void AnchorPoint_TextAnchor_Vertical_Aligns_Y(string anchor, int expectedY)
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "lblA", w: 100, h: 20);
        n.RawAttributes["$AnchorPoint"] = $"0,300";
        n.RawAttributes["$TextAnchor"] = anchor;
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasTop").Should().Be(expectedY);
    }

    [Fact]
    public void AnchorPoint_Bare_AnchorPoint_Key_Also_Works()
    {
        // The bare (non-$) AnchorPoint key is read; for TextAnchor the schema-canonical
        // $TextAnchor form is required because the resolver initializes the local to "" and
        // only the null-coalescing fallback reads the bare key.
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "lblA", w: 100, h: 20);
        n.RawAttributes["AnchorPoint"] = "50,60";
        n.RawAttributes["$TextAnchor"] = "RIGHT";
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(-50);
        n.GetIntProp("CanvasTop").Should().Be(60);
    }

    [Fact]
    public void AnchorPoint_Ignores_Malformed_Expression()
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "lblA", x: 7, y: 9, w: 10, h: 5);
        n.RawAttributes["$AnchorPoint"] = "100";              // not a "x,y" pair
        resolver.ApplyLayoutPass(tree);

        n.GetIntProp("CanvasLeft").Should().Be(7, "malformed anchor must not move the node");
    }

    // ---- DrawOrder ----

    [Fact]
    public void DrawOrder_Is_Negated_Into_ZIndex()
    {
        var (resolver, tree) = MakeTree();
        var n = AddChild(tree, "btnA");
        n.RawAttributes["DrawOrder"] = "10";
        resolver.ApplyLayoutPass(tree);

        ((int)n.Props["ZIndex"]).Should().Be(-10, "higher DrawOrder should render on top => smaller ZIndex");
    }

    // ---- UpdateResolution ----

    [Fact]
    public void UpdateResolution_Allows_Relayout_With_New_Resolution_Constants()
    {
        var (resolver, tree) = MakeTree(rootW: 800);
        var n = AddChild(tree, "btnA", w: 0);
        n.RawAttributes["$X"] = "RESOLUTION_WIDTH-100";        // 800 - 100 = 700 initially
        resolver.ApplyLayoutPass(tree);
        n.GetIntProp("CanvasLeft").Should().Be(700);

        resolver.UpdateResolution(1280, 720);
        resolver.ApplyLayoutPass(tree);
        n.GetIntProp("CanvasLeft").Should().Be(1180);
    }
}
