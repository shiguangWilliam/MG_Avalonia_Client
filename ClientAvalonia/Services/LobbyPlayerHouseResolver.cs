using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Resolves UI side/color picks into spawn.ini house indices (XNA PlayerHouseInfo + Randomize).</summary>
public static class LobbyPlayerHouseResolver
{
    public sealed class ResolvedHouse
    {
        public int InternalSideIndex { get; init; }

        public int GameColorIndex { get; init; }

        public bool IsSpectator { get; init; }

        /// <summary>
        /// DX PlayerHouseInfo.RandomizeStart outcome: N-1 for an explicitly picked
        /// start (Slot.StartIndex is 1-based), 90 for spectators, -1 for "let the
        /// game randomize" (unset). Consumed by [SpawnLocations] writers.
        /// </summary>
        public int StartingWaypoint { get; init; }
    }

    /// <summary>
    /// Phase 3 P3-1：Session-aware 主入口——吃 <see cref="IReadOnlyList{IPlayerSlot}"/>，
    /// 不再依赖 <see cref="LobbyPlayerSlot"/> 具体类型。
    /// </summary>
    public static IReadOnlyList<ResolvedHouse> Resolve(
        IReadOnlyList<IPlayerSlot> occupiedSlots,
        int randomSeed)
    {
        if (occupiedSlots.Count == 0)
            return [];

        LobbySideCatalogSnapshot sides = LobbySideCatalog.GetSnapshot();
        int sideCount = sides.SideCount;
        int randomSelectorCount = sides.RandomSelectorCount;
        int spectatorSideId = sides.SpectatorSideIndex;

        var random = new Random(randomSeed);
        var freeColors = Enumerable.Range(0, MultiplayerColorCatalog.Load().Count).ToList();
        var mpColors = MultiplayerColorCatalog.Load();
        var randomSelectors = sides.RandomSelectorSideIds;
        var disallowedSides = new bool[sideCount];

        var resolved = new List<ResolvedHouse>(occupiedSlots.Count);
        foreach (IPlayerSlot slot in occupiedSlots)
        {
            int sideId = slot.SideIndex;
            bool isSpectator = sideId == spectatorSideId;
            int sideIndex = ResolveSideIndex(sideId, sideCount, randomSelectorCount, randomSelectors, random, disallowedSides);
            int colorIndex = ResolveColorIndex(slot.ColorIndex, freeColors, mpColors, random);

            resolved.Add(new ResolvedHouse
            {
                InternalSideIndex = ToInternalSideIndex(sideIndex, isSpectator),
                GameColorIndex = colorIndex,
                IsSpectator = isSpectator,
                StartingWaypoint = ResolveStartingWaypoint(slot.StartIndex, isSpectator),
            });
        }

        return resolved;
    }

    /// <summary>
    /// DX PlayerHouseInfo.RandomizeStart (client-managed branch): an explicit pick
    /// becomes waypoint = pick - 1; spectators pin to 90 (spawner observer waypoint);
    /// unset (0) stays -1 so the game's own placement logic runs.
    /// </summary>
    private static int ResolveStartingWaypoint(int startIndex, bool isSpectator)
    {
        if (isSpectator)
            return 90;

        return startIndex > 0 ? startIndex - 1 : -1;
    }

    private static int ResolveSideIndex(
        int sideId,
        int sideCount,
        int randomSelectorCount,
        IReadOnlyList<int[]> randomSelectors,
        Random random,
        bool[] disallowedSideArray)
    {
        if (sideId == 0 || sideId == sideCount + randomSelectorCount)
        {
            int sideIndex;
            do
                sideIndex = random.Next(0, sideCount);
            while (sideCount > 0 && disallowedSideArray[sideIndex]);

            return sideIndex;
        }

        if (sideId < randomSelectorCount)
        {
            int[] randomSides = randomSelectors[sideId - 1];
            int sideIndex;
            do
                sideIndex = randomSides[random.Next(0, randomSides.Length)];
            while (disallowedSideArray[sideIndex]);

            return sideIndex;
        }

        return sideId - randomSelectorCount;
    }

    private static int ResolveColorIndex(
        int colorId,
        List<int> freeColors,
        IReadOnlyList<MultiplayerColorCatalog.MultiplayerColorEntry> mpColors,
        Random random)
    {
        if (mpColors.Count == 0)
            return Math.Max(0, colorId);

        if (colorId == 0)
        {
            if (freeColors.Count == 0)
                return mpColors[0].GameColorIndex;

            int randomizedColorIndex = random.Next(0, freeColors.Count);
            int actualColorId = freeColors[randomizedColorIndex];
            freeColors.RemoveAt(randomizedColorIndex);
            return mpColors[actualColorId].GameColorIndex;
        }

        int pick = colorId - 1;
        if (pick >= 0 && pick < mpColors.Count)
        {
            freeColors.Remove(pick);
            return mpColors[pick].GameColorIndex;
        }

        return mpColors[0].GameColorIndex;
    }

    private static int ToInternalSideIndex(int sideIndex, bool isSpectator)
    {
        if (isSpectator && !string.IsNullOrEmpty(AppState.Configuration.Legacy.SpectatorInternalSideIndex)
            && int.TryParse(AppState.Configuration.Legacy.SpectatorInternalSideIndex, out int spectatorIndex))
            return spectatorIndex;

        string internalIndices = AppState.Configuration.Legacy.InternalSideIndices;
        if (!string.IsNullOrEmpty(internalIndices))
        {
            int[] mapped = internalIndices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToArray();
            if (sideIndex >= 0 && sideIndex < mapped.Length)
                return mapped[sideIndex];
        }

        return sideIndex;
    }

    /// <summary>
    /// Maps AI level (0/1/2) to spawn.ini HouseHandicaps value (DX GameLobbyBase).
    /// Phase 3 P3-2：从 LobbyPlayerState 迁出到此处（与 house index 解析同一工具类）。
    /// </summary>
    public static int HouseHandicapFromAiLevel(int aiLevel) => Math.Abs(aiLevel - 2);
}
