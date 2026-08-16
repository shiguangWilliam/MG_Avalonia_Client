using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// Tactical geospatial globe with real continent outlines: simplified coastline
/// polygons (lat/lon) are clipped against the visible hemisphere in 3D and
/// projected through a perspective camera every frame. Land is filled with a
/// translucent dark tone and stroked with a hairline; the sphere disc gets a
/// radial limb-darkening gradient for depth. Supports drag-rotate, inertia,
/// slow auto-rotation and mission nodes bound to (lat, lon).
/// </summary>
public class TacticalGlobeView : Control
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

    // Sphere render cache: re-rasterized only when the pose moves past the
    // quantization step, so auto-rotate costs a few rebuilds per second.
    private const int SphereRenderSize = 320;
    private const double PoseQuantizationDegrees = 0.6;

    private readonly List<NodeMarker> _markers = new();
    private DispatcherTimer? _timer;
    private WriteableBitmap? _sphereBitmap;
    private double _cachedYaw = double.NaN;
    private double _cachedPitch = double.NaN;
    private IBrush? _lineBrush;
    private IBrush? _mutedLineBrush;
    private IBrush? _accentBrush;
    private IBrush? _accentInverseBrush;
    private bool _dragging;
    private Point _lastPointer;
    private double _inertiaYaw;
    private DateTime _lastFrame = DateTime.UtcNow;

    public TacticalGlobeView()
    {
        ClipToBounds = true;
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

            if (Math.Abs(_inertiaYaw) > 0.05)
            {
                Yaw += _inertiaYaw * dt * 60.0;
                _inertiaYaw *= Math.Pow(0.9, dt * 60.0);
                InvalidateVisual();
            }
            else if (AutoRotateEnabled)
            {
                Yaw = (Yaw + AutoRotateDegreesPerSecond * dt) % 360.0;
                InvalidateVisual();
            }
        };
        _timer.Start();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 480.0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 480.0 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        ResolveBrushes();
        Size size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4)
            return;

        double cx = size.Width / 2.0;
        double cy = size.Height / 2.0;
        double radius = Math.Min(cx, cy) * RadiusFactor * 2.0;

        // Quantize the pose once so the rasterized texture, vector coastlines
        // and node markers all share one exact pose (and the bitmap rebuild
        // rate stays bounded during drags/auto-rotate).
        double qYaw = Math.Round(Yaw / PoseQuantizationDegrees) * PoseQuantizationDegrees;
        double qPitch = Math.Round(Pitch / PoseQuantizationDegrees) * PoseQuantizationDegrees;
        double yawRad = qYaw * Math.PI / 180.0;
        double pitchRad = qPitch * Math.PI / 180.0;
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

        // ---- Sphere: texture-mapped surface with baked-in shading ----
        WriteableBitmap? sphere = RenderSphereBitmap(yawRad, pitchRad);
        if (sphere != null)
        {
            double sr = radius * SilhouetteFactor;
            context.DrawImage(
                sphere,
                new Rect(0, 0, sphere.PixelSize.Width, sphere.PixelSize.Height),
                new Rect(cx - sr, cy - sr, sr * 2, sr * 2));
        }
        else
        {
            // Fallback while the albedo bakes: limb-darkened disc at silhouette size.
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

        // ---- Continents: hairline coastline overlay (vector, theme-... stroke) ----
        var landPen = new Pen(_mutedLineBrush ?? Brushes.SlateGray, 0.6, null, PenLineCap.Round, PenLineJoin.Round);
        foreach (double[] outline in ContinentOutlines.All)
        {
            List<(double X, double Y, double Z)>? front = ClipToHemisphere(outline, Dir);
            if (front is not { Count: > 2 })
                continue;

            var geo = new StreamGeometry();
            using (StreamGeometryContext gc = geo.Open())
            {
                gc.BeginFigure(Project(front[0].X, front[0].Y, front[0].Z), false);
                for (int i = 1; i < front.Count; i++)
                    gc.LineTo(Project(front[i].X, front[i].Y, front[i].Z));
            }

            context.DrawGeometry(null, landPen, geo);
        }

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

        // ---- Nodes ----
        RebuildMarkers();
        for (int i = 0; i < _markers.Count; i++)
        {
            NodeMarker marker = _markers[i];
            (double x, double y1, double z1) = Dir(marker.Node.LatitudeDegrees, marker.Node.LongitudeDegrees);
            bool front = z1 > 0;
            Point sp = Project(x, y1, z1);
            marker.Position = sp;
            marker.IsFront = front;
            marker.Scale = front ? 1.0 : 0.6;

            bool selected = i == SelectedNodeIndex;
            IBrush brush = marker.Node.Locked
                ? ApplyOpacity(_mutedLineBrush ?? Brushes.Gray, 0.7)
                : selected || marker.IsHovered
                    ? _accentInverseBrush ?? Brushes.OrangeRed
                    : _accentBrush ?? Brushes.Cyan;

            if (!front)
                brush = ApplyOpacity(brush, 0.30);

            // Square tactical marker with a center dot.
            double s = (selected ? 3.4 : 2.4) * marker.Scale;
            context.DrawRectangle(
                null,
                new Pen(brush, 1.0),
                new Rect(sp.X - s, sp.Y - s, s * 2, s * 2));
            context.DrawEllipse(brush, null, sp, 1.1, 1.1);

            if (selected && front)
            {
                // Pulsing bracket reticle.
                double b = s + 4.0;
                var pen = new Pen(_accentInverseBrush ?? Brushes.OrangeRed, 1.0);
                const double t = 3.2;
                DrawCorner(context, pen, sp, -b, -b, t, 1, 1);
                DrawCorner(context, pen, sp, b, -b, t, -1, 1);
                DrawCorner(context, pen, sp, -b, b, t, 1, -1);
                DrawCorner(context, pen, sp, b, b, t, -1, -1);

                var typeface = new Typeface(FontFamily.Parse("Microsoft YaHei UI, Segoe UI, Inter"));
                var formatted = new FormattedText(
                    marker.Node.Label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    _accentInverseBrush ?? Brushes.OrangeRed);
                context.DrawText(formatted, new Point(sp.X + b + 6, sp.Y - formatted.Height / 2));
            }
        }
    }

    /// <summary>
    /// Rasterizes the textured sphere into a cached bitmap: per-pixel ray/sphere
    /// intersection (exact inverse of Project), albedo sampling, N·L diffuse
    /// from a fixed key light plus limb darkening. The bitmap spans the true
    /// perspective silhouette (F/√(F²−1)) so vector overlays stay registered.
    /// Rebuilt only when the pose changes past the quantization step.
    /// </summary>
    private WriteableBitmap? RenderSphereBitmap(double yawRad, double pitchRad)
    {
        uint[] albedo;
        try
        {
            albedo = GlobeTextureBaker.Albedo;
        }
        catch
        {
            return null;
        }

        // Callers pass the quantized pose; rebuild only when it actually moves.
        if (_sphereBitmap == null || yawRad != _cachedYaw || pitchRad != _cachedPitch)
        {
            const int n = SphereRenderSize;
            _sphereBitmap ??= new WriteableBitmap(
                new PixelSize(n, n), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888);

            double cosPitch = Math.Cos(pitchRad);
            double sinPitch = Math.Sin(pitchRad);

            // Perspective silhouette radius (unit sphere, camera at F).
            double sMax = FocalFactor / Math.Sqrt(FocalFactor * FocalFactor - 1.0);
            double sMaxSq = sMax * sMax;
            double f2 = FocalFactor * FocalFactor;

            // Key light slightly off-center for a defined terminator.
            const double Lx = 0.45, Ly = 0.35, Lz = 0.82;

            using (ILockedFramebuffer fb = _sphereBitmap.Lock())
            {
                unsafe
                {
                    uint* px = (uint*)fb.Address;
                    for (int yy = 0; yy < n; yy++)
                    {
                        double v = (1.0 - 2.0 * yy / (n - 1.0)) * sMax; // +up
                        for (int xx = 0; xx < n; xx++)
                        {
                            int o = yy * n + xx;
                            double u = (2.0 * xx / (n - 1.0) - 1.0) * sMax; // +right
                            double q = u * u + v * v;
                            if (q >= sMaxSq)
                            {
                                px[o] = 0;
                                continue;
                            }

                            // Ray from camera (0,0,F) through screen point (u,v,0)
                            // with v = +up (bitmap top row ↔ camera +y):
                            // λ²(u²+v²+F²) − 2F²λ + (F²−1) = 0 → near root.
                            double a = q + f2;
                            double disc = f2 * f2 - a * (f2 - 1.0);
                            if (disc <= 0)
                            {
                                px[o] = 0;
                                continue;
                            }

                            double lambda = (f2 - Math.Sqrt(disc)) / a;
                            double sx = lambda * u;
                            double sy = lambda * v;
                            double sz = FocalFactor * (1.0 - lambda);

                            // Camera → world: invert the pitch rotation.
                            double wy = sy * cosPitch + sz * sinPitch;
                            double wz = -sy * sinPitch + sz * cosPitch;
                            double wx = sx;

                            double lon = Math.Atan2(wx, wz) - yawRad;
                            double lat = Math.Asin(Math.Clamp(wy, -1.0, 1.0));

                            int texX = (int)((lon / (2 * Math.PI) + 0.5) * GlobeTextureBaker.Width);
                            int texY = (int)((0.5 - lat / Math.PI) * GlobeTextureBaker.Height);
                            texX = ((texX % GlobeTextureBaker.Width) + GlobeTextureBaker.Width) % GlobeTextureBaker.Width;
                            texY = Math.Clamp(texY, 0, GlobeTextureBaker.Height - 1);
                            uint src = albedo[texY * GlobeTextureBaker.Width + texX];

                            // N·L on the surface normal (camera space), ambient + limb darkening.
                            double ndl = Math.Max(0.0, sx * Lx + sy * Ly + sz * Lz);
                            double lum = (0.20 + 0.95 * ndl) * (0.35 + 0.65 * sz);

                            byte br = (byte)Math.Clamp((src & 0xFF) * lum, 0, 255);
                            byte bg = (byte)Math.Clamp(((src >> 8) & 0xFF) * lum, 0, 255);
                            byte bb = (byte)Math.Clamp(((src >> 16) & 0xFF) * lum, 0, 255);
                            px[o] = (uint)(0xFFu << 24 | (uint)bb << 16 | (uint)bg << 8 | br);
                        }
                    }
                }
            }

            _cachedYaw = yawRad;
            _cachedPitch = pitchRad;
        }

        return _sphereBitmap;
    }

    /// <summary>Silhouette radius factor: the perspective sphere projects to F/√(F²−1) × radius.</summary>
    private static double SilhouetteFactor => FocalFactor / Math.Sqrt(FocalFactor * FocalFactor - 1.0);

    private static void DrawCorner(DrawingContext ctx, Pen pen, Point c, double ox, double oy, double len, int sx, int sy)
    {
        var geo = new StreamGeometry();
        using StreamGeometryContext gc = geo.Open();
        gc.BeginFigure(new Point(c.X + ox, c.Y + oy + sy * len), false);
        gc.LineTo(new Point(c.X + ox, c.Y + oy));
        gc.LineTo(new Point(c.X + ox + sx * len, c.Y + oy));
        ctx.DrawGeometry(null, pen, geo);
    }

    /// <summary>Sutherland-Hodgman clip of a lat/lon polygon against the camera-facing hemisphere.</summary>
    private static List<(double X, double Y, double Z)>? ClipToHemisphere(
        double[] outline,
        Func<double, double, (double X, double Y, double Z)> dir)
    {
        var result = new List<(double, double, double)>();
        int count = outline.Length / 2;
        (double X, double Y, double Z) prev = dir(outline[0], outline[1]);
        bool prevInside = prev.Z > 0;

        for (int i = 1; i <= count; i++)
        {
            int idx = (i % count) * 2;
            (double X, double Y, double Z) cur = dir(outline[idx], outline[idx + 1]);
            bool curInside = cur.Z > 0;

            if (curInside != prevInside)
            {
                // Interpolate to the horizon (z=0) on the unit sphere.
                double t = prev.Z / (prev.Z - cur.Z);
                double ix = prev.X + (cur.X - prev.X) * t;
                double iy = prev.Y + (cur.Y - prev.Y) * t;
                double norm = Math.Sqrt(ix * ix + iy * iy);
                if (norm > 1e-6)
                {
                    ix /= norm;
                    iy /= norm;
                }

                result.Add((ix, iy, 0.0));
            }

            if (curInside)
                result.Add(cur);

            prev = cur;
            prevInside = curInside;
        }

        return result.Count > 2 ? result : null;
    }

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

    private void ResolveBrushes()
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
    }

    private static IBrush ApplyOpacity(IBrush brush, double alpha)
    {
        if (brush is SolidColorBrush scb)
            return new SolidColorBrush(Color.FromArgb((byte)(scb.Color.A * alpha), scb.Color.R, scb.Color.G, scb.Color.B));
        return brush;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        _inertiaYaw = 0;
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

        Yaw += dx * DragSensitivity;
        Pitch = Math.Clamp(Pitch - dy * DragSensitivity * 0.6, -40.0, 40.0);
        InvalidateVisual();
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

        if (changed)
            InvalidateVisual();
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

    /// <summary>Rotates the camera toward the given node's longitude (one easing step).</summary>
    public void FocusNode(int index)
    {
        if (index < 0 || Nodes is null || index >= Nodes.Count)
            return;

        GlobeNode target = Nodes[index];
        double targetYaw = -target.LongitudeDegrees;
        double delta = ((targetYaw - Yaw + 540.0) % 360.0) - 180.0;
        Yaw += delta * 0.12;
        InvalidateVisual();
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
        public GlobeNode(string label, double latitudeDegrees, double longitudeDegrees, bool locked = false, string side = "")
        {
            Label = label;
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            Locked = locked;
            Side = side;
        }

        public string Label { get; }
        public double LatitudeDegrees { get; }
        public double LongitudeDegrees { get; }
        public bool Locked { get; }
        public string Side { get; }
    }
}
