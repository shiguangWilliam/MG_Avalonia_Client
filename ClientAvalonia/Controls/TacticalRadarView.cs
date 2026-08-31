using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClientCore;
using Application = Avalonia.Application;

namespace ClientAvalonia.Controls;

/// <summary>
/// Decorative tactical radar for the Tactical main-menu console: rotating sweep
/// sector, cross hairs, range rings and drifting noise blips. Purely ambient —
/// it renders no real data, takes no input and must never overlap the genesis
/// core. Uses the user-configurable tactical accent (unlike the genesis layer's
/// fixed gold/molten brand palette).
/// </summary>
public class TacticalRadarView : Control
{
    private const double SweepDegPerSecond = 42.0;
    private const int BlipCount = 7;

    private sealed class Blip
    {
        public double AngleDeg;
        public double RadiusFactor;
        public double Phase;
        public double Speed;
    }

    private readonly Blip[] _blips = new Blip[BlipCount];
    private readonly Random _random = new(20260817);

    private DispatcherTimer? _timer;
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _sweepDeg;
    private double _time;

    public TacticalRadarView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;

        for (int i = 0; i < BlipCount; i++)
        {
            _blips[i] = new Blip
            {
                AngleDeg = _random.NextDouble() * 360.0,
                RadiusFactor = 0.25 + _random.NextDouble() * 0.68,
                Phase = _random.NextDouble() * Math.PI * 2.0,
                Speed = 0.2 + _random.NextDouble() * 0.5,
            };
        }
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

    private Color Accent
    {
        get
        {
            if (Application.Current?.TryFindResource("DxAccentPrimaryBrush", out object? value) == true
                && value is ISolidColorBrush solid)
                return solid.Color;
            return Colors.Teal;
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

        if (!AnimationsEnabled)
            return; // frozen frame — keep the last sweep position

        _time += dt;
        _sweepDeg = (_sweepDeg + SweepDegPerSecond * dt) % 360.0;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? 148.0
            : Math.Min(availableSize.Width, availableSize.Height);
        return new Size(Math.Max(side, 64.0), Math.Max(side, 64.0));
    }

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        if (size.Width < 16 || size.Height < 16)
            return;

        double cx = size.Width / 2.0;
        double cy = size.Height / 2.0;
        double radius = Math.Min(cx, cy) - 2.0;

        Color accent = Accent;
        var framePen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)), 1.0);
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)), 1.0);

        // Frame + range rings.
        context.DrawEllipse(Brushes.Transparent, framePen, new Point(cx, cy), radius, radius);
        context.DrawEllipse(Brushes.Transparent, ringPen, new Point(cx, cy), radius * 0.66, radius * 0.66);
        context.DrawEllipse(Brushes.Transparent, ringPen, new Point(cx, cy), radius * 0.33, radius * 0.33);

        // Cross hairs.
        context.DrawLine(ringPen, new Point(cx - radius, cy), new Point(cx + radius, cy));
        context.DrawLine(ringPen, new Point(cx, cy - radius), new Point(cx, cy + radius));

        // Sweep sector (conic approximation via gradient stroke arc).
        double sweep = Math.PI * _sweepDeg / 180.0;
        var sweepGeometry = new StreamGeometry();
        using (StreamGeometryContext ctx = sweepGeometry.Open())
        {
            ctx.BeginFigure(new Point(cx, cy), true);
            for (int d = 0; d <= 26; d++)
            {
                double a = sweep - (d / 26.0) * (Math.PI / 3.2);
                ctx.LineTo(new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius));
            }

            ctx.EndFigure(true);
        }

        var sweepBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x55, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0x08, accent.R, accent.G, accent.B), 1),
            },
        };
        context.DrawGeometry(sweepBrush, null, sweepGeometry);

        // Sweep leading edge.
        var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(0xB4, accent.R, accent.G, accent.B)), 1.2);
        context.DrawLine(edgePen, new Point(cx, cy), new Point(cx + Math.Cos(sweep) * radius, cy + Math.Sin(sweep) * radius));

        // Noise blips with slow drift + fade pulse.
        foreach (Blip blip in _blips)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(_time * blip.Speed + blip.Phase);
            byte alpha = (byte)(40 + pulse * 130);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)),
                null,
                new Point(cx + Math.Cos(blip.AngleDeg * Math.PI / 180.0) * blip.RadiusFactor * radius,
                          cy + Math.Sin(blip.AngleDeg * Math.PI / 180.0) * blip.RadiusFactor * radius),
                1.6,
                1.6);
        }

        // Center dot.
        context.DrawEllipse(new SolidColorBrush(accent), null, new Point(cx, cy), 1.5, 1.5);
    }
}
