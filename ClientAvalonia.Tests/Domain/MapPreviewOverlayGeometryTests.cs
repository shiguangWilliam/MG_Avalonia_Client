using ClientAvalonia.Domain;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Domain;

public sealed class MapPreviewOverlayGeometryTests
{
    [Fact]
    public void ComputeLetterbox_WiderControl_CentersHorizontally()
    {
        // control 400x200, preview 100x100 → yRatio is limiting → texture 200x200, offsetX=99
        var layout = MapPreviewOverlayGeometry.ComputeLetterbox(400, 200, 100, 100);

        layout.Ratio.Should().BeApproximately(1.98, 0.01); // (200-2)/100 = 1.98
        layout.TextureHeight.Should().Be(198);
        layout.TextureWidth.Should().Be(198);
        layout.TextureX.Should().Be(100); // (400-2-198)/2 = 100
        layout.TextureY.Should().Be(1);
    }

    [Fact]
    public void ComputeLetterbox_TallerControl_CentersVertically()
    {
        var layout = MapPreviewOverlayGeometry.ComputeLetterbox(200, 400, 100, 100);

        layout.Ratio.Should().BeApproximately(1.98, 0.01); // (200-2)/100
        layout.TextureWidth.Should().Be(198);
        layout.TextureHeight.Should().Be(198);
        layout.TextureX.Should().Be(1);
        layout.TextureY.Should().Be(101); // (400-2-198)/2 + 1
    }

    [Fact]
    public void ProjectIndicator_CentersRectOnProjectedPoint()
    {
        var layout = new MapPreviewOverlayGeometry.LetterboxLayout(2.0, 10, 20, 200, 200);
        var bounds = MapPreviewOverlayGeometry.ProjectIndicator(layout, previewPixelX: 5, previewPixelY: 10, diameter: 32);

        bounds.CenterX.Should().Be(20); // 10 + 5*2
        bounds.CenterY.Should().Be(40); // 20 + 10*2
        bounds.Left.Should().Be(4);
        bounds.Top.Should().Be(24);
        bounds.Width.Should().Be(32);
        bounds.Height.Should().Be(32);
    }

    [Fact]
    public void HitTest_ReturnsLastOverlappingMarker()
    {
        var a = new MapPreviewOverlayGeometry.IndicatorBounds(0, 0, 0, 0, 20, 20);
        var b = new MapPreviewOverlayGeometry.IndicatorBounds(0, 0, 10, 10, 20, 20);
        MapPreviewOverlayGeometry.IndicatorBounds[] markers = [a, b];

        MapPreviewOverlayGeometry.HitTest(markers, 15, 15).Should().Be(1);
        MapPreviewOverlayGeometry.HitTest(markers, 5, 5).Should().Be(0);
        MapPreviewOverlayGeometry.HitTest(markers, 50, 50).Should().BeNull();
    }
}
