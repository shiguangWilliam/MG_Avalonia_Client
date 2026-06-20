using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

public static class SettingBindingApplier
{
    private const string DefaultSection = "CustomSettings";

    public static IReadOnlyList<SettingBindingEntry> Apply(UiNodeViewModel root, IUserSettingsStore settings)
    {
        var entries = new List<SettingBindingEntry>();
        foreach (UiNodeViewModel vm in EnumerateTree(root))
        {
            SettingBindingEntry? entry = TryCreateEntry(vm);
            if (entry == null)
                continue;

            ApplyLoad(entry, settings);
            entries.Add(entry);
        }

        return entries;
    }

    public static void Commit(IReadOnlyList<SettingBindingEntry> entries, IUserSettingsStore settings)
    {
        foreach (SettingBindingEntry entry in entries)
            ApplySave(entry, settings);

        settings.Save();
    }

    private static SettingBindingEntry? TryCreateEntry(UiNodeViewModel vm)
    {
        return vm.ControlType switch
        {
            "SettingCheckBox" or "FileSettingCheckBox" => CreateCheckBoxEntry(vm),
            "SettingDropDown" or "FileSettingDropDown" => CreateDropDownEntry(vm),
            "XNACheckBox" or "XNAClientCheckBox" => CreateKnownOrGenericCheckBoxEntry(vm),
            "XNADropDown" or "XNAClientDropDown" => CreateKnownDropDownEntry(vm),
            "XNATextBox" or "XNAChatTextBox" => CreateKnownTextBoxEntry(vm),
            _ => null,
        };
    }

    private static SettingBindingEntry CreateCheckBoxEntry(UiNodeViewModel vm)
    {
        ResolveSettingKeys(vm, "_Checked", out string section, out string key);
        return new SettingBindingEntry
        {
            ViewModel = vm,
            Kind = SettingBindingKind.CheckBox,
            Section = section,
            Key = key,
            DefaultBool = ReadDefaultBool(vm),
            WriteSettingValue = ReadBoolProp(vm, "WriteSettingValue"),
            EnabledSettingValue = ReadStringProp(vm, "EnabledSettingValue"),
            DisabledSettingValue = ReadStringProp(vm, "DisabledSettingValue"),
        };
    }

    private static SettingBindingEntry? CreateKnownOrGenericCheckBoxEntry(UiNodeViewModel vm)
    {
        if (!KnownOptionSettings.TryResolve(vm.Id, out string section, out string key))
            return null;

        return new SettingBindingEntry
        {
            ViewModel = vm,
            Kind = SettingBindingKind.CheckBox,
            Section = section,
            Key = key,
            DefaultBool = ReadDefaultBool(vm),
        };
    }

    private static SettingBindingEntry CreateDropDownEntry(UiNodeViewModel vm)
    {
        ResolveSettingKeys(vm, "_SelectedIndex", out string section, out string key);
        return new SettingBindingEntry
        {
            ViewModel = vm,
            Kind = SettingBindingKind.DropDown,
            Section = section,
            Key = key,
            DefaultIndex = ReadDefaultIndex(vm),
            WriteItemValue = ReadBoolProp(vm, "WriteItemValue"),
        };
    }

    private static SettingBindingEntry? CreateKnownTextBoxEntry(UiNodeViewModel vm)
    {
        if (!KnownOptionSettings.TryResolve(vm.Id, out string section, out string key))
            return null;

        return new SettingBindingEntry
        {
            ViewModel = vm,
            Kind = SettingBindingKind.Text,
            Section = section,
            Key = key,
        };
    }

    private static SettingBindingEntry? CreateKnownDropDownEntry(UiNodeViewModel vm)
    {
        if (!KnownOptionSettings.TryResolve(vm.Id, out string section, out string key))
            return null;

        return new SettingBindingEntry
        {
            ViewModel = vm,
            Kind = SettingBindingKind.DropDown,
            Section = section,
            Key = key,
            DefaultIndex = ReadDefaultIndex(vm),
            WriteItemValue = ReadBoolProp(vm, "WriteItemValue"),
        };
    }

    private static void ResolveSettingKeys(UiNodeViewModel vm, string defaultSuffix, out string section, out string key)
    {
        section = ReadStringProp(vm, "SettingSection");
        key = ReadStringProp(vm, "SettingKey");

        if (string.IsNullOrWhiteSpace(section))
            section = DefaultSection;

        if (string.IsNullOrWhiteSpace(key))
            key = vm.Id + defaultSuffix;

        if (KnownOptionSettings.TryResolve(vm.Id, out string knownSection, out string knownKey))
        {
            if (section == DefaultSection && string.IsNullOrWhiteSpace(ReadStringProp(vm, "SettingSection")))
                section = knownSection;
            if (key == vm.Id + defaultSuffix && string.IsNullOrWhiteSpace(ReadStringProp(vm, "SettingKey")))
                key = knownKey;
        }
    }

    private static void ApplyLoad(SettingBindingEntry entry, IUserSettingsStore settings)
    {
        switch (entry.Kind)
        {
            case SettingBindingKind.CheckBox:
            {
                bool value = entry.WriteSettingValue
                    ? LoadWriteSettingValue(entry, settings)
                    : settings.GetBool(entry.Section, entry.Key, entry.DefaultBool);
                entry.ViewModel.IsChecked = value;
                break;
            }
            case SettingBindingKind.DropDown:
            {
                int index = entry.WriteItemValue
                    ? FindItemIndexByValue(entry.ViewModel, settings.GetString(entry.Section, entry.Key, string.Empty))
                    : settings.GetInt(entry.Section, entry.Key, entry.DefaultIndex);
                entry.ViewModel.SelectedIndex = index;
                break;
            }
            case SettingBindingKind.Text:
            {
                string value = entry.Key.Equals("Handle", StringComparison.OrdinalIgnoreCase)
                    ? PlayerNameSettings.LoadForDisplay()
                    : settings.GetString(entry.Section, entry.Key, string.Empty);
                entry.ViewModel.InputText = value;
                break;
            }
        }
    }

    private static void ApplySave(SettingBindingEntry entry, IUserSettingsStore settings)
    {
        switch (entry.Kind)
        {
            case SettingBindingKind.CheckBox:
                if (entry.WriteSettingValue)
                    settings.SetString(entry.Section, entry.Key,
                        entry.ViewModel.IsChecked ? entry.EnabledSettingValue : entry.DisabledSettingValue);
                else
                    settings.SetBool(entry.Section, entry.Key, entry.ViewModel.IsChecked);
                break;
            case SettingBindingKind.DropDown:
                if (entry.WriteItemValue)
                {
                    IReadOnlyList<string> items = entry.ViewModel.ComboItems;
                    int idx = entry.ViewModel.SelectedIndex;
                    string value = idx >= 0 && idx < items.Count ? items[idx] : string.Empty;
                    settings.SetString(entry.Section, entry.Key, value);
                }
                else
                    settings.SetInt(entry.Section, entry.Key, entry.ViewModel.SelectedIndex);
                break;
            case SettingBindingKind.Text:
                if (entry.Key.Equals("Handle", StringComparison.OrdinalIgnoreCase))
                    PlayerNameSettings.SaveFromInput(entry.ViewModel.InputText);
                else
                    settings.SetString(entry.Section, entry.Key, entry.ViewModel.InputText);
                break;
        }
    }

    private static bool LoadWriteSettingValue(SettingBindingEntry entry, IUserSettingsStore settings)
    {
        string value = settings.GetString(entry.Section, entry.Key, string.Empty);
        if (value == entry.EnabledSettingValue)
            return true;
        if (value == entry.DisabledSettingValue)
            return false;
        return entry.DefaultBool;
    }

    private static int FindItemIndexByValue(UiNodeViewModel vm, string value)
    {
        if (string.IsNullOrEmpty(value))
            return vm.SelectedIndex;

        IReadOnlyList<string> items = vm.ComboItems;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return ReadDefaultIndex(vm);
    }

    private static bool ReadDefaultBool(UiNodeViewModel vm)
    {
        if (vm.Node.Props.TryGetValue("DefaultValue", out object? dv) && dv is bool defaultBool)
            return defaultBool;

        if (vm.Node.Props.TryGetValue("IsChecked", out object? c) && c is bool checkedValue)
            return checkedValue;

        return false;
    }

    private static int ReadDefaultIndex(UiNodeViewModel vm)
        => vm.Node.Props.TryGetValue("DefaultIndex", out object? v) && v is int i ? i : 0;

    private static string ReadStringProp(UiNodeViewModel vm, string key)
        => vm.Node.Props.TryGetValue(key, out object? v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static bool ReadBoolProp(UiNodeViewModel vm, string key)
        => vm.Node.Props.TryGetValue(key, out object? v) && v is bool b && b;

    private static IEnumerable<UiNodeViewModel> EnumerateTree(UiNodeViewModel root)
    {
        yield return root;
        foreach (UiNodeViewModel child in root.Children)
        {
            foreach (UiNodeViewModel node in EnumerateTree(child))
                yield return node;
        }
    }
}
