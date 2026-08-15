using ClientAvalonia.CnCNet;
using ClientAvalonia.Configuration;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Rendering;
using ClientCore;
using Rampastring.Tools;

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

        IGameConfiguration config = EnvironmentServices.Resolve<IGameConfiguration>();
        return new CnCNetGameOptionsState
        {
            CheckBoxValues = checkBoxes.Select(c => c.IsChecked).ToList(),
            DropDownIndices = dropDowns.Select(d => d.SelectedIndex).ToList(),
            MapOfficial = map?.IsOfficial ?? false,
            MapSha1 = map?.Sha1 ?? string.Empty,
            GameModeName = gameMode?.Name ?? string.Empty,
            MapUntranslatedName = map?.UntranslatedName ?? string.Empty,
            FrameSendRate = config.DefaultFrameSendRate,
            MaxAhead = config.DefaultMaxAhead,
            ProtocolVersion = config.DefaultProtocolVersion,
            RandomSeed = randomSeed,
            RemoveStartingLocations = removeStartingLocations,
        };
    }

    public static void Apply(
        UiNodeViewModel? root,
        CnCNetGameOptionsState state,
        GameResourceCatalog resources,
        LobbySessionState? session = null)
    {
        if (root == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);

        CnCNetGameOptionsCatalog.ApplyCheckBoxValues(checkBoxes, state.CheckBoxValues);
        CnCNetGameOptionsCatalog.ApplyDropDownIndices(dropDowns, state.DropDownIndices);

        MapEntry? map = ResolveMap(resources, state);
        if (map != null)
            ApplyMapSelection(root, resources, session, map);
        else if (!state.MapOfficial && !string.IsNullOrWhiteSpace(state.MapSha1))
            Logger.Log($"CnCNet GO: custom map {state.MapSha1} not installed.");

        if (!string.IsNullOrWhiteSpace(state.GameModeName))
            ApplyGameModeSelection(root, resources, session, state.GameModeName);
    }

    private static MapEntry? ResolveMap(GameResourceCatalog resources, CnCNetGameOptionsState state)
    {
        resources.EnsureLoaded();

        if (!string.IsNullOrWhiteSpace(state.MapSha1))
        {
            MapEntry? byHash = resources.Maps.FirstOrDefault(m =>
                m.Sha1.Equals(state.MapSha1, StringComparison.OrdinalIgnoreCase));
            if (byHash != null)
                return byHash;
        }

        if (!string.IsNullOrWhiteSpace(state.MapUntranslatedName))
        {
            return resources.Maps.FirstOrDefault(m =>
                m.UntranslatedName.Equals(state.MapUntranslatedName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static void ApplyMapSelection(
        UiNodeViewModel root,
        GameResourceCatalog resources,
        LobbySessionState? session,
        MapEntry map)
    {
        int filterIndex = resources.FindFilterIndexForMap(map);
        UiNodeViewModel? ddGameMode = FindNode(root, "ddGameMode");
        if (ddGameMode != null)
        {
            ddGameMode.SetSelectedIndexSilent(filterIndex);
            if (session != null)
                session.FilterIndex = filterIndex;
        }

        if (session == null)
            return;

        IReadOnlyList<MapEntry> visible = resources.GetMapsForFilterIndex(filterIndex);
        visible = resources.FilterMapsBySearch(visible, session.MapSearchText);
        session.SetVisibleMaps(visible);

        int visibleIndex = GameResourceCatalog.IndexOfMapInList(visible, map);
        UiNodeViewModel? lbMapList = FindNode(root, "lbMapList");
        if (lbMapList == null || visibleIndex < 0)
            return;

        lbMapList.SetListItems(visible.Select(m => m.DisplayName));
        lbMapList.SetSelectedIndexSilent(visibleIndex);
    }

    private static void ApplyGameModeSelection(
        UiNodeViewModel root,
        GameResourceCatalog resources,
        LobbySessionState? session,
        string gameModeName)
    {
        GameModeEntry? mode = resources.GameModes.FirstOrDefault(m =>
            m.Name.Equals(gameModeName, StringComparison.OrdinalIgnoreCase)
            || m.UntranslatedUIName.Equals(gameModeName, StringComparison.OrdinalIgnoreCase));

        if (mode == null)
            return;

        int modeIndex = resources.GameModes.ToList().FindIndex(m =>
            m.Name.Equals(mode.Name, StringComparison.OrdinalIgnoreCase));
        if (modeIndex < 0)
            return;

        int filterIndex = LobbySessionState.FavoriteFilterIndex + 1 + modeIndex;
        UiNodeViewModel? ddGameMode = FindNode(root, "ddGameMode");
        if (ddGameMode != null)
        {
            ddGameMode.SetSelectedIndexSilent(filterIndex);
            if (session != null)
                session.FilterIndex = filterIndex;
        }
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
