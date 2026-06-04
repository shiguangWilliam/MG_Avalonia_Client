using Avalonia.Controls;
using Avalonia.Input;

namespace ClientAvalonia.Controls;

public partial class DxTextureButton : UserControl
{
    public DxTextureButton() => InitializeComponent();

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (HoverImage.Source != null)
        {
            HoverImage.IsVisible = true;
            IdleImage.IsVisible = false;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        HoverImage.IsVisible = false;
        IdleImage.IsVisible = IdleImage.Source != null;
    }
}
