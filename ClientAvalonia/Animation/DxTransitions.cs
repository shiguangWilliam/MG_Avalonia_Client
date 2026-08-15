using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ClientCore;

namespace ClientAvalonia.Animation;

public enum SlideDirection
{
    FromRight,
    FromLeft,
    FromBottom,
}

/// <summary>
/// Cross-panel transition helpers. Every method is fail-soft: if animations are
/// disabled or the control is detached, the swap runs synchronously instead.
/// </summary>
public static class DxTransitions
{
    private static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ThemeDuration = TimeSpan.FromMilliseconds(350);

    private static readonly ConditionalWeakTable<Animatable, CancellationTokenSource> ActiveTokens = new();

    public static bool Enabled
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

    /// <summary>Old content fades out, swap runs, new content fades in.</summary>
    public static void FadeSwap(Animatable host, Action swapContent)
        => RunSwap(host, swapContent,
            (c, token) => FadeAsync(c, 1.0, 0.0, PanelDuration, token),
            (c, token) => FadeAsync(c, 0.0, 1.0, PanelDuration, token));

    /// <summary>New content fades in with a slide from the given direction (panel navigation).</summary>
    public static void SlideSwap(Animatable host, Action swapContent, SlideDirection direction = SlideDirection.FromRight)
        => RunSwap(host, swapContent,
            (c, token) => FadeAsync(c, 1.0, 0.0, PanelDuration, token),
            (c, token) => SlideInAsync(c, direction, PanelDuration, token));

    /// <summary>Theme switch transition for the whole window content.</summary>
    public static void ThemeSwap(Animatable host, Action applyTheme)
        => RunSwap(host, applyTheme,
            (c, token) => FadeAsync(c, 1.0, 0.0, ThemeDuration, token),
            (c, token) => FadeAsync(c, 0.0, 1.0, ThemeDuration, token));

    private static async void RunSwap(
        Animatable host,
        Action swap,
        Func<Animatable, CancellationToken, Task> outAnim,
        Func<Animatable, CancellationToken, Task> inAnim)
    {
        if (host is not Control control || !Enabled)
        {
            swap();
            return;
        }

        CancellationTokenSource cts = CancelActive(host);
        try
        {
            await outAnim(host, cts.Token);
            if (cts.Token.IsCancellationRequested)
                return;

            swap();

            if (cts.Token.IsCancellationRequested)
                return;
            await inAnim(host, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Fail-soft: never block navigation on animation errors.
            control.Opacity = 1.0;
            control.RenderTransform = null;
        }
    }

    private static CancellationTokenSource CancelActive(Animatable host)
    {
        if (ActiveTokens.TryGetValue(host, out CancellationTokenSource? existing))
        {
            existing.Cancel();
            ActiveTokens.Remove(host);
        }

        var cts = new CancellationTokenSource();
        ActiveTokens.Add(host, cts);
        return cts;
    }

    private static Task FadeAsync(Animatable target, double from, double to, TimeSpan duration, CancellationToken token)
        => BuildAnimation(Visual.OpacityProperty, from, to, duration).RunAsync(target, token);

    private static async Task SlideInAsync(Animatable target, SlideDirection direction, TimeSpan duration, CancellationToken token)
    {
        if (target is not Control control)
        {
            await FadeAsync(target, 0.0, 1.0, duration, token);
            return;
        }

        double offsetX = direction switch
        {
            SlideDirection.FromRight => 24.0,
            SlideDirection.FromLeft => -24.0,
            _ => 0.0,
        };
        double offsetY = direction == SlideDirection.FromBottom ? 16.0 : 0.0;

        var transform = new TranslateTransform(offsetX, offsetY);
        control.RenderTransform = transform;
        control.Opacity = 0.0;

        Task fade = BuildAnimation(Visual.OpacityProperty, 0.0, 1.0, duration).RunAsync(control, token);
        Task slideX = offsetX != 0.0
            ? BuildAnimation(TranslateTransform.XProperty, offsetX, 0.0, duration).RunAsync(transform, token)
            : Task.CompletedTask;
        Task slideY = offsetY != 0.0
            ? BuildAnimation(TranslateTransform.YProperty, offsetY, 0.0, duration).RunAsync(transform, token)
            : Task.CompletedTask;
        await Task.WhenAll(fade, slideX, slideY);
    }

    private static Avalonia.Animation.Animation BuildAnimation(AvaloniaProperty property, double from, double to, TimeSpan duration) => new()
    {
        Duration = duration,
        FillMode = FillMode.Forward,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters = { new Setter(property, from) },
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters = { new Setter(property, to) },
            },
        },
    };
}
