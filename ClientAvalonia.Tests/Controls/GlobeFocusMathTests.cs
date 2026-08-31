using System;
using ClientAvalonia.Controls;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// F1 focus math: target pose derivation (Dir must land on the camera axis),
/// shortest-arc yaw and ease-out monotonicity. These formulas are shared by
/// the overlay projection, so a regression here would misalign every anchor.
/// </summary>
public sealed class GlobeFocusMathTests
{
    // Mirrors TacticalGlobeView.RenderOverlay's Dir() (yaw then pitch).
    private static (double X, double Y, double Z) Dir(double latDeg, double lonDeg, double yawDeg, double pitchDeg)
    {
        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;
        double yaw = yawDeg * Math.PI / 180.0;
        double pitch = pitchDeg * Math.PI / 180.0;

        double x = Math.Cos(lat) * Math.Sin(lon + yaw);
        double y = Math.Sin(lat);
        double z = Math.Cos(lat) * Math.Cos(lon + yaw);
        double y1 = y * Math.Cos(pitch) - z * Math.Sin(pitch);
        double z1 = y * Math.Sin(pitch) + z * Math.Cos(pitch);
        return (x, y1, z1);
    }

    [Theory]
    [InlineData(51.5, -0.13)]   // London
    [InlineData(33.3, 44.4)]    // Baghdad
    [InlineData(-33.9, 151.2)]  // Sydney
    [InlineData(0.0, 0.0)]      // Gulf of Guinea
    [InlineData(41.9, 12.5)]    // Rome
    [InlineData(64.1, -21.9)]   // Reykjavik
    public void TargetPose_Centers_Coordinate_On_Camera_Axis(double lat, double lon)
    {
        double yaw = GlobeMath.TargetYaw(lon);
        double pitch = GlobeMath.TargetPitch(lat);

        // Unclamped latitudes center exactly; clamped ones still face forward.
        if (Math.Abs(lat) <= GlobeMath.PitchClampDegrees)
        {
            (double x, double y, double z) = Dir(lat, lon, yaw, pitch);
            Assert.Equal(0.0, x, 10);
            Assert.Equal(0.0, y, 10);
            Assert.Equal(1.0, z, 10);
        }
        else
        {
            (double _, double _, double z) = Dir(lat, lon, yaw, pitch);
            Assert.True(z > 0.5, $"clamped target should still face camera, z={z}");
        }
    }

    [Fact]
    public void TargetPitch_Clamps_Poles()
    {
        Assert.Equal(40.0, GlobeMath.TargetPitch(89.0));
        Assert.Equal(-40.0, GlobeMath.TargetPitch(-89.0));
        Assert.Equal(0.0, GlobeMath.TargetPitch(0.0));
    }

    [Theory]
    [InlineData(170.0, -170.0, 20.0)]   // across the antimeridian, short way east
    [InlineData(-170.0, 170.0, -20.0)]  // short way west
    [InlineData(0.0, 90.0, 90.0)]
    [InlineData(350.0, 10.0, 20.0)]
    [InlineData(10.0, 10.0, 0.0)]
    public void ShortestYawDelta_Picks_Short_Arc(double from, double to, double expected)
    {
        Assert.Equal(expected, GlobeMath.ShortestYawDelta(from, to), 6);
        Assert.True(Math.Abs(GlobeMath.ShortestYawDelta(from, to)) <= 180.0 + 1e-9);
    }

    [Fact]
    public void EaseOutCubic_Is_Monotonic_And_Bounded()
    {
        double prev = -1;
        for (int i = 0; i <= 20; i++)
        {
            double t = i / 20.0;
            double v = GlobeMath.EaseOutCubic(t);
            Assert.True(v >= prev - 1e-12, "ease-out must be monotonic non-decreasing");
            Assert.InRange(v, 0.0, 1.0);
            prev = v;
        }

        Assert.Equal(0.0, GlobeMath.EaseOutCubic(0.0), 12);
        Assert.Equal(1.0, GlobeMath.EaseOutCubic(1.0), 12);
    }

    [Fact]
    public void Focus_Curve_Reaches_Target_Without_Long_Tour()
    {
        // Simulate the StepFocus integration from start pose to target.
        double start = 170.0;
        double target = GlobeMath.TargetYaw(-170.0); // = 170
        double delta = GlobeMath.ShortestYawDelta(start, target);
        Assert.True(Math.Abs(delta) < 180.0);

        double t = 0.0;
        double pose = start;
        while (t < 1.0)
        {
            t = Math.Min(1.0, t + 0.033 / 0.8);
            pose = start + delta * GlobeMath.EaseOutCubic(t);
        }

        Assert.Equal(target, pose, 6);
    }
}
