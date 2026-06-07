using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientCore;
using Rampastring.Tools;
using System.Linq;

namespace ClientAvalonia.Services;

/// <summary>Collects/applies GO CTCP game options via multiplayer lobby UI tree (XNA CheckBoxes/DropDowns).</summary>
public static class CnCNetGameOptionsUiBridge
{
    public static (int CheckBoxCount, int DropDownCount) GetControlCounts(UiNodeViewModel? root)
    {
        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);
        return (checkBoxes.Count, dropDowns.Count);
    }

    public static CnCNetGameOptionsState Collect(
        UiNodeViewModel? root,
        MapEntry? map,
        GameModeEntry? gameMode,
        int randomSeed,
        bool removeStartingLocations)
    {
        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);

        return new CnCNetGameOptionsState
        {
            CheckBoxValues = checkBoxes.Select(c => c.IsChecked).ToList(),
            DropDownIndices = dropDowns.Select(d => d.SelectedIndex).ToList(),
            MapOfficial = map?.IsOfficial ?? false,
            MapSha1 = map?.Sha1 ?? string.Empty,
            GameModeName = gameMode?.Name ?? string.Empty,
            MapUntranslatedName = map?.UntranslatedName ?? string.Empty,
            FrameSendRate = ClientConfiguration.Instance.DefaultFrameSendRate,
            MaxAhead = ClientConfiguration.Instance.DefaultMaxAhead,
            ProtocolVersion = ClientConfiguration.Instance.DefaultProtocolVersion,
            RandomSeed = randomSeed,
            RemoveStartingLocations = removeStartingLocations,
        };
    }

    public static void Apply(UiNodeViewModel? root, CnCNetGameOptionsState state, GameResourceCatalog resources)
    {
        if (root == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);

        CnCNetGameOptionsCatalog.ApplyCheckBoxValues(checkBoxes, state.CheckBoxValues);
        CnCNetGameOptionsCatalog.ApplyDropDownIndices(dropDowns, state.DropDownIndices);

        if (!string.IsNullOrWhiteSpace(state.MapSha1) || !string.IsNullOrWhiteSpace(state.MapUntranslatedName))
        {
            MapEntry? map = resources.Maps.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(state.MapSha1) && m.Sha1.Equals(state.MapSha1, StringComparison.OrdinalIgnoreCase))
                || m.UntranslatedName.Equals(state.MapUntranslatedName, StringComparison.OrdinalIgnoreCase));

            if (map != null)
            {
                UiNodeViewModel? lbMapList = FindNode(root, "lbMapList");
                int index = IndexOfMap(resources.Maps, map);
                if (lbMapList != null && index >= 0)
                    lbMapList.SelectedIndex = index;
            }
            else if (!state.MapOfficial && !string.IsNullOrWhiteSpace(state.MapSha1))
            {
                Logger.Log($"CnCNet GO: custom map {state.MapSha1} not installed.");
            }
        }

        if (!string.IsNullOrWhiteSpace(state.GameModeName))
        {
            GameModeEntry? mode = resources.GameModes.FirstOrDefault(m =>
                m.Name.Equals(state.GameModeName, StringComparison.OrdinalIgnoreCase)
                || m.UntranslatedUIName.Equals(state.GameModeName, StringComparison.OrdinalIgnoreCase));

            if (mode != null)
            {
                UiNodeViewModel? lbGameMode = FindNode(root, "lbGameMode");
                int modeIndex = IndexOfGameMode(resources.GameModes, mode);
                if (lbGameMode != null && modeIndex >= 0)
                    lbGameMode.SelectedIndex = modeIndex;
            }
        }
    }

    private static int IndexOfMap(IReadOnlyList<MapEntry> maps, MapEntry map)
    {
        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i].Sha1.Equals(map.Sha1, StringComparison.OrdinalIgnoreCase)
                && maps[i].BaseFilePath.Equals(map.BaseFilePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int IndexOfGameMode(IReadOnlyList<GameModeEntry> modes, GameModeEntry mode)
    {
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i].Name.Equals(mode.Name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static UiNodeViewModel? FindNode(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindNode(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
