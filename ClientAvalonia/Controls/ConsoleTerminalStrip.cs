using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClientCore;
using Application = Avalonia.Application;

namespace ClientAvalonia.Controls;

/// <summary>
/// Decorative command-console strip for the Tactical main menu: monospace boot /
/// status log lines scrolling in, a read-only prompt line with a blinking cursor.
/// Purely ambient — no command parsing, no focus, no input. Real client state
/// (version / online count) can be injected via <see cref="PushStatus"/> so the
/// log is not pure fiction.
/// </summary>
public class ConsoleTerminalStrip : Control
{
    private const int MaxLines = 5;
    private const double LineHeight = 13.0;
    private const double BlinkSeconds = 1.0;

    private static readonly string[] BootSequence =
    {
        "GENESIS TERMINAL v2.12 — BOOT OK",
        "LINK :: CNCNET RELAY ....... STABLE",
        "MAP SAT-LINK ............... ACQUIRED",
        "GENESIS CORE ............... NOMINAL",
        "TACTICAL MAP DATABASE ...... SYNCED",
        "AWAITING COMMANDER ORDERS _",
    };

    private readonly List<(string Text, bool Dim)> _lines = new();
    private readonly Queue<string> _pending = new();

    private DispatcherTimer? _timer;
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _time;
    private double _bootTimer;
    private int _bootIndex;
    private bool _bootDone;

    public ConsoleTerminalStrip()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;

        foreach (string line in BootSequence)
            _pending.Enqueue(line);
    }

    /// <summary>Appends a real status line (version / online count refreshes).</summary>
    public void PushStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _lines.Add((text.Trim(), false));
        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);

        InvalidateVisual();
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

    private static Typeface MonoTypeface => new("Cascadia Mono, Consolas, Courier New, monospace");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(66),
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
            return; // freeze blinking cursor / typewriter; log stays readable

        _time += dt;

        // Reveal one boot line every ~0.55s until exhausted.
        if (!_bootDone)
        {
            _bootTimer += dt;
            if (_bootTimer >= 0.55)
            {
                _bootTimer = 0;
                if (_pending.Count > 0)
                {
                    _lines.Add((_pending.Dequeue(), _bootIndex > 0 && _bootIndex % 2 == 1));
                    while (_lines.Count > MaxLines)
                        _lines.RemoveAt(0);
                    _bootIndex++;
                    InvalidateVisual();
                }
                else
                {
                    _bootDone = true;
                }
            }
        }

        // Cursor blink repaint.
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 420.0 : availableSize.Width;
        return new Size(width, LineHeight * MaxLines + 10.0);
    }

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        if (size.Width < 10)
            return;

        Color accent = Accent;
        var primary = new SolidColorBrush(Color.FromArgb(0xD8, accent.R, accent.G, accent.B));
        var dim = new SolidColorBrush(Color.FromArgb(0x78, accent.R, accent.G, accent.B));

        double y = 4.0;
        foreach ((string text, bool isDim) in _lines)
        {
            var formatted = new FormattedText(
                "> " + text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                MonoTypeface,
                10.5,
                isDim ? dim : primary)
            {
                MaxTextWidth = size.Width,
            };
            context.DrawText(formatted, new Point(6.0, y));
            y += LineHeight;
        }

        // Read-only prompt line with blinking cursor.
        bool blinkOn = !AnimationsEnabled || Math.Floor(_time / BlinkSeconds) % 2.0 == 0.0;
        var prompt = new FormattedText(
            "CMD://MAINMENU",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            10.5,
            dim);
        context.DrawText(prompt, new Point(6.0, y));

        if (blinkOn)
        {
            double cursorX = 6.0 + prompt.Width + 3.0;
            context.DrawRectangle(primary, null, new Rect(cursorX, y + 2.0, 6.0, LineHeight - 5.0));
        }
    }
}
