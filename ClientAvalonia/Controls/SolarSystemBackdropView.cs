using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ClientCore;

namespace ClientAvalonia.Controls;

/// <summary>
/// Host panel for the persistent 3D solar-system backdrop. Owns the render
/// clock (33ms) and the animations-enabled semantics: when animations are off
/// the scene freezes but pose navigation still snaps (one frame renders per
/// navigation). Hit-test invisible; lives behind every panel in MainWindow.
/// </summary>
public class SolarSystemBackdropView : Panel
{
    private readonly SolarSystemGlControl _gl = new();
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTicks;
    private bool _animationsEnabled = true;

    public SolarSystemBackdropView()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        _gl.IsHitTestVisible = false;
        Children.Add(_gl);
    }

    /// <summary>Scene accessor for the director (pose navigation, earth bridge).</summary>
    internal SolarSystemGlControl Gl => _gl;

    /// <summary>Forces one rendered frame even with animations disabled (pose snap).</summary>
    public void RenderOnce() => _gl.Tick(0.0);

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

        _lastTicks = _clock.ElapsedTicks;
        _timer.Start();
        RenderOnce();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void Tick()
    {
        long now = _clock.ElapsedTicks;
        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        _animationsEnabled = AnimationsEnabled;
        if (!_animationsEnabled)
        {
            // Keep the camera blend alive (navigation still needs frames) but
            // freeze orbital motion: advance pose time only.
            if (_gl.Scene.IsPoseAnimating)
                _gl.Tick(dt);
            SolarSystemDirector.OnBackdropTick(dt);
            return;
        }

        _gl.Tick(dt);
        SolarSystemDirector.OnBackdropTick(dt);
    }
}
