using ClientAvalonia.Domain;

namespace ClientAvalonia.Session;

/// <summary>
/// Helpers for building a full <see cref="LobbyPlayerSlot.MaxSlots"/> grid before
/// <see cref="IPlayerSlotSink.CopyFrom"/> — keeps bulk writes on the sink contract.
/// </summary>
internal static class LobbySlotGrid
{
    public static List<LobbyPlayerSlot> CreateEmpty()
        => Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
            .Select(_ => new LobbyPlayerSlot())
            .ToList();

    public static void ApplyToSink(IGameSession session, IReadOnlyList<IPlayerSlot> grid)
        => session.SlotSink.CopyFrom(grid);
}
