using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;

namespace ClientAvalonia.Views;

public partial class StartupLoadingView : UserControl
{
    private const int MinDisplayMilliseconds = 3200;
    private const int ReadyHoldMilliseconds = 900;

    private readonly DispatcherTimer _pulseTimer;
    private double _progress;
    private bool _pulseUp = true;

    public StartupLoadingView()
    {
        InitializeComponent();
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _pulseTimer.Tick += OnPulseTick;
    }

    public void SetBackground(Bitmap? bitmap)
    {
        PART_LoadingImage.Source = bitmap;
        PART_LoadingImage.Opacity = 0;
        _ = AnimateFadeInAsync();
    }

    public void SetStatus(string text) => PART_Status.Text = text;

    public void SetProgress(double value)
    {
        _progress = Math.Clamp(value, 0, 1);
        UpdateProgressBar();
    }

    public void StartAnimation() => _pulseTimer.Start();

    public void StopAnimation() => _pulseTimer.Stop();

    private async Task AnimateFadeInAsync()
    {
        for (int i = 0; i <= 20; i++)
        {
            PART_LoadingImage.Opacity = i / 20.0 * 0.96;
            await Task.Delay(24).ConfigureAwait(true);
        }
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        if (_pulseUp)
        {
            _progress += 0.010;
            if (_progress >= 0.92)
                _pulseUp = false;
        }
        else
        {
            _progress -= 0.003;
            if (_progress <= 0.08)
                _pulseUp = true;
        }

        UpdateProgressBar();
    }

    private void UpdateProgressBar()
    {
        double trackWidth = Math.Max(120, Bounds.Width - 48);
        if (trackWidth <= 0)
            trackWidth = 640;

        PART_ProgressFill.Width = trackWidth * _progress;
    }

    public static async Task RunStartupSequenceAsync(
        StartupLoadingView view,
        ResourceResolver resources,
        Func<Task> loadWork)
    {
        view.SetBackground(resources.LoadFirstBitmap(["loadingscreen.png", "loadingScreen.png"]));
        view.StartAnimation();
        view.SetStatus("Loading resources...");
        view.SetProgress(0.12);

        var elapsed = Stopwatch.StartNew();
        await Task.Run(loadWork).ConfigureAwait(true);

        int remaining = MinDisplayMilliseconds - (int)elapsed.ElapsedMilliseconds;
        if (remaining > 0)
        {
            view.SetStatus("Loading resources...");
            await Task.Delay(remaining).ConfigureAwait(true);
        }

        view.SetProgress(1);
        view.SetStatus("Ready.");
        await Task.Delay(ReadyHoldMilliseconds).ConfigureAwait(true);
        view.StopAnimation();
    }
}
