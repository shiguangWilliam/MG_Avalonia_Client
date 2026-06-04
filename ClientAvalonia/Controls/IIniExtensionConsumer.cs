using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.Controls;

/// <summary>
/// Optional hook for business controls to consume unrecognized INI keys from UiNode.RawAttributes.
/// </summary>
public interface IIniExtensionConsumer
{
    void ApplyExtensions(UiNode source);
}
