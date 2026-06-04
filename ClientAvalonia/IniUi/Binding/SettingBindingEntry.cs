using ClientAvalonia.Rendering;
using ClientAvalonia.Services;

namespace ClientAvalonia.IniUi.Binding;

public sealed class SettingBindingEntry
{
    public required UiNodeViewModel ViewModel { get; init; }

    public required SettingBindingKind Kind { get; init; }

    public required string Section { get; init; }

    public required string Key { get; init; }

    public bool DefaultBool { get; init; }

    public int DefaultIndex { get; init; }

    public bool WriteSettingValue { get; init; }

    public string EnabledSettingValue { get; init; } = string.Empty;

    public string DisabledSettingValue { get; init; } = string.Empty;

    public bool WriteItemValue { get; init; }
}

public enum SettingBindingKind
{
    CheckBox,
    DropDown,
    Text,
}
