using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ClientAvalonia.Animation;
using ClientAvalonia.Assets;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Views;

/// <summary>
/// Tactical main-menu command console. Reparents INI-defined child nodes by ID into
/// left nav / telemetry / footer slots so Genesis stays the full-bleed hero in the
/// middle-right. Terminal, radar, SYS clock and LINK lamp are layout-owned ambience
/// and never steal focus or change MainMenuBehaviors.
/// </summary>
public partial class DxMainMenuTacticalLayout : UserControl
{
    private static readonly string[] NavOrder =
    [
        "btnNewCampaign",
        "btnLoadGame",
        "btnCnCNet",
        "btnLan",
        "btnSkirmish",
        "btnOptions",
        "btnExit",
    ];

    private DispatcherTimer? _clockTimer;
    private double _linkPhase;
    private bool _entrancePlayed;

    public DxMainMenuTacticalLayout()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => DistributeChildren();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyArtPlate();
        DistributeChildren();
        StartAmbientTimers();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
    }

    private void ApplyArtPlate()
    {
        Bitmap? panel = GlmAssets.TacticalPanel;
        if (panel == null || LeftPanel == null)
            return;

        LeftPanel.Background = new ImageBrush(panel)
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.4,
        };
    }

    private void StartAmbientTimers()
    {
        if (_clockTimer != null)
            return;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _clockTimer.Tick += (_, _) => TickAmbient();
        _clockTimer.Start();
        TickAmbient();
    }

    private void TickAmbient()
    {
        if (SysClock != null)
            SysClock.Text = DateTime.Now.ToString("HH:mm:ss");

        if (!DxTransitions.Enabled || LinkLamp == null)
            return;

        _linkPhase += 0.25;
        double pulse = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(_linkPhase));
        LinkLamp.Opacity = pulse;
    }

    public void DistributeChildren()
    {
        if (DataContext is not UiNodeViewModel vm)
            return;

        NavHost.Children.Clear();
        TelemetryHost.Children.Clear();
        FooterHost.Children.Clear();
        MiscHost.Children.Clear();

        // Stable nav order regardless of INI declaration order.
        var byId = new System.Collections.Generic.Dictionary<string, UiNodeViewModel>(
            StringComparer.OrdinalIgnoreCase);
        foreach (UiNodeViewModel child in vm.Children)
            byId[child.Id] = child;

        int index = 1;
        foreach (string id in NavOrder)
        {
            if (!byId.TryGetValue(id, out UiNodeViewModel? child))
                continue;

            NavHost.Children.Add(WrapNavRow(child, index++));
            byId.Remove(id);
        }

        foreach (UiNodeViewModel child in vm.Children)
        {
            if (!byId.ContainsKey(child.Id))
                continue;

            Control wrapped = Wrap(child);
            switch (child.Id)
            {
                case "lblCnCNetStatus":
                case "lblCnCNetPlayerCount":
                case "txtVersion":
                case "lblVersion":
                case "lblUpdateStatus":
                    TelemetryHost.Children.Add(wrapped);
                    break;

                case "btnStatistics":
                case "btnCredits":
                case "btnMapEditor":
                    FooterHost.Children.Add(wrapped);
                    break;

                default:
                    // Logo / Extras / RankedMatch stay hidden but alive.
                    MiscHost.Children.Add(wrapped);
                    break;
            }
        }

        PushTerminalStatus(vm);
        _entrancePlayed = false;
        PlayEntranceIfNeeded();
    }

    private void PushTerminalStatus(UiNodeViewModel root)
    {
        if (TerminalStrip == null)
            return;

        string? version = FindText(root, "lblVersion");
        string? online = FindText(root, "lblCnCNetPlayerCount");
        if (!string.IsNullOrWhiteSpace(version))
            TerminalStrip.PushStatus($"VERSION ............... {version}");
        if (!string.IsNullOrWhiteSpace(online))
            TerminalStrip.PushStatus($"CNCNET ONLINE ......... {online}");
    }

    private static string? FindText(UiNodeViewModel root, string id)
    {
        foreach (UiNodeViewModel child in root.Children)
        {
            if (child.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return child.Text;
        }

        return null;
    }

    private async void PlayEntranceIfNeeded()
    {
        if (_entrancePlayed || NavHost.Children.Count == 0)
            return;

        _entrancePlayed = true;

        // Snapshot before any await — DistributeChildren may Clear() the live
        // collection (DataContextChanged / re-attach), which would throw
        // "Collection was modified" on a live enumerator.
        Control[] rows = new Control[NavHost.Children.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = (Control)NavHost.Children[i]!;

        if (!DxTransitions.Enabled)
        {
            foreach (Control child in rows)
                child.Opacity = 1.0;
            return;
        }

        foreach (Control child in rows)
        {
            child.Opacity = 0.0;
            child.RenderTransform = new TranslateTransform(-12, 0);
        }

        int delayMs = 0;
        foreach (Control child in rows)
        {
            await Task.Delay(delayMs);
            delayMs = Math.Min(delayMs + 36, 200);
            // Skip rows that were removed by a later DistributeChildren.
            if (child.Parent != NavHost)
                continue;
            _ = AnimateNavIn(child);
        }
    }

    private static async Task AnimateNavIn(Control control)
    {
        var transform = control.RenderTransform as TranslateTransform
                        ?? new TranslateTransform(-12, 0);
        control.RenderTransform = transform;

        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            FillMode = FillMode.Forward,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 0.0) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 1.0) },
                },
            },
        };
        var slide = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            FillMode = FillMode.Forward,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.XProperty, -12.0) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.XProperty, 0.0) },
                },
            },
        };

        try
        {
            await Task.WhenAll(fade.RunAsync(control), slide.RunAsync(transform));
        }
        catch
        {
            control.Opacity = 1.0;
            control.RenderTransform = null;
        }
    }

    private static Control WrapNavRow(UiNodeViewModel child, int index)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Opacity = DxTransitions.Enabled ? 0.0 : 1.0,
        };

        var indexLabel = new TextBlock
        {
            Text = index.ToString("00"),
            Classes = { "nav-index" },
        };
        Grid.SetColumn(indexLabel, 0);
        row.Children.Add(indexLabel);

        Control content = Wrap(child);
        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        return row;
    }

    private static Control Wrap(UiNodeViewModel child)
        => new ContentControl { Content = child, ContentTemplate = new DxNodeTemplateSelector() };
}
