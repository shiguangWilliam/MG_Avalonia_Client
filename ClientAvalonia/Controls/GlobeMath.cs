using System;

namespace ClientAvalonia.Controls;

/// <summary>
/// Pure pose/geometry math shared by the F1 focus animation, F4A holo board
/// layout and their unit tests. No Avalonia dependency on purpose.
/// </summary>
internal static class GlobeMath
{
    /// <summary>Pitch clamp shared by focus targets and manual drag.</summary>
    public const double PitchClampDegrees = 40.0;

    /// <summary>
    /// Yaw that centers (lat, lon) on screen: Dir(lat, lon) after
    /// Rx(pitch)·Ry(yaw) must equal (0, 0, 1); solving per-component yields
    /// yaw = −lon (degrees, any 360-equivalent).
    /// </summary>
    public static double TargetYaw(double longitudeDegrees) => -longitudeDegrees;

    /// <summary>Pitch that centers the latitude, clamped away from the poles.</summary>
    public static double TargetPitch(double latitudeDegrees)
        => Math.Clamp(latitudeDegrees, -PitchClampDegrees, PitchClampDegrees);

    /// <summary>Shortest signed yaw delta from <paramref name="from"/> to <paramref name="to"/> in (−180, 180].</summary>
    public static double ShortestYawDelta(double from, double to)
        => ((to - from + 540.0) % 360.0) - 180.0;

    /// <summary>ease-out cubic: fast start, gentle settle.</summary>
    public static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), 3.0);

    /// <summary>
    /// F4A board placement: clamps horizontally into the viewport and flips
    /// below the anchor when there is no headroom. Returns
    /// (left, top, placedBelow).
    /// </summary>
    public static (double Left, double Top, bool Below) ClampHoloBoard(
        double anchorX,
        double anchorY,
        double boardWidth,
        double boardHeight,
        double viewportWidth,
        double viewportHeight)
    {
        double left = Math.Clamp(anchorX - boardWidth / 2.0, 4.0, Math.Max(4.0, viewportWidth - boardWidth - 4.0));

        double margin = 12.0;
        double topCeiling = Math.Max(4.0, viewportHeight - boardHeight - 4.0);
        bool below = anchorY - boardHeight - margin < 4.0;
        double top = below
            ? Math.Clamp(anchorY + margin, 4.0, topCeiling)
            : Math.Clamp(anchorY - boardHeight - margin, 4.0, topCeiling);

        return (left, top, below);
    }
}
