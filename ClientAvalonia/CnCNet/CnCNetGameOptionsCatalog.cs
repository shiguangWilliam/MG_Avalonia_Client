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
            checkBoxes[i].IsChecked = values[i];
    }

    public static void ApplyDropDownIndices(IReadOnlyList<UiNodeViewModel> dropDowns, IReadOnlyList<int> indices)
    {
        for (int i = 0; i < dropDowns.Count && i < indices.Count; i++)
        {
            int index = indices[i];
            if (index >= -1 && index < dropDowns[i].ComboItems.Count)
                dropDowns[i].SelectedIndex = index;
        }
    }

    private static bool IsGameLobbyCheckBox(UiNodeViewModel vm)
        => !string.IsNullOrWhiteSpace(vm.GetIniString("SpawnIniOption"))
           && IsCheckBox(vm);

    private static bool IsGameLobbyDropDown(UiNodeViewModel vm)
        => !string.IsNullOrWhiteSpace(vm.GetIniString("SpawnIniOption"))
           && IsDropDown(vm);

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
