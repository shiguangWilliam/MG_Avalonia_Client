using System;
using System.Collections.Generic;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks the expression grammar used by DX ClientGUI.Parser — the small recursive-descent
/// evaluator that lets INI authors write <c>$X = %50 + getX(btnOther)</c> instead of literal
/// pixel values. We mirror the DX grammar (add, sub, mul, div, parens, getX/getY/getWidth/
/// getHeight/getRight/getBottom/horizontalCenterOnParent, $ParentControl / $Self references,
/// RESOLUTION_WIDTH / RESOLUTION_HEIGHT constants and extra parser constants).
/// </summary>
public sealed class ExpressionEvaluatorTests
{
    private static UiNodeTree MakeTree(params UiNode[] nodes)
    {
        var root = new UiNode { Id = "Root", ControlType = "XNAWindow", TemplateKey = "Window" };
        var tree = new UiNodeTree { Root = root, SourcePath = "<test>" };
        foreach (UiNode n in nodes)
        {
            n.Parent = root;
            root.Children.Add(n);
        }
        return tree;
    }

    private static UiNode Node(string id, int x = 0, int y = 0, int w = 0, int h = 0)
    {
        var n = new UiNode { Id = id, ControlType = "XNAControl", TemplateKey = "Control" };
        n.Props["CanvasLeft"] = (double)x;
        n.Props["CanvasTop"] = (double)y;
        n.Props["Width"] = (double)w;
        n.Props["Height"] = (double)h;
        return n;
    }

    private static int Eval(string expr, UiNodeTree tree, UiNode? parsing = null, int rw = 1280, int rh = 720)
    {
        var evaluator = new ExpressionEvaluator(rw, rh);
        return evaluator.Evaluate(expr, tree, parsing ?? tree.Root);
    }

    // ---- literals and arithmetic ----

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("12345", 12345)]
    public void Integer_Literal(string expr, int expected) => Eval(expr, MakeTree()).Should().Be(expected);

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("10-3", 7)]
    [InlineData("4*5", 20)]
    [InlineData("20/4", 5)]
    [InlineData("2+3*4", 20)]              // DX evaluator is left-associative: (2+3)*4
    [InlineData("(2+3)*4", 20)]
    [InlineData("17/0", 17)]               // DX: division-by-zero leaves the previous value intact
    public void Arithmetic(string expr, int expected) => Eval(expr, MakeTree()).Should().Be(expected);

    [Fact]
    public void Whitespace_Is_Ignored()
    {
        Eval("  1  +  2  ", MakeTree()).Should().Be(3);
    }

    // ---- constants ----

    [Theory]
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(640)]
    public void Resolution_Width_Constant_Tracks_Evaluator(int rw)
    {
        Eval("RESOLUTION_WIDTH", MakeTree(), rw: rw).Should().Be(rw);
    }

    [Fact]
    public void Resolution_Height_Constant()
    {
        Eval("RESOLUTION_HEIGHT", MakeTree(), rh: 1080).Should().Be(1080);
    }

    [Fact]
    public void Extra_Constants_Are_Resolved()
    {
        var evaluator = new ExpressionEvaluator(1280, 720, new Dictionary<string, int> { ["GAME_WIDTH"] = 960 });
        evaluator.Evaluate("GAME_WIDTH", MakeTree(), MakeParsingNode()).Should().Be(960);
    }

    private static UiNode MakeParsingNode() => new() { Id = "X", ControlType = "XNAControl", TemplateKey = "Control" };

    [Fact]
    public void Unknown_Constant_Throws()
    {
        var act = () => Eval("DOES_NOT_EXIST", MakeTree());
        act.Should().Throw<KeyNotFoundException>();
    }

    // ---- getX/getY/getWidth/getHeight/getRight/getBottom ----

    [Fact]
    public void GetX_Reads_CanvasLeft_Of_Named_Control()
    {
        var tree = MakeTree(Node("btnOther", x: 30));
        Eval("getX(btnOther)", tree).Should().Be(30);
    }

    [Fact]
    public void GetY_Reads_CanvasTop_Of_Named_Control()
    {
        var tree = MakeTree(Node("btnOther", y: 70));
        Eval("getY(btnOther)", tree).Should().Be(70);
    }

    [Fact]
    public void GetWidth_And_GetHeight_Read_Dimensions_Of_Named_Control()
    {
        var tree = MakeTree(Node("btnOther", w: 110, h: 28));
        Eval("getWidth(btnOther)", tree).Should().Be(110);
        Eval("getHeight(btnOther)", tree).Should().Be(28);
    }

    [Fact]
    public void GetRight_And_GetBottom_Are_XPlusWidth_And_YPlusHeight()
    {
        var tree = MakeTree(Node("btnOther", x: 100, y: 200, w: 50, h: 30));
        Eval("getRight(btnOther)", tree).Should().Be(150);
        Eval("getBottom(btnOther)", tree).Should().Be(230);
    }

    [Fact]
    public void Combined_Expression_With_Function()
    {
        var tree = MakeTree(Node("btnOther", x: 100, w: 50));
        // getRight(btnOther) + 10 == 160
        Eval("getRight(btnOther)+10", tree).Should().Be(160);
    }

    // ---- $ParentControl / $Self ----

    [Fact]
    public void ParentControl_References_The_Parent_Node()
    {
        UiNode child = Node("btnChild", x: 0, y: 0, w: 0, h: 0);
        UiNode parent = Node("PanelX", x: 50, y: 60, w: 200, h: 100);
        child.Parent = parent;
        parent.Children.Add(child);
        var tree = MakeTree(parent);

        // getX($ParentControl) where $ParentControl == PanelX
        Eval("getX($ParentControl)", tree, parsing: child).Should().Be(50);
    }

    [Fact]
    public void Self_References_The_Parsing_Node()
    {
        UiNode self = Node("btnSelf", x: 90, y: 0, w: 0, h: 0);
        var tree = MakeTree(self);

        Eval("getX($Self)", tree, parsing: self).Should().Be(90);
    }

    [Fact]
    public void ParentControl_On_Root_Throws()
    {
        var tree = MakeTree();
        var act = () => Eval("getX($ParentControl)", tree, parsing: tree.Root);
        act.Should().Throw<InvalidOperationException>();
    }

    // ---- horizontalCenterOnParent ----

    [Fact]
    public void HorizontalCenterOnParent_Centers_Child_And_Writes_CanvasLeft()
    {
        UiNode parent = Node("PanelX", w: 400, h: 200);
        UiNode child = Node("btnCenter", w: 100, h: 20);
        child.Parent = parent;
        parent.Children.Add(child);
        var tree = MakeTree(parent);

        int result = Eval("horizontalCenterOnParent($Self)", tree, parsing: child);
        result.Should().Be((400 - 100) / 2);
        child.GetIntProp("CanvasLeft").Should().Be(result);
    }

    [Fact]
    public void HorizontalCenterOnParent_On_Root_Keeps_Existing_CanvasLeft()
    {
        UiNode root = Node("Root", x: 12, w: 100);
        var tree = new UiNodeTree { Root = root, SourcePath = "<test>" };

        int result = Eval("horizontalCenterOnParent($Self)", tree, parsing: root);
        result.Should().Be(12);
    }

    // ---- unknown control / function ----

    [Fact]
    public void Unknown_Function_Throws()
    {
        var tree = MakeTree(Node("btnA", x: 10));
        var act = () => Eval("frobnicate(btnA)", tree);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Unknown_Control_Reference_Throws()
    {
        var tree = MakeTree();
        var act = () => Eval("getX(missingControl)", tree);
        act.Should().Throw<KeyNotFoundException>();
    }

    // ---- UpdateResolution ----

    [Fact]
    public void UpdateResolution_Swaps_Constants()
    {
        var evaluator = new ExpressionEvaluator(1280, 720);
        var tree = MakeTree();
        evaluator.Evaluate("RESOLUTION_WIDTH", tree, MakeParsingNode()).Should().Be(1280);
        evaluator.UpdateResolution(1920, 1080);
        evaluator.Evaluate("RESOLUTION_WIDTH", tree, MakeParsingNode()).Should().Be(1920);
        evaluator.Evaluate("RESOLUTION_HEIGHT", tree, MakeParsingNode()).Should().Be(1080);
    }
}
