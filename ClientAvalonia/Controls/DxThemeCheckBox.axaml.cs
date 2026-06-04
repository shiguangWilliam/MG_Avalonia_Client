using Avalonia.Controls;
using Avalonia.Input;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Controls;

public partial class DxThemeCheckBox : UserControl
{
    public DxThemeCheckBox() => InitializeComponent();

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not UiNodeViewModel vm || !vm.IsEnabled)
            return;

        vm.IsChecked = !vm.IsChecked;
        e.Handled = true;
    }
}
