using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// "Moment of Genesis" main-menu dynamic backdrop: 3-layer parallax starfield in
/// gold/white plus a slowly rotating wireframe dodecahedron ("genesis core") on the
/// right side. CPU-rendered, hit-test invisible, distinct from the campaign globe.
/// </summary>
public class GenesisBackdropView : Control
{
    private const int StarCountFar = 90;
    private const int StarCountMid = 50;
    private const int StarCountNear = 24;
    private const double CoreRadiusFactor = 0.30;
    private const double FocalFactor = 3.4;
    private const double CoreYawPerSecond = 6.0;

    private static readonly DodecaVertex[] DodecaVertices = BuildDodecaVertices();
    private static readonly DodecahedronEdge[] CoreEdges = BuildDodecahedronEdges();

    private readonly Star[] _starsFar = new Star[StarCountFar];
    private readonly Star[] _starsMid = new Star[StarCountMid];
    private readonly Star[] _starsNear = new Star[StarCountNear];
    private readonly Random _random = new(20260815);

    private DispatcherTimer? _timer;
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _starTime;
    private double _coreYaw;
    private IBrush? _goldBrush;
    private IBrush? _goldSoftBrush;
    private bool _animEnabled = true;

    public GenesisBackdropView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        InitStars(_starsFar, 0.8, 1.4);
        InitStars(_starsMid, 1.4, 2.0);
        InitStars(_starsNear, 2.0, 2.8);
    }

    private bool AnimationsEnabled
    {
        get
        {
            try
            {
                return UserINISettings.Instance.UiAnimationsEnabled.Value;
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
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33),
            };
            _timer.Tick += (_, _) => Tick();
        }

        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void Tick()
    {
        DateTime now = DateTime.UtcNow;
        double dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        _animEnabled = AnimationsEnabled;
        if (!_animEnabled)
            return;

        _starTime += dt;
        _coreYaw = (_coreYaw + CoreYawPerSecond * dt) % 360.0;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 1280.0 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 720.0 : availableSize.Height;
        return new Size(w, h);
    }

    public override void Render(DrawingContext context)
    {
        ResolveBrushes();
        Size size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4)
            return;

        // Base vignette: near-black with slightly lighter center.
        var baseFill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x08, 0x08, 0x0A), 0),
                new GradientStop(Color.FromRgb(0x0D, 0x0D, 0x11), 0.5),
                new GradientStop(Color.FromRgb(0x05, 0x05, 0x07), 1),
            },
        };
        context.DrawRectangle(baseFill, null, new Rect(0, 0, size.Width, size.Height));

        // Starfield (3 parallax layers). When animations are off, layers freeze in place.
        if (_animEnabled)
        {
            DrawStarLayer(context, _starsFar, 2.5, 0.45);
            DrawStarLayer(context, _starsMid, 5.5, 0.6);
            DrawStarLayer(context, _starsNear, 10.0, 0.8);
        }
        else
        {
            DrawStarLayer(context, _starsFar, 0, 0.45);
            DrawStarLayer(context, _starsMid, 0, 0.6);
            DrawStarLayer(context, _starsNear, 0, 0.8);
        }

        DrawCore(context, size);

        if (_animEnabled)
        {
            double phase = (_starTime % 4.0) / 4.0;
            DrawScanRing(context, size, phase);
        }
    }

    private void DrawStarLayer(DrawingContext context, Star[] stars, double driftPixelsPerSecond, double alpha)
    {
        Size size = Bounds.Size;
        foreach (Star star in stars)
        {
            double x = (star.X * size.Width + driftPixelsPerSecond * _starTime) % size.Width;
            double y = (star.Y * size.Height + driftPixelsPerSecond * 0.15 * _starTime) % size.Height;
            context.DrawEllipse(ApplyAlpha(_goldSoftBrush ?? Brushes.Gold, star.Brightness * alpha), null, new Point(x, y), star.Size, star.Size);
        }
    }

    private void DrawCore(DrawingContext context, Size size)
    {
        double cx = size.Width * 0.68;
        double cy = size.Height * 0.46;
        double radius = Math.Min(size.Width, size.Height) * CoreRadiusFactor;
        double focal = Math.Min(size.Width, size.Height) * FocalFactor;

        double yawRad = _coreYaw * Math.PI / 180.0;
        double cosYaw = Math.Cos(yawRad);
        double sinYaw = Math.Sin(yawRad);
        double tiltRad = 22.0 * Math.PI / 180.0;
        double cosTilt = Math.Cos(tiltRad);
        double sinTilt = Math.Sin(tiltRad);

        Point Project(DodecaVertex v)
        {
            double x = v.X * cosYaw - v.Z * sinYaw;
            double z = v.X * sinYaw + v.Z * cosYaw;
            double y = v.Y * cosTilt - z * sinTilt;
            z = v.Y * sinTilt + z * cosTilt;
            double scale = focal / (focal - z * radius);
            return new Point(cx + x * radius * scale, cy - y * radius * scale);
        }

        IBrush brush = _goldBrush ?? Brushes.Gold;
        foreach (DodecahedronEdge edge in CoreEdges)
        {
            Point a = Project(edge.A);
            Point b = Project(edge.B);
            double depth = (edge.A.Z + edge.B.Z) / 2.0;
            double alpha = depth > 0 ? 0.9 : 0.3;
            context.DrawLine(new Pen(ApplyAlpha(brush, alpha), depth > 0 ? 1.2 : 0.8), a, b);
        }

        foreach (DodecaVertex spark in DodecaVertices)
        {
            Point p = Project(spark);
            context.DrawEllipse(ApplyAlpha(_goldBrush ?? Brushes.Gold, spark.Z > 0 ? 0.95 : 0.4), null, p, 1.8, 1.8);
        }
    }

    private void DrawScanRing(DrawingContext context, Size size, double phase)
    {
        double cx = size.Width * 0.68;
        double cy = size.Height * 0.46;
        double baseR = Math.Min(size.Width, size.Height) * CoreRadiusFactor * 0.6;
        double r = baseR + phase * Math.Min(size.Width, size.Height) * 0.22;
        double alpha = Math.Max(0.0, 0.28 * (1.0 - phase));
        context.DrawEllipse(null, new Pen(ApplyAlpha(_goldBrush ?? Brushes.Gold, alpha), 1.0), new Point(cx, cy), r, r);
    }

    private void ResolveBrushes()
    {
        _goldBrush = this.TryFindResource("DxAccentPrimaryBrush", out object? accentObj) && accentObj is IBrush accent
            ? accent
            : Brushes.Gold;

        _goldSoftBrush = this.TryFindResource("DxAccentSoftBrush", out object? softObj) && softObj is IBrush soft
            ? soft
            : Brushes.Gold;
    }

    private void InitStars(Star[] stars, double minSize, double maxSize)
    {
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = new Star(
                _random.NextDouble(),
                _random.NextDouble(),
                minSize + _random.NextDouble() * (maxSize - minSize),
                0.4 + _random.NextDouble() * 0.6);
        }
    }

    private static IBrush ApplyAlpha(IBrush brush, double alpha)
    {
        if (brush is SolidColorBrush scb)
            return new SolidColorBrush(Color.FromArgb((byte)(scb.Color.A * Math.Clamp(alpha, 0, 1)), scb.Color.R, scb.Color.G, scb.Color.B));
        return brush;
    }

    /// <summary>
    /// Regular dodecahedron vertices on a unit sphere: 4 cube-style corners plus
    /// 8 scaled axis points derived from the golden ratio.
    /// </summary>
    private static DodecaVertex[] BuildDodecaVertices()
    {
        const double b = 0.61803398875; // 1/phi
        const double c = 1.61803398875; // phi

        return new[]
        {
            new DodecaVertex(+1, +1, +1),
            new DodecaVertex(+1, +1, -1),
            new DodecaVertex(+1, -1, +1),
            new DodecaVertex(+1, -1, -1),
            new DodecaVertex(-1, +1, +1),
            new DodecaVertex(-1, +1, -1),
            new DodecaVertex(-1, -1, +1),
            new DodecaVertex(-1, -1, -1),
            new DodecaVertex(0, +b, +c),
            new DodecaVertex(0, +b, -c),
            new DodecaVertex(0, -b, +c),
            new DodecaVertex(0, -b, -c),
            new DodecaVertex(+b, +c, 0),
            new DodecaVertex(+b, -c, 0),
            new DodecaVertex(-b, +c, 0),
            new DodecaVertex(-b, -c, 0),
            new DodecaVertex(+c, 0, +b),
            new DodecaVertex(+c, 0, -b),
            new DodecaVertex(-c, 0, +b),
            new DodecaVertex(-c, 0, -b),
        };
    }

    private static DodecahedronEdge[] BuildDodecahedronEdges()
    {
        var edges = new List<DodecahedronEdge>();
        for (int i = 0; i < DodecaVertices.Length; i++)
        {
            for (int j = i + 1; j < DodecaVertices.Length; j++)
            {
                double dx = DodecaVertices[i].X - DodecaVertices[j].X;
                double dy = DodecaVertices[i].Y - DodecaVertices[j].Y;
                double dz = DodecaVertices[i].Z - DodecaVertices[j].Z;
                double distSq = dx * dx + dy * dy + dz * dz;
                // Edge length = 2/phi ≈ 1.236 → distSq ≈ 1.528; use tolerance window.
                if (distSq > 1.3 && distSq < 1.8)
                {
                    edges.Add(new DodecahedronEdge(DodecaVertices[i], DodecaVertices[j]));
                }
            }
        }

        return edges.ToArray();
    }

    private sealed class Star
    {
        public Star(double x, double y, double size, double brightness)
        {
            X = x;
            Y = y;
            Size = size;
            Brightness = brightness;
        }

        public double X { get; }
        public double Y { get; }
        public double Size { get; }
        public double Brightness { get; }
    }

    private sealed class DodecaVertex
    {
        public DodecaVertex(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    private sealed class DodecahedronEdge
    {
        public DodecahedronEdge(DodecaVertex a, DodecaVertex b)
        {
            A = a;
            B = b;
        }

        public DodecaVertex A { get; }
        public DodecaVertex B { get; }
    }
}
