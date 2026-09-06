using System.Runtime.CompilerServices;
using ClientAvalonia.Rendering;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>
/// Issue #21: explicit wiring/lifecycle flags for lobby binding appliers.
///
/// Previously these one-shot flags lived inside <c>Node.Props</c>
/// (“LobbyPlayerSlotsBuilt”, “ChannelLobbyWired”, …) — a dictionary meant for
/// INI render properties — which had two problems:
/// <list type="bullet">
/// <item>Props could no longer be treated as a pure render-property bag; flag
/// keys leaked into every Props dump/serialization path.</item>
/// <item>A modder writing such a key in INI (or a future code path copying
/// Props) would silently change binding behavior.</item>
/// </list>
/// Flags are now attached via <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// (same lifetime semantics as the existing PanelShields pattern) and
/// <c>Node.Props</c> is contractually a pure render-property bag again.
/// </summary>
public static class LobbyUiState
{
    private static readonly ConditionalWeakTable<UiNodeViewModel, Flags> Table = new();

    /// <summary>True once <c>LobbyPlayerBindingApplier</c> built the slot rows
    /// (dropdowns + captions) for this panel. Later Apply calls only re-layout
    /// and sync from state.</summary>
    public static bool GetPlayerSlotsBuilt(UiNodeViewModel panel)
        => Table.GetValue(panel, static _ => new Flags()).PlayerSlotsBuilt;

    public static void MarkPlayerSlotsBuilt(UiNodeViewModel panel)
        => Table.GetValue(panel, static _ => new Flags()).PlayerSlotsBuilt = true;

    /// <summary>True once the channel-lobby SelectionChanged handler was wired
    /// onto this dropdown (prevents duplicate subscriptions on re-apply).</summary>
    public static bool GetChannelLobbyWired(UiNodeViewModel dropdown)
        => Table.GetValue(dropdown, static _ => new Flags()).ChannelLobbyWired;

    public static void MarkChannelLobbyWired(UiNodeViewModel dropdown)
        => Table.GetValue(dropdown, static _ => new Flags()).ChannelLobbyWired = true;

    /// <summary>True once the hosted-games list SelectionChanged handler was
    /// wired onto this list (prevents duplicate subscriptions on re-apply).</summary>
    public static bool GetChannelLobbyGamesWired(UiNodeViewModel list)
        => Table.GetValue(list, static _ => new Flags()).ChannelLobbyGamesWired;

    public static void MarkChannelLobbyGamesWired(UiNodeViewModel list)
        => Table.GetValue(list, static _ => new Flags()).ChannelLobbyGamesWired = true;

    private sealed class Flags
    {
        public bool PlayerSlotsBuilt;
        public bool ChannelLobbyWired;
        public bool ChannelLobbyGamesWired;
    }
}
