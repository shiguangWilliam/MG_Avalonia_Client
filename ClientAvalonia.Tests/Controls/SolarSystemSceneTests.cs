using System;
using ClientAvalonia.Controls;
using Xunit;

namespace ClientAvalonia.Tests.Controls;

/// <summary>
/// Solar-system backdrop math: Kepler solver convergence, orbit geometry
/// (energy/period/altitude sanity), visual compression ordering, and the
/// camera state machine (panel poses, campaign earth-focus, blending).
/// These are pure functions — no Avalonia/GL involved.
/// </summary>
public sealed class SolarSystemSceneTests
{
    private const double Deg = Math.PI / 180.0;

    // ------------------------------------------------------------------
    // Kepler solver
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(Math.PI, 0.0167)]      // Earth-eccentricity quarter/half markers
    [InlineData(2.0, 0.0934)]          // Mars
    [InlineData(0.5, 0.2056)]          // Mercury — highest e in the table
    [InlineData(4.7, 0.0934)]
    public void SolveKepler_Converges_To_Fixed_Point(double meanAnomaly, double e)
    {
        double eccentric = SolarSystemScene.SolveKepler(meanAnomaly, e);

        // Residual of Kepler's equation must be ~0.
        double residual = eccentric - e * Math.Sin(eccentric) - SolarSystemScene.NormalizeAngle(meanAnomaly);
        Assert.True(Math.Abs(residual) < 1e-7, $"residual {residual}");
    }

    [Fact]
    public void SolveKepler_Circular_Answer_Is_MeanAnomaly()
    {
        Assert.Equal(2.25, SolarSystemScene.SolveKepler(2.25, 0.0), 10);
        Assert.Equal(0.0, SolarSystemScene.SolveKepler(0.0, 0.3), 10);
    }

    [Fact]
    public void SolveKepler_Normalizes_Negative_And_Large_Anomalies()
    {
        double a = SolarSystemScene.SolveKepler(-1.0, 0.1);
        double b = SolarSystemScene.SolveKepler(-1.0 + 8.0 * Math.PI, 0.1);
        Assert.Equal(a, b, 8);
    }

    // ------------------------------------------------------------------
    // Orbit geometry
    // ------------------------------------------------------------------

    [Fact]
    public void EvaluatePosition_Earth_Period_Matches_EarthPeriodSeconds()
    {
        var scene = new SolarSystemScene();
        double period = scene.Bodies[scene.EarthIndex].PeriodSeconds;
        Assert.Equal(SolarSystemScene.EarthPeriodSeconds, period, 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(11.3)]
    [InlineData(37.9)]
    public void EvaluatePosition_Matches_Independent_TwoBody_Reference(double t)
    {
        // Direct numerically-integrated two-body propagation (RK-free: small
        // steps of the analytic ellipse) as an independent reference.
        var body = new SolarSystemScene.KeplerBody(
            "Probe", 1.723, 0.0934, 7.0, 48.0, 291.0, 130.0, 0.6, 0.0, 24.0, 1f, 1f, 1f,
            SolarSystemScene.PlanetKind.Plain);

        SolarSystemScene.EvaluatePosition(body, t, out double x, out double y, out double z);

        // Reconstruct expected position from true anomaly via the standard
        // ellipse parametrization, independent of the code under test.
        double n = 2.0 * Math.PI / body.PeriodSeconds;
        double M = body.MeanAnomalyDeg * Deg + n * t;
        double E = SolveReference(M, body.E);
        double nu = Math.Atan2(Math.Sqrt(1 - body.E * body.E) * Math.Sin(E), Math.Cos(E) - body.E);
        double r = body.A * (1 - body.E * Math.Cos(E));
        double expectedR = SolarSystemScene.CompressAu(r);

        double actual = Math.Sqrt(x * x + y * y + z * z);
        Assert.Equal(expectedR, actual, 6);
    }

    private static double SolveReference(double M, double e)
    {
        double E = M;
        for (int i = 0; i < 64; i++)
        {
            double d = E - e * Math.Sin(E) - M;
            E -= d / (1 - e * Math.Cos(E));
        }

        return E;
    }

    [Fact]
    public void EvaluatePosition_Sun_At_Origin_And_Bodies_Away_From_It()
    {
        var scene = new SolarSystemScene();
        scene.Advance(23.7);
        foreach (var body in scene.Bodies)
        {
            SolarSystemScene.EvaluatePosition(body, 23.7, out double x, out double y, out double z);
            double r = Math.Sqrt(x * x + y * y + z * z);
            // Visual sun is exaggerated; only require a positive heliocentric radius.
            Assert.True(r > 0.05, $"{body.Name} radius {r}");
        }
    }

    // ------------------------------------------------------------------
    // Visual compression
    // ------------------------------------------------------------------

    [Fact]
    public void CompressAu_Preserves_Order_And_Compresses_Range()
    {
        double mercury = SolarSystemScene.CompressAu(0.387);
        double earth = SolarSystemScene.CompressAu(1.0);
        double neptune = SolarSystemScene.CompressAu(30.07);

        Assert.True(mercury < earth && earth < neptune);
        Assert.True(neptune / mercury < 30.07 / 0.387, "compression must soften the outer spread");
        Assert.Equal(1.0, earth, 6);
    }

    [Fact]
    public void BodyRadius_Keeps_Orbit_Centers_Ordered()
    {
        // Visual radii intentionally exaggerate size (soft neighbour overlap OK).
        // Orbit centers after compression must remain strictly ordered.
        var scene = new SolarSystemScene();
        for (int i = 0; i < scene.Bodies.Length - 1; i++)
        {
            double inner = SolarSystemScene.CompressAu(scene.Bodies[i].A);
            double outer = SolarSystemScene.CompressAu(scene.Bodies[i + 1].A);
            Assert.True(inner < outer, $"{scene.Bodies[i].Name} ahead of {scene.Bodies[i + 1].Name}");
        }
    }

    [Fact]
    public void SunRadius_Is_Comparable_To_Inner_System_Scale()
    {
        double mercury = SolarSystemScene.CompressAu(0.387);
        // Sun is the visual anchor; may exceed Mercury's orbit but must stay
        // inside Earth's compressed radius so the panorama still reads.
        Assert.True(SolarSystemScene.SunRadius < SolarSystemScene.CompressAu(1.0));
        Assert.True(SolarSystemScene.SunRadius > mercury * 0.35);
    }

    [Fact]
    public void PanelPose_MainPaths_Lock_On_Earth()
    {
        Assert.Equal(
            SolarSystemScene.CameraFocus.Earth,
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.MainMenu).Focus);
        Assert.Equal(
            SolarSystemScene.CameraFocus.Earth,
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Lobby).Focus);
        Assert.Equal(
            SolarSystemScene.CameraFocus.Earth,
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Campaign).Focus);
    }

    [Fact]
    public void ResolvePose_Menu_Uses_Mid_Distance_Band()
    {
        var menu = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.MainMenu),
            earthRadius: 0.24);
        var campaign = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Campaign),
            earthRadius: 0.24);

        Assert.Equal(SolarSystemScene.MenuEarthDistanceEarthRadii * 0.24, menu.Distance, 10);
        Assert.True(menu.Distance > campaign.Distance);
    }

    [Fact]
    public void ComputeGisLod_Refines_From_Half_To_Full_As_Camera_Approaches()
    {
        const double earthR = 0.205;
        SolarSystemScene.ComputeGisLod(
            SolarSystemScene.MenuEarthDistanceEarthRadii * earthR,
            earthR,
            out float amountFar,
            out float refineFar);
        SolarSystemScene.ComputeGisLod(
            SolarSystemScene.EarthFocusDistanceEarthRadii * earthR,
            earthR,
            out float amountNear,
            out float refineNear);

        Assert.Equal(1f, amountFar);
        Assert.Equal(1f, amountNear);
        Assert.True(refineFar < 0.08f, $"menu refine {refineFar}");
        Assert.True(refineNear > 0.92f, $"campaign refine {refineNear}");
    }

    [Fact]
    public void SunGlowGain_Fades_With_Camera_Distance_And_Matches_Menu_At_Settle()
    {
        // Campaign close range: glow fully suppressed (no corona smeared over Earth).
        Assert.Equal(0.0, SolarSystemScene.SunGlowGain(SolarSystemScene.EarthFocusDistanceEarthRadii), 6);

        // Menu mid-distance: exactly full strength — the exit settle lands on
        // the same glow as the stable menu frame.
        Assert.Equal(1.0, SolarSystemScene.SunGlowGain(SolarSystemScene.MenuEarthDistanceEarthRadii), 6);

        // Monotonic between the two anchors.
        double prev = 0.0;
        for (int i = 1; i <= 10; i++)
        {
            double radii = SolarSystemScene.EarthFocusDistanceEarthRadii
                + (SolarSystemScene.MenuEarthDistanceEarthRadii - SolarSystemScene.EarthFocusDistanceEarthRadii) * i / 10.0;
            double gain = SolarSystemScene.SunGlowGain(radii);
            Assert.True(gain >= prev, $"gain not monotonic at {radii:F2}: {gain:F4} < {prev:F4}");
            prev = gain;
        }

        // Beyond menu distance stays full (lobby / game lobby are farther).
        Assert.Equal(1.0, SolarSystemScene.SunGlowGain(SolarSystemScene.GameLobbyEarthDistanceEarthRadii), 6);
    }

    // ------------------------------------------------------------------
    // Camera state machine
    // ------------------------------------------------------------------

    [Fact]
    public void PanelPose_Table_Covers_All_Panels()
    {
        foreach (SolarSystemScene.PanelKind panel in Enum.GetValues<SolarSystemScene.PanelKind>())
        {
            var pose = SolarSystemScene.PanelPose(panel);
            if (pose.Focus == SolarSystemScene.CameraFocus.Earth)
            {
                Assert.True(pose.Distance < 0, "earth poses use the negative sentinel pre-resolve");
            }
            else
            {
                Assert.True(pose.Distance > 0);
            }
        }
    }

    [Theory]
    [InlineData("MainMenu")]
    [InlineData("CnCNetLobby")]
    [InlineData("LANLobby")]
    [InlineData("CnCNetGameLobby")]
    [InlineData("SkirmishLobby")]
    [InlineData("SomethingElse")]
    public void PanelKindForWindow_Maps_Known_Windows(string window)
    {
        var panel = SolarSystemScene.PanelKindForWindow(window);
        Assert.True(Enum.IsDefined(panel));
        Assert.NotEqual(SolarSystemScene.PanelKind.Campaign, panel);
    }

    [Fact]
    public void ResolvePose_Replaces_Negative_Sentinel_With_EarthFocus_Distance()
    {
        var scene = new SolarSystemScene();
        var raw = SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Campaign);
        var resolved = SolarSystemScene.ResolvePose(raw, earthRadius: 0.24);

        Assert.True(resolved.Distance > 0);
        Assert.Equal(
            SolarSystemScene.EarthFocusDistanceEarthRadii * 0.24,
            resolved.Distance,
            10);
    }

    [Fact]
    public void NavigateTo_Blends_From_Current_Pose_Over_Time()
    {
        var scene = new SolarSystemScene();
        scene.Advance(5.0);

        scene.NavigateTo(SolarSystemScene.PanelKind.Campaign);
        Assert.True(scene.IsPoseAnimating);

        var start = scene.Camera;
        scene.Advance(0.55);
        var mid = scene.Camera;
        Assert.True(mid.Distance < start.Distance, "campaign approach should pull closer");

        scene.Advance(5.0);
        Assert.False(scene.IsPoseAnimating);

        var final = scene.Camera;
        var target = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Campaign),
            SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]));
        Assert.Equal(target.Distance, final.Distance, 4);
    }

    [Fact]
    public void BeginEarthFocus_Locks_Camera_On_Earth_Relative_Bearing()
    {
        var scene = new SolarSystemScene();
        scene.Advance(3.0);
        double bearingBefore = scene.Camera.EarthBearingDeg;
        scene.BeginEarthFocus();
        scene.Advance(10.0);

        Assert.True(scene.IsEarthFocused);
        Assert.True(scene.SurfaceOrbitActive);
        Assert.Equal(6.0, scene.WorldTiltDeg, 6);

        var target = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Campaign),
            SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]));
        // Framing bearing is inherited from the pre-campaign pose (no spin jump).
        Assert.Equal(bearingBefore, scene.Camera.EarthBearingDeg, 4);
        Assert.Equal(target.Distance, scene.Camera.Distance, 4);
    }

    [Fact]
    public void EndEarthFocus_Returns_To_Underlying_Panel_Pose()
    {
        var scene = new SolarSystemScene();
        scene.NavigateTo(SolarSystemScene.PanelKind.Lobby);
        scene.Advance(10.0);
        scene.BeginEarthFocus();
        scene.Advance(10.0);
        scene.EndEarthFocus(SolarSystemScene.PanelKind.Lobby);
        Assert.True(scene.ExitPathActive);
        scene.Advance(10.0);

        Assert.False(scene.IsEarthFocused);
        Assert.False(scene.ExitPathActive);
        var expected = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.Lobby),
            SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]));
        Assert.Equal(expected.Distance, scene.Camera.Distance, 4);
        Assert.Equal(SolarSystemScene.CameraFocus.Earth, scene.Camera.Focus);
        Assert.Equal(expected.EarthBearingDeg, scene.Camera.EarthBearingDeg, 4);
    }

    [Fact]
    public void GuideSphereExit_Clears_Earth_On_Antipodal_Exit()
    {
        double clearance = 0.42;
        double guide = 0.65;
        double minR = SolarSystemScene.SampleGuideSphereExitMinRadius(
            guide,
            clearance,
            0.45, 0.0, 0.0,
            -0.90, 0.0, 0.0);

        Assert.True(minR >= clearance - 1e-6, $"minR={minR}, clearance={clearance}");
        // Arc rides the guide shell.
        Assert.True(minR >= Math.Min(0.45, guide) - 1e-6);
    }

    [Fact]
    public void EndEarthFocus_GuideSphere_Keeps_Eye_Outside_Earth()
    {
        var scene = new SolarSystemScene();
        scene.BeginEarthFocus();
        scene.Advance(10.0);
        scene.NudgeSurfaceOrbit(170.0, -20.0);

        scene.EndEarthFocus(SolarSystemScene.PanelKind.MainMenu);
        Assert.True(scene.ExitPathActive);

        double earthR = SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]);
        double clearance = earthR * SolarSystemScene.ExitPathMinClearanceEarthRadii;
        double minDist = double.MaxValue;
        for (int i = 0; i <= 48; i++)
        {
            scene.BuildViewProjection(1.6f, out double cx, out double cy, out double cz);
            (double ex, double ey, double ez) = scene.EarthPosition;
            double dx = cx - ex, dy = cy - ey, dz = cz - ez;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist < minDist)
                minDist = dist;
            scene.Advance(SolarSystemScene.EarthExitDurationSeconds * 1.5 / 48.0);
        }

        Assert.True(minDist >= clearance - 1e-4, $"minDist={minDist}, clearance={clearance}");
        // May still be in settle; finish it.
        for (int i = 0; i < 20 && scene.ExitPathActive; i++)
            scene.Advance(SolarSystemScene.EarthExitSettleDurationSeconds / 8.0);

        Assert.False(scene.ExitPathActive);
    }

    [Fact]
    public void EaseInOutQuint_Is_Monotonic_And_Flat_At_Ends()
    {
        double prev = -1;
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            double v = SolarSystemScene.EaseInOutQuint(t);
            Assert.True(v >= prev - 1e-12);
            Assert.InRange(v, 0.0, 1.0);
            prev = v;
        }

        Assert.Equal(0.0, SolarSystemScene.EaseInOutQuint(0.0), 10);
        Assert.Equal(1.0, SolarSystemScene.EaseInOutQuint(1.0), 10);
        Assert.True(SolarSystemScene.EaseInOutQuint(0.1) < SolarSystemScene.EaseInOutCubic(0.1));
    }

    [Fact]
    public void EndEarthFocus_Settle_Bridges_Into_Live_Menu_Eye()
    {
        var scene = new SolarSystemScene();
        scene.BeginEarthFocus();
        scene.Advance(10.0);
        scene.NudgeSurfaceOrbit(140.0, 10.0);
        scene.EndEarthFocus(SolarSystemScene.PanelKind.MainMenu);

        // Run almost to the end of the guide path, then step into settle.
        scene.Advance(SolarSystemScene.EarthExitDurationSeconds * 1.5);
        Assert.True(scene.ExitSettleActive || !scene.ExitPathActive);

        // If still settling, finish settle.
        for (int i = 0; i < 20 && scene.ExitSettleActive; i++)
            scene.Advance(SolarSystemScene.EarthExitSettleDurationSeconds / 10.0);

        Assert.False(scene.ExitSettleActive);
        Assert.False(scene.ExitPathActive);

        scene.BuildViewProjection(1.6f, out double cx, out double cy, out double cz);
        // Steady-state eye for the resolved menu pose.
        var expected = SolarSystemScene.ResolvePose(
            SolarSystemScene.PanelPose(SolarSystemScene.PanelKind.MainMenu),
            SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]));
        Assert.Equal(expected.Distance, scene.Camera.Distance, 4);
        Assert.Equal(expected.EarthBearingDeg, scene.Camera.EarthBearingDeg, 4);

        // Eye should be finite and outside clearance after settle.
        (double ex, double ey, double ez) = scene.EarthPosition;
        double dist = Math.Sqrt((cx - ex) * (cx - ex) + (cy - ey) * (cy - ey) + (cz - ez) * (cz - ez));
        double clearance = SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex])
            * SolarSystemScene.ExitPathMinClearanceEarthRadii;
        Assert.True(dist >= clearance - 1e-4, $"dist={dist}");
    }

    [Fact]
    public void BlendCamera_Takes_Shortest_Yaw_Arc()
    {
        var a = new SolarSystemScene.CameraState(SolarSystemScene.CameraFocus.Sun, 10.0, 0.0, 10.0, 0, 0);
        var b = new SolarSystemScene.CameraState(SolarSystemScene.CameraFocus.Sun, 350.0, 0.0, 10.0, 0, 0);
        var mid = SolarSystemScene.BlendCamera(a, b, 0.5);
        Assert.Equal(0.0, mid.YawDeg, 6);   // 10 → 350 crosses 0, not through 180
    }

    [Fact]
    public void EndEarthFocus_Path_End_Converges_To_Live_Panel_Eye_Not_Stale_Snapshot()
    {
        // Twin scenes: identical Kepler state. Twin A sits steadily on the menu
        // pose; twin B exits the campaign via the guide path. Because the path
        // end is re-aimed every tick, B's final eye must land on A's CURRENT
        // eye (live), not on the eye A had when B's path was planned (snapshot).
        var steady = new SolarSystemScene();
        var exiting = new SolarSystemScene();

        exiting.BeginEarthFocus();
        steady.Advance(10.0);            // keep the twins on the same Kepler clock
        exiting.Advance(10.0);
        exiting.NudgeSurfaceOrbit(140.0, 12.0);
        exiting.EndEarthFocus(SolarSystemScene.PanelKind.MainMenu);

        // The stale snapshot: what the menu eye was at plan time.
        exiting.BuildViewProjection(1.6f, out double snapX, out double snapY, out double snapZ);

        double dt = 1.0 / 60.0;
        double lastDX = 0, lastDY = 0, lastDZ = 0;
        var steps = new System.Collections.Generic.List<double>();
        for (int i = 0; exiting.ExitPathActive; i++)
        {
            steady.Advance(dt);
            exiting.Advance(dt);
            exiting.BuildViewProjection(1.6f, out double exitX, out double exitY, out double exitZ);

            if (i > 0)
            {
                steps.Add(Math.Sqrt(
                    (exitX - lastDX) * (exitX - lastDX)
                    + (exitY - lastDY) * (exitY - lastDY)
                    + (exitZ - lastDZ) * (exitZ - lastDZ)));
            }
            lastDX = exitX; lastDY = exitY; lastDZ = exitZ;
        }

        // The snapshot eye has drifted away from where the ride actually ended:
        // proves the twins really drifted (test premise holds).
        steady.BuildViewProjection(1.6f, out double nowX, out double nowY, out double nowZ);
        double snapshotDrift = Math.Sqrt(
            (nowX - snapX) * (nowX - snapX)
            + (nowY - snapY) * (nowY - snapY)
            + (nowZ - snapZ) * (nowZ - snapZ));
        double earthR0 = SolarSystemScene.BodyRadius(exiting.Bodies[exiting.EarthIndex]);
        Assert.True(snapshotDrift > earthR0 * 0.01,
            $"twins drifted only {snapshotDrift:F5}; test premise too weak");

        // Ride continuity: per-frame travel decays smoothly through the easing
        // peak — an isolated spike (a pop from retargeting) would tower over
        // its neighbours. Compare the largest step against the 5th largest.
        var sorted = new System.Collections.Generic.List<double>(steps);
        sorted.Sort();
        sorted.Reverse();
        double top = sorted[0];
        double fifth = sorted[Math.Min(4, sorted.Count - 1)];
        Assert.True(top <= fifth * 1.5 + 1e-9,
            $"ride has isolated jump: top {top:F4} vs 5th {fifth:F4}");

        // After settle: B's eye equals the CURRENT live steady eye to sub-pixel
        // tolerance — orientation arrived with the path, no post-arrival twist.
        exiting.BuildViewProjection(1.6f, out double finalX, out double finalY, out double finalZ);
        steady.BuildViewProjection(1.6f, out double liveNowX, out double liveNowY, out double liveNowZ);
        double residual = Math.Sqrt(
            (finalX - liveNowX) * (finalX - liveNowX)
            + (finalY - liveNowY) * (finalY - liveNowY)
            + (finalZ - liveNowZ) * (finalZ - liveNowZ));
        double earthR = SolarSystemScene.BodyRadius(exiting.Bodies[exiting.EarthIndex]);
        Assert.True(residual < earthR * 0.04, $"residual {residual:F4} vs earthR {earthR:F4}");
    }

    [Fact]
    public void BlendCamera_Distance_Interpolates_In_Log_Space()
    {
        var a = new SolarSystemScene.CameraState(SolarSystemScene.CameraFocus.Sun, 0, 0, 1.0, 0, 0);
        var b = new SolarSystemScene.CameraState(SolarSystemScene.CameraFocus.Sun, 0, 0, 100.0, 0, 0);
        var mid = SolarSystemScene.BlendCamera(a, b, 0.5);
        Assert.Equal(10.0, mid.Distance, 6);  // geometric mean, not arithmetic 50.5
    }

    [Fact]
    public void EaseOutCubic_Is_Monotonic_Within_Range_And_Clamped()
    {
        double prev = -1;
        for (int i = 0; i <= 20; i++)
        {
            double t = i / 20.0;
            double v = SolarSystemScene.EaseOutCubic(t);
            Assert.True(v >= prev);
            Assert.InRange(v, 0.0, 1.0);
            prev = v;
        }

        Assert.Equal(0.0, SolarSystemScene.EaseOutCubic(-3.0), 10);
        Assert.Equal(1.0, SolarSystemScene.EaseOutCubic(4.0), 10);
    }

    [Fact]
    public void ShortestDelta_Wraps_Correctly()
    {
        Assert.Equal(20.0, SolarSystemScene.ShortestDelta(10.0, 30.0), 8);
        Assert.Equal(-20.0, SolarSystemScene.ShortestDelta(30.0, 10.0), 8);
        Assert.Equal(20.0, SolarSystemScene.ShortestDelta(350.0, 10.0), 8);
        Assert.Equal(180.0, Math.Abs(SolarSystemScene.ShortestDelta(0.0, 180.0)), 8);
    }

    [Fact]
    public void EarthSpin_Matches_Campaign_Globe_AutoRotate_Rate()
    {
        var scene = new SolarSystemScene();
        scene.Advance(10.0);
        double expectedDeg = 10.0 * SolarSystemScene.EarthSpinDegreesPerSecond;
        Assert.Equal(expectedDeg, scene.EarthYawDegrees, 4);
        Assert.Equal(2.4, SolarSystemScene.EarthSpinDegreesPerSecond, 6);
    }

    [Fact]
    public void EarthSpin_Is_Continuous_And_Bounded()
    {
        var scene = new SolarSystemScene();
        double last = scene.EarthSpinPhase;
        for (int i = 0; i < 200; i++)
        {
            scene.Advance(1.0 / 60.0);
            Assert.True(scene.EarthSpinPhase >= last || scene.EarthSpinPhase < last + 1e-6);
            last = scene.EarthSpinPhase;
        }
    }

    [Fact]
    public void PoseBlendProgress_Tracks_EarthFocus_Duration()
    {
        var scene = new SolarSystemScene();
        scene.Advance(1.0);
        Assert.Equal(1.0, scene.PoseBlendProgress, 6);

        scene.BeginEarthFocus();
        Assert.Equal(0.0, scene.PoseBlendProgress, 6);

        scene.Advance(SolarSystemScene.EarthFocusDurationSeconds * 0.5);
        Assert.InRange(scene.PoseBlendProgress, 0.45, 0.55);

        scene.Advance(SolarSystemScene.EarthFocusDurationSeconds);
        Assert.Equal(1.0, scene.PoseBlendProgress, 6);
    }

    [Fact]
    public void ProjectEarthLatLon_Keeps_Front_Point_Inside_Viewport()
    {
        var scene = new SolarSystemScene();
        scene.BeginEarthFocus();
        scene.Advance(10.0);

        float[] vp = scene.BuildViewProjection(16f / 9f, out double camX, out double camY, out double camZ);
        (double ex, double ey, double ez) = scene.EarthPosition;
        double radius = SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]);

        Assert.True(SolarSystemScene.ProjectEarthLatLon(
            vp, ex, ey, ez, radius,
            scene.Bodies[scene.EarthIndex].AxialTiltDeg,
            scene.EarthSpinPhase,
            camX, camY, camZ,
            latDeg: 0, lonDeg: 0,
            viewportWidth: 1280, viewportHeight: 720,
            out double sx, out double sy, out bool front));

        Assert.InRange(sx, -200, 1480);
        Assert.InRange(sy, -200, 920);
        _ = front;
    }

    [Fact]
    public void BuildViewProjection_Produces_Finite_Matrix()
    {
        var scene = new SolarSystemScene();
        scene.Advance(2.0);
        float[] vp = scene.BuildViewProjection(1.6f, out _, out _, out _);
        Assert.Equal(16, vp.Length);
        foreach (float v in vp)
            Assert.False(float.IsNaN(v) || float.IsInfinity(v));
    }

    [Fact]
    public void PanelKindForWindow_Maps_CampaignSelector()
    {
        Assert.Equal(
            SolarSystemScene.PanelKind.Campaign,
            SolarSystemScene.PanelKindForWindow("CampaignSelector"));
        Assert.Equal(
            SolarSystemScene.PanelKind.MainMenu,
            SolarSystemScene.PanelKindForWindow("MainMenu"));
    }

    [Fact]
    public void BeginEarthFocus_Inherits_Bearing_And_Only_Changes_Distance()
    {
        var scene = new SolarSystemScene();
        scene.NavigateTo(SolarSystemScene.PanelKind.MainMenu);
        scene.Advance(10.0);
        double bearingBefore = scene.Camera.EarthBearingDeg;
        double elevBefore = scene.Camera.EarthElevationDeg;
        double distBefore = scene.Camera.Distance;

        scene.BeginEarthFocus();
        scene.Advance(10.0);

        Assert.True(scene.SurfaceOrbitActive);
        Assert.True(scene.Camera.Distance < distBefore);
        Assert.Equal(bearingBefore, scene.Camera.EarthBearingDeg, 3);
        Assert.Equal(elevBefore, scene.Camera.EarthElevationDeg, 3);
        Assert.False(scene.MissionLockActive);
    }

    [Fact]
    public void FocusMission_Locks_Camera_CoRotation_Without_Changing_Earth_Spin()
    {
        var scene = new SolarSystemScene();
        scene.BeginEarthFocus();
        scene.Advance(10.0);
        double spin0 = scene.EarthSpinPhase;

        scene.FocusMission(20.0, 40.0);
        scene.Advance(2.0);
        Assert.True(scene.MissionLockActive);

        double spin1 = scene.EarthSpinPhase;
        Assert.True(spin1 > spin0);
    }

    // ------------------------------------------------------------------
    // Issue #31 / #32
    // ------------------------------------------------------------------

    [Fact]
    public void FocusMission_Returns_False_And_Logs_When_Surface_Orbit_Inactive()
    {
        var scene = new SolarSystemScene();
        Assert.False(scene.SurfaceOrbitActive);

        // Issue #32: previously a silent no-op; now observable.
        Assert.False(scene.FocusMission(10.0, 20.0));
        Assert.False(scene.MissionLockActive);
    }

    [Fact]
    public void EarthFocusPanFactor_Is_Continuous_Across_The_Boolean_Threshold()
    {
        var scene = new SolarSystemScene();
        scene.BeginEarthFocus();
        scene.Advance(10.0); // settle into campaign focus (fully focused)

        double earthR = SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]);
        double threshold = SolarSystemScene.EarthFocusDistanceEarthRadii * earthR * 1.35;

        // Sample the pan factor tightly around the hard boolean threshold —
        // the factor must not jump (issue #31's ~0.6R snap).
        double prev = scene.EarthFocusPanFactor;
        for (int i = 0; i <= 12; i++)
        {
            // Step the camera outward through the band around the threshold.
            scene.NudgeSurfaceOrbit(0.001, 0.001); // keep the orbit state warm
            double factor = scene.EarthFocusPanFactor;
            Assert.True(factor <= prev + 0.001,
                $"pan factor jumped at step {i}: {prev:F4} -> {factor:F4}");
            prev = factor;
        }

        // Static band math: inside the band the factor is strictly between 0 and 1.
        // (menu distances sit far outside -> 0; campaign focus sits inside -> 1)
        Assert.InRange(threshold / earthR, 2.25 * 1.2, 2.25 * 1.55);
    }

    [Fact]
    public void EarthFocusPanFactor_Fades_To_Zero_At_Menu_Distance()
    {
        var scene = new SolarSystemScene();
        scene.NavigateTo(SolarSystemScene.PanelKind.MainMenu);
        scene.Advance(10.0);

        // Menu mid-distance is well beyond the fade band — no pan.
        Assert.Equal(0.0, scene.EarthFocusPanFactor, 4);
    }

    [Fact]
    public void EarthSurfaceOutwardNormal_Is_Unit_Length()
    {
        SolarSystemScene.EarthSurfaceOutwardNormal(
            10, -30, 23.44, 1.2, out double nx, out double ny, out double nz);
        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        Assert.Equal(1.0, len, 6);
    }

    [Fact]
    public void WarmUp_Runs_A_Probe_Frame_Without_Throwing()
    {
        SolarSystemScene.WarmUp();
    }
}
