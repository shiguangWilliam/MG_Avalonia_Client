using System;
using Avalonia;
using ClientAvalonia.Services;

namespace ClientAvalonia.Controls;

/// <summary>
/// Coordinates the shared solar-system backdrop between window navigation and
/// campaign earth interaction. Campaign is a root panel:
///   - NavigateTo(CampaignSelector): clear backdrop + zoom (inherit framing)
///   - Drag / mission focus: orbit the camera; Earth keeps Kepler spin
///   - Mission lock: camera co-rotates so the selected point stays in frame
///   - Navigate away: push camera back only (no Earth spin bake)
/// </summary>
public static class SolarSystemDirector
{
    private static SolarSystemBackdropView? _backdrop;

    private static float[]? _vp;
    private static double _earthX, _earthY, _earthZ, _earthRadius;
    private static double _spinPhase, _axialTiltDeg;
    private static double _camX, _camY, _camZ;
    private static double _viewportW, _viewportH;
    private static bool _projectionValid;

    private static bool _campaignRevealActive;
    private static double _campaignRevealElapsed;
    private const double PanelRevealStartSeconds = 0.55;
    private const double PanelRevealDurationSeconds = 0.60;
    private const double AnchorRevealStartSeconds = 0.95;
    private const double AnchorRevealDurationSeconds = 0.55;

    /// <summary>Fade outer system back in before the exit camera pull.</summary>
    private static bool _exitRestoreActive;
    private static double _exitRestoreElapsed;
    private static string? _pendingExitWindow;
    private const double OuterRestoreDurationSeconds = 0.70;
    private const double OuterRestoreHoldSeconds = 0.18;

    private static double _orbitInertiaYaw;
    private static double _orbitInertiaPitch;

    internal static SolarSystemScene? Scene => _backdrop?.Gl.Scene;

    public static bool IsActive => _backdrop != null;

    /// <summary>Campaign panel: interaction mode (clear framing until exit restore finishes starting the pull).</summary>
    public static bool ClearBackdropMode { get; private set; }

    /// <summary>
    /// 0 = campaign-only Earth; 1 = full solar system (sun / outer planets / orbits).
    /// Exit fades this up before the camera pull so the outer system does not pop in.
    /// </summary>
    public static double OuterSystemOpacity { get; private set; } = 1.0;

    public static double PoseBlendProgress => Scene?.PoseBlendProgress ?? 1.0;

    public static double PanelRevealOpacity { get; private set; } = 1.0;

    public static double AnchorRevealOpacity { get; private set; } = 1.0;

    /// <summary>
    /// Issue #34: true while director-driven animation (exit outer-restore fade,
    /// orbit inertia) still needs render frames. Lets the backdrop stop its
    /// 33ms clock when both this and the scene pose blend are idle.
    /// </summary>
    public static bool HasContinuousAnimation => _exitRestoreActive
        || Math.Abs(_orbitInertiaYaw) > 0.02
        || Math.Abs(_orbitInertiaPitch) > 0.02;

    public static event Action? FrameAdvanced;

    /// <summary>Always the Kepler Earth spin — interaction never overrides it.</summary>
    public static double EffectiveEarthSpinPhase => Scene?.EarthSpinPhase ?? 0.0;

    public static void Attach(SolarSystemBackdropView backdrop) => _backdrop = backdrop;

    public static void Detach()
    {
        if (ReferenceEquals(_backdrop, null))
            return;

        _backdrop = null;
        _projectionValid = false;
        ClearBackdropMode = false;
        OuterSystemOpacity = 1.0;
        _campaignRevealActive = false;
        _exitRestoreActive = false;
        _pendingExitWindow = null;
        _orbitInertiaYaw = 0;
        _orbitInertiaPitch = 0;
        PanelRevealOpacity = 1.0;
        AnchorRevealOpacity = 1.0;
    }

    public static void OnNavigateTo(string windowName)
    {
        if (_backdrop is null)
            return;

        bool campaign = FloatingOverlayLayout.IsCampaignWindow(windowName);
        if (campaign)
        {
            EnterCampaignPanel();
            return;
        }

        if (ClearBackdropMode)
            ExitCampaignPanel(windowName);
        else
        {
            _backdrop.Gl.Scene.NavigateTo(SolarSystemScene.PanelKindForWindow(windowName));
            _backdrop.RenderOnce();
            _backdrop.EnsureClockRunning();
        }
    }

    private static void EnterCampaignPanel()
    {
        if (_backdrop is null)
            return;

        // Cancel a mid-flight exit restore if the user re-enters campaign.
        _exitRestoreActive = false;
        _pendingExitWindow = null;

        ClearBackdropMode = true;
        OuterSystemOpacity = 0.0;
        _orbitInertiaYaw = 0;
        _orbitInertiaPitch = 0;
        _backdrop.Gl.Scene.BeginEarthFocus();
        BeginCampaignReveal();
        _backdrop.RenderOnce();
        _backdrop.EnsureClockRunning();
        PublishProjectionFrame(0.0);
    }

    private static void ExitCampaignPanel(string underlyingWindow)
    {
        if (_backdrop is null)
            return;

        ClearBackdropMode = false;
        _orbitInertiaYaw = 0;
        _orbitInertiaPitch = 0;
        EndCampaignReveal();

        if (!UiAnimationsEnabled())
        {
            OuterSystemOpacity = 1.0;
            _exitRestoreActive = false;
            _pendingExitWindow = null;
            _backdrop.Gl.Scene.EndEarthFocus(SolarSystemScene.PanelKindForWindow(underlyingWindow));
            _backdrop.RenderOnce();
            PublishProjectionFrame(0.0);
            return;
        }

        // Hold the close framing, fade the outer system in, then pull the camera.
        _pendingExitWindow = underlyingWindow;
        _exitRestoreElapsed = 0;
        _exitRestoreActive = true;
        OuterSystemOpacity = 0.0;
        _backdrop.RenderOnce();
        _backdrop.EnsureClockRunning();
        PublishProjectionFrame(0.0);
    }

    private static bool UiAnimationsEnabled()
    {
        try
        {
            return ClientCore.UserINISettings.Instance.UiAnimationsEnabled.Value;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (NullReferenceException)
        {
            return true;
        }
    }

    /// <summary>
    /// Campaign drag: move the camera (inverse of pointer). Earth spin untouched.
    /// </summary>
    public static void NudgeCameraOrbit(double deltaYawDeg, double deltaPitchDeg)
    {
        _backdrop?.Gl.Scene.NudgeSurfaceOrbit(deltaYawDeg, deltaPitchDeg);
        _backdrop?.EnsureClockRunning();
    }

    /// <summary>Pointer-release inertia for camera orbit.</summary>
    public static void SetCameraOrbitInertia(double yawPerFrame, double pitchPerFrame)
    {
        _orbitInertiaYaw = yawPerFrame;
        _orbitInertiaPitch = pitchPerFrame;
    }

    /// <summary>Select a mission: camera focuses then co-rotates with Earth.</summary>
    public static bool FocusMission(double latitudeDegrees, double longitudeDegrees)
    {
        _orbitInertiaYaw = 0;
        _orbitInertiaPitch = 0;
        _backdrop?.EnsureClockRunning();
        return _backdrop?.Gl.Scene.FocusMission(latitudeDegrees, longitudeDegrees) ?? false;
    }

    public static void ClearMissionLock() => _backdrop?.Gl.Scene.ClearMissionLock();

    public static void OnBackdropTick(double dt)
    {
        if (_backdrop is null)
            return;

        if (_exitRestoreActive)
            StepExitOuterRestore(dt);

        if (ClearBackdropMode
            && (Math.Abs(_orbitInertiaYaw) > 0.02 || Math.Abs(_orbitInertiaPitch) > 0.02))
        {
            _backdrop.Gl.Scene.NudgeSurfaceOrbit(_orbitInertiaYaw * dt * 60.0, _orbitInertiaPitch * dt * 60.0);
            _orbitInertiaYaw *= Math.Pow(0.9, dt * 60.0);
            _orbitInertiaPitch *= Math.Pow(0.9, dt * 60.0);
        }

        PublishProjectionFrame(dt);
    }

    private static void StepExitOuterRestore(double dt)
    {
        if (_backdrop is null || !_exitRestoreActive)
            return;

        _exitRestoreElapsed += Math.Max(dt, 0);
        OuterSystemOpacity = SmoothStep(_exitRestoreElapsed / OuterRestoreDurationSeconds);

        if (_exitRestoreElapsed < OuterRestoreDurationSeconds + OuterRestoreHoldSeconds)
            return;

        OuterSystemOpacity = 1.0;
        _exitRestoreActive = false;
        string window = _pendingExitWindow ?? "MainMenu";
        _pendingExitWindow = null;
        _backdrop.Gl.Scene.EndEarthFocus(SolarSystemScene.PanelKindForWindow(window));
    }

    private static void BeginCampaignReveal()
    {
        _campaignRevealActive = true;
        _campaignRevealElapsed = 0;
        PanelRevealOpacity = 0;
        AnchorRevealOpacity = 0;

        if (!UiAnimationsEnabled())
        {
            PanelRevealOpacity = 1;
            AnchorRevealOpacity = 1;
            _campaignRevealActive = false;
        }
    }

    private static void EndCampaignReveal()
    {
        _campaignRevealActive = false;
        PanelRevealOpacity = 1;
        AnchorRevealOpacity = 1;
    }

    private static void PublishProjectionFrame(double dt)
    {
        if (_backdrop is null)
            return;

        if (_campaignRevealActive)
        {
            _campaignRevealElapsed += Math.Max(dt, 0);
            PanelRevealOpacity = SmoothStep(
                (_campaignRevealElapsed - PanelRevealStartSeconds) / PanelRevealDurationSeconds);
            AnchorRevealOpacity = SmoothStep(
                (_campaignRevealElapsed - AnchorRevealStartSeconds) / AnchorRevealDurationSeconds);
            if (PanelRevealOpacity >= 1.0 && AnchorRevealOpacity >= 1.0)
                _campaignRevealActive = false;
        }

        var scene = _backdrop.Gl.Scene;
        double w = _backdrop.Bounds.Width;
        double h = _backdrop.Bounds.Height;
        if (w < 2 || h < 2)
        {
            FrameAdvanced?.Invoke();
            return;
        }

        float aspect = (float)(w / h);
        _vp = scene.BuildViewProjection(aspect, out _camX, out _camY, out _camZ);
        (_earthX, _earthY, _earthZ) = scene.EarthPosition;
        _earthRadius = SolarSystemScene.BodyRadius(scene.Bodies[scene.EarthIndex]);
        _spinPhase = scene.EarthSpinPhase;
        _axialTiltDeg = scene.Bodies[scene.EarthIndex].AxialTiltDeg;
        _viewportW = w;
        _viewportH = h;
        _projectionValid = true;

        FrameAdvanced?.Invoke();
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Legacy bridge query — yaw mirrors Kepler spin for any residual UI.</summary>
    public static bool TryGetEarthPose(out double yaw, out double pitch)
    {
        yaw = 20.0;
        pitch = -16.0;
        if (_backdrop is null)
            return false;

        yaw = _backdrop.Gl.Scene.EarthYawDegrees;
        pitch = -16.0;
        return true;
    }

    public static bool TryProjectEarthLatLon(
        double latDeg,
        double lonDeg,
        Visual target,
        out Point localPoint,
        out bool frontFacing)
    {
        localPoint = default;
        frontFacing = false;

        if (!_projectionValid || _vp is null || _backdrop is null)
            return false;

        if (!SolarSystemScene.ProjectEarthLatLon(
                _vp,
                _earthX, _earthY, _earthZ,
                _earthRadius,
                _axialTiltDeg,
                _spinPhase,
                _camX, _camY, _camZ,
                latDeg, lonDeg,
                _viewportW, _viewportH,
                out double screenX, out double screenY, out frontFacing))
            return false;

        Point? translated = _backdrop.TranslatePoint(new Point(screenX, screenY), target);
        if (translated is null)
            return false;

        localPoint = translated.Value;
        return true;
    }
}
