using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ClientAvalonia.Controls;
using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.Rendering;

public static class DxControlFactory
{
    public static void ApplyExtensions(Control control, UiNode node)
    {
        if (control is IIniExtensionConsumer consumer)
            consumer.ApplyExtensions(node);
    }
}
