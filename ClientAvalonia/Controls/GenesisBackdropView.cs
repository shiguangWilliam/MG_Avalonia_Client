using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClientAvalonia.Assets;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// "Moment of Genesis" main-menu backdrop. Fixed brand palette (charcoal / gold /
/// molten red — independent of the user accent) telling the genesis moment: a new
/// world cresting a horizon under a rotating genesis core, embers rising toward it.
/// CPU-rendered, hit-test invisible, distinct from the campaign globe.
/// </summary>
public class GenesisBackdropView : Control
{
    private const int EmberCount = 46;
    private const double CoreRadiusFactor = 0.26;
    private const double FocalFactor = 3.4;
    private const double CoreYawPerSecond = 7.0;

    // Brand palette — deliberately NOT the runtime accent: the genesis identity
    // must read the same regardless of the user's tactical accent choice.
    private static readonly Color Gold = Color.FromRgb(0xE8, 0xB4, 0x5A);
    private static readonly Color GoldBright = Color.FromRgb(0xFF, 0xD8, 0x8C);
    private static readonly Color Ember = Color.FromRgb(0xE0, 0x4A, 0x2E);
    private static readonly Color EmberDeep = Color.FromRgb(0x8C, 0x20, 0x14);

    private static readonly DodecaVertex[] DodecaVertices = BuildDodecaVertices();
    private static readonly DodecahedronEdge[] CoreEdges = BuildDodecahedronEdges();

    private readonly EmberParticle[] _embers = new EmberParticle[EmberCount];
    private readonly Random _random = new(20260815);

    private DispatcherTimer? _timer;
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _time;
    private double _coreYaw;
    private bool _animEnabled = true;

    public GenesisBackdropView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        InitEmbers();
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

        _time += dt;
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
        Size size = Bounds.Size;
        if (size.Width < 4 || size.Height < 4)
            return;

        double horizonY = size.Height * 0.72;

        // Art plate (GLM-Image) as the grounded scene; procedural layers
        // continue on top so the genesis core / orbits / embers stay alive.
        if (!DrawArtPlate(context, size))
            DrawSky(context, size, horizonY);

        // When the art plate is present it already paints the crest — skip the
        // procedural horizon arc to avoid double-drawing the molten limb.
        if (GlmAssets.GenesisHorizon is null)
            DrawHorizon(context, size, horizonY);

        DrawCore(context, size, horizonY);
        DrawOrbits(context, size, horizonY);
        if (_animEnabled)
            DrawEmbers(context, size, horizonY);
        DrawIgnitionThread(context, size, horizonY);
    }

    /// <summary>Full-bleed cover of the genesis horizon plate. Returns false when missing.</summary>
    private static bool DrawArtPlate(DrawingContext context, Size size)
    {
        Bitmap? plate = GlmAssets.GenesisHorizon;
        if (plate is null)
            return false;

        double srcW = plate.PixelSize.Width;
        double srcH = plate.PixelSize.Height;
        double scale = Math.Max(size.Width / srcW, size.Height / srcH);
        double dw = srcW * scale;
        double dh = srcH * scale;
        double dx = (size.Width - dw) / 2.0;
        double dy = (size.Height - dh) / 2.0;

        // Slight darken so procedural gold/ember overlays stay readable.
        using (context.PushOpacity(0.92))
        {
            context.DrawImage(plate, new Rect(0, 0, srcW, srcH), new Rect(dx, dy, dw, dh));
        }

        // Top vignette so the menu buttons sit on darker sky.
        var vignette = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xAA, 0x04, 0x03, 0x04), 0),
                new GradientStop(Color.FromArgb(0x00, 0x04, 0x03, 0x04), 0.45),
                new GradientStop(Color.FromArgb(0x40, 0x04, 0x03, 0x04), 1),
            },
        };
        context.DrawRectangle(vignette, null, new Rect(0, 0, size.Width, size.Height));
        return true;
    }

    /// <summary>Sky: deep charcoal dome brightening toward the crest point.</summary>
    private void DrawSky(DrawingContext context, Size size, double horizonY)
    {
        var sky = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.62, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.62, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x06, 0x05, 0x07), 0),
                new GradientStop(Color.FromRgb(0x10, 0x0C, 0x0A), 0.55),
                new GradientStop(Color.FromArgb(0x50, 0x40, 0x1C, 0x0C), 0.85),
                new GradientStop(Color.FromArgb(0x30, 0x50, 0x22, 0x0E), 1),
            },
        };
        context.DrawRectangle(sky, null, new Rect(0, 0, size.Width, horizonY));

        // Below the horizon: void with a molten under-glow.
        var below = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x60, EmberDeep.R, EmberDeep.G, EmberDeep.B), 0),
                new GradientStop(Color.FromRgb(0x05, 0x04, 0x05), 0.35),
                new GradientStop(Color.FromRgb(0x04, 0x03, 0x04), 1),
            },
        };
        context.DrawRectangle(below, null, new Rect(0, horizonY, size.Width, size.Height - horizonY));
    }

    /// <summary>
    /// The genesis horizon: a vast arc of the new world cresting into view,
    /// gold limb with molten red under-light. This is the mod's namesake image.
    /// </summary>
    private void DrawHorizon(DrawingContext context, Size size, double horizonY)
    {
        double arcRadius = size.Width * 0.85;
        double breathe = 0.5 + 0.5 * Math.Sin(_time * 0.5);

        // Molten under-light hugging the limb.
        var underRect = new Rect(0, horizonY - size.Height * 0.30, size.Width, size.Height * 0.32);
        using (context.PushClip(underRect))
        {
            var halo = new RadialGradientBrush
            {
                Center = new RelativePoint(0.62, 0.9, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(0x60 + 0x20 * breathe), Ember.R, Ember.G, Ember.B), 0),
                    new GradientStop(Color.FromArgb(0x28, EmberDeep.R, EmberDeep.G, EmberDeep.B), 0.5),
                    new GradientStop(Color.FromArgb(0x00, EmberDeep.R, EmberDeep.G, EmberDeep.B), 1),
                },
            };
            context.DrawRectangle(halo, null, underRect);
        }

        // The limb: a vast circle whose topmost point crests exactly at the
        // horizon — deterministic geometry instead of an ambiguous ArcTo.
        double crestX = size.Width * 0.62;
        var limb = new EllipseGeometry(new Rect(
            crestX - arcRadius,
            horizonY,
            arcRadius * 2,
            arcRadius * 2));

        context.DrawGeometry(null, new Pen(Solid(Gold, 0.90), 1.6), limb);
        context.DrawGeometry(null, new Pen(Solid(Ember, 0.35), 1.0), limb);
    }

    /// <summary>
    /// Genesis core: wireframe dodecahedron suspended above the crest with a
    /// molten pulsing heart — the "seed" of the new world.
    /// </summary>
    private void DrawCore(DrawingContext context, Size size, double horizonY)
    {
        double cx = size.Width * 0.62;
        double cy = size.Height * 0.42;
        double radius = Math.Min(size.Width, size.Height) * CoreRadiusFactor;
        double focal = Math.Min(size.Width, size.Height) * FocalFactor;

        double yawRad = _coreYaw * Math.PI / 180.0;
        double cosYaw = Math.Cos(yawRad);
        double sinYaw = Math.Sin(yawRad);
        double tiltRad = 18.0 * Math.PI / 180.0;
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

        double pulse = 0.5 + 0.5 * Math.Sin(_time * 1.6);

        // Molten heart: layered radial pulse in brand red.
        context.DrawEllipse(Solid(EmberDeep, 0.16 + 0.10 * pulse), null, new Point(cx, cy), radius * 0.42, radius * 0.42);
        context.DrawEllipse(Solid(Ember, 0.20 + 0.16 * pulse), null, new Point(cx, cy), radius * 0.26, radius * 0.26);
        context.DrawEllipse(Solid(GoldBright, 0.30 + 0.22 * pulse), null, new Point(cx, cy), radius * 0.11, radius * 0.11);

        // Gold wireframe over the heart; back edges dim, front edges bright.
        foreach (DodecahedronEdge edge in CoreEdges)
        {
            Point a = Project(edge.A);
            Point b = Project(edge.B);
            double depth = (edge.A.Z + edge.B.Z) / 2.0;
            bool front = depth > 0;
            double alpha = front ? 0.85 : 0.22;
            var color = front ? Gold : Color.FromRgb(0x9A, 0x7A, 0x48);
            context.DrawLine(new Pen(Solid(color, alpha), front ? 1.1 : 0.8), a, b);
        }

        // Vertex sparks: gold on the front, ember on the back — reading as the
        // frame catching light from the heart.
        foreach (DodecaVertex spark in DodecaVertices)
        {
            Point p = Project(spark);
            bool front = spark.Z > 0;
            double dotSize = front ? 1.8 : 1.1;
            context.DrawEllipse(Solid(front ? GoldBright : Ember, front ? 0.95 : 0.40), null, p, dotSize, dotSize);
        }
    }

    /// <summary>
    /// Counter-rotating orbit rings around the core — armillary-sphere energy
    /// containment, one gold, one ember, crossing above the horizon.
    /// </summary>
    private void DrawOrbits(DrawingContext context, Size size, double horizonY)
    {
        double cx = size.Width * 0.62;
        double cy = size.Height * 0.42;
        double baseR = Math.Min(size.Width, size.Height) * CoreRadiusFactor * 1.5;

        for (int i = 0; i < 2; i++)
        {
            double rx = baseR * (i == 0 ? 1.0 : 0.82);
            double ry = rx * (i == 0 ? 0.30 : 0.24);
            double angleDeg = _time * (i == 0 ? 12.0 : -8.0) + (i == 0 ? 0 : 40);
            double rad = angleDeg * Math.PI / 180.0;

            var ellipse = new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
            Matrix rotateAboutCenter =
                Matrix.CreateTranslation(cx, cy)
                * Matrix.CreateRotation(rad)
                * Matrix.CreateTranslation(-cx, -cy);
            using (context.PushTransform(rotateAboutCenter))
            {
                context.DrawGeometry(null, new Pen(Solid(i == 0 ? Gold : Ember, 0.34), 1.0), ellipse);
            }

            // Travelling node on each ring: a bright spark at the parameterized head.
            // Track the rotated ellipse: parametrize in ring-local coords, then rotate.
            double theta = _time * (i == 0 ? 1.1 : -0.8);
            double lx = rx * Math.Cos(theta);
            double ly = ry * Math.Sin(theta);
            double headX = cx + lx * Math.Cos(rad) - ly * Math.Sin(rad);
            double headY = cy + lx * Math.Sin(rad) + ly * Math.Cos(rad);
            Point head = new(headX, headY);
            context.DrawEllipse(Solid(i == 0 ? GoldBright : Ember, 0.9), null, head, 2.0, 2.0);
        }
    }

    /// <summary>Embers drifting up from the horizon toward the core.</summary>
    private void DrawEmbers(DrawingContext context, Size size, double horizonY)
    {
        foreach (EmberParticle ember in _embers)
        {
            double cycle = (_time * ember.RiseSpeed + ember.Phase) % 1.0;
            double y = horizonY - cycle * (horizonY - size.Height * 0.16);
            double spread = Math.Sin(cycle * Math.PI) * ember.Drift * size.Width * 0.06;
            double x = size.Width * ember.X + spread;
            double alpha = Math.Sin(cycle * Math.PI) * ember.Brightness;
            double dot = ember.Size * (1.0 - 0.4 * cycle);

            bool nearCore = ember.Hue == 0 && cycle > 0.75;
            Color c = ember.Hue == 0 ? Gold : Ember;
            if (nearCore)
                c = GoldBright;

            context.DrawEllipse(Solid(c, alpha * 0.8), null, new Point(x, y), dot, dot);
        }
    }

    /// <summary>
    /// Ignition thread: a single vertical energy filament from the horizon crest
    /// up to the core — the moment of ignition made visible. Breathing slowly.
    /// </summary>
    private void DrawIgnitionThread(DrawingContext context, Size size, double horizonY)
    {
        double x = size.Width * 0.62;
        double top = size.Height * 0.42 + Math.Min(size.Width, size.Height) * CoreRadiusFactor * 0.5;
        double breathe = 0.5 + 0.5 * Math.Sin(_time * 0.9);

        var thread = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, Gold.R, Gold.G, Gold.B), 0),
                new GradientStop(Color.FromArgb((byte)(0x60 + 0x30 * breathe), Gold.R, Gold.G, Gold.B), 0.5),
                new GradientStop(Color.FromArgb((byte)(0x30 + 0x20 * breathe), GoldBright.R, GoldBright.G, GoldBright.B), 0.8),
                new GradientStop(Color.FromArgb(0x00, Gold.R, Gold.G, Gold.B), 1),
            },
        };
        double halfWidth = 1.5 + breathe;
        context.DrawRectangle(thread, null, new Rect(x - halfWidth, top, halfWidth * 2, horizonY - top));
    }

    private void InitEmbers()
    {
        for (int i = 0; i < _embers.Length; i++)
        {
            _embers[i] = new EmberParticle(
                0.30 + _random.NextDouble() * 0.65,
                _random.NextDouble(),
                0.6 + _random.NextDouble() * 0.9,
                0.5 + _random.NextDouble() * 1.8,
                0.4 + _random.NextDouble() * 0.6,
                0.8 + _random.NextDouble() * 1.6,
                _random.Next(0, 2));
        }
    }

    private static SolidColorBrush Solid(Color color, double alpha)
        => new(Color.FromArgb((byte)(color.A * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B));

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

    private sealed class EmberParticle
    {
        public EmberParticle(double x, double phase, double riseSpeed, double drift, double brightness, double size, int hue)
        {
            X = x;
            Phase = phase;
            RiseSpeed = riseSpeed;
            Drift = drift;
            Brightness = brightness;
            Size = size;
            Hue = hue;
        }

        public double X { get; }
        public double Phase { get; }
        public double RiseSpeed { get; }
        public double Drift { get; }
        public double Brightness { get; }
        public double Size { get; }
        public int Hue { get; }
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
