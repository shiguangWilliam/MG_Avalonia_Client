using ClientAvalonia.Rendering;
using ClientCore;
using ClientUpdater;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Load/save updater tab (DX <c>UpdaterOptionsPanel</c> Load/Save).</summary>
public static class UpdaterOptionsApplier
{
    public static void Apply(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null)
            return;

        UiNodeViewModel? list = Find(optionsRoot, "lbUpdateServerList");
        if (list != null)
        {
            var items = new List<string>();
            if (Updater.UpdateMirrors != null)
            {
                foreach (UpdateMirror mirror in Updater.UpdateMirrors)
                {
                    string name = mirror.Name;
                    string location = mirror.Location;
                    items.Add(string.IsNullOrEmpty(location) ? name : $"{name} ({location})");
                }
            }

            list.SetListItems(items);
            if (items.Count > 0 && list.SelectedIndex < 0)
                list.SelectedIndex = 0;
        }

        Find(optionsRoot, "chkAutoCheck")?.SetIsCheckedSilent(UserINISettings.Instance.CheckForUpdates);
    }

    public static void Save(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null)
            return;

        UserINISettings ini = UserINISettings.Instance;
        UiNodeViewModel? chk = Find(optionsRoot, "chkAutoCheck");
        if (chk != null)
            ini.CheckForUpdates.Value = chk.IsChecked;

        ini.SettingsIni.EraseSectionKeys("DownloadMirrors");
        if (Updater.UpdateMirrors != null)
        {
            int id = 0;
            foreach (UpdateMirror mirror in Updater.UpdateMirrors)
            {
                ini.SettingsIni.SetStringValue("DownloadMirrors", id.ToString(), mirror.Name);
                id++;
            }
        }

        Logger.Log($"UpdaterOptionsApplier: saved autoCheck={ini.CheckForUpdates.Value}, mirrors={Updater.UpdateMirrors?.Count ?? 0}");
    }

    public static void MoveSelectedMirrorUp(UiNodeViewModel? optionsRoot)
    {
        UiNodeViewModel? list = Find(optionsRoot, "lbUpdateServerList");
        if (list == null)
            return;

        int index = list.SelectedIndex;
        if (index < 1)
            return;

        Updater.MoveMirrorUp(index);
        Apply(optionsRoot);
        list.SelectedIndex = index - 1;
    }

    public static void MoveSelectedMirrorDown(UiNodeViewModel? optionsRoot)
    {
        UiNodeViewModel? list = Find(optionsRoot, "lbUpdateServerList");
        if (list == null || Updater.UpdateMirrors == null)
            return;

        int index = list.SelectedIndex;
        if (index < 0 || index >= Updater.UpdateMirrors.Count - 1)
            return;

        Updater.MoveMirrorDown(index);
        Apply(optionsRoot);
        list.SelectedIndex = index + 1;
    }

    private static UiNodeViewModel? Find(UiNodeViewModel? root, string id)
    {
        if (root == null)
            return null;

        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
