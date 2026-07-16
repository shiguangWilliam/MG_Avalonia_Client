using ClientAvalonia.Rendering;
using ClientCore;
using ClientCore.Extensions;
using ClientUpdater;
using System.IO;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Refresh component install/update button labels (DX <c>ComponentsPanel</c> Load).</summary>
public static class ComponentsOptionsApplier
{
    public static void Apply(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null || Updater.CustomComponents == null)
            return;

        foreach (CustomComponent component in Updater.CustomComponents)
        {
            UiNodeViewModel? button = FindComponentButton(optionsRoot, component.ININame);
            if (button == null)
                continue;

            bool exists = File.Exists(Path.Combine(ProgramConstants.GamePath, component.LocalPath));
            string text = "Not Available".L10N("Client:DTAConfig:NotAvailable");
            bool enabled = false;

            if (exists)
            {
                text = "Uninstall".L10N("Client:DTAConfig:Uninstall");
                enabled = true;
                if (!string.Equals(component.LocalIdentifier, component.RemoteIdentifier, StringComparison.Ordinal))
                    text = "Update".L10N("Client:DTAConfig:Update");
            }
            else if (!string.IsNullOrEmpty(component.RemoteIdentifier))
            {
                text = "Install".L10N("Client:DTAConfig:Install");
                enabled = true;
            }

            if (component.IsBeingDownloaded || !component.Initialized)
                enabled = false;

            button.SetDisplayText(text);
            button.IsEnabled = enabled;
        }
    }

    private static UiNodeViewModel? FindComponentButton(UiNodeViewModel root, string iniName)
    {
        foreach (UiNodeViewModel node in Enumerate(root))
        {
            if (!node.Id.StartsWith("btnComponent_", StringComparison.OrdinalIgnoreCase))
                continue;

            string? prop = node.GetIniString("ComponentIniName");
            if (string.Equals(prop, iniName, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }

    private static IEnumerable<UiNodeViewModel> Enumerate(UiNodeViewModel root)
    {
        yield return root;
        foreach (UiNodeViewModel child in root.Children)
        {
            foreach (UiNodeViewModel n in Enumerate(child))
                yield return n;
        }
    }
}
