using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Issue #6: single source of truth for the start-location dropdown
/// ("ddPlayerStart0..N") semantics — combo index 0 = "-" (unset; the game
/// engine assigns a random free waypoint), k = start location k
/// (1..<see cref="LobbyPlayerSlot.MaxSlots"/>, identity-mapped).
///
/// Previously this contract was spread across three call sites in
/// LobbyPlayerBindingApplier (items construction, UI→state write-back,
/// state→UI mapping) plus long comments; a change to any one of them could
/// silently desync the others (the spectator-regression bug was exactly this
/// shape). All mapping logic now lives here and is locked by unit tests.
/// </summary>
public static class StartLocationCombo
{
    /// <summary>Display text of the unset / random entry.</summary>
    public const string RandomItemText = "-";

    /// <summary>
    /// Builds the combo item list: "-" followed by "1"..<paramref name="maxSlots"/>.
    /// Single construction point — do not build this array elsewhere.
    /// </summary>
    public static string[] Items(int maxSlots)
        => new[] { RandomItemText }
            .Concat(Enumerable.Range(1, maxSlots).Select(i => i.ToString()))
            .ToArray();

    /// <summary>
    /// Combo SelectedIndex → session StartIndex. Identity for valid picks
    /// (0 ↔ 0 "-"; k ↔ k). Out-of-range falls back to 0 (random) — never
    /// produces an invalid StartIndex from a glitchy dropdown.
    /// </summary>
    public static int ToStartIndex(int selectedIndex, int maxSlots)
    {
        if (selectedIndex < 0 || selectedIndex > maxSlots)
            return 0;

        return selectedIndex;
    }

    /// <summary>
    /// Session StartIndex → combo SelectedIndex. Identity for valid values
    /// (0 = "-"; k = "k"). Invalid (< 0 or &gt; maxSlots) maps to -1 so the
    /// dropdown visually clears rather than showing a wrong spot.
    /// </summary>
    public static int ToSelectedIndex(int startIndex, int maxSlots)
        => startIndex >= 0 && startIndex <= maxSlots ? startIndex : -1;

    /// <summary>
    /// True when the dropdown currently holds a real selection (≥ 0 and within
    /// item range). A -1 is a legitimate transient/unset state the caller
    /// resolves by keeping the session value — this is deliberately DIFFERENT
    /// from the side/color/team combos where -1 always means "rebuild transient".
    /// </summary>
    public static bool HasValidSelection(UiNodeViewModel? dropdown)
        => dropdown != null
           && dropdown.SelectedIndex >= 0
           && dropdown.SelectedIndex < dropdown.ComboItems.Count;
}
