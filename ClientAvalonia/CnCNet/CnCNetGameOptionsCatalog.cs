using ClientAvalonia.Rendering;
using System;
using System.Collections.Generic;

namespace ClientAvalonia.CnCNet;

/// <summary>Enumerates game-lobby option controls in INI tree order (XNA CheckBoxes / DropDowns lists).</summary>
public static class CnCNetGameOptionsCatalog
{
    public static (IReadOnlyList<UiNodeViewModel> CheckBoxes, IReadOnlyList<UiNodeViewModel> DropDowns) Enumerate(
        UiNodeViewModel? root)
    {
        if (root == null)
            return ([], []);

        var checkBoxes = new List<UiNodeViewModel>();
        var dropDowns = new List<UiNodeViewModel>();

        foreach (UiNodeViewModel vm in EnumerateTree(root))
        {
            if (IsGameLobbyCheckBox(vm))
                checkBoxes.Add(vm);
            else if (IsGameLobbyDropDown(vm))
                dropDowns.Add(vm);
        }

        return (checkBoxes, dropDowns);
    }

    public static void ApplyCheckBoxValues(IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<bool> values)
    {
        for (int i = 0; i < checkBoxes.Count && i < values.Count; i++)
            checkBoxes[i].SetIsCheckedSilent(values[i]);
    }

    public static void ApplyDropDownIndices(IReadOnlyList<UiNodeViewModel> dropDowns, IReadOnlyList<int> indices)
    {
        for (int i = 0; i < dropDowns.Count && i < indices.Count; i++)
        {
            int index = indices[i];
            if (index >= -1 && index < dropDowns[i].ComboItems.Count)
                dropDowns[i].SetSelectedIndexSilent(index);
        }
    }

    private static bool IsGameLobbyCheckBox(UiNodeViewModel vm)
        => IsCheckBox(vm) && IsGameLobbyOptionControl(vm);

    private static bool IsGameLobbyDropDown(UiNodeViewModel vm)
        => IsDropDown(vm) && IsGameLobbyOptionControl(vm);

    /// <summary>
    /// Matches DX GameLobby* registration: SpawnIniOption, MapCode dropdowns (OptionName/DataWriteMode),
    /// and CustomIniPath checkboxes.
    /// </summary>
    private static bool IsGameLobbyOptionControl(UiNodeViewModel vm)
    {
        if (vm.ControlType.Contains("GameLobby", StringComparison.OrdinalIgnoreCase)
            || vm.ControlType.Contains("GameSession", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(vm.GetIniString("SpawnIniOption")))
            return true;

        if (!string.IsNullOrWhiteSpace(vm.GetIniString("CustomIniPath")))
            return true;

        if (!string.IsNullOrWhiteSpace(vm.GetIniString("OptionName")))
            return true;

        string? mode = vm.GetIniString("DataWriteMode");
        return !string.IsNullOrWhiteSpace(mode);
    }

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
