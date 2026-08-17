using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// Tactical globe: a host panel that owns the pose (yaw/pitch), pointer input,
/// inertia, optional auto-rotation, the F1 focus animation and the F4A city
/// hologram state machine. The sphere itself is drawn by an embedded
/// <see cref="TacticalGlobeGlControl"/> (OpenGL, texture-mapped UV sphere fed
/// by the baked equirectangular map plus the F2 country border line layer);
/// a sibling overlay layer draws the graticule, atmosphere rim, F3 mission
/// markers and the F4A holo board through the very same projection formula,
/// keeping anchors registered with texture continents. If GL fails to
/// initialize, a static limb-darkened disc keeps the layout.
/// </summary>
public class TacticalGlobeView : Panel
{
    public static readonly StyledProperty<IList<GlobeNode>> NodesProperty =
        AvaloniaProperty.Register<TacticalGlobeView, IList<GlobeNode>>(nameof(Nodes), new List<GlobeNode>());

    public static readonly StyledProperty<int> SelectedNodeIndexProperty =
        AvaloniaProperty.Register<TacticalGlobeView, int>(nameof(SelectedNodeIndex), -1);

    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<TacticalGlobeView, double>(nameof(Yaw), 20.0);

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<TacticalGlobeView, double>(nameof(Pitch), -16.0);

    private const int Meridians = 12;
    private const int Parallels = 6;
    private const double RadiusFactor = 0.44;
    private const double FocalFactor = 3.4;
    private const double DragSensitivity = 0.32;
    private const double AutoRotateDegreesPerSecond = 2.4;

    // F1 focus animation parameters (ease-out cubic, shortest arc).
    private const double FocusDurationSeconds = 0.8;
    private const double FocusCompletionEpsilonDegrees = 0.05;

    // F4A city holo board.
    private const double HoloBoardWidth = 300;
    private const double HoloBoardHeight = 170;
    private const double HoloEnterDelaySeconds = 0.3;
    private const double HoloFadeSeconds = 0.3;

    private readonly TacticalGlobeGlControl _gl = new();
    private readonly OverlayLayer _overlay;
    private readonly List<NodeMarker> _markers = new();
    private DispatcherTimer? _timer;
    private IBrush? _lineBrush;
    private IBrush? _mutedLineBrush;
    private IBrush? _accentBrush;
    private IBrush? _accentInverseBrush;
    private bool _dragging;
    private Point _lastPointer;
    private double _inertiaYaw;
    private DateTime _lastFrame = DateTime.UtcNow;
    private Color _glAccent;
    private bool _glAccentPushed;

    // F1 focus animation state.
    private bool _focusAnimating;
    private double _focusStartYaw;
    private double _focusStartPitch;
    private double _focusTargetYaw;
    private double _focusTargetPitch;
    private double _focusElapsed;

    // F4A city holo state machine: Dormant → Delayed → FadingIn → Visible.
    private bool _holoActive;
    private bool _holoPending;
    private double _holoElapsed;
    private double _holoAlpha;
    private int _holoNodeIndex = -1;
    private bool _suppressFocusOnce;

    /// <summary>Fired once the F1 focus animation settles on its target.</summary>
    public event EventHandler? FocusCompleted;

    /// <summary>True while the F1 focus animation is running.</summary>
    public bool IsFocusing => _focusAnimating;

    /// <summary>
    /// Bridge mode: the shared solar-system backdrop owns the Earth marble
    /// (single GIS/pose source). Local GL sphere is hidden; overlay keeps
    /// graticule/markers and mirrors yaw/pitch from the scene.
    /// </summary>
    public bool BridgeFromSolarSystem
    {
        get => _bridgeFromSolarSystem;
        set
        {
            if (_bridgeFromSolarSystem == value)
                return;

            _bridgeFromSolarSystem = value;
            _gl.IsVisible = !value;
            if (value)
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            }

            InvalidatePose();
            InvalidateMeasure();
        }
    }

    private bool _bridgeFromSolarSystem;
    private double _bridgeAnchorOpacity = 1.0;

    /// <summary>
    /// When bridged, mission markers fade in with the campaign enter
    /// choreography (0 = hidden, 1 = full).
    /// </summary>
    public double BridgeAnchorOpacity
    {
        get => _bridgeAnchorOpacity;
        set
        {
            double v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_bridgeAnchorOpacity - v) < 1e-4)
                return;
            _bridgeAnchorOpacity = v;
            _overlay.InvalidateVisual();
        }
    }

    public TacticalGlobeView()
    {
        ClipToBounds = true;
        // Transparent background keeps the panel itself hit-testable so drag
        // and node clicks work even though both children ignore the pointer.
        Background = Brushes.Transparent;
        _gl.IsHitTestVisible = false;
        Children.Add(_gl);
        _overlay = new OverlayLayer(this)
        {
            IsHitTestVisible = false,
        };
        Children.Add(_overlay);
    }

    public IList<GlobeNode> Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public int SelectedNodeIndex
    {
        get => GetValue(SelectedNodeIndexProperty);
        set => SetValue(SelectedNodeIndexProperty, value);
    }

    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public event EventHandler<int>? NodeClicked;

    private bool AutoRotateEnabled
    {
        get
        {
            try
            {
                return UserINISettings.Instance.GlobeAutoRotateEnabled.Value;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private bool FocusEnabled
    {
        get
        {
            try
            {
                return UserINISettings.Instance.GlobeFocusEnabled.Value;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private bool CityHoloEnabled
    {
        get
        {
            try
            {
                return UserINISettings.Instance.GlobeCityHoloEnabled.Value;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedNodeIndexProperty)
            OnSelectedNodeChanged(change.GetNewValue<int>());
    }

    private void OnSelectedNodeChanged(int index)
    {
        _holoPending = false;
        _holoActive = false;
        _holoAlpha = 0;

        GlobeNode? node = index >= 0 && Nodes is not null && index < Nodes.Count ? Nodes[index] : null;
        _gl.SetHighlightedCountry(node?.CountryCode);

        if (node is null || !FocusEnabled)
            return;

        if (_bridgeFromSolarSystem && SolarSystemDirector.IsActive)
        {
            // Camera orbits to face the mission; Earth Kepler spin is unchanged.
            SolarSystemDirector.FocusMission(node.LatitudeDegrees, node.LongitudeDegrees);
            if (CityHoloEnabled && !node.Locked)
            {
                _holoPending = true;
                _holoNodeIndex = index;
                _holoElapsed = 0;
                _holoAlpha = 0;
            }

            return;
        }

        // Both real INI coordinates and hash-fallback spread are valid targets.
        BeginFocus(node.LatitudeDegrees, node.LongitudeDegrees);
        if (CityHoloEnabled && !node.Locked)
        {
            _holoPending = true;
            _holoNodeIndex = index;
            _holoElapsed = 0;
            _holoAlpha = 0;
        }
    }

    /// <summary>
    /// F1: animates the pose toward (lat, lon) over 0.8s with ease-out cubic
    /// easing and the shortest yaw arc. Interrupted by pointer press.
    /// </summary>
    public void FocusNode(int index)
    {
        if (index < 0 || Nodes is null || index >= Nodes.Count)
            return;

        GlobeNode target = Nodes[index];
        if (_bridgeFromSolarSystem && SolarSystemDirector.IsActive)
        {
            SolarSystemDirector.FocusMission(target.LatitudeDegrees, target.LongitudeDegrees);
            return;
        }

        BeginFocus(target.LatitudeDegrees, target.LongitudeDegrees);
    }

    internal void BeginFocus(double latitudeDegrees, double longitudeDegrees)
    {
        _focusStartYaw = Yaw;
        _focusStartPitch = Pitch;
        _focusTargetYaw = GlobeMath.TargetYaw(longitudeDegrees);
        _focusTargetPitch = GlobeMath.TargetPitch(latitudeDegrees);
        _focusElapsed = 0;
        _focusAnimating = true;
        _inertiaYaw = 0;
        _suppressFocusOnce = false;
        InvalidatePose();
    }

    private void StepFocus(double dt)
    {
        if (!_focusAnimating)
            return;

        _focusElapsed += dt;
        double t = Math.Clamp(_focusElapsed / FocusDurationSeconds, 0.0, 1.0);
        double eased = GlobeMath.EaseOutCubic(t);

        // Integrate from the animation start pose so the curve stays
        // deterministic when dt jitters.
        Yaw = _focusStartYaw + GlobeMath.ShortestYawDelta(_focusStartYaw, _focusTargetYaw) * eased;
        Pitch = _focusStartPitch + (_focusTargetPitch - _focusStartPitch) * eased;
        InvalidatePose();

        bool settled = Math.Abs(GlobeMath.ShortestYawDelta(Yaw, _focusTargetYaw)) < FocusCompletionEpsilonDegrees
                       && Math.Abs(Pitch - _focusTargetPitch) < FocusCompletionEpsilonDegrees;
        if (t >= 1.0 || settled)
        {
            _focusAnimating = false;
            Yaw = _focusTargetYaw;
            Pitch = _focusTargetPitch;
            InvalidatePose();
            FocusCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StepHolo(double dt)
    {
        if (!_holoPending && !_holoActive)
        {
            if (_holoAlpha > 0)
            {
                _holoAlpha = Math.Max(0, _holoAlpha - dt / 0.2);
                _overlay.InvalidateVisual();
            }

            return;
        }

        _holoElapsed += dt;

        if (_holoPending)
        {
            if (_holoElapsed >= HoloEnterDelaySeconds)
            {
                _holoPending = false;
                _holoActive = true;
                _holoElapsed = 0;
            }

            return;
        }

        if (_holoActive && _holoElapsed >= HoloFadeSeconds)
        {
            _holoAlpha = 1;
        }
        else if (_holoActive)
        {
            _holoAlpha = Math.Clamp(_holoElapsed / HoloFadeSeconds, 0.0, 1.0);
        }

        _overlay.InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void StartTimer()
    {
        if (_timer != null)
        {
            _timer.Start();
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += (_, _) =>
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastFrame).TotalSeconds;
            _lastFrame = now;

            if (_dragging)
                return;

            // Bridge: anchors project via shared VP; camera orbit is driven by
            // Director (drag / mission lock). Local yaw no longer owns Earth.
            if (_bridgeFromSolarSystem && SolarSystemDirector.IsActive)
            {
                _overlay.InvalidateVisual();
                StepHolo(dt);
                return;
            }

            if (_focusAnimating)
            {
                StepFocus(dt);
                StepHolo(dt);
                return;
            }

            if (Math.Abs(_inertiaYaw) > 0.05)
            {
                Yaw += _inertiaYaw * dt * 60.0;
                _inertiaYaw *= Math.Pow(0.9, dt * 60.0);
                InvalidatePose();
            }
            else if (AutoRotateEnabled && !_holoActive)
            {
                Yaw = (Yaw + AutoRotateDegreesPerSecond * dt) % 360.0;
                InvalidatePose();
            }
            else if (HasAnimatedOverlay())
            {
                // Selection pulse / holo fade need repaints even when the
                // pose is static; the timer runs outside the render pass so
                // invalidating here is safe.
                _overlay.InvalidateVisual();
            }

            StepHolo(dt);
        };
        _timer.Start();
    }

    private bool HasAnimatedOverlay()
        => _holoActive || _holoPending || _holoAlpha > 0
           || (SelectedNodeIndex >= 0 && _markers.Count > 0);

    private void InvalidatePose()
    {
        _gl.Pose = (Yaw, Pitch);
        _overlay.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Bridge mode fills the campaign overlay so 3D-projected anchors can
        // land anywhere the backdrop Earth appears inside the panel.
        if (_bridgeFromSolarSystem)
        {
            double w = double.IsInfinity(availableSize.Width) ? 800.0 : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? 600.0 : availableSize.Height;
            var fill = new Size(Math.Max(1, w), Math.Max(1, h));
            _gl.Measure(fill);
            _overlay.Measure(fill);
            return fill;
        }

        double side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 480.0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 480.0 : availableSize.Height);

        var square = new Size(side, side);
        _gl.Measure(square);
        _overlay.Measure(square);
        return square;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _gl.Arrange(new Rect(finalSize));
        _overlay.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void DrawCorner(DrawingContext ctx, Pen pen, Point c, double ox, double oy, double len, int sx, int sy)
    {
        var geo = new StreamGeometry();
        using StreamGeometryContext gc = geo.Open();
        gc.BeginFigure(new Point(c.X + ox, c.Y + oy + sy * len), false);
        gc.LineTo(new Point(c.X + ox, c.Y + oy));
        gc.LineTo(new Point(c.X + ox + sx * len, c.Y + oy));
        ctx.DrawGeometry(null, pen, geo);
    }

    private static IBrush ApplyOpacity(IBrush brush, double alpha)
    {
        if (brush is SolidColorBrush scb)
            return new SolidColorBrush(Color.FromArgb((byte)(scb.Color.A * alpha), scb.Color.R, scb.Color.G, scb.Color.B));
        return brush;
    }

    internal void ResolveBrushes()
    {
        _lineBrush = this.TryFindResource("DxLineBrightBrush", out object? lineObj) && lineObj is IBrush line
            ? line
            : Brushes.Silver;

        _mutedLineBrush = this.TryFindResource("DxLineBrush", out object? mutedObj) && mutedObj is IBrush muted
            ? muted
            : Brushes.Gray;

        _accentBrush = this.TryFindResource("DxAccentPrimaryBrush", out object? accentObj) && accentObj is IBrush accent
            ? accent
            : Brushes.Cyan;

        _accentInverseBrush = this.TryFindResource("DxAccentInverseBrush", out object? inverseObj) && inverseObj is IBrush inverse
            ? inverse
            : Brushes.OrangeRed;

        if (_accentBrush is SolidColorBrush accentScb)
        {
            var c = accentScb.Color;
            var gl = new Color(c.A, c.R, c.G, c.B);
            if (_glAccentPushed && _glAccent == gl)
                return;

            _glAccent = gl;
            _glAccentPushed = true;
            _gl.SetAccent(
                c.R / 255f * (c.A / 255f),
                c.G / 255f * (c.A / 255f),
                c.B / 255f * (c.A / 255f));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        _dragging = true;
        _inertiaYaw = 0;
        _focusAnimating = false; // F1: manual input interrupts the animation.
        _holoPending = false;    // F4A: dragging cancels the city hologram.
        _holoActive = false;
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            UpdateHover(e.GetPosition(this));
            return;
        }

        Point pos = e.GetPosition(this);
        double dx = pos.X - _lastPointer.X;
        double dy = pos.Y - _lastPointer.Y;
        _inertiaYaw = dx * DragSensitivity;
        _lastPointer = pos;

        if (_bridgeFromSolarSystem && SolarSystemDirector.IsActive)
        {
            // Inverse camera orbit: dragging right feels like spinning the globe left.
            SolarSystemDirector.NudgeCameraOrbit(-dx * DragSensitivity, dy * DragSensitivity * 0.55);
            SolarSystemDirector.SetCameraOrbitInertia(-_inertiaYaw, dy * DragSensitivity * 0.55);
            _overlay.InvalidateVisual();
            e.Handled = true;
            return;
        }

        Yaw += dx * DragSensitivity;
        Pitch = Math.Clamp(Pitch - dy * DragSensitivity * 0.6, -40.0, 40.0);
        InvalidatePose();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        Point pos = e.GetPosition(this);

        double dist = Math.Sqrt(Math.Pow(pos.X - _lastPointer.X, 2) + Math.Pow(pos.Y - _lastPointer.Y, 2));
        if (dist < 4.0)
        {
            int hit = HitTest(pos);
            if (hit >= 0)
                NodeClicked?.Invoke(this, hit);
        }
        else if (_bridgeFromSolarSystem && SolarSystemDirector.IsActive)
        {
            SolarSystemDirector.SetCameraOrbitInertia(-_inertiaYaw, 0);
        }

        e.Handled = true;
    }

    private void UpdateHover(Point pos)
    {
        int hit = HitTest(pos);
        bool changed = false;
        for (int i = 0; i < _markers.Count; i++)
        {
            bool hovered = i == hit;
            if (_markers[i].IsHovered != hovered)
            {
                _markers[i].IsHovered = hovered;
                changed = true;
            }
        }

        Cursor = hit >= 0 ? new Cursor(StandardCursorType.Hand) : null;

        if (changed)
            _overlay.InvalidateVisual();
    }

    private int HitTest(Point pos)
    {
        int best = -1;
        double bestDist = 12.0;
        for (int i = 0; i < _markers.Count; i++)
        {
            if (!_markers[i].IsFront)
                continue;
            double d = Math.Sqrt(Math.Pow(_markers[i].Position.X - pos.X, 2) + Math.Pow(_markers[i].Position.Y - pos.Y, 2));
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private void RebuildMarkers()
    {
        if (_markers.Count == Nodes?.Count)
            return;

        _markers.Clear();
        if (Nodes is null)
            return;

        foreach (GlobeNode node in Nodes)
            _markers.Add(new NodeMarker(node));
    }

    /// <summary>Silhouette radius factor: the perspective sphere projects to F/√(F²−1) × radius.</summary>
    private static double SilhouetteFactor => FocalFactor / Math.Sqrt(FocalFactor * FocalFactor - 1.0);

    /// <summary>
    /// Overlay drawn above the GL sphere: graticule, atmosphere rim, F3 node
    /// markers, the selection reticle and the F4A holo board. Shares the
    /// host's projection so it stays locked to the texture-mapped surface.
    /// </summary>
    private sealed class OverlayLayer : Control
    {
        private readonly TacticalGlobeView _host;

        public OverlayLayer(TacticalGlobeView host) => _host = host;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            _host.RenderOverlay(context);
        }
    }

    internal void RenderOverlay(DrawingContext context)
    {
        ResolveBrushes();
        Size size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4)
            return;

        double cx = size.Width / 2.0;
        double cy = size.Height / 2.0;
        double radius = Math.Min(cx, cy) * RadiusFactor * 2.0;

        double yawRad = Yaw * Math.PI / 180.0;
        double pitchRad = Pitch * Math.PI / 180.0;
        double cosPitch = Math.Cos(pitchRad);
        double sinPitch = Math.Sin(pitchRad);

        // Unit sphere direction after yaw/pitch, plus camera-space z for clipping.
        (double X, double Y, double Z) Dir(double latDeg, double lonDeg)
        {
            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;
            double x = Math.Cos(lat) * Math.Sin(lon + yawRad);
            double y = Math.Sin(lat);
            double z = Math.Cos(lat) * Math.Cos(lon + yawRad);
            double y1 = y * cosPitch - z * sinPitch;
            double z1 = y * sinPitch + z * cosPitch;
            return (x, y1, z1);
        }

        Point Project(double x, double y1, double z1)
        {
            double scale = FocalFactor / (FocalFactor - z1);
            return new Point(cx + x * radius * scale, cy - y1 * radius * scale);
        }

        // Keep the GL sphere in sync with this pose every overlay pass.
        _gl.Pose = (Yaw, Pitch);

        // Fallback disc only while the local GL initializes and we are NOT
        // bridged to the shared solar-system Earth (that marble is the source).
        if (!_bridgeFromSolarSystem && !_gl.HasRendered)
        {
            double fr = radius * SilhouetteFactor;
            var disc = new EllipseGeometry(new Rect(cx - fr, cy - fr, fr * 2, fr * 2));
            var surfaceBrush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.42, 0.38, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x11, 0x16, 0x1C), 0.0),
                    new GradientStop(Color.FromRgb(0x0B, 0x0E, 0x13), 0.62),
                    new GradientStop(Color.FromRgb(0x05, 0x07, 0x0A), 1.0),
                },
            };
            context.DrawGeometry(surfaceBrush, null, disc);
        }

        // Bridge: no local rim / graticule / atmosphere — those read as a
        // second sphere sitting on top of the shared 3D Earth.
        if (!_bridgeFromSolarSystem)
        {
            // ---- Graticule (very subtle) ----
            var gridPen = new Pen(_mutedLineBrush ?? Brushes.Gray, 0.6);
            var gridPenBright = new Pen(ApplyOpacity(_lineBrush ?? Brushes.Silver, 0.35), 0.7);
            for (int m = 0; m < Meridians; m++)
                DrawGreatCircleArc(context, m * 360.0 / Meridians, Dir, Project, m == 0 ? gridPenBright : gridPen);
            for (int p = 1; p < Parallels; p++)
            {
                double lat = -90.0 + p * (180.0 / Parallels);
                DrawParallel(context, lat, Dir, Project, p == Parallels / 2 ? gridPenBright : gridPen);
            }

            // ---- Rim ----
            double rimRadius = radius * SilhouetteFactor;
            context.DrawEllipse(
                null,
                new Pen(ApplyOpacity(_lineBrush ?? Brushes.Silver, 0.55), 1.0),
                new Point(cx, cy),
                rimRadius,
                rimRadius);
            // Faint atmosphere ring just outside the rim.
            context.DrawEllipse(
                null,
                new Pen(ApplyOpacity(_accentBrush ?? Brushes.Cyan, 0.18), 3.0),
                new Point(cx, cy),
                rimRadius + 3.5,
                rimRadius + 3.5);
        }

        // ---- F3 nodes ----
        RebuildMarkers();
        Point selectedPoint = default;
        bool hasSelection = false;
        double bridgeAlpha = _bridgeFromSolarSystem ? _bridgeAnchorOpacity : 1.0;
        if (bridgeAlpha < 0.02)
            return;

        for (int i = 0; i < _markers.Count; i++)
        {
            NodeMarker marker = _markers[i];
            bool front;
            Point sp;
            double depthScale;

            if (_bridgeFromSolarSystem
                && SolarSystemDirector.TryProjectEarthLatLon(
                    marker.Node.LatitudeDegrees,
                    marker.Node.LongitudeDegrees,
                    this,
                    out sp,
                    out front))
            {
                depthScale = front ? 1.0 : 0.6;
            }
            else
            {
                (double x, double y1, double z1) = Dir(marker.Node.LatitudeDegrees, marker.Node.LongitudeDegrees);
                front = z1 > 0;
                sp = Project(x, y1, z1);
                depthScale = front ? 0.75 + 0.25 * z1 : 0.6;
            }

            marker.Position = sp;
            marker.IsFront = front;

            // Depth scaling keeps far-side markers from dominating.
            marker.Scale = depthScale;

            bool selected = i == SelectedNodeIndex;
            IBrush brush = marker.Node.Locked
                ? ApplyOpacity(_mutedLineBrush ?? Brushes.Gray, 0.7 * bridgeAlpha)
                : selected || marker.IsHovered
                    ? ApplyOpacity(_accentInverseBrush ?? Brushes.OrangeRed, bridgeAlpha)
                    : ApplyOpacity(_accentBrush ?? Brushes.Cyan, bridgeAlpha);

            if (!front)
                brush = ApplyOpacity(brush, 0.30);

            // Diamond marker (square rotated 45°) + halo ring.
            double s = (selected ? 3.4 : 2.4) * (marker.IsHovered ? 1.5 : 1.0) * marker.Scale;
            var diamond = new StreamGeometry();
            using (StreamGeometryContext gc = diamond.Open())
            {
                gc.BeginFigure(new Point(sp.X, sp.Y - s), true);
                gc.LineTo(new Point(sp.X + s, sp.Y), false);
                gc.LineTo(new Point(sp.X, sp.Y + s), false);
                gc.LineTo(new Point(sp.X - s, sp.Y), false);
            }

            if (marker.Node.Locked)
            {
                // Locked: hollow outline only.
                context.DrawGeometry(null, new Pen(brush, 1.0), diamond);
            }
            else
            {
                context.DrawGeometry(selected ? brush : ApplyOpacity(brush, 0.45), null, diamond);
                context.DrawGeometry(null, new Pen(brush, 1.0), diamond);
            }

            context.DrawEllipse(null, new Pen(ApplyOpacity(brush, 0.55), 0.8), sp, s + 2, s + 2);

            if (selected)
            {
                selectedPoint = sp;
                hasSelection = true;
                if (front)
                {
                    // Breathing bracket reticle (1.6s cycle).
                    double phase = (Environment.TickCount64 % 1600) / 1600.0;
                    double b = s + 4.0 + Math.Sin(phase * 2.0 * Math.PI) * 0.8;
                    var pen = new Pen(ApplyOpacity(_accentInverseBrush ?? Brushes.OrangeRed, bridgeAlpha), 1.0);
                    const double t = 3.2;
                    DrawCorner(context, pen, sp, -b, -b, t, 1, 1);
                    DrawCorner(context, pen, sp, b, -b, t, -1, 1);
                    DrawCorner(context, pen, sp, -b, b, t, 1, -1);
                    DrawCorner(context, pen, sp, b, b, t, -1, -1);

                    var typeface = new Typeface(FontFamily.Parse("Microsoft YaHei UI, Segoe UI, Inter"));
                    string label = TruncateLabel(marker.Node.Label, 12);
                    var formatted = new FormattedText(
                        label,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        11,
                        ApplyOpacity(_accentInverseBrush ?? Brushes.OrangeRed, bridgeAlpha));
                    context.DrawText(formatted, new Point(sp.X + b + 6, sp.Y - formatted.Height / 2));
                }
            }

            if (!selected && marker.IsHovered && front && !string.IsNullOrEmpty(marker.Node.Label))
            {
                // F3: hover tooltip.
                var typeface = new Typeface(FontFamily.Parse("Microsoft YaHei UI, Segoe UI, Inter"));
                var formatted = new FormattedText(
                    marker.Node.Label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    ApplyOpacity(_lineBrush ?? Brushes.Silver, bridgeAlpha));
                double ty = sp.Y - formatted.Height - 10;
                context.DrawText(formatted, new Point(sp.X - formatted.Width / 2, ty));
            }
        }

        // ---- F4A city holo board ----
        if (_holoActive && _holoAlpha > 0 && hasSelection && bridgeAlpha > 0.4)
        {
            DrawCityHoloBoard(context, size, selectedPoint, SelectedNodeIndex);
        }

        // NOTE: never call InvalidateVisual() here — Avalonia throws
        // "Visual was invalidated during the render pass". Continuous
        // animations (selection pulse, holo fade) are driven by the
        // DispatcherTimer in StartTimer, which runs outside the render pass.
    }

    private static string TruncateLabel(string label, int max)
        => label.Length <= max ? label : label[..(max - 1)] + "…";

    /// <summary>
    /// F4A: glass board rising above the selected anchor with a procedural
    /// holographic city (or the mission's custom art), leader line, title and
    /// coordinates. Pure decision logic lives in GlobeMath.ClampHoloBoard.
    /// </summary>
    private void DrawCityHoloBoard(DrawingContext context, Size size, Point anchor, int nodeIndex)
    {
        GlobeNode? node = nodeIndex >= 0 && Nodes is not null && nodeIndex < Nodes.Count ? Nodes[nodeIndex] : null;
        if (node is null)
            return;

        (double bx, double by, bool below) = GlobeMath.ClampHoloBoard(
            anchor.X, anchor.Y, HoloBoardWidth, HoloBoardHeight, size.Width, size.Height);

        double alpha = _holoAlpha;
        IBrush accent = _accentBrush ?? Brushes.Cyan;

        // Leader line from the anchor to the board.
        double leaderTop = below ? anchor.Y : by + HoloBoardHeight;
        var leaderPen = new Pen(ApplyOpacity(accent, 0.8 * alpha), 1.5);
        context.DrawLine(leaderPen, anchor, new Point(anchor.X, leaderTop));

        // Glass body.
        var boardBrush = new SolidColorBrush(Color.FromArgb((byte)(230 * alpha), 0x0A, 0x14, 0x20));
        var borderPen = new Pen(ApplyOpacity(accent, 0.9 * alpha), 1.0);
        var boardRect = new Rect(bx, by, HoloBoardWidth, HoloBoardHeight);
        context.DrawRectangle(boardBrush, borderPen, boardRect, 6, 6);

        // Title + coordinates.
        var typeface = new Typeface(FontFamily.Parse("Microsoft YaHei UI, Segoe UI, Inter"));
        var title = new FormattedText(
            TruncateLabel(node.Label, 18),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            _lineBrush ?? Brushes.Silver);
        context.DrawText(title, new Point(bx + 12, by + 10));

        string coord = $"{LatPrefix(node.LatitudeDegrees)}{Math.Abs(node.LatitudeDegrees):F1} " +
                       $"{LonPrefix(node.LongitudeDegrees)}{Math.Abs(node.LongitudeDegrees):F1}";
        var coordText = new FormattedText(
            coord,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            10,
            ApplyOpacity(_mutedLineBrush ?? Brushes.Gray, 0.9));
        context.DrawText(coordText, new Point(bx + 12, by + 28));

        // Procedural holographic city skyline (deterministic per node label).
        var skyline = new StreamGeometry();
        uint hash = 2166136261;
        foreach (char c in node.Label)
            hash = (hash ^ c) * 16777619;

        var rng = new Random((int)hash);
        double baseY = by + HoloBoardHeight - 18;
        using (StreamGeometryContext gc = skyline.Open())
        {
            bool started = false;
            double x = bx + 14;
            const double step = (HoloBoardWidth - 28.0) / 10.0;
            double runStart = x;
            while (x < bx + HoloBoardWidth - 14)
            {
                double h = 18 + rng.NextDouble() * 52;
                var p0 = new Point(x, baseY);
                var p1 = new Point(x, baseY - h);
                var p2 = new Point(x + step * 0.72, baseY - h);
                var p3 = new Point(x + step * 0.72, baseY);
                if (!started)
                {
                    gc.BeginFigure(p0, true);
                    started = true;
                }
                else
                {
                    gc.LineTo(p0, false);
                }

                gc.LineTo(p1, false);
                gc.LineTo(p2, false);
                gc.LineTo(p3, false);
                x += step;
            }

            gc.LineTo(new Point(x, baseY), false);
            gc.LineTo(new Point(runStart, baseY), false);
        }

        context.DrawGeometry(ApplyOpacity(accent, 0.30 * alpha), null, skyline);
        context.DrawGeometry(null, new Pen(ApplyOpacity(accent, 0.55 * alpha), 0.8), skyline);

        // Horizon + scanlines.
        var horizonPen = new Pen(ApplyOpacity(accent, 0.7 * alpha), 1.2);
        context.DrawLine(horizonPen, new Point(bx + 10, baseY), new Point(bx + HoloBoardWidth - 10, baseY));
        var scanPen = new Pen(ApplyOpacity(accent, 0.15 * alpha), 1.0);
        context.DrawLine(scanPen, new Point(bx + 10, by + HoloBoardHeight - 34), new Point(bx + HoloBoardWidth - 10, by + HoloBoardHeight - 34));
        context.DrawLine(scanPen, new Point(bx + 10, by + HoloBoardHeight - 12), new Point(bx + HoloBoardWidth - 10, by + HoloBoardHeight - 12));
    }

    private static string LatPrefix(double lat) => lat >= 0 ? "N" : "S";
    private static string LonPrefix(double lon) => lon >= 0 ? "E" : "W";

    private void DrawGreatCircleArc(
        DrawingContext context,
        double lonDeg,
        Func<double, double, (double X, double Y, double Z)> dir,
        Func<double, double, double, Point> project,
        Pen pen)
    {
        var geo = new StreamGeometry();
        using StreamGeometryContext gc = geo.Open();
        bool started = false;
        for (int p = 0; p <= 60; p++)
        {
            double lat = -90.0 + p * 3.0;
            (double x, double y, double z) = dir(lat, lonDeg);
            if (z <= 0)
            {
                started = false;
                continue;
            }

            Point pt = project(x, y, z);
            if (!started)
            {
                gc.BeginFigure(pt, false);
                started = true;
            }
            else
            {
                gc.LineTo(pt);
            }
        }

        context.DrawGeometry(null, pen, geo);
    }

    private void DrawParallel(
        DrawingContext context,
        double latDeg,
        Func<double, double, (double X, double Y, double Z)> dir,
        Func<double, double, double, Point> project,
        Pen pen)
    {
        var geo = new StreamGeometry();
        using StreamGeometryContext gc = geo.Open();
        bool started = false;
        for (int s = 0; s <= 90; s++)
        {
            double lon = s * 4.0;
            (double x, double y, double z) = dir(latDeg, lon);
            if (z <= 0)
            {
                started = false;
                continue;
            }

            Point pt = project(x, y, z);
            if (!started)
            {
                gc.BeginFigure(pt, false);
                started = true;
            }
            else
            {
                gc.LineTo(pt);
            }
        }

        context.DrawGeometry(null, pen, geo);
    }

    private sealed class NodeMarker
    {
        public NodeMarker(GlobeNode node) => Node = node;
        public GlobeNode Node { get; }
        public Point Position { get; set; }
        public bool IsFront { get; set; }
        public bool IsHovered { get; set; }
        public double Scale { get; set; } = 1.0;
    }

    public sealed class GlobeNode
    {
        public GlobeNode(string label, double latitudeDegrees, double longitudeDegrees, bool locked = false, string side = "", string? countryCode = null)
        {
            Label = label;
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            Locked = locked;
            Side = side;
            CountryCode = countryCode;
        }

        public string Label { get; }
        public double LatitudeDegrees { get; }
        public double LongitudeDegrees { get; }
        public bool Locked { get; }
        public string Side { get; }

        /// <summary>ISO 3166-1 alpha-2/3 country code driving the F2 border highlight. Null = none.</summary>
        public string? CountryCode { get; }
    }
}
