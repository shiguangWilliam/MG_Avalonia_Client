using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>One lobby side dropdown row (protocol SideId = list index).</summary>
public sealed class LobbySideEntry
{
    public required string InternalName { get; init; }

    public required string DisplayName { get; init; }

    public string IconBaseName { get; init; } = string.Empty;

    /// <summary>Index on wire (PO/OR CTCP) and in combo box.</summary>
    public int ProtocolIndex { get; init; }

    public bool IsSpectator { get; init; }

    public bool IsRandomSelector { get; init; }

    public bool IsRandomSide { get; init; }
}

/// <summary>
/// Lobby side list aligned with GameLobbyBase.InitPlayerOptionDropdowns:
/// Random → RandomSelectors (GameOptions.ini) → Sides (GameOptions [General]) → Spectator.
/// </summary>
public static class LobbySideCatalog
{
    public const string RandomInternalName = "Random";
    public const string SpectatorInternalName = "Spectator";

    private static LobbySideCatalogSnapshot? _cachedWithSpectator;
    private static LobbySideCatalogSnapshot? _cachedWithoutSpectator;

    public static LobbySideCatalogSnapshot GetSnapshot(bool includeSpectator = true)
    {
        if (includeSpectator)
            return _cachedWithSpectator ??= BuildSnapshot(includeSpectator: true);

        return _cachedWithoutSpectator ??= BuildSnapshot(includeSpectator: false);
    }

    public static IReadOnlyList<LobbySideEntry> Load(bool includeSpectator = true)
        => GetSnapshot(includeSpectator).Entries;

    /// <summary>SideCount from GameOptions.ini [General] Sides= (XNA SideCount).</summary>
    public static int SideCount => GetSnapshot().SideCount;

    /// <summary>RandomSelectors.Count + 1 (XNA RandomSelectorCount).</summary>
    public static int RandomSelectorCount => GetSnapshot().RandomSelectorCount;

    /// <summary>SideCount + RandomSelectorCount (XNA GetSpectatorSideIndex).</summary>
    public static int SpectatorSideIndex => GetSnapshot().SpectatorSideIndex;

    public static IReadOnlyList<int[]> RandomSelectorSideIds => GetSnapshot().RandomSelectorSideIds;

    public static void InvalidateCache()
    {
        _cachedWithSpectator = null;
        _cachedWithoutSpectator = null;
    }

    private static LobbySideCatalogSnapshot BuildSnapshot(bool includeSpectator)
    {
        string[] sides = ClientConfiguration.Instance.Sides
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var randomSelectors = LoadRandomSelectors(sides.Length);
        var entries = new List<LobbySideEntry>();
        int index = 0;

        entries.Add(new LobbySideEntry
        {
            InternalName = RandomInternalName,
            DisplayName = RandomInternalName.L10N("Client:Sides:RandomSide"),
            IconBaseName = "random",
            ProtocolIndex = index++,
            IsRandomSide = true,
        });

        foreach ((string name, int[] sideIds) in randomSelectors)
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = name,
                DisplayName = name.L10N($"INI:Sides:{name}"),
                IconBaseName = name,
                ProtocolIndex = index++,
                IsRandomSelector = true,
            });
        }

        foreach (string side in sides)
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = side,
                DisplayName = side.L10N($"INI:Sides:{side}"),
                IconBaseName = side,
                ProtocolIndex = index++,
            });
        }

        int randomSelectorCount = randomSelectors.Count + 1;
        int spectatorSideIndex = sides.Length + randomSelectorCount;

        if (includeSpectator)
        {
            entries.Add(new LobbySideEntry
            {
                InternalName = SpectatorInternalName,
                DisplayName = SpectatorInternalName.L10N("Client:Sides:SpectatorSide"),
                IconBaseName = "spectator",
                ProtocolIndex = index,
                IsSpectator = true,
            });
        }

        return new LobbySideCatalogSnapshot(
            entries,
            sides.Length,
            randomSelectorCount,
            spectatorSideIndex,
            randomSelectors.Select(r => r.SideIds).ToList());
    }

    /// <summary>GameOptions.ini [RandomSelectors] (XNA GetRandomSelectors).</summary>
    private static List<(string Name, int[] SideIds)> LoadRandomSelectors(int sideCount)
    {
        string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS);
        if (!File.Exists(path))
            return [];

        var ini = new IniFile(path);
        List<string>? keys = ini.GetSectionKeys("RandomSelectors");
        if (keys == null)
            return [];

        var selectors = new List<(string, int[])>();
        foreach (string key in keys)
        {
            try
            {
                string[] tmp = ini.GetStringListValue("RandomSelectors", key, string.Empty);
                var sideIds = Array.ConvertAll(tmp, int.Parse)
                    .Where(id => id >= 0 && id < sideCount)
                    .ToArray();

                if (sideIds.Length > 1)
                    selectors.Add((key, sideIds));
            }
            catch (FormatException)
            {
            }
        }

        return selectors;
    }
}

public sealed class LobbySideCatalogSnapshot
{
    internal LobbySideCatalogSnapshot(
        IReadOnlyList<LobbySideEntry> entries,
        int sideCount,
        int randomSelectorCount,
        int spectatorSideIndex,
        IReadOnlyList<int[]> randomSelectorSideIds)
    {
        Entries = entries;
        SideCount = sideCount;
        RandomSelectorCount = randomSelectorCount;
        SpectatorSideIndex = spectatorSideIndex;
        RandomSelectorSideIds = randomSelectorSideIds;
    }

    public IReadOnlyList<LobbySideEntry> Entries { get; }

    public int SideCount { get; }

    public int RandomSelectorCount { get; }

    public int SpectatorSideIndex { get; }

    public IReadOnlyList<int[]> RandomSelectorSideIds { get; }
}
