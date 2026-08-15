using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// L1 wireframe globe: lat/long grid projected through a perspective camera on the CPU,
/// rendered with Avalonia Path primitives. Supports drag-rotate, slow auto-rotation and
/// mission nodes bound to (lat, lon) coordinates.
/// </summary>
public class TacticalGlobeView : Control
{
    public static readonly StyledProperty<IList<GlobeNode>> NodesProperty =
        AvaloniaProperty.Register<TacticalGlobeView, IList<GlobeNode>>(nameof(Nodes), new List<GlobeNode>());

    public static readonly StyledProperty<int> SelectedNodeIndexProperty =
        AvaloniaProperty.Register<TacticalGlobeView, int>(nameof(SelectedNodeIndex), -1);

    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<TacticalGlobeView, double>(nameof(Yaw), 0.0);

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<TacticalGlobeView, double>(nameof(Pitch), -18.0);

    private const int Meridians = 20;
    private const int Parallels = 12;
    private const double RadiusFactor = 0.40;
    private const double FocalFactor = 3.2;
    private const double BackSideAlpha = 0.22;
    private const double DragSensitivity = 0.32;
    private const double AutoRotateDegreesPerSecond = 3.0;

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
        double focal = Math.Min(cx, cy) * FocalFactor;

        double yawRad = Yaw * Math.PI / 180.0;
        double pitchRad = Pitch * Math.PI / 180.0;
        double cosYaw = Math.Cos(yawRad);
        double sinYaw = Math.Sin(yawRad);
        double cosPitch = Math.Cos(pitchRad);
        double sinPitch = Math.Sin(pitchRad);

        // Camera sits at +Z looking at origin; perspective divide by camera-space z.
        (double sx, double sy, double cz, bool front) Project(double latDeg, double lonDeg)
        {
            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;

            double x = Math.Cos(lat) * Math.Sin(lon + yawRad);
            double y = Math.Sin(lat);
            double z = Math.Cos(lat) * Math.Cos(lon + yawRad);

            // Pitch rotation around X axis.
            double y1 = y * cosPitch - z * sinPitch;
            double z1 = y * sinPitch + z * cosPitch;

            bool isFront = z1 > 0;
            double scale = focal / (focal - z1 * radius);
            return (cx + x * radius * scale, cy - y1 * radius * scale, z1, isFront);
        }

        // Meridians (longitude lines).
        for (int m = 0; m < Meridians; m++)
        {
            var front = new StreamGeometry();
            var back = new StreamGeometry();
            using (StreamGeometryContext fc = front.Open())
            using (StreamGeometryContext bc = back.Open())
            {
                bool frontStarted = false;
                bool backStarted = false;
                for (int p = 0; p <= 90; p++)
                {
                    double lat = -90.0 + p * (180.0 / 90);
                    (double sx, double sy, double cz, bool isFront) = Project(lat, m * (360.0 / Meridians));
                    if (isFront)
                    {
                        if (!frontStarted) { fc.BeginFigure(new Point(sx, sy), false); frontStarted = true; }
                        else fc.LineTo(new Point(sx, sy));
                    }
                    else
                    {
                        if (!backStarted) { bc.BeginFigure(new Point(sx, sy), false); backStarted = true; }
                        else bc.LineTo(new Point(sx, sy));
                    }
                }
            }

            DrawPolylines(context, front, back, m == 0, _lineBrush, _mutedLineBrush);
        }

        // Parallels (latitude lines).
        for (int p = 1; p < Parallels; p++)
        {
            double lat = -90.0 + p * (180.0 / Parallels);
            var front = new StreamGeometry();
            var back = new StreamGeometry();
            using (StreamGeometryContext fc = front.Open())
            using (StreamGeometryContext bc = back.Open())
            {
                bool frontStarted = false;
                bool backStarted = false;
                for (int s = 0; s <= 120; s++)
                {
                    double lon = s * (360.0 / 120);
                    (double sx, double sy, double cz, bool isFront) = Project(lat, lon);
                    if (isFront)
                    {
                        if (!frontStarted) { fc.BeginFigure(new Point(sx, sy), false); frontStarted = true; }
                        else fc.LineTo(new Point(sx, sy));
                    }
                    else
                    {
                        if (!backStarted) { bc.BeginFigure(new Point(sx, sy), false); backStarted = true; }
                        else bc.LineTo(new Point(sx, sy));
                    }
                }
            }

            DrawPolylines(context, front, back, p == Parallels / 2, _lineBrush, _mutedLineBrush);
        }

        // Nodes.
        RebuildMarkers();
        for (int i = 0; i < _markers.Count; i++)
        {
            NodeMarker marker = _markers[i];
            (double sx, double sy, double cz, bool front) = Project(marker.Node.LatitudeDegrees, marker.Node.LongitudeDegrees);
            marker.Position = new Point(sx, sy);
            marker.IsFront = front;
            marker.Scale = front ? 1.0 : 0.6;

            IBrush brush = marker.Node.Locked
                ? (_mutedLineBrush ?? Brushes.DarkGray)
                : i == SelectedNodeIndex || marker.IsHovered
                    ? (_accentInverseBrush ?? Brushes.OrangeRed)
                    : (_accentBrush ?? Brushes.Cyan);

            if (!front)
                brush = ApplyOpacity(brush, 0.35);

            double nodeRadius = (i == SelectedNodeIndex ? 3.6 : 2.6) * marker.Scale;
            context.DrawEllipse(brush, null, new Point(sx, sy), nodeRadius, nodeRadius);

            if (i == SelectedNodeIndex && front)
            {
                // Pulsing selection ring.
                context.DrawEllipse(null, new Pen(_accentInverseBrush ?? Brushes.OrangeRed, 1.2), new Point(sx, sy), 6.5, 6.5);
            }
        }
    }

    private static void DrawPolylines(DrawingContext context, StreamGeometry front, StreamGeometry back, bool highlight, IBrush? lineBrush, IBrush? mutedLineBrush)
    {
        IBrush frontBrush = lineBrush ?? Brushes.Silver;
        IBrush backBrush = mutedLineBrush ?? Brushes.Gray;

        context.DrawGeometry(null, new Pen(backBrush, 1.0, null, PenLineCap.Round), back);
        context.DrawGeometry(null, new Pen(highlight ? frontBrush : ApplyOpacity(frontBrush, 0.8), highlight ? 1.2 : 1.0, null, PenLineCap.Round), front);
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
        _lastPointer = pos;

        Yaw += dx * DragSensitivity;
        Pitch = Math.Clamp(Pitch - dy * DragSensitivity, -35.0, 35.0);
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
        for (int i = 0; i < _markers.Count; i++)
            _markers[i].IsHovered = i == hit;
        if (hit >= 0)
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
