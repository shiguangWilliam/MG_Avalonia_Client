using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClientAvalonia.Controls;

/// <summary>
/// Three-segment difficulty picker replacing the legacy slider look.
/// Values remain 0 (CASUAL) / 1 (STANDARD) / 2 (MENTAL) to preserve the
/// trbDifficultySelector.SelectedIndex contract.
/// </summary>
public partial class DxCampaignDifficultySegment : UserControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<DxCampaignDifficultySegment, int>(nameof(Value), 1);

    private Button[] _segments = Array.Empty<Button>();

    public DxCampaignDifficultySegment()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => WireSegments();
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 2));
    }

    private void WireSegments()
    {
        if (_segments.Length > 0)
            return;

        _segments = new[] { SegmentCasual, SegmentStandard, SegmentMental };
        foreach (Button segment in _segments)
            segment.Click += OnSegmentClick;

        UpdateVisual();
    }

    private void OnSegmentClick(object? sender, RoutedEventArgs e)
    {
        int index = Array.IndexOf(_segments, sender);
        if (index < 0)
            return;

        Value = index;
        UpdateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
            UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_segments.Length == 0)
            return;

        for (int i = 0; i < _segments.Length; i++)
            _segments[i].Classes.Set("selected", i == Value);
    }
}
