using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Resolves UI side/color picks into spawn.ini house indices (XNA PlayerHouseInfo + Randomize).</summary>
public static class LobbyPlayerHouseResolver
{
    public sealed class ResolvedHouse
    {
        public int InternalSideIndex { get; init; }

        public int GameColorIndex { get; init; }

        public bool IsSpectator { get; init; }
    }

    public static IReadOnlyList<ResolvedHouse> Resolve(
        IReadOnlyList<LobbyPlayerSlot> occupiedSlots,
        int randomSeed)
    {
        if (occupiedSlots.Count == 0)
            return [];

        string[] sideNames = ClientConfiguration.Instance.Sides
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int sideCount = sideNames.Length;
        int randomSelectorCount = LoadRandomSelectorCount();
        int spectatorSideId = sideCount + randomSelectorCount;

        var random = new Random(randomSeed);
        var freeColors = Enumerable.Range(0, MultiplayerColorCatalog.Load().Count).ToList();
        var mpColors = MultiplayerColorCatalog.Load();
        var randomSelectors = LoadRandomSelectors();
        var disallowedSides = new bool[sideCount];

        var resolved = new List<ResolvedHouse>(occupiedSlots.Count);
        foreach (LobbyPlayerSlot slot in occupiedSlots)
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
            });
        }

        return resolved;
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
        if (isSpectator && !string.IsNullOrEmpty(ClientConfiguration.Instance.SpectatorInternalSideIndex)
            && int.TryParse(ClientConfiguration.Instance.SpectatorInternalSideIndex, out int spectatorIndex))
            return spectatorIndex;

        string internalIndices = ClientConfiguration.Instance.InternalSideIndices;
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

    private static int LoadRandomSelectorCount()
        => LoadRandomSelectors().Count + 1;

    private static List<int[]> LoadRandomSelectors()
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS);
        if (!File.Exists(path))
            return [];

        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("RandomSelectors");
        if (keys == null)
            return [];

        var selectors = new List<int[]>();
        foreach (string key in keys)
        {
            string raw = ini.GetStringValue("RandomSelectors", key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            int[] sides = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .ToArray();
            if (sides.Length > 0)
                selectors.Add(sides);
        }

        return selectors;
    }
}
