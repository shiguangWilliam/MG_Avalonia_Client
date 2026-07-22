using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks <see cref="MeasurePass"/> estimation rules — the port of DX XNA control measurement
/// when neither the INI nor a PNG provides explicit Width/Height:
///   - CheckBox-like nodes (chk* prefix or *CheckBox type) get the glyph-bearing min sizes
///     (Width ≥ 70, Height ≥ 22).
///   - Labels and text-bearing nodes grow Width/Height to fit the text (Latin vs CJK width
///     estimate, multi-line wrap).
///   - Nodes with an IdleTexture but missing on disk fall back to 200×54 (MainMenu button
///     row spacing).
///   - Explicit Width/Height in INI is preserved when larger than the estimate.
/// </summary>
public sealed class MeasurePassTests
{
    private static UiNodeTree MakeTree(params UiNode[] children)
    {
        var root = new UiNode { Id = "Root", ControlType = "XNAWindow", TemplateKey = "Window" };
        root.Props["Width"] = 1280.0;
        root.Props["Height"] = 720.0;
        foreach (UiNode c in children)
        {
            c.Parent = root;
            root.Children.Add(c);
        }
        return new UiNodeTree { Root = root, SourcePath = "<test>" };
    }

    private static UiNode Node(string id, string controlType = "XNAControl",
        int? width = null, int? height = null, string? text = null)
    {
        var n = new UiNode { Id = id, ControlType = controlType, TemplateKey = "Control" };
        if (width.HasValue) n.Props["Width"] = (double)width.Value;
        if (height.HasValue) n.Props["Height"] = (double)height.Value;
        if (text != null) n.Props["Text"] = text;
        return n;
    }

    // ---- CheckBox-like defaults ----

    [Fact]
    public void CheckBox_Like_Node_Gets_Min_Width_And_Height()
    {
        // chk* prefix with empty text → IsCheckBoxLike() applies the floor (W ≥ 70, H ≥ 22).
        UiNode chk = Node("chkBases", controlType: "XNACheckBox");
        UiNodeTree tree = MakeTree(chk);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        chk.GetIntProp("Width").Should().BeGreaterThanOrEqualTo(70);
        chk.GetIntProp("Height").Should().BeGreaterThanOrEqualTo(22);
    }

    [Fact]
    public void CheckBox_Type_Alone_Also_Triggers_CheckBox_Like_Branch()
    {
        UiNode chk = Node("customName", controlType: "GameLobbyCheckBox");
        UiNodeTree tree = MakeTree(chk);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        chk.GetIntProp("Width").Should().BeGreaterThanOrEqualTo(70);
        chk.GetIntProp("Height").Should().BeGreaterThanOrEqualTo(22);
    }

    // ---- text-bearing nodes ----

    [Fact]
    public void Label_With_Text_Expands_Width_To_Fit()
    {
        UiNode lbl = Node("lblTitle", controlType: "XNALabel", text: "Some Quite Long Label Text");
        UiNodeTree tree = MakeTree(lbl);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        lbl.GetIntProp("Width").Should().BeGreaterThan(50, "label must grow to fit text");
        lbl.GetIntProp("Height").Should().BeGreaterThan(0);
    }

    [Fact]
    public void Label_With_CJK_Text_Uses_CJK_Char_Width()
    {
        UiNode lbl = Node("lblCjk", controlType: "XNALabel", text: "中文标签");
        UiNodeTree tree = MakeTree(lbl);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        // 4 CJK chars × 13.5 ≈ 54 + padding 8 → ≈ 62.
        lbl.GetIntProp("Width").Should().BeGreaterThan(50);
    }

    [Fact]
    public void Explicit_Larger_Width_Is_Preserved()
    {
        UiNode lbl = Node("lblKeep", controlType: "XNALabel", width: 500, text: "Tiny");
        UiNodeTree tree = MakeTree(lbl);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        lbl.GetIntProp("Width").Should().Be(500, "explicit larger width wins over estimate");
    }

    // ---- texture fallback ----

    [Fact]
    public void Texture_Fallback_Applies_200x54_When_File_Missing()
    {
        UiNode btn = Node("btnA", controlType: "XNAButton");
        btn.Props["IdleTexture"] = "non-existent.png";
        UiNodeTree tree = MakeTree(btn);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        btn.GetIntProp("Width").Should().Be(200, "MainMenu fallback applies when PNG missing");
        btn.GetIntProp("Height").Should().Be(54);
    }

    [Fact]
    public void Texture_Fallback_Does_Not_Override_Explicit_Size()
    {
        UiNode btn = Node("btnA", controlType: "XNAButton", width: 300, height: 80);
        btn.Props["IdleTexture"] = "non-existent.png";
        UiNodeTree tree = MakeTree(btn);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        btn.GetIntProp("Width").Should().Be(300);
        btn.GetIntProp("Height").Should().Be(80);
    }

    // ---- multi-line text ----

    [Fact]
    public void Multi_Line_Text_Grows_Height_With_Line_Count()
    {
        UiNode single = Node("lblSingle", controlType: "XNALabel", width: 400, text: "One line");
        UiNode multi = Node("lblMulti", controlType: "XNALabel", width: 400, text: "Line1\nLine2\nLine3");
        UiNodeTree tree = MakeTree(single, multi);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        multi.GetIntProp("Height").Should().BeGreaterThan(single.GetIntProp("Height"),
            "3 lines must produce more height than 1 line");
    }

    // ---- empty node stays empty ----

    [Fact]
    public void Empty_Node_Keeps_Zero_Size()
    {
        UiNode empty = Node("panel", controlType: "XNAPanel");
        UiNodeTree tree = MakeTree(empty);

        new MeasurePass(new ResourceResolver()).Apply(tree);

        // No text, no texture, not button-like, not checkbox-like — nothing fills in.
        empty.GetIntProp("Width").Should().Be(0);
        empty.GetIntProp("Height").Should().Be(0);
    }
}
