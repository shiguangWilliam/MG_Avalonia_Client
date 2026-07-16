using System.Globalization;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Applies lobby/campaign UI controls to spawn.ini and spawnmap.ini (XNA GameSessionCheckBox/DropDown).</summary>
public static class SpawnIniApplier
{
    public static void ApplyLobbyControls(UiNodeViewModel? root, IniFile spawnIni)
    {
        if (root == null)
            return;

        foreach (UiNodeViewModel vm in EnumerateTree(root))
        {
            if (!string.IsNullOrWhiteSpace(vm.GetIniString("SpawnIniOption")))
            {
                if (IsCheckBox(vm))
                    ApplyCheckBox(vm, spawnIni);
                else if (IsDropDown(vm))
                    ApplyDropDown(vm, spawnIni);
            }
        }
    }

    public static void ApplyMapCodeControls(UiNodeViewModel? root, IniFile mapIni, GameModeEntry gameMode)
    {
        if (root == null)
            return;

        foreach (UiNodeViewModel vm in EnumerateTree(root))
        {
            if (IsDropDown(vm) && IsMapCodeDropDown(vm))
                ApplyDropDownMapCode(vm, mapIni, gameMode);

            if (IsCheckBox(vm) && !string.IsNullOrWhiteSpace(vm.GetIniString("CustomIniPath")))
                ApplyCheckBoxMapCode(vm, mapIni, gameMode);
        }
    }

    public static void ApplySpawnDefaults(IniFile spawnIni)
    {
        spawnIni.SetBooleanValue("Settings", "SidebarHack", ClientConfiguration.Instance.SidebarHack);
        spawnIni.SetIntValue("Settings", "FrameSendRate", ClientConfiguration.Instance.DefaultFrameSendRate);
        spawnIni.SetIntValue("Settings", "Protocol", ClientConfiguration.Instance.DefaultProtocolVersion);
        spawnIni.SetIntValue("Settings", "MaxAhead", ClientConfiguration.Instance.DefaultMaxAhead);
    }

    private static void ApplyCheckBox(UiNodeViewModel vm, IniFile spawnIni)
    {
        string? spawnKey = vm.GetIniString("SpawnIniOption");
        if (string.IsNullOrWhiteSpace(spawnKey))
            return;

        bool reversed = IniConversions.BooleanFromString(vm.GetIniString("Reversed") ?? string.Empty, false);
        string enabledValue = vm.GetIniString("EnabledSpawnIniValue") ?? "True";
        string disabledValue = vm.GetIniString("DisabledSpawnIniValue") ?? "False";
        string value = vm.IsChecked != reversed ? enabledValue : disabledValue;

        spawnIni.SetStringValue("Settings", spawnKey, value);
        Logger.Log($"SpawnIniApplier: {vm.Id} → Settings.{spawnKey}={value}");
    }

    private static void ApplyDropDown(UiNodeViewModel vm, IniFile spawnIni)
    {
        if (IsMapCodeDropDown(vm))
            return;

        string? spawnKey = vm.GetIniString("SpawnIniOption");
        if (string.IsNullOrWhiteSpace(spawnKey))
            return;

        if (vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.ComboItems.Count)
            return;

        string mode = (vm.GetIniString("DataWriteMode") ?? "String").ToUpperInvariant();
        switch (mode)
        {
            case "BOOLEAN":
                spawnIni.SetBooleanValue("Settings", spawnKey, vm.SelectedIndex > 0);
                Logger.Log($"SpawnIniApplier: {vm.Id} → Settings.{spawnKey}={vm.SelectedIndex > 0}");
                break;
            case "INDEX":
                spawnIni.SetIntValue("Settings", spawnKey, vm.SelectedIndex);
                Logger.Log($"SpawnIniApplier: {vm.Id} → Settings.{spawnKey}={vm.SelectedIndex}");
                break;
            case "MAPCODE":
                break;
            default:
                string raw = vm.GetSelectedComboValue() ?? vm.ComboItems[vm.SelectedIndex];
                spawnIni.SetStringValue("Settings", spawnKey, raw);
                Logger.Log($"SpawnIniApplier: {vm.Id} → Settings.{spawnKey}={raw}");
                break;
        }
    }

    private static void ApplyDropDownMapCode(UiNodeViewModel vm, IniFile mapIni, GameModeEntry gameMode)
    {
        if (vm.SelectedIndex < 0 || vm.SelectedIndex >= vm.ComboItems.Count)
            return;

        string? customIniPath = vm.GetSelectedComboValue();
        if (string.IsNullOrWhiteSpace(customIniPath))
            return;

        MapCodeHelper.ApplyMapCode(mapIni, customIniPath, gameMode);
        Logger.Log($"SpawnIniApplier: {vm.Id} map code → {customIniPath}");
    }

    private static void ApplyCheckBoxMapCode(UiNodeViewModel vm, IniFile mapIni, GameModeEntry gameMode)
    {
        bool reversed = IniConversions.BooleanFromString(vm.GetIniString("Reversed") ?? string.Empty, false);
        if (vm.IsChecked == reversed)
            return;

        string? customIniPath = vm.GetIniString("CustomIniPath");
        if (string.IsNullOrWhiteSpace(customIniPath))
            return;

        MapCodeHelper.ApplyMapCode(mapIni, customIniPath, gameMode);
        Logger.Log($"SpawnIniApplier: {vm.Id} map code → {customIniPath}");
    }

    private static bool IsMapCodeDropDown(UiNodeViewModel vm)
        => string.Equals(vm.GetIniString("DataWriteMode"), "MAPCODE", StringComparison.OrdinalIgnoreCase);

    private static bool IsCheckBox(UiNodeViewModel vm)
        => vm.TemplateKey == "DxCheckBox"
           || vm.ControlType.Contains("CheckBox", StringComparison.OrdinalIgnoreCase);

    private static bool IsDropDown(UiNodeViewModel vm)
        => vm.TemplateKey is "DxComboBox" or "DxLobbyComboBox"
           || vm.ControlType.Contains("DropDown", StringComparison.OrdinalIgnoreCase);

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
