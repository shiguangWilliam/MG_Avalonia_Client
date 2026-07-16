using System;

namespace ClientAvalonia.Domain;

/// <summary>
/// Letterbox layout + indicator hit-testing for MapPreviewBox overlays.
/// Mirrors DXMainClient <c>MapPreviewBox.UpdateMap</c> geometry.
/// </summary>
public static class MapPreviewOverlayGeometry
{
    /// <summary>DX uses <c>baseTexture * 0.25</c>; we expose an equivalent fixed diameter.</summary>
    public const double DefaultIndicatorDiameter = 32;

    public readonly record struct LetterboxLayout(
        double Ratio,
        int TextureX,
        int TextureY,
        int TextureWidth,
        int TextureHeight);

    public readonly record struct IndicatorBounds(
        double CenterX,
        double CenterY,
        double Left,
        double Top,
        double Width,
        double Height);

    /// <summary>
    /// Fit-inside (letterbox) scale for a preview texture into a control, matching DX
    /// <c>(Width-2)/(tex.W)</c> / <c>(Height-2)/(tex.H)</c> logic.
    /// </summary>
    public static LetterboxLayout ComputeLetterbox(int controlWidth, int controlHeight, int previewWidth, int previewHeight)
    {
        if (controlWidth <= 2 || controlHeight <= 2 || previewWidth <= 0 || previewHeight <= 0)
            return new LetterboxLayout(0, 0, 0, 0, 0);

        double xRatio = (controlWidth - 2) / (double)previewWidth;
        double yRatio = (controlHeight - 2) / (double)previewHeight;

        if (xRatio > yRatio)
        {
            double ratio = yRatio;
            int textureHeight = controlHeight - 2;
            int textureWidth = (int)(previewWidth * ratio);
            int textureX = (controlWidth - 2 - textureWidth) / 2;
            return new LetterboxLayout(ratio, textureX, 1, textureWidth, textureHeight);
        }
        else
        {
            double ratio = xRatio;
            int textureWidth = controlWidth - 2;
            int textureHeight = (int)(previewHeight * ratio);
            int textureY = (controlHeight - 2 - textureHeight) / 2 + 1;
            return new LetterboxLayout(ratio, 1, textureY, textureWidth, textureHeight);
        }
    }

    /// <summary>
    /// Maps a preview-texture pixel into a control-space indicator rect centered on the point.
    /// </summary>
    public static IndicatorBounds ProjectIndicator(
        LetterboxLayout layout,
        int previewPixelX,
        int previewPixelY,
        double diameter = DefaultIndicatorDiameter)
    {
        if (layout.Ratio <= 0 || diameter <= 0)
            return new IndicatorBounds(0, 0, 0, 0, 0, 0);

        double centerX = layout.TextureX + previewPixelX * layout.Ratio;
        double centerY = layout.TextureY + previewPixelY * layout.Ratio;
        double left = centerX - diameter / 2.0;
        double top = centerY - diameter / 2.0;
        return new IndicatorBounds(centerX, centerY, left, top, diameter, diameter);
    }

    /// <summary>
    /// Returns the 0-based marker index whose rect contains <paramref name="controlX"/>/<paramref name="controlY"/>,
    /// or <c>null</c> when no marker is hit. Last-drawn (highest index) wins on overlap.
    /// </summary>
    public static int? HitTest(ReadOnlySpan<IndicatorBounds> markers, double controlX, double controlY)
    {
        for (int i = markers.Length - 1; i >= 0; i--)
        {
            IndicatorBounds m = markers[i];
            if (controlX >= m.Left
                && controlX < m.Left + m.Width
                && controlY >= m.Top
                && controlY < m.Top + m.Height)
            {
                return i;
            }
        }

        return null;
    }
}
