using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ClientAvalonia.Controls;

/// <summary>
/// Lazy-load splash shown while Tactical theme assets are warmed up in the background.
/// A small accent bar sweeps back and forth; the status line can be updated by the loader.
/// </summary>
public partial class DxThemeLoadingOverlay : UserControl
{
    private const double TrackWidth = 220.0;
    private const double BarWidth = 72.0;
    private const double SweepPerSecond = 300.0;

    private DispatcherTimer? _timer;
    private double _phase;
    private bool _forward = true;
    private DateTime _lastFrame = DateTime.UtcNow;

    public DxThemeLoadingOverlay()
    {
        InitializeComponent();
    }

    public void SetStatus(string text)
    {
        if (StatusText is not null)
            StatusText.Text = text;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_timer != null)
            _timer.Tick -= OnTick;
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (LoadingBar is null)
            return;

        DateTime now = DateTime.UtcNow;
        double dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        _phase += (_forward ? 1 : -1) * SweepPerSecond * dt;
        if (_phase > TrackWidth - BarWidth)
        {
            _phase = TrackWidth - BarWidth;
            _forward = false;
        }
        else if (_phase < 0)
        {
            _phase = 0;
            _forward = true;
        }

        LoadingBar.Margin = new Thickness(_phase, 0, 0, 0);
    }
}
