using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Controls;

public partial class DxReadyActionButton : UserControl
{
    private UiNodeViewModel? _vm;

    public DxReadyActionButton()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindViewModel();
    }

    private void BindViewModel()
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as UiNodeViewModel;
        if (_vm != null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        RefreshVisualState();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UiNodeViewModel.Text) or nameof(UiNodeViewModel.IsEnabled))
            RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        if (_vm == null)
            return;

        string label = _vm.Text ?? string.Empty;
        bool isReady = label.Contains("Not Ready", StringComparison.OrdinalIgnoreCase);
        bool enabled = _vm.IsEnabled;

        IBrush accent = isReady
            ? new SolidColorBrush(Color.FromRgb(72, 190, 92))
            : new SolidColorBrush(Color.FromRgb(255, 166, 72));

        PART_Frame.BorderBrush = accent;
        PART_OuterRing.Stroke = accent;
        PART_InnerDot.Fill = isReady ? accent : Brushes.Transparent;
        PART_Label.Foreground = enabled
            ? new SolidColorBrush(Color.FromRgb(232, 212, 176))
            : new SolidColorBrush(Color.FromArgb(128, 232, 212, 176));
        Opacity = enabled ? 1 : 0.55;
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_vm == null || !_vm.IsEnabled)
            return;

        _vm.InvokeClick();
        e.Handled = true;
    }
}
