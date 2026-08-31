using System;

namespace ClientAvalonia.Controls;

/// <summary>
/// Pure solar-system math for the shared 3D backdrop. Owns the Kepler orbit
/// solver, body definitions, visual compression and the camera state machine
/// (panel poses + campaign earth-focus). No Avalonia dependency on purpose so
/// it stays unit-testable — see SolarSystemSceneTests.
/// Scene axes: XZ is the ecliptic (Y up), the Sun sits at the origin.
/// </summary>
internal sealed class SolarSystemScene
{
    /// <summary>Visual seconds per Earth year (drives every period via Kepler III).</summary>
    public const double EarthPeriodSeconds = 75.0;

    /// <summary>Compression exponent mapping real AU → scene units; keeps order, softens spacing.</summary>
    public const double AuCompressionExponent = 0.46;

    private const double Deg = Math.PI / 180.0;

    /// <summary>Bodies in orbital order. Angles in degrees (heliocentric ecliptic J2000-ish).</summary>
    internal readonly struct KeplerBody
    {
        public KeplerBody(
            string name,
            double a,
            double e,
            double inclinationDeg,
            double ascendingNodeDeg,
            double argPerihelionDeg,
            double meanAnomalyDeg,
            double relativeRadius,
            double axialTiltDeg,
            double rotationHours,
            float r,
            float g,
            float b,
            PlanetKind kind)
        {
            Name = name;
            A = a;
            E = e;
            InclinationDeg = inclinationDeg;
            AscendingNodeDeg = ascendingNodeDeg;
            ArgPerihelionDeg = argPerihelionDeg;
            MeanAnomalyDeg = meanAnomalyDeg;
            RelativeRadius = relativeRadius;
            AxialTiltDeg = axialTiltDeg;
            RotationHours = rotationHours;
            R = r;
            G = g;
            B = b;
            Kind = kind;
        }

        public string Name { get; }
        public double A { get; }
        public double E { get; }
        public double InclinationDeg { get; }
        public double AscendingNodeDeg { get; }
        public double ArgPerihelionDeg { get; }
        public double MeanAnomalyDeg { get; }
        public double RelativeRadius { get; }
        public double AxialTiltDeg { get; }
        public double RotationHours { get; }
        public float R { get; }
        public float G { get; }
        public float B { get; }
        public PlanetKind Kind { get; }

        /// <summary>Visual period from Kepler III with Earth anchored at EarthPeriodSeconds.</summary>
        public double PeriodSeconds => EarthPeriodSeconds * Math.Pow(A, 1.5);
    }

    internal enum PlanetKind
    {
        Plain,
        Banded,   // Jupiter / Saturn-style latitude bands
        Earth,
        Ringed,   // Saturn (draws the ring pass)
    }

    internal static KeplerBody[] CreateBodies() => new[]
    {
        new KeplerBody("Mercury", 0.387, 0.2056, 7.00, 48.33, 29.12, 174.8, 0.383, 0.03, 1407.6, 0.62f, 0.58f, 0.55f, PlanetKind.Plain),
        new KeplerBody("Venus",   0.723, 0.0068, 3.39, 76.68, 54.88, 50.4,  0.949, 177.4, -5832.5, 0.91f, 0.82f, 0.60f, PlanetKind.Plain),
        new KeplerBody("Earth",   1.000, 0.0167, 0.00, -11.26, 114.21, 358.6, 1.000, 23.44, 23.93, 0.23f, 0.47f, 0.78f, PlanetKind.Earth),
        new KeplerBody("Mars",    1.524, 0.0934, 1.85, 49.56, 286.50, 19.4, 0.532, 25.19, 24.62, 0.76f, 0.35f, 0.22f, PlanetKind.Plain),
        new KeplerBody("Jupiter", 5.203, 0.0489, 1.30, 100.46, 273.87, 20.0, 11.21, 3.13, 9.93, 0.82f, 0.63f, 0.42f, PlanetKind.Banded),
        new KeplerBody("Saturn",  9.537, 0.0565, 2.49, 113.66, 339.39, 317.0, 9.45, 26.73, 10.66, 0.88f, 0.78f, 0.53f, PlanetKind.Ringed),
        new KeplerBody("Uranus", 19.19, 0.0472, 0.77, 74.01, 96.99, 142.2, 4.01, 97.77, -17.24, 0.48f, 0.81f, 0.85f, PlanetKind.Plain),
        new KeplerBody("Neptune",30.07, 0.0086, 1.77, 131.78, 276.34, 256.2, 3.88, 28.32, 16.11, 0.17f, 0.29f, 0.75f, PlanetKind.Plain),
    };

    public KeplerBody[] Bodies { get; } = CreateBodies();

    /// <summary>Earth's index inside <see cref="Bodies"/>.</summary>
    public int EarthIndex { get; } = 2;

    /// <summary>Simulation time in seconds (paused when UI animations are off).</summary>
    public double Time { get; private set; }

    /// <summary>Current camera; advanced by <see cref="Advance"/> toward the active pose.</summary>
    public CameraState Camera { get; private set; }

    // Pose interpolation state.
    private CameraState _poseFrom;
    private CameraState _poseTo;
    private double _poseElapsed;
    private double _poseDuration;
    private bool _poseAnimating;

    // Campaign surface-orbit camera: Earth keeps Kepler spin; the camera orbits.
    private bool _surfaceOrbitActive;
    private double _orbitYawDeg;
    private double _orbitPitchDeg;
    private bool _missionLockActive;
    private double _lockLatDeg;
    private double _lockLonDeg;
    private bool _missionFocusAnimating;
    private double _missionFocusElapsed;
    private double _focusStartDx, _focusStartDy, _focusStartDz;
    private double _focusEndDx, _focusEndDy, _focusEndDz;
    private const double MissionFocusDurationSeconds = 0.85;

    // Campaign exit: concentric guide-sphere path (radial → spherical arc →
    // radial). The arc's instantaneous velocity is tangent to the guide sphere,
    // so the eye never chords through Earth. Stored Earth-relative.
    // The START stays frozen; the END is re-aimed at the live menu eye every
    // tick (Kepler drift ~10° per exit) so t=1 lands on the current steady
    // pose instead of a stale snapshot — orientation arrives with the path.
    private bool _exitPathActive;
    private double _exitU0x, _exitU0y, _exitU0z;
    private double _exitU3x, _exitU3y, _exitU3z;
    private double _exitR0, _exitR3, _exitGuideR;
    private double _exitLenRadialIn, _exitLenArc, _exitLenRadialOut;
    private double _exitLook0x, _exitLook0y, _exitLook0z;
    private double _exitLook1x, _exitLook1y, _exitLook1z;
    private double _exitStartRx, _exitStartRy, _exitStartRz;

    // Short settle after the guide path: blends the (possibly drifted) path end
    // into the live sun-relative menu eye so t=1 does not hard-cut.
    private bool _exitSettleActive;
    private double _exitSettleElapsed;
    private double _settleFromRx, _settleFromRy, _settleFromRz;
    private double _settleFromLx, _settleFromLy, _settleFromLz;

    // Cached orbit positions (scene units), recomputed in Advance.
    private readonly double[] _orbitX;
    private readonly double[] _orbitY;
    private readonly double[] _orbitZ;

    public SolarSystemScene()
    {
        _orbitX = new double[Bodies.Length];
        _orbitY = new double[Bodies.Length];
        _orbitZ = new double[Bodies.Length];

        Camera = ResolvePose(PanelPose(PanelKind.MainMenu), BodyRadius(Bodies[EarthIndex]));
        _poseFrom = Camera;
        _poseTo = Camera;
        Advance(0.0);
    }

    /// <summary>Pushes time and evaluates every body's position plus the camera blend.</summary>
    public void Advance(double dt)
    {
        Time += dt;

        for (int i = 0; i < Bodies.Length; i++)
            EvaluatePosition(Bodies[i], Time, out _orbitX[i], out _orbitY[i], out _orbitZ[i]);

        if (_exitPathActive)
            RetargetExitPathToLivePanelEye(_poseTo);

        if (_poseAnimating)
        {
            _poseElapsed += dt;
            double t = _poseDuration <= 0 ? 1.0 : Math.Clamp(_poseElapsed / _poseDuration, 0.0, 1.0);
            double eased = _exitPathActive ? EaseInOutQuint(t) : EaseOutCubic(t);
            Camera = BlendCamera(_poseFrom, _poseTo, eased);
            if (t >= 1.0)
            {
                _poseAnimating = false;
                Camera = _poseTo;
                if (_exitPathActive)
                    BeginExitSettleFromGuideEnd();
                else
                    _exitPathActive = false;
            }
        }
        else
        {
            Camera = _poseTo;
            if (!_exitSettleActive)
                _exitPathActive = false;
        }

        if (_exitSettleActive)
            StepExitSettle(dt);

        StepMissionFocus(dt);
        if (_missionLockActive && !_missionFocusAnimating)
            SyncOrbitAnglesFromLock();
    }

    public (double X, double Y, double Z) GetPosition(int bodyIndex)
        => (_orbitX[bodyIndex], _orbitY[bodyIndex], _orbitZ[bodyIndex]);

    /// <summary>Earth's heliocentric position in scene units.</summary>
    public (double X, double Y, double Z) EarthPosition => GetPosition(EarthIndex);

    /// <summary>
    /// Earth's spin phase (radians). Matched to the campaign globe auto-rotate
    /// (~2.4°/s) so bridge mode keeps anchors readable; previously used year/40
    /// (~1.9s/rev) which made the campaign overlay spin wildly.
    /// </summary>
    public const double EarthSpinDegreesPerSecond = 2.4;

    public double EarthSpinPhase => Time * EarthSpinDegreesPerSecond * Math.PI / 180.0;

    /// <summary>Sets the camera target for a UI panel and starts the blend.</summary>
    public void NavigateTo(PanelKind panel)
    {
        CancelExitCameraOverrides();
        _surfaceOrbitActive = false;
        ClearMissionLock();
        CameraState target = ResolvePose(PanelPose(panel), BodyRadius(Bodies[EarthIndex]));
        BeginPoseTransition(target, durationSeconds: 1.1);
    }

    /// <summary>
    /// Campaign zoom-in: inherits the current view bearing so entry does not
    /// spin the framing; only distance moves to the close orbit. Earth spin
    /// is untouched — interaction will orbit the camera instead.
    /// </summary>
    public void BeginEarthFocus()
    {
        CancelExitCameraOverrides();
        SeedSurfaceOrbitFromCamera(Camera);
        ClearMissionLock();
        _surfaceOrbitActive = true;

        CameraState campaign = ResolvePose(PanelPose(PanelKind.Campaign), BodyRadius(Bodies[EarthIndex]));
        var target = new CameraState(
            CameraFocus.Earth,
            0,
            0,
            campaign.Distance,
            Camera.EarthBearingDeg,
            Camera.EarthElevationDeg);
        BeginPoseTransition(target, durationSeconds: EarthFocusDurationSeconds);
    }

    /// <summary>
    /// Returns to the pose of the underlying window panel after the campaign closes.
    /// Path: Earth-relative radial → guide-sphere arc → radial. The end (eye and
    /// look target) is re-aimed at the live steady panel pose every tick, so
    /// Kepler drift during the ride is absorbed by the arc itself and the
    /// orientation arrives WITH the path; the short settle afterwards only
    /// smooths the last fraction of residual drift.
    /// </summary>
    public void EndEarthFocus(PanelKind underlying)
    {
        ClearMissionLock();
        CancelExitCameraOverrides();

        BuildViewProjection(1f, out double startX, out double startY, out double startZ);
        (double earthX, double earthY, double earthZ) = EarthPosition;
        double earthR = BodyRadius(Bodies[EarthIndex]);

        CameraState target = ResolvePose(PanelPose(underlying), earthR);

        double srx = startX - earthX, sry = startY - earthY, srz = startZ - earthZ;

        _exitStartRx = srx;
        _exitStartRy = sry;
        _exitStartRz = srz;
        _exitLook0x = 0; _exitLook0y = 0; _exitLook0z = 0;
        RetargetExitPathToLivePanelEye(target);

        EncodeEarthRelativeEyeAsCamera(srx, sry, srz, out CameraState fromPose);
        _surfaceOrbitActive = false;
        Camera = fromPose;

        double angle = AngleBetweenUnit(
            _exitU0x, _exitU0y, _exitU0z,
            _exitU3x, _exitU3y, _exitU3z);
        double duration = EarthExitDurationSeconds * (0.88 + 0.45 * (angle / Math.PI));
        duration = Math.Clamp(duration, EarthExitDurationSeconds * 0.95, EarthExitDurationSeconds * 1.45);
        BeginPoseTransition(target, durationSeconds: duration);
    }

    /// <summary>True while campaign-exit guide path or settle blend owns the rendered eye.</summary>
    public bool ExitPathActive => _exitPathActive || _exitSettleActive;

    /// <summary>True during the short post-path settle into the live menu eye.</summary>
    public bool ExitSettleActive => _exitSettleActive;

    private void CancelExitCameraOverrides()
    {
        _exitPathActive = false;
        _exitSettleActive = false;
        _exitSettleElapsed = 0;
    }

    /// <summary>
    /// Re-plans the exit path END against the live steady panel eye (start frozen).
    /// Called at plan time and every tick while the path is active so Kepler
    /// drift during the ~2s ride is absorbed by the arc itself — t=1 lands on
    /// the current steady pose, orientation included, instead of a stale snapshot.
    /// </summary>
    private void RetargetExitPathToLivePanelEye(CameraState pose)
    {
        double earthR = BodyRadius(Bodies[EarthIndex]);
        ComputeEarthCameraEyeAndLookAt(
            pose,
            applyMenuPan: true,
            out double endX, out double endY, out double endZ,
            out double lookEndX, out double lookEndY, out double lookEndZ);
        (double earthX, double earthY, double earthZ) = EarthPosition;

        double erx = endX - earthX, ery = endY - earthY, erz = endZ - earthZ;
        PlanGuideSphereExit(
            earthR * ExitGuideSphereEarthRadii,
            earthR * ExitPathMinClearanceEarthRadii,
            _exitStartRx, _exitStartRy, _exitStartRz,
            erx, ery, erz,
            out _exitU0x, out _exitU0y, out _exitU0z,
            out _exitU3x, out _exitU3y, out _exitU3z,
            out _exitR0, out _exitR3, out _exitGuideR,
            out _exitLenRadialIn, out _exitLenArc, out _exitLenRadialOut);

        _exitLook1x = lookEndX - earthX;
        _exitLook1y = lookEndY - earthY;
        _exitLook1z = lookEndZ - earthZ;
        _exitPathActive = true;
    }

    private void BeginExitSettleFromGuideEnd()
    {
        EvaluateGuideSphereExit(
            1.0,
            _exitU0x, _exitU0y, _exitU0z,
            _exitU3x, _exitU3y, _exitU3z,
            _exitR0, _exitR3, _exitGuideR,
            _exitLenRadialIn, _exitLenArc, _exitLenRadialOut,
            out double rx, out double ry, out double rz);
        double clearance = BodyRadius(Bodies[EarthIndex]) * ExitPathMinClearanceEarthRadii;
        InflateEarthRelative(ref rx, ref ry, ref rz, clearance);

        _settleFromRx = rx;
        _settleFromRy = ry;
        _settleFromRz = rz;
        _settleFromLx = _exitLook1x;
        _settleFromLy = _exitLook1y;
        _settleFromLz = _exitLook1z;

        _exitPathActive = false;
        _exitSettleElapsed = 0;
        _exitSettleActive = true;
    }

    private void StepExitSettle(double dt)
    {
        if (!_exitSettleActive)
            return;

        _exitSettleElapsed += Math.Max(dt, 0);
        if (_exitSettleElapsed < EarthExitSettleDurationSeconds)
            return;

        _exitSettleActive = false;
        _exitSettleElapsed = 0;
        Camera = _poseTo;
    }

    /// <summary>True while the campaign panel owns a surface-orbit / mission-lock camera.</summary>
    public bool SurfaceOrbitActive => _surfaceOrbitActive;

    /// <summary>True while the camera is co-rotating to keep a mission fixed in frame.</summary>
    public bool MissionLockActive => _missionLockActive;

    /// <summary>Drag: orbit the camera (inverse feel). Clears mission lock.</summary>
    public void NudgeSurfaceOrbit(double deltaYawDeg, double deltaPitchDeg)
    {
        if (!_surfaceOrbitActive)
            return;

        ClearMissionLock();
        _orbitYawDeg = NormalizeDegrees(_orbitYawDeg + deltaYawDeg);
        _orbitPitchDeg = Math.Clamp(_orbitPitchDeg + deltaPitchDeg, -78.0, 78.0);
    }

    /// <summary>
    /// Jump: animate the camera to face (lat, lon), then lock co-rotation so
    /// the mission stays fixed while Earth keeps spinning underneath.
    /// </summary>
    public void FocusMission(double latitudeDegrees, double longitudeDegrees)
    {
        if (!_surfaceOrbitActive)
            return;

        GetSurfaceOrbitDirection(out _focusStartDx, out _focusStartDy, out _focusStartDz);
        EarthSurfaceOutwardNormal(
            latitudeDegrees,
            longitudeDegrees,
            Bodies[EarthIndex].AxialTiltDeg,
            EarthSpinPhase,
            out _focusEndDx,
            out _focusEndDy,
            out _focusEndDz);

        _lockLatDeg = latitudeDegrees;
        _lockLonDeg = longitudeDegrees;
        _missionFocusElapsed = 0;
        _missionFocusAnimating = true;
        _missionLockActive = false;
    }

    public void ClearMissionLock()
    {
        _missionLockActive = false;
        _missionFocusAnimating = false;
    }

    private void StepMissionFocus(double dt)
    {
        if (!_missionFocusAnimating)
            return;

        _missionFocusElapsed += dt;
        double t = Math.Clamp(_missionFocusElapsed / MissionFocusDurationSeconds, 0.0, 1.0);
        double eased = EaseOutCubic(t);
        SlerpDirection(
            _focusStartDx, _focusStartDy, _focusStartDz,
            _focusEndDx, _focusEndDy, _focusEndDz,
            eased,
            out double dx, out double dy, out double dz);
        DirectionToOrbitAngles(dx, dy, dz, out _orbitYawDeg, out _orbitPitchDeg);

        if (t >= 1.0)
        {
            _missionFocusAnimating = false;
            _missionLockActive = true;
            SyncOrbitAnglesFromLock();
        }
    }

    private void SyncOrbitAnglesFromLock()
    {
        EarthSurfaceOutwardNormal(
            _lockLatDeg,
            _lockLonDeg,
            Bodies[EarthIndex].AxialTiltDeg,
            EarthSpinPhase,
            out double dx, out double dy, out double dz);
        DirectionToOrbitAngles(dx, dy, dz, out _orbitYawDeg, out _orbitPitchDeg);
    }

    private void SeedSurfaceOrbitFromCamera(CameraState cam)
    {
        ComputeSunRelativeEarthCameraEye(cam, out double cx, out double cy, out double cz);
        (double ex, double ey, double ez) = EarthPosition;
        double dx = cx - ex;
        double dy = cy - ey;
        double dz = cz - ez;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-9)
        {
            _orbitYawDeg = cam.EarthBearingDeg;
            _orbitPitchDeg = cam.EarthElevationDeg;
            return;
        }

        DirectionToOrbitAngles(dx / len, dy / len, dz / len, out _orbitYawDeg, out _orbitPitchDeg);
    }

    private void GetSurfaceOrbitDirection(out double dx, out double dy, out double dz)
    {
        if (_missionLockActive)
        {
            EarthSurfaceOutwardNormal(
                _lockLatDeg, _lockLonDeg,
                Bodies[EarthIndex].AxialTiltDeg, EarthSpinPhase,
                out dx, out dy, out dz);
            return;
        }

        OrbitAnglesToDirection(_orbitYawDeg, _orbitPitchDeg, out dx, out dy, out dz);
    }

    /// <summary>Unit outward normal of a lat/lon on the spinning Earth marble.</summary>
    public static void EarthSurfaceOutwardNormal(
        double latDeg,
        double lonDeg,
        double axialTiltDeg,
        double spinPhase,
        out double nx,
        out double ny,
        out double nz)
    {
        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;
        double cl = Math.Cos(lat);
        double lx = cl * Math.Sin(lon);
        double ly = Math.Sin(lat);
        double lz = cl * Math.Cos(lon);

        double tilt = axialTiltDeg * Math.PI / 180.0;
        double ct = Math.Cos(tilt);
        double st = Math.Sin(tilt);
        double cs = Math.Cos(spinPhase);
        double ss = Math.Sin(spinPhase);

        double m00 = cs, m01 = 0, m02 = ss;
        double m10 = st * ss, m11 = ct, m12 = -st * cs;
        double m20 = -ct * ss, m21 = st, m22 = ct * cs;

        nx = m00 * lx + m01 * ly + m02 * lz;
        ny = m10 * lx + m11 * ly + m12 * lz;
        nz = m20 * lx + m21 * ly + m22 * lz;
    }

    public static void OrbitAnglesToDirection(double yawDeg, double pitchDeg, out double dx, out double dy, out double dz)
    {
        double yaw = yawDeg * Math.PI / 180.0;
        double pitch = pitchDeg * Math.PI / 180.0;
        double cp = Math.Cos(pitch);
        dx = cp * Math.Sin(yaw);
        dy = Math.Sin(pitch);
        dz = cp * Math.Cos(yaw);
    }

    public static void DirectionToOrbitAngles(double dx, double dy, double dz, out double yawDeg, out double pitchDeg)
    {
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-9)
        {
            yawDeg = 0;
            pitchDeg = 0;
            return;
        }

        dx /= len;
        dy /= len;
        dz /= len;
        pitchDeg = Math.Asin(Math.Clamp(dy, -1.0, 1.0)) * 180.0 / Math.PI;
        yawDeg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
    }

    public static void SlerpDirection(
        double ax, double ay, double az,
        double bx, double by, double bz,
        double t,
        out double dx, out double dy, out double dz)
    {
        double dot = Math.Clamp(ax * bx + ay * by + az * bz, -1.0, 1.0);
        if (dot > 0.9995)
        {
            dx = ax + (bx - ax) * t;
            dy = ay + (by - ay) * t;
            dz = az + (bz - az) * t;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len > 1e-9)
            {
                dx /= len;
                dy /= len;
                dz /= len;
            }

            return;
        }

        double omega = Math.Acos(dot);
        double so = Math.Sin(omega);
        double w0 = Math.Sin((1.0 - t) * omega) / so;
        double w1 = Math.Sin(t * omega) / so;
        dx = ax * w0 + bx * w1;
        dy = ay * w0 + by * w1;
        dz = az * w0 + bz * w1;
    }

    public static double NormalizeDegrees(double deg)
    {
        deg %= 360.0;
        if (deg < 0)
            deg += 360.0;
        return deg;
    }

    /// <summary>Campaign world tilt (degrees) — cinematic roll while earth-focused.</summary>
    public double WorldTiltDeg => Camera.Focus == CameraFocus.Earth ? 6.0 : 0.0;

    public bool IsPoseAnimating => _poseAnimating || _exitSettleActive;

    /// <summary>0→1 progress of the active pose blend (1 when idle).</summary>
    public double PoseBlendProgress
    {
        get
        {
            if (!_poseAnimating)
                return 1.0;
            if (_poseDuration <= 0)
                return 1.0;
            return Math.Clamp(_poseElapsed / _poseDuration, 0.0, 1.0);
        }
    }

    /// <summary>Duration used by <see cref="BeginEarthFocus"/>.</summary>
    public const double EarthFocusDurationSeconds = 1.4;

    /// <summary>Base duration for <see cref="EndEarthFocus"/> (scaled by turn angle).</summary>
    public const double EarthExitDurationSeconds = 1.85;

    /// <summary>Short blend from guide-path end into the live sun-relative menu eye.</summary>
    public const double EarthExitSettleDurationSeconds = 0.28;

    /// <summary>Hard floor for eye–Earth distance during exit, in Earth radii.</summary>
    public const double ExitPathMinClearanceEarthRadii = 2.05;

    /// <summary>
    /// Concentric guide sphere used for the exit arc (between campaign close and
    /// menu mid-distance). Camera rides this shell so motion is tangent to it.
    /// </summary>
    public const double ExitGuideSphereEarthRadii = 3.15;

    private void BeginPoseTransition(CameraState target, double durationSeconds)
    {
        _poseFrom = Camera;
        _poseTo = target;
        _poseElapsed = 0;
        _poseDuration = durationSeconds;
        _poseAnimating = true;
    }

    // ------------------------------------------------------------------
    // Kepler solver
    // ------------------------------------------------------------------

    /// <summary>Newton iteration for the eccentric anomaly; converges to 1e-8 for e&lt;0.21.</summary>
    public static double SolveKepler(double meanAnomalyRad, double e)
    {
        meanAnomalyRad = NormalizeAngle(meanAnomalyRad);
        double eccentric = meanAnomalyRad + e * Math.Sin(meanAnomalyRad);
        for (int i = 0; i < 8; i++)
        {
            double delta = eccentric - e * Math.Sin(eccentric) - meanAnomalyRad;
            double derivative = 1.0 - e * Math.Cos(eccentric);
            if (Math.Abs(derivative) < 1e-12)
                break;

            double step = delta / derivative;
            eccentric -= step;
            if (Math.Abs(step) < 1e-8)
                break;
        }

        return eccentric;
    }

    /// <summary>Evaluates a body's heliocentric ecliptic position at simulation time t.</summary>
    public static void EvaluatePosition(in KeplerBody body, double t, out double x, out double y, out double z)
    {
        double meanMotion = 2.0 * Math.PI / body.PeriodSeconds;
        double meanAnomaly = body.MeanAnomalyDeg * Deg + meanMotion * t;
        double eccentric = SolveKepler(meanAnomaly, body.E);

        // Orbital plane coordinates (focus at the Sun).
        double rOrbit = body.A * (1.0 - body.E * Math.Cos(eccentric));
        double trueAnomaly = Math.Atan2(
            Math.Sqrt(1.0 - body.E * body.E) * Math.Sin(eccentric),
            Math.Cos(eccentric) - body.E);
        double argument = trueAnomaly + body.ArgPerihelionDeg * Deg;

        double px = rOrbit * Math.Cos(argument);
        double py = rOrbit * Math.Sin(argument);

        // Rotate: argument of perihelion already folded into `argument`;
        // inclination around the node axis, then the node in the ecliptic.
        double inc = body.InclinationDeg * Deg;
        double node = body.AscendingNodeDeg * Deg;
        double cosNode = Math.Cos(node);
        double sinNode = Math.Sin(node);
        double cosInc = Math.Cos(inc);
        double sinInc = Math.Sin(inc);

        double ex = px * cosNode - py * cosInc * sinNode;
        double ey = px * sinNode + py * cosInc * cosNode;
        double ez = py * sinInc;

        // Ecliptic (XZ plane, Y up) + visual compression of real AU.
        double compressed = CompressAu(rOrbit);
        double s = rOrbit > 1e-9 ? compressed / rOrbit : 0.0;
        x = ex * s;
        y = ez * s;
        z = -ey * s;
    }

    /// <summary>au^0.62 compression into scene units.</summary>
    public static double CompressAu(double au) => Math.Pow(Math.Max(au, 0.0), AuCompressionExponent);

    /// <summary>Camera distance from Earth's center during campaign focus, in Earth radii.</summary>
    public const double EarthFocusDistanceEarthRadii = 2.25;

    /// <summary>Main-menu mid-distance — Earth is the visual focal point; sun still peeks in.</summary>
    public const double MenuEarthDistanceEarthRadii = 4.35;

    /// <summary>Lobby mid-distance — slightly wider than the menu.</summary>
    public const double LobbyEarthDistanceEarthRadii = 5.35;

    /// <summary>Game-lobby mid-distance.</summary>
    public const double GameLobbyEarthDistanceEarthRadii = 6.15;

    /// <summary>
    /// Distance-coupled sun glow strength (0..1). The corona is invisible at
    /// campaign close range (a depth-off sun draw would smear a giant glow
    /// over the Earth at 2.25 radii) and reaches full strength exactly at menu
    /// mid-distance — so the glow blooms in naturally with the exit camera
    /// pull and the settle lands on an identical menu frame.
    /// </summary>
    public static double SunGlowGain(double cameraDistanceEarthRadii)
    {
        double near = EarthFocusDistanceEarthRadii;   // 2.25
        double far = MenuEarthDistanceEarthRadii;     // 4.35
        double t = Math.Clamp((cameraDistanceEarthRadii - near) / (far - near), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Scene-unit camera distance for the campaign focus.</summary>
    public double EarthFocusDistanceUnits => EarthFocusDistanceEarthRadii * BodyRadius(Bodies[EarthIndex]);

    /// <summary>
    /// Body radius in scene units. Earth is hero-scaled for the mid-distance
    /// menu; outer planets stay smaller so they do not steal the focal point.
    /// </summary>
    public static double BodyRadius(in KeplerBody body)
    {
        if (body.Kind == PlanetKind.Earth)
            return 0.205;

        double r = Math.Pow(body.RelativeRadius, 0.38) * 0.11 + 0.038;
        if (body.A > 2.5)
            r *= 0.72;
        return Math.Min(r, SunRadius * 0.30);
    }

    /// <summary>Sun visual radius — secondary light anchor (kept smaller than Earth hero).</summary>
    public const double SunRadius = 0.40;

    /// <summary>
    /// GIS LOD for the shared Earth marble: always fill with GIS albedo, but
    /// refine sampling from ~1:2 (half linear) at menu mid-distance to full
    /// resolution at campaign close — no texture rebake mid-zoom.
    /// </summary>
    public static void ComputeGisLod(double cameraDistanceToEarth, double earthRadius, out float gisAmount, out float gisRefine)
    {
        double near = EarthFocusDistanceEarthRadii * earthRadius;
        double far = MenuEarthDistanceEarthRadii * earthRadius;
        double t = Math.Clamp((far - cameraDistanceToEarth) / Math.Max(far - near, 1e-6), 0.0, 1.0);
        t = t * t * (3.0 - 2.0 * t);
        gisAmount = 1f;
        gisRefine = (float)t;
    }

    /// <summary>Position on the orbit ellipse parameterized by eccentric anomaly (for line geometry).</summary>
    public static void OrbitPoint(in KeplerBody body, double eccentricAnomaly, out double x, out double y, out double z)
    {
        double rOrbit = body.A * (1.0 - body.E * Math.Cos(eccentricAnomaly));
        double trueAnomaly = Math.Atan2(
            Math.Sqrt(1.0 - body.E * body.E) * Math.Sin(eccentricAnomaly),
            Math.Cos(eccentricAnomaly) - body.E);
        double argument = trueAnomaly + body.ArgPerihelionDeg * Deg;

        double px = rOrbit * Math.Cos(argument);
        double py = rOrbit * Math.Sin(argument);

        double inc = body.InclinationDeg * Deg;
        double node = body.AscendingNodeDeg * Deg;
        double ex = px * Math.Cos(node) - py * Math.Cos(inc) * Math.Sin(node);
        double ey = px * Math.Sin(node) + py * Math.Cos(inc) * Math.Cos(node);
        double ez = py * Math.Sin(inc);

        double compressed = CompressAu(rOrbit);
        double s = rOrbit > 1e-9 ? compressed / rOrbit : 0.0;
        x = ex * s;
        y = ez * s;
        z = -ey * s;
    }

    // ------------------------------------------------------------------
    // Camera
    // ------------------------------------------------------------------

    internal enum CameraFocus
    {
        Sun,
        Earth,
    }

    internal readonly struct CameraState
    {
        public CameraState(CameraFocus focus, double yawDeg, double pitchDeg, double distance, double earthBearingDeg, double earthElevationDeg)
        {
            Focus = focus;
            YawDeg = yawDeg;
            PitchDeg = pitchDeg;
            Distance = distance;
            EarthBearingDeg = earthBearingDeg;
            EarthElevationDeg = earthElevationDeg;
        }

        public CameraFocus Focus { get; }
        public double YawDeg { get; }
        public double PitchDeg { get; }
        public double Distance { get; }

        /// <summary>Camera bearing around Earth (campaign focus), degrees.</summary>
        public double EarthBearingDeg { get; }

        /// <summary>Camera elevation above Earth's orbit plane (campaign focus), degrees.</summary>
        public double EarthElevationDeg { get; }
    }

    internal enum PanelKind
    {
        MainMenu,
        Lobby,
        GameLobby,
        Campaign,
    }

    /// <summary>Panel → camera pose table (the "same scene, different sample" contract).</summary>
    public static CameraState PanelPose(PanelKind panel) => panel switch
    {
        // Earth mid-distance: Earth is the visual focal point; non-zero bearing
        // keeps the sun in-frame as a secondary light anchor (day/side lit).
        // Negative Distance = Earth-radii sentinel resolved in ResolvePose.
        PanelKind.Lobby => new CameraState(CameraFocus.Earth, 0, 0, -3.0, 38.0, 14.0),
        PanelKind.GameLobby => new CameraState(CameraFocus.Earth, 0, 0, -4.0, 34.0, 16.0),
        PanelKind.Campaign => new CameraState(CameraFocus.Earth, 0, 0, -1.0, 48.0, 9.0),
        _ => new CameraState(CameraFocus.Earth, 0, 0, -2.0, 42.0, 12.0),
    };

    /// <summary>
    /// Resolves Earth-focus distance sentinels into scene units:
    /// -1 campaign, -2 main menu, -3 lobby, -4 game lobby.
    /// </summary>
    public static CameraState ResolvePose(CameraState pose, double earthRadius)
    {
        if (pose.Distance >= 0)
            return pose;

        double radii = pose.Distance switch
        {
            -1.0 => EarthFocusDistanceEarthRadii,
            -2.0 => MenuEarthDistanceEarthRadii,
            -3.0 => LobbyEarthDistanceEarthRadii,
            -4.0 => GameLobbyEarthDistanceEarthRadii,
            _ => MenuEarthDistanceEarthRadii,
        };

        return new CameraState(
            pose.Focus,
            pose.YawDeg,
            pose.PitchDeg,
            radii * earthRadius,
            pose.EarthBearingDeg,
            pose.EarthElevationDeg);
    }

    /// <summary>Maps a window/overlay name to its panel pose.</summary>
    public static PanelKind PanelKindForWindow(string windowName) => windowName switch
    {
        _ when Services.FloatingOverlayLayout.IsCampaignWindow(windowName) => PanelKind.Campaign,
        "CnCNetLobby" or "LANLobby" => PanelKind.Lobby,
        "CnCNetGameLobby" or "LANGameLobby" or "SkirmishLobby" or "SkirmishBattle" => PanelKind.GameLobby,
        _ => PanelKind.MainMenu,
    };

    /// <summary>True while the camera is in a close campaign earth orbit (not menu mid-distance).</summary>
    public bool IsEarthFocused
        => Camera.Focus == CameraFocus.Earth
           && Camera.Distance <= EarthFocusDistanceEarthRadii * BodyRadius(Bodies[EarthIndex]) * 1.35;

    /// <summary>Resolves Earth's spin yaw (degrees) for bridge consumers (campaign globe overlay).</summary>
    public double EarthYawDegrees => (EarthSpinPhase * 180.0 / Math.PI) % 360.0;

    /// <summary>
    /// Blends two camera states: shortest-arc yaw, log-space distance, and
    /// Earth-focus flags resolved by whichever endpoint owns them.
    /// </summary>
    public static CameraState BlendCamera(CameraState from, CameraState to, double t)
    {
        CameraFocus focus = t < 0.5 ? from.Focus : to.Focus;

        double yaw = from.YawDeg + ShortestDelta(from.YawDeg, to.YawDeg) * t;
        double pitch = from.PitchDeg + (to.PitchDeg - from.PitchDeg) * t;

        double dist = Math.Exp(
            Math.Log(Math.Max(from.Distance, 1e-4)) * (1 - t)
            + Math.Log(Math.Max(to.Distance, 1e-4)) * t);

        double bearing = from.EarthBearingDeg + ShortestDelta(from.EarthBearingDeg, to.EarthBearingDeg) * t;
        double elevation = from.EarthElevationDeg + (to.EarthElevationDeg - from.EarthElevationDeg) * t;

        return new CameraState(focus, yaw, pitch, dist, bearing, elevation);
    }

    public static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), 3.0);

    public static double EaseInOutCubic(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    /// <summary>Smoother accel/decel for campaign exit (flatter mid velocity than cubic).</summary>
    public static double EaseInOutQuint(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 16.0 * t * t * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 5.0) / 2.0;
    }

    public static double AngleBetweenUnit(
        double ax, double ay, double az,
        double bx, double by, double bz)
        => Math.Acos(Math.Clamp(ax * bx + ay * by + az * bz, -1.0, 1.0));

    /// <summary>
    /// Plans a concentric guide-sphere exit: radial onto the shell, spherical
    /// arc (tangent motion), radial to the end eye. Guarantees radius ≥ min(clearance, guide)
    /// on the arc and never below min(r0, r3, clearance) overall when endpoints are clear.
    /// </summary>
    public static void PlanGuideSphereExit(
        double guideRadius,
        double clearance,
        double p0x, double p0y, double p0z,
        double p3x, double p3y, double p3z,
        out double u0x, out double u0y, out double u0z,
        out double u3x, out double u3y, out double u3z,
        out double r0, out double r3, out double guideR,
        out double lenRadialIn, out double lenArc, out double lenRadialOut)
    {
        guideR = Math.Max(guideRadius, clearance);
        r0 = Math.Sqrt(p0x * p0x + p0y * p0y + p0z * p0z);
        r3 = Math.Sqrt(p3x * p3x + p3y * p3y + p3z * p3z);

        if (r0 < 1e-9)
        {
            u0x = 1; u0y = 0; u0z = 0;
            r0 = clearance;
        }
        else
        {
            u0x = p0x / r0; u0y = p0y / r0; u0z = p0z / r0;
            if (r0 < clearance)
                r0 = clearance;
        }

        if (r3 < 1e-9)
        {
            u3x = 1; u3y = 0; u3z = 0;
            r3 = Math.Max(guideR, clearance);
        }
        else
        {
            u3x = p3x / r3; u3y = p3y / r3; u3z = p3z / r3;
            if (r3 < clearance)
                r3 = clearance;
        }

        // Near-antipodal: Slerp mid is unstable — bias the end direction slightly
        // via a perpendicular so the arc has a well-defined tangent plane.
        double dot = u0x * u3x + u0y * u3y + u0z * u3z;
        if (dot < -0.999)
        {
            double rx = -u0z, ry = 0.0, rz = u0x;
            double rl = Math.Sqrt(rx * rx + rz * rz);
            if (rl < 1e-6)
            {
                rx = 1.0; rz = 0.0;
            }
            else
            {
                rx /= rl; rz /= rl;
            }

            // Keep end radius/target, but route the arc through a 90° waypoint
            // by replacing u3 with a direction 179° away via the lift (handled in eval
            // by slerp with a fixed mid). Here we nudge u3 off exact antipode.
            u3x = rx * 0.02 + u3x * 0.98;
            u3y = ry * 0.02 + u3y * 0.98;
            u3z = rz * 0.02 + u3z * 0.98;
            double n = Math.Sqrt(u3x * u3x + u3y * u3y + u3z * u3z);
            u3x /= n; u3y /= n; u3z /= n;
        }

        lenRadialIn = Math.Abs(guideR - r0);
        lenArc = guideR * AngleBetweenUnit(u0x, u0y, u0z, u3x, u3y, u3z);
        lenRadialOut = Math.Abs(r3 - guideR);
    }

    /// <summary>
    /// Evaluates the guide-sphere exit at arc-length fraction s∈[0,1] (already eased).
    /// </summary>
    public static void EvaluateGuideSphereExit(
        double s,
        double u0x, double u0y, double u0z,
        double u3x, double u3y, double u3z,
        double r0, double r3, double guideR,
        double lenRadialIn, double lenArc, double lenRadialOut,
        out double x, out double y, out double z)
    {
        s = Math.Clamp(s, 0.0, 1.0);
        double total = Math.Max(lenRadialIn + lenArc + lenRadialOut, 1e-6);
        double dist = s * total;

        if (dist <= lenRadialIn)
        {
            double t = lenRadialIn <= 1e-12 ? 1.0 : dist / lenRadialIn;
            double r = r0 + (guideR - r0) * t;
            x = u0x * r;
            y = u0y * r;
            z = u0z * r;
            return;
        }

        dist -= lenRadialIn;
        if (dist <= lenArc)
        {
            double t = lenArc <= 1e-12 ? 1.0 : dist / lenArc;
            SlerpDirection(u0x, u0y, u0z, u3x, u3y, u3z, t, out double ux, out double uy, out double uz);
            x = ux * guideR;
            y = uy * guideR;
            z = uz * guideR;
            return;
        }

        dist -= lenArc;
        double tOut = lenRadialOut <= 1e-12 ? 1.0 : Math.Clamp(dist / lenRadialOut, 0.0, 1.0);
        double rOut = guideR + (r3 - guideR) * tOut;
        x = u3x * rOut;
        y = u3y * rOut;
        z = u3z * rOut;
    }

    /// <summary>
    /// Sampled minimum radius of a planned guide-sphere exit (for tests).
    /// </summary>
    public static double SampleGuideSphereExitMinRadius(
        double guideRadius,
        double clearance,
        double p0x, double p0y, double p0z,
        double p3x, double p3y, double p3z,
        int samples = 64)
    {
        PlanGuideSphereExit(
            guideRadius, clearance,
            p0x, p0y, p0z, p3x, p3y, p3z,
            out double u0x, out double u0y, out double u0z,
            out double u3x, out double u3y, out double u3z,
            out double r0, out double r3, out double guideR,
            out double lenIn, out double lenArc, out double lenOut);

        double minR = double.MaxValue;
        for (int i = 0; i <= samples; i++)
        {
            double s = i / (double)samples;
            EvaluateGuideSphereExit(
                s,
                u0x, u0y, u0z, u3x, u3y, u3z,
                r0, r3, guideR, lenIn, lenArc, lenOut,
                out double x, out double y, out double z);
            double r = Math.Sqrt(x * x + y * y + z * z);
            if (r < minR)
                minR = r;
        }

        return minR;
    }

    /// <summary>
    /// Discrete tangent-support samples on the guide sphere (control points for
    /// visualization / debugging). Count includes shell endpoints.
    /// </summary>
    public static void SampleGuideSphereTangentControls(
        double u0x, double u0y, double u0z,
        double u3x, double u3y, double u3z,
        double guideR,
        int count,
        Span<(double X, double Y, double Z)> dst)
    {
        int n = Math.Min(count, dst.Length);
        if (n <= 0)
            return;
        if (n == 1)
        {
            dst[0] = (u0x * guideR, u0y * guideR, u0z * guideR);
            return;
        }

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)(n - 1);
            SlerpDirection(u0x, u0y, u0z, u3x, u3y, u3z, t, out double ux, out double uy, out double uz);
            dst[i] = (ux * guideR, uy * guideR, uz * guideR);
        }
    }

    public static void InflateEarthRelative(ref double x, ref double y, ref double z, double clearance)
    {
        double r = Math.Sqrt(x * x + y * y + z * z);
        if (r >= clearance)
            return;

        if (r < 1e-9)
        {
            x = clearance;
            y = 0;
            z = 0;
            return;
        }

        double s = clearance / r;
        x *= s;
        y *= s;
        z *= s;
    }

    private void EncodeEarthRelativeEyeAsCamera(
        double rx, double ry, double rz,
        out CameraState state)
    {
        (double tx, double ty, double tz) = EarthPosition;
        double dist = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        if (dist < 1e-9)
        {
            state = new CameraState(CameraFocus.Earth, 0, 0, BodyRadius(Bodies[EarthIndex]) * ExitPathMinClearanceEarthRadii, 0, 0);
            return;
        }

        // Invert sun-relative bearing/elevation for metadata continuity.
        double elen = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (elen < 1e-6)
            elen = 1e-6;
        double sx = -tx / elen;
        double sy = -ty / elen;
        double sz = -tz / elen;
        double rightX = sz, rightZ = -sx;
        double rl = Math.Sqrt(rightX * rightX + rightZ * rightZ);
        if (rl < 1e-6)
        {
            rightX = 1;
            rightZ = 0;
        }
        else
        {
            rightX /= rl;
            rightZ /= rl;
        }

        double ux = rx / dist, uy = ry / dist, uz = rz / dist;
        double elev = Math.Asin(Math.Clamp(uy, -1.0, 1.0)) * 180.0 / Math.PI;
        double alongSun = ux * sx + uy * sy + uz * sz;
        double alongRight = ux * rightX + uz * rightZ;
        double bearing = Math.Atan2(alongRight, alongSun) * 180.0 / Math.PI;

        state = new CameraState(CameraFocus.Earth, 0, 0, dist, NormalizeDegrees(bearing), elev);
    }

    private void ComputeEarthCameraEyeAndLookAt(
        CameraState cam,
        bool applyMenuPan,
        out double cameraX, out double cameraY, out double cameraZ,
        out double lookX, out double lookY, out double lookZ)
    {
        (double tx, double ty, double tz) = EarthPosition;
        lookX = tx;
        lookY = ty;
        lookZ = tz;
        double earthR = BodyRadius(Bodies[EarthIndex]);
        double dist = Math.Max(cam.Distance, earthR * ExitPathMinClearanceEarthRadii);
        ComputeSunRelativeEarthCameraEye(cam, dist, tx, ty, tz, out cameraX, out cameraY, out cameraZ);

        if (applyMenuPan)
        {
            double elen = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (elen < 1e-6)
                elen = 1e-6;
            double sx = -tx / elen;
            double sz = -tz / elen;
            double rx = sz, rz = -sx;
            double rl = Math.Sqrt(rx * rx + rz * rz);
            if (rl > 1e-6)
            {
                rx /= rl;
                rz /= rl;
                double pan = dist * 0.20;
                lookX -= rx * pan;
                lookZ -= rz * pan;
                lookY += dist * 0.03;
            }
        }
    }

    public static double ShortestDelta(double fromDeg, double toDeg)
        => ((toDeg - fromDeg + 540.0) % 360.0) - 180.0;

    public static double NormalizeAngle(double rad)
    {
        const double twoPi = 2.0 * Math.PI;
        rad %= twoPi;
        if (rad < 0)
            rad += twoPi;
        return rad;
    }

    /// <summary>
    /// Optional pre-JIT of the solver/pose paths so the first backdrop frame
    /// does not pay JIT cost. Safe to call from any thread.
    /// </summary>
    public static void WarmUp()
    {
        var probe = new SolarSystemScene();
        probe.Advance(1.0 / 60.0);
        _ = probe.Camera;
    }

    // ------------------------------------------------------------------
    // View-projection + Earth lat/lon screen projection (shared with GL
    // and the campaign overlay so mission anchors lock to the marble).
    // ------------------------------------------------------------------

    public const double FieldOfViewDeg = 46.0;
    public const double NearPlane = 0.02;
    public const double FarPlane = 200.0;

    /// <summary>
    /// Builds the same column-major VP matrix the GL control uses. Also
    /// returns the camera eye position used for lighting / front-face tests.
    /// </summary>
    public float[] BuildViewProjection(float aspect, out double cameraX, out double cameraY, out double cameraZ)
    {
        var cam = Camera;
        double tx, ty, tz;

        if (_exitSettleActive)
        {
            (double ex, double ey, double ez) = EarthPosition;
            double t = Math.Clamp(_exitSettleElapsed / EarthExitSettleDurationSeconds, 0.0, 1.0);
            double eased = EaseInOutQuint(t);

            ComputeEarthCameraEyeAndLookAt(
                _poseTo,
                applyMenuPan: true,
                out double toX, out double toY, out double toZ,
                out double toLookX, out double toLookY, out double toLookZ);

            double toRx = toX - ex, toRy = toY - ey, toRz = toZ - ez;
            double toLx = toLookX - ex, toLy = toLookY - ey, toLz = toLookZ - ez;

            double rx = _settleFromRx + (toRx - _settleFromRx) * eased;
            double ry = _settleFromRy + (toRy - _settleFromRy) * eased;
            double rz = _settleFromRz + (toRz - _settleFromRz) * eased;
            double clearance = BodyRadius(Bodies[EarthIndex]) * ExitPathMinClearanceEarthRadii;
            InflateEarthRelative(ref rx, ref ry, ref rz, clearance);

            cameraX = ex + rx;
            cameraY = ey + ry;
            cameraZ = ez + rz;
            tx = ex + _settleFromLx + (toLx - _settleFromLx) * eased;
            ty = ey + _settleFromLy + (toLy - _settleFromLy) * eased;
            tz = ez + _settleFromLz + (toLz - _settleFromLz) * eased;
            return FinishViewProjection(aspect, tx, ty, tz, cameraX, cameraY, cameraZ);
        }

        if (_exitPathActive)
        {
            (double ex, double ey, double ez) = EarthPosition;
            double t = PoseBlendProgress;
            double eased = EaseInOutQuint(t);
            EvaluateGuideSphereExit(
                eased,
                _exitU0x, _exitU0y, _exitU0z,
                _exitU3x, _exitU3y, _exitU3z,
                _exitR0, _exitR3, _exitGuideR,
                _exitLenRadialIn, _exitLenArc, _exitLenRadialOut,
                out double rx, out double ry, out double rz);
            double clearance = BodyRadius(Bodies[EarthIndex]) * ExitPathMinClearanceEarthRadii;
            InflateEarthRelative(ref rx, ref ry, ref rz, clearance);
            cameraX = ex + rx;
            cameraY = ey + ry;
            cameraZ = ez + rz;
            tx = ex + _exitLook0x + (_exitLook1x - _exitLook0x) * eased;
            ty = ey + _exitLook0y + (_exitLook1y - _exitLook0y) * eased;
            tz = ez + _exitLook0z + (_exitLook1z - _exitLook0z) * eased;
            return FinishViewProjection(aspect, tx, ty, tz, cameraX, cameraY, cameraZ);
        }

        if (cam.Focus == CameraFocus.Earth)
        {
            (tx, ty, tz) = EarthPosition;
            double earthR = BodyRadius(Bodies[EarthIndex]);
            double dist = Math.Max(cam.Distance, earthR * 2.05);

            if (_surfaceOrbitActive)
            {
                GetSurfaceOrbitDirection(out double dx, out double dy, out double dz);
                cameraX = tx + dist * dx;
                cameraY = ty + dist * dy;
                cameraZ = tz + dist * dz;
            }
            else
            {
                ComputeSunRelativeEarthCameraEye(cam, dist, tx, ty, tz, out cameraX, out cameraY, out cameraZ);

                // Mid-menu: bias look-at so Earth sits in the open right-center (left HUD).
                if (!IsEarthFocused)
                {
                    double elen = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    if (elen < 1e-6)
                        elen = 1e-6;
                    double sx = -tx / elen;
                    double sz = -tz / elen;
                    double rx = sz, rz = -sx;
                    double rl = Math.Sqrt(rx * rx + rz * rz);
                    if (rl > 1e-6)
                    {
                        rx /= rl;
                        rz /= rl;
                        double pan = dist * 0.20;
                        tx -= rx * pan;
                        tz -= rz * pan;
                        ty += dist * 0.03;
                    }
                }
            }
        }
        else
        {
            tx = ty = tz = 0.0;
            double yaw = cam.YawDeg * Math.PI / 180.0;
            double pitch = cam.PitchDeg * Math.PI / 180.0;
            double cp = Math.Cos(pitch);
            cameraX = cam.Distance * cp * Math.Sin(yaw);
            cameraY = cam.Distance * Math.Sin(pitch);
            cameraZ = cam.Distance * cp * Math.Cos(yaw);

            double pan = cam.Distance * 0.06;
            double prx = cameraZ, prz = -cameraX;
            double prl = Math.Sqrt(prx * prx + prz * prz);
            if (prl > 1e-6)
            {
                tx -= (prx / prl) * pan;
                tz -= (prz / prl) * pan;
            }
        }

        return FinishViewProjection(aspect, tx, ty, tz, cameraX, cameraY, cameraZ);
    }

    private void ComputeSunRelativeEarthCameraEye(CameraState cam, out double cameraX, out double cameraY, out double cameraZ)
    {
        (double tx, double ty, double tz) = EarthPosition;
        double earthR = BodyRadius(Bodies[EarthIndex]);
        double dist = Math.Max(cam.Distance, earthR * 2.05);
        ComputeSunRelativeEarthCameraEye(cam, dist, tx, ty, tz, out cameraX, out cameraY, out cameraZ);
    }

    private void ComputeSunRelativeEarthCameraEye(
        CameraState cam,
        double dist,
        double tx, double ty, double tz,
        out double cameraX, out double cameraY, out double cameraZ)
    {
        double elen = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (elen < 1e-6)
            elen = 1e-6;
        double sx = -tx / elen;
        double sy = -ty / elen;
        double sz = -tz / elen;

        double rx = sz, rz = -sx;
        double rl = Math.Sqrt(rx * rx + rz * rz);
        if (rl < 1e-6)
        {
            rx = 1;
            rz = 0;
        }
        else
        {
            rx /= rl;
            rz /= rl;
        }

        double elev = cam.EarthElevationDeg * Math.PI / 180.0;
        double bearing = cam.EarthBearingDeg * Math.PI / 180.0;
        double ce = Math.Cos(elev);
        double se = Math.Sin(elev);
        double cb = Math.Cos(bearing);
        double sb = Math.Sin(bearing);

        cameraX = tx + dist * (sx * ce * cb + rx * ce * sb);
        cameraY = ty + dist * (sy * ce * cb + se);
        cameraZ = tz + dist * (sz * ce * cb + rz * ce * sb);

        double sunClear = SunRadius * 1.65;
        double cLen = Math.Sqrt(cameraX * cameraX + cameraY * cameraY + cameraZ * cameraZ);
        if (cLen < sunClear && cLen > 1e-6)
        {
            double s = sunClear / cLen;
            cameraX *= s;
            cameraY *= s;
            cameraZ *= s;
        }
    }

    private float[] FinishViewProjection(
        float aspect,
        double tx, double ty, double tz,
        double cameraX, double cameraY, double cameraZ)
    {
        double fx = tx - cameraX, fy = ty - cameraY, fz = tz - cameraZ;
        double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        if (fl < 1e-9)
            fl = 1e-9;
        fx /= fl; fy /= fl; fz /= fl;

        double rbx = fz, rby = 0, rbz = -fx;
        double rbl = Math.Sqrt(rbx * rbx + rbz * rbz);
        if (rbl < 1e-9)
        {
            rbx = 1; rby = 0; rbz = 0;
        }
        else
        {
            rbx /= rbl; rbz /= rbl;
        }

        double ux = fy * rbz - fz * rby;
        double uy = fz * rbx - fx * rbz;
        double uz = fx * rby - fy * rbx;

        double tiltRad = WorldTiltDeg * Math.PI / 180.0;
        if (tiltRad != 0)
        {
            double ct = Math.Cos(tiltRad);
            double st = Math.Sin(tiltRad);
            (rbx, ux) = (rbx * ct + ux * st, ux * ct - rbx * st);
            (rby, uy) = (rby * ct + uy * st, uy * ct - rby * st);
            (rbz, uz) = (rbz * ct + uz * st, uz * ct - rbz * st);
        }

        double fovRad = FieldOfViewDeg * Math.PI / 180.0;
        double f = 1.0 / Math.Tan(fovRad / 2.0);
        double p00 = f / Math.Max(aspect, 1e-6);
        double p11 = f;
        double p22 = (FarPlane + NearPlane) / (NearPlane - FarPlane);
        double p23 = 2.0 * FarPlane * NearPlane / (NearPlane - FarPlane);

        double v00 = rbx, v01 = rby, v02 = rbz;
        double v10 = ux, v11 = uy, v12 = uz;
        double v20 = -fx, v21 = -fy, v22 = -fz;
        double v03 = -(v00 * cameraX + v01 * cameraY + v02 * cameraZ);
        double v13 = -(v10 * cameraX + v11 * cameraY + v12 * cameraZ);
        double v23 = -(v20 * cameraX + v21 * cameraY + v22 * cameraZ);

        var vp = new float[16];
        vp[0] = (float)(p00 * v00); vp[4] = (float)(p00 * v01); vp[8] = (float)(p00 * v02); vp[12] = (float)(p00 * v03);
        vp[1] = (float)(p11 * v10); vp[5] = (float)(p11 * v11); vp[9] = (float)(p11 * v12); vp[13] = (float)(p11 * v13);
        vp[2] = (float)(p22 * v20); vp[6] = (float)(p22 * v21); vp[10] = (float)(p22 * v22); vp[14] = (float)(p22 * v23 + p23);
        vp[3] = (float)(-v20); vp[7] = (float)(-v21); vp[11] = (float)(-v22); vp[15] = (float)(-v23);
        return vp;
    }

    /// <summary>
    /// Maps a geographic lat/lon on the shared Earth marble to backdrop DIP
    /// coordinates. Matches the GL sphere mesh + Rx(tilt)·Ry(spin) model.
    /// </summary>
    public static bool ProjectEarthLatLon(
        float[] viewProjection,
        double earthX, double earthY, double earthZ,
        double earthRadius,
        double axialTiltDeg,
        double spinPhase,
        double cameraX, double cameraY, double cameraZ,
        double latDeg, double lonDeg,
        double viewportWidth, double viewportHeight,
        out double screenX, out double screenY, out bool frontFacing)
    {
        screenX = screenY = 0;
        frontFacing = false;
        if (viewProjection is null || viewProjection.Length < 16
            || viewportWidth < 1 || viewportHeight < 1)
            return false;

        double lat = latDeg * Math.PI / 180.0;
        double lon = lonDeg * Math.PI / 180.0;
        double cl = Math.Cos(lat);
        double lx = cl * Math.Sin(lon);
        double ly = Math.Sin(lat);
        double lz = cl * Math.Cos(lon);

        double tilt = axialTiltDeg * Math.PI / 180.0;
        double ct = Math.Cos(tilt);
        double st = Math.Sin(tilt);
        double cs = Math.Cos(spinPhase);
        double ss = Math.Sin(spinPhase);

        // Rx(tilt)·Ry(spin) · local  (same as MultiplyAffine)
        double m00 = cs, m01 = 0, m02 = ss;
        double m10 = st * ss, m11 = ct, m12 = -st * cs;
        double m20 = -ct * ss, m21 = st, m22 = ct * cs;

        double nx = m00 * lx + m01 * ly + m02 * lz;
        double ny = m10 * lx + m11 * ly + m12 * lz;
        double nz = m20 * lx + m21 * ly + m22 * lz;

        double wx = earthX + nx * earthRadius;
        double wy = earthY + ny * earthRadius;
        double wz = earthZ + nz * earthRadius;

        // Column-major VP · (wx,wy,wz,1)
        double clipX = viewProjection[0] * wx + viewProjection[4] * wy + viewProjection[8] * wz + viewProjection[12];
        double clipY = viewProjection[1] * wx + viewProjection[5] * wy + viewProjection[9] * wz + viewProjection[13];
        double clipW = viewProjection[3] * wx + viewProjection[7] * wy + viewProjection[11] * wz + viewProjection[15];
        if (Math.Abs(clipW) < 1e-9)
            return false;

        double ndcX = clipX / clipW;
        double ndcY = clipY / clipW;
        screenX = (ndcX * 0.5 + 0.5) * viewportWidth;
        screenY = (1.0 - (ndcY * 0.5 + 0.5)) * viewportHeight;

        double toCamX = cameraX - earthX;
        double toCamY = cameraY - earthY;
        double toCamZ = cameraZ - earthZ;
        frontFacing = nx * toCamX + ny * toCamY + nz * toCamZ > 0.0;
        return true;
    }
}
