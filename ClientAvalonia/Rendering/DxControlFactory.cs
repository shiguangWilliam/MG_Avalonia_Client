// "Dx" prefix means "aligned with the DX (XNA/DirectX) upstream client", NOT DirectX.
// These helpers translate DX-control semantics into Avalonia controls/templates.
// See docs/ARCHITECTURE.md §2.3 for the full explanation of the Dx* naming convention.
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
