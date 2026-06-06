using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClientAvalonia.Views;

/// <summary>Auto-hiding wrapper for <see cref="CnCNetTopBar"/> (XNA TopBar slide behavior).</summary>
public partial class CnCNetTopBarHost : UserControl
{
    private const double BarHeight = 36;
    private const double HiddenOffset = -BarHeight;
    private const double RevealZoneHeight = 8;
    private static readonly TimeSpan HoldAfterLeave = TimeSpan.FromSeconds(1.2);

    private readonly TranslateTransform _transform = new();
    private readonly DispatcherTimer _animTimer;
    private Window? _window;
    private bool _active;
    private double _offsetY = HiddenOffset;
    private double _lastPointerY = 999;
    private DateTime _holdUntil = DateTime.MinValue;

    public CnCNetTopBarHost()
    {
        InitializeComponent();
        PART_Bar.RenderTransform = _transform;
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;

        PART_RevealZone.PointerEntered += (_, _) => RequestShow();
        PART_Bar.PointerEntered += (_, _) => RequestShow();
    }

    public CnCNetTopBar Bar => PART_Bar;

    public void Activate(Window window)
    {
        if (_active && _window == window)
            return;

        if (_window != null)
            DetachWindowHandlers();

        _window = window;
        _active = true;
        _offsetY = HiddenOffset;
        _lastPointerY = 999;
        _holdUntil = DateTime.MinValue;
        ApplyOffset();

        window.PointerMoved += OnWindowPointerMoved;
        window.PointerExited += OnWindowPointerExited;

        IsVisible = true;
        if (!_animTimer.IsEnabled)
            _animTimer.Start();
    }

    public void Deactivate()
    {
        _active = false;
        _animTimer.Stop();
        DetachWindowHandlers();
        _window = null;
        _offsetY = HiddenOffset;
        ApplyOffset();
        IsVisible = false;
    }

    private void DetachWindowHandlers()
    {
        if (_window == null)
            return;

        _window.PointerMoved -= OnWindowPointerMoved;
        _window.PointerExited -= OnWindowPointerExited;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_active || _window == null)
            return;

        _lastPointerY = e.GetCurrentPoint(_window).Position.Y;
        if (ShouldKeepVisible(_lastPointerY))
            RequestShow();
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs e)
    {
        _lastPointerY = 999;
        _holdUntil = DateTime.MinValue;
    }

    private void RequestShow()
    {
        _holdUntil = DateTime.UtcNow + HoldAfterLeave;
    }

    private bool ShouldKeepVisible(double pointerY)
    {
        if (pointerY <= RevealZoneHeight)
            return true;

        return _offsetY > HiddenOffset + 4 && pointerY <= BarHeight + 6;
    }

    private bool IsShowRequested()
    {
        if (ShouldKeepVisible(_lastPointerY))
            return true;

        return DateTime.UtcNow < _holdUntil;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (!_active)
            return;

        double target = IsShowRequested() ? 0 : HiddenOffset;
        double step = (target - _offsetY) * 0.22;
        if (Math.Abs(step) < 0.35)
            _offsetY = target;
        else
            _offsetY += step;

        ApplyOffset();
    }

    private void ApplyOffset()
    {
        _transform.Y = _offsetY;
        bool barVisible = _offsetY > HiddenOffset + 4;
        PART_Bar.IsHitTestVisible = barVisible;
        PART_RevealZone.IsHitTestVisible = _active;
        IsHitTestVisible = _active;
        Height = barVisible ? BarHeight : RevealZoneHeight;
    }
}
