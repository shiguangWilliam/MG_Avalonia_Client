using Avalonia.Controls;
using Avalonia.Input;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.Controls;

public partial class DxLobbyToolbarCheckBox : UserControl
{
    public DxLobbyToolbarCheckBox() => InitializeComponent();

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not UiNodeViewModel vm || !vm.IsEnabled)
            return;

        vm.IsChecked = !vm.IsChecked;
        e.Handled = true;
    }
}
