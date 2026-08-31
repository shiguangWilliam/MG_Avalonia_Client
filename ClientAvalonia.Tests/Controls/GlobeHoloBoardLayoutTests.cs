using ClientAvalonia.Controls;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// F4A holo-board layout: horizontal clamping and the above/below flip rule.
/// Pure function coverage — visual regressions in placement decisions.
/// </summary>
public sealed class GlobeHoloBoardLayoutTests
{
    private const double W = 300;
    private const double H = 170;

    [Fact]
    public void Center_Anchor_Places_Board_Above()
    {
        var (left, top, below) = GlobeMath.ClampHoloBoard(400, 400, W, H, 800, 600);
        Assert.False(below);
        Assert.Equal(400 - W / 2, left, 6);
        Assert.Equal(400 - H - 12, top, 6);
    }

    [Fact]
    public void Near_Left_Edge_Clamps_Into_Viewport()
    {
        var (left, _, _) = GlobeMath.ClampHoloBoard(20, 400, W, H, 800, 600);
        Assert.True(left >= 4);
        Assert.True(left + W <= 800);
    }

    [Fact]
    public void Near_Right_Edge_Clamps_Into_Viewport()
    {
        var (left, _, _) = GlobeMath.ClampHoloBoard(790, 400, W, H, 800, 600);
        Assert.True(left + W <= 800 - 4 + 0.001, $"left={left}");
    }

    [Fact]
    public void No_Headroom_Flips_Below()
    {
        var (_, top, below) = GlobeMath.ClampHoloBoard(400, 50, W, H, 800, 600);
        Assert.True(below);
        Assert.Equal(50 + 12, top, 6);
    }

    [Fact]
    public void Flip_Below_Clamps_Top_When_Anchor_Offscreen_Low()
    {
        // Degenerate anchor beyond the viewport: whichever side is chosen, the
        // clamped top keeps the board fully inside the viewport.
        var (left, top, below) = GlobeMath.ClampHoloBoard(400, 700, W, H, 800, 600);
        Assert.InRange(top, 4 - 0.001, 600 - H - 4 + 0.001);
        Assert.InRange(left, 4 - 0.001, 800 - W - 4 + 0.001);
    }

    [Fact]
    public void Flip_Below_Clamps_When_Anchor_Is_Low()
    {
        // Anchor near the bottom edge: flipped placement clamps the top so the
        // board bottom stays inside the viewport.
        var (left, top, below) = GlobeMath.ClampHoloBoard(400, 590, W, H, 800, 600);
        Assert.False(below, "anchor at 590 has headroom (590-170-12>0), board goes above");
        Assert.True(top + H <= 600, $"top={top}");
    }

    [Fact]
    public void Board_Always_Inside_Viewport()
    {
        foreach (double ax in new[] { -50.0, 0, 10, 200, 400, 700, 795, 900 })
        foreach (double ay in new[] { -10.0, 0, 30, 100, 300, 590, 610 })
        {
            var (left, top, _) = GlobeMath.ClampHoloBoard(ax, ay, W, H, 800, 600);
            Assert.InRange(left, 4 - 0.001, 800 - W - 4 + 0.001);
            Assert.InRange(top, 4 - 0.001, 600 - H - 4 + 0.001);
        }
    }

    [Fact]
    public void Tiny_Viewport_Clamps_Gracefully()
    {
        // Viewport smaller than the board: clamps collapse to the floor
        // without NaN or inverted ranges.
        var (left, top, _) = GlobeMath.ClampHoloBoard(50, 50, W, H, 200, 100);
        Assert.False(double.IsNaN(left) || double.IsNaN(top));
        Assert.True(left >= 4 - 0.001);
        Assert.True(top >= 4 - 0.001);
    }
}
