using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// Issue #4: results are cached per (game mode, MPMaps.ini LastWriteTimeUtc) —
    /// the file is tens of KB and its forced set only changes when the mode or
    /// the file content changes; re-parsing it on every lobby entry (and logging
    /// one Info line per key) was pure churn.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Mode, DateTime Mtime), HashSet<string>> ForcedCache = new();

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

            DateTime mtime = File.GetLastWriteTimeUtc(mpMapsPath);
            var cacheKey = (gameMode.Name, mtime);
            if (ForcedCache.TryGetValue(cacheKey, out HashSet<string>? cached))
                return cached;

            var ini = new IniFile(mpMapsPath);
            if (ini.SectionExists(gameMode.Name))
            {
                string sectionName = ini.GetStringValue(gameMode.Name, "ForcedOptions", string.Empty);
                if (string.IsNullOrWhiteSpace(sectionName))
                    sectionName = gameMode.Name + "ForcedOptions";

                List<string>? keys = ini.GetSectionKeys(sectionName);
                if (keys != null)
                {
                    foreach (string key in keys)
                        forced.Add(key);
                }
            }

            // Issue #4: one aggregate line per (mode, file) resolution, not one
            // line per key — a busy lobby visit used to emit N Info lines.
            if (forced.Count > 0)
                Logger.Log($"SkirmishGameOptionsSnapshot: {forced.Count} controls forced by game mode {gameMode.Name} — saved values ignored.");

            ForcedCache[cacheKey] = forced;
        }
        catch (Exception ex)
        {
            Logger.Log($"SkirmishGameOptionsSnapshot: forced-control lookup failed: {ex.Message}");
        }

        return forced;
    }
}
