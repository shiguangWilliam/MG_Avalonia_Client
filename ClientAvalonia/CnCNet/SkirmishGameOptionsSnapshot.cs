using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Collects / applies skirmish game-option control values (DX SkirmishLobby
/// SaveSettings/LoadSettings [GameOptions] round-trip). Reuses
/// <see cref="CnCNetGameOptionsCatalog"/> so skirmish and CnCNet enumerate the
/// same control set.
/// </summary>
public static class SkirmishGameOptionsSnapshot
{
    /// <summary>Reads current checkbox/dropdown values: id → "True/False" | index.</summary>
    public static IReadOnlyDictionary<string, string> Collect(UiNodeViewModel? root)
    {
        var result = new Dictionary<string, string>();
        if (root == null)
            return result;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);

        foreach (UiNodeViewModel chk in checkBoxes)
            result[chk.Id] = chk.IsChecked.ToString();

        foreach (UiNodeViewModel dd in dropDowns)
            result[dd.Id] = dd.SelectedIndex.ToString();

        return result;
    }

    /// <summary>
    /// Applies saved values back onto the controls (silently — no SelectionChanged storms).
    /// Controls missing from <paramref name="values"/> keep their INI defaults.
    /// </summary>
    /// <param name="root">Lobby UI root.</param>
    /// <param name="values">Saved id → value map.</param>
    /// <param name="gameMode">Current game mode; its ForcedOptions controls are skipped (DX parity).</param>
    public static void Apply(
        UiNodeViewModel? root,
        IReadOnlyDictionary<string, string> values,
        GameModeEntry? gameMode = null)
    {
        if (root == null || values.Count == 0)
            return;

        HashSet<string> forcedControls = ResolveForcedControls(gameMode);

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(root);

        foreach (UiNodeViewModel chk in checkBoxes)
        {
            if (forcedControls.Contains(chk.Id))
                continue;

            if (values.TryGetValue(chk.Id, out string? raw)
                && bool.TryParse(raw, out bool checkedState))
            {
                chk.SetIsCheckedSilent(checkedState);
            }
        }

        foreach (UiNodeViewModel dd in dropDowns)
        {
            if (forcedControls.Contains(dd.Id))
                continue;

            if (!values.TryGetValue(dd.Id, out string? raw)
                || !int.TryParse(raw, out int index))
            {
                continue;
            }

            // DX guards bounds before assigning SelectedIndex.
            if (index > -1 && index < dd.ComboItems.Count)
                dd.SetSelectedIndexSilent(index);
        }
    }

    /// <summary>
    /// Control ids forced by the game mode (<c>&lt;Mode&gt;ForcedOptions</c> in MPMaps.ini).
    /// Those overrides always win over saved user choices (DX LoadSettings parity).
    /// </summary>
    private static HashSet<string> ResolveForcedControls(GameModeEntry? gameMode)
    {
        var forced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (gameMode == null)
            return forced;

        try
        {
            string mpMapsPath = SafePath.CombineFilePath(
                AppState.Environment.GamePath,
                AppState.Configuration.Legacy.MPMapsIniPath);
            if (!File.Exists(mpMapsPath))
                return forced;

            var ini = new IniFile(mpMapsPath);
            if (!ini.SectionExists(gameMode.Name))
                return forced;

            string sectionName = ini.GetStringValue(gameMode.Name, "ForcedOptions", string.Empty);
            if (string.IsNullOrWhiteSpace(sectionName))
                sectionName = gameMode.Name + "ForcedOptions";

            List<string>? keys = ini.GetSectionKeys(sectionName);
            if (keys == null)
                return forced;

            foreach (string key in keys)
                forced.Add(key);

            foreach (string key in forced)
                Logger.Log($"SkirmishGameOptionsSnapshot: '{key}' forced by game mode {gameMode.Name} — saved value ignored.");
        }
        catch (Exception ex)
        {
            Logger.Log($"SkirmishGameOptionsSnapshot: forced-control lookup failed: {ex.Message}");
        }

        return forced;
    }
}
