using Avalonia.Controls;
using Avalonia.Input;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Controls;

/// <summary>
/// Map preview with starting-location marker overlay.
/// Marker pointer events are routed to <see cref="UiNodeViewModel.NotifyStartMarkerLeftClick"/> /
/// <see cref="UiNodeViewModel.NotifyStartMarkerRightClick"/>. Empty-area left-click still
/// fires <see cref="UiNodeViewModel.ClickCommand"/> (favorite toggle when registered).
/// </summary>
public partial class MapPreviewBoxView : UserControl
{
    public MapPreviewBoxView() => InitializeComponent();

    private void OnMarkerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UiNodeViewModel previewVm)
            return;

        if (sender is not Control { DataContext: MapStartMarkerVm marker })
            return;

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            previewVm.NotifyStartMarkerLeftClick(marker.Index);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            previewVm.NotifyStartMarkerRightClick(marker.Index);
            e.Handled = true;
        }
    }

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UiNodeViewModel previewVm)
            return;

        if (e.Handled)
            return;

        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        previewVm.ClickCommand.Execute(null);
        e.Handled = true;
    }
}
