using Avalonia.Media;
using ClientAvalonia.IniUi.Loading;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks DX/INI presentation primitives:
///   - <see cref="IniTextUtil"/> : DX uses '@' and "\n" as line breaks in Text/ToolTip fields.
///   - <see cref="IniDrawMode"/> : DX PanelBackgroundImageDrawMode (Stretched / Centered / Tiled)
///     maps to Avalonia Stretch + (optional) tiled ImageBrush.
///   - <see cref="IniKeyAliases"/> : normalizes legacy INI keys to schema names so a single
///     PropertyResolver pipeline can handle both XNA and Avalonia authored INIs.
/// </summary>
public sealed class IniPrimitivesTests
{
    // ---- IniTextUtil.NormalizeDisplayText ----

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Hello", "Hello")]
    [InlineData("Line1@Line2", "Line1\nLine2")]
    [InlineData("Line1\\nLine2", "Line1\nLine2")]
    [InlineData("A@B\\nC", "A\nB\nC")]
    public void NormalizeDisplayText_Converts_Dx_LineBreak_Tokens(string? input, string expected)
    {
        IniTextUtil.NormalizeDisplayText(input).Should().Be(expected);
    }

    // ---- IniDrawMode.Parse ----

    [Theory]
    [InlineData(null, IniBackgroundDrawMode.Stretched)]
    [InlineData("", IniBackgroundDrawMode.Stretched)]
    [InlineData("   ", IniBackgroundDrawMode.Stretched)]
    [InlineData("Stretched", IniBackgroundDrawMode.Stretched)]
    [InlineData("STRETCHED", IniBackgroundDrawMode.Stretched)]
    [InlineData("anything-else", IniBackgroundDrawMode.Stretched)]
    [InlineData("Centered", IniBackgroundDrawMode.Centered)]
    [InlineData("CENTER", IniBackgroundDrawMode.Centered)]
    [InlineData("center", IniBackgroundDrawMode.Centered)]
    [InlineData("Tiled", IniBackgroundDrawMode.Tiled)]
    [InlineData("TILE", IniBackgroundDrawMode.Tiled)]
    public void Parse_Maps_Dx_DrawMode_Tokens(string? input, IniBackgroundDrawMode expected)
    {
        IniDrawMode.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(IniBackgroundDrawMode.Stretched, Stretch.Fill)]
    [InlineData(IniBackgroundDrawMode.Centered, Stretch.Uniform)]
    [InlineData(IniBackgroundDrawMode.Tiled, Stretch.None)]
    public void ToImageStretch_Maps_To_Avalonia_Stretch(IniBackgroundDrawMode mode, Stretch expected)
    {
        IniDrawMode.ToImageStretch(mode).Should().Be(expected);
    }

    [Fact]
    public void CreateTiledBrush_Returns_Null_When_No_Bitmap()
    {
        IniDrawMode.CreateTiledBrush(null).Should().BeNull();
    }

    // ---- IniKeyAliases.Normalize ----

    [Theory]
    [InlineData("ClickSound", "ClickSoundEffect")]
    [InlineData("HoverSound", "HoverSoundEffect")]
    [InlineData("TextAnchor", "$TextAnchor")]
    [InlineData("AnchorPoint", "$AnchorPoint")]
    [InlineData("LeftClickAction", "$LeftClickAction")]
    [InlineData("AnythingElse", "AnythingElse")]
    [InlineData("Text", "Text")]
    public void Normalize_Renames_Legacy_Keys_To_Schema_Names(string input, string expected)
    {
        IniKeyAliases.Normalize(input).Should().Be(expected);
    }
}
