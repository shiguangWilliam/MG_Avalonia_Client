using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Shared map/mission catalog loaded at startup (maps from MPMaps.ini, missions from Battle.ini).</summary>
public sealed class GameResourceCatalog
{
    private static readonly GameResourceCatalog _instance = new();

    private readonly object _lock = new();

    public static GameResourceCatalog Instance => _instance;

    public bool IsLoaded { get; private set; }

    public event Action? Loaded;

    public IReadOnlyList<GameModeEntry> GameModes { get; private set; } = [];

    public IReadOnlyList<MapEntry> Maps { get; private set; } = [];

    public IReadOnlyList<MissionEntry> Missions { get; private set; } = [];

    public string FavoriteMapsLabel { get; private set; } = "Favorite Maps";

    public void EnsureLoaded()
    {
        if (!ClientCoreBootstrap.IsInitialized)
        {
            Logger.Log("GameResourceCatalog: skipped — ClientCore not initialized.");
            return;
        }

        if (IsLoaded && Maps.Count > 0)
            return;

        lock (_lock)
        {
            if (!ClientCoreBootstrap.IsInitialized)
                return;

            if (IsLoaded && Maps.Count > 0)
                return;


            GameModes = MapCatalogLoader.LoadGameModes()
                .Where(m => !m.MultiplayerOnly)
                .ToList();
            Maps = MapCatalogLoader.LoadMaps();
            Missions = MissionCatalogLoader.LoadMissions();
            FavoriteMapsLabel = "Favorite Maps".L10N("Client:Main:FavoriteMaps");
            IsLoaded = Maps.Count > 0 || GameModes.Count > 0 || Missions.Count > 0;
            if (IsLoaded)
                Loaded?.Invoke();
        }
    }

    /// <summary>Drop cached catalogs before binding a different workspace.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            IsLoaded = false;
            GameModes = [];
            Maps = [];
            Missions = [];
        }
    }

    /// <summary>Filter index 0 = favorites; 1+ = game mode at index-1.</summary>
    public IReadOnlyList<MapEntry> GetMapsForFilterIndex(int filterIndex)
    {
        EnsureLoaded();

        if (filterIndex <= LobbySessionState.FavoriteFilterIndex)
            return GetFavoriteMaps();

        int gameModeIndex = filterIndex - LobbySessionState.FavoriteFilterIndex - 1;
        if (gameModeIndex < 0 || gameModeIndex >= GameModes.Count)
            gameModeIndex = 0;

        return GetMapsForGameModeIndex(gameModeIndex);
    }

    public IReadOnlyList<MapEntry> GetMapsForGameModeIndex(int gameModeIndex)
    {
        EnsureLoaded();
        if (GameModes.Count == 0)
            return Maps;

        if (gameModeIndex < 0 || gameModeIndex >= GameModes.Count)
            gameModeIndex = 0;

        string modeName = GameModes[gameModeIndex].Name;
        return Maps
            .Where(m => m.GameModes.Any(gm => gm.Equals(modeName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public GameModeEntry? GetGameModeForFilterIndex(int filterIndex)
    {
        EnsureLoaded();
        if (filterIndex <= LobbySessionState.FavoriteFilterIndex)
            return GameModes.Count > 0 ? GameModes[0] : null;

        int gameModeIndex = filterIndex - LobbySessionState.FavoriteFilterIndex - 1;
        return gameModeIndex >= 0 && gameModeIndex < GameModes.Count ? GameModes[gameModeIndex] : null;
    }

    /// <summary>Combo filter index (0=favorites, 1+=game mode) that includes <paramref name="map"/>.</summary>
    public int FindFilterIndexForMap(MapEntry map)
    {
        EnsureLoaded();

        for (int modeIndex = 0; modeIndex < GameModes.Count; modeIndex++)
        {
            string modeName = GameModes[modeIndex].Name;
            if (!map.GameModes.Any(gm => gm.Equals(modeName, StringComparison.OrdinalIgnoreCase)))
                continue;

            return LobbySessionState.FavoriteFilterIndex + 1 + modeIndex;
        }

        return GameModes.Count > 0 ? LobbySessionState.FavoriteFilterIndex + 1 : LobbySessionState.FavoriteFilterIndex;
    }

    public static int IndexOfMapInList(IReadOnlyList<MapEntry> maps, MapEntry map)
    {
        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i].Sha1.Equals(map.Sha1, StringComparison.OrdinalIgnoreCase)
                && maps[i].BaseFilePath.Equals(map.BaseFilePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public IReadOnlyList<MapEntry> GetFavoriteMaps()
    {
        EnsureLoaded();
        var favorites = new List<MapEntry>();

        foreach (MapEntry map in Maps)
        {
            foreach (string gameMode in map.GameModes)
            {
                if (IsFavoriteMap(map, gameMode) && !favorites.Contains(map))
                {
                    favorites.Add(map);
                    break;
                }
            }
        }

        return favorites;
    }

    public bool IsFavoriteMap(MapEntry map, string gameModeName)
    {
        EnsureLoaded();
        return UserINISettings.Instance.IsFavoriteMap(map.Sha1, map.UntranslatedName, gameModeName);
    }

    public bool ToggleFavoriteMap(MapEntry map, GameModeEntry? gameMode)
    {
        EnsureLoaded();
        string modeName = gameMode?.Name ?? map.GameModes.FirstOrDefault() ?? "Default";
        bool wasFavorite = IsFavoriteMap(map, modeName);
        UserINISettings.Instance.ToggleFavoriteMap(map.Sha1, modeName, wasFavorite);
        return !wasFavorite;
    }

    /// <summary>
    /// A1 fix: interface-typed overload so <see cref="IResourceCatalog"/> consumers
    /// (and unit tests with mock <see cref="IMapResource"/> implementations) are
    /// accepted without being forced to construct <see cref="MapEntry"/> instances.
    /// The legacy <c>(MapEntry, GameModeEntry?)</c> overload delegates here for
    /// backward compatibility with internal callers (e.g. <c>MainWindow</c>).
    /// </summary>
    public bool ToggleFavoriteMap(IMapResource map, IGameModeResource? gameMode)
    {
        ArgumentNullException.ThrowIfNull(map);
        EnsureLoaded();
        string modeName = gameMode?.Name ?? map.GameModes.FirstOrDefault() ?? "Default";
        bool wasFavorite = UserINISettings.Instance.IsFavoriteMap(map.Sha1, map.UntranslatedName, modeName);
        UserINISettings.Instance.ToggleFavoriteMap(map.Sha1, modeName, wasFavorite);
        return !wasFavorite;
    }

    public int PickRandomMapIndex(IReadOnlyList<MapEntry> visibleMaps, int playerCount = 2)
    {
        if (visibleMaps.Count == 0)
            return -1;

        List<MapEntry> candidates = visibleMaps
            .Where(m => m.MaxPlayers == 0 || m.MaxPlayers >= playerCount)
            .ToList();

        if (candidates.Count == 0)
            candidates = visibleMaps.ToList();

        MapEntry picked = candidates[Random.Shared.Next(candidates.Count)];
        for (int i = 0; i < visibleMaps.Count; i++)
        {
            if (visibleMaps[i].Sha1 == picked.Sha1 && visibleMaps[i].BaseFilePath == picked.BaseFilePath)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// A1 fix: interface-typed overload so <see cref="IResourceCatalog"/> consumers
    /// (and unit tests with mock <see cref="IMapResource"/> implementations) are
    /// accepted. Index matching falls back to reference equality then Sha1 equality
    /// (interface implementations may not expose <c>BaseFilePath</c>, so we cannot
    /// use the same comparison as the legacy overload).
    /// </summary>
    public int PickRandomMapIndex(IReadOnlyList<IMapResource> visibleMaps, int playerCount = 2)
    {
        ArgumentNullException.ThrowIfNull(visibleMaps);
        if (visibleMaps.Count == 0)
            return -1;

        List<IMapResource> candidates = visibleMaps
            .Where(m => m.MaxPlayers == 0 || m.MaxPlayers >= playerCount)
            .ToList();

        if (candidates.Count == 0)
            candidates = visibleMaps.ToList();

        IMapResource picked = candidates[Random.Shared.Next(candidates.Count)];

        // Prefer reference equality (covers the common MapEntry case, matches
        // legacy behavior exactly); fall back to Sha1 for mock/cross-type matches.
        for (int i = 0; i < visibleMaps.Count; i++)
        {
            IMapResource m = visibleMaps[i];
            if (ReferenceEquals(m, picked))
                return i;
        }

        for (int i = 0; i < visibleMaps.Count; i++)
        {
            if (string.Equals(visibleMaps[i].Sha1, picked.Sha1, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    public IReadOnlyList<MapEntry> FilterMapsBySearch(IReadOnlyList<MapEntry> maps, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return maps;

        search = search.Trim();
        string[] searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var exactMatches = maps.Where(m =>
            m.DisplayName.Equals(search, StringComparison.CurrentCultureIgnoreCase)
            || m.UntranslatedName.Equals(search, StringComparison.InvariantCultureIgnoreCase)).ToList();

        var substringMatches = maps.Except(exactMatches).Where(m =>
            m.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || m.UntranslatedName.Contains(search, StringComparison.InvariantCultureIgnoreCase)).ToList();

        var multiWordMatches = maps.Except(exactMatches).Except(substringMatches).Where(m =>
        {
            bool allInDisplay = searchWords.All(word =>
                m.DisplayName.Contains(word, StringComparison.CurrentCultureIgnoreCase));
            bool allInUntranslated = searchWords.All(word =>
                m.UntranslatedName.Contains(word, StringComparison.InvariantCultureIgnoreCase));
            return allInDisplay || allInUntranslated;
        }).ToList();

        return [.. exactMatches, .. substringMatches, .. multiWordMatches];
    }

    public IReadOnlyList<MissionEntry> GetMissionsForSideFilter(CampaignSideFilter filter)
    {
        EnsureLoaded();
        if (filter == CampaignSideFilter.All)
            return Missions;

        string sideName = SideNameForFilter(filter);
        if (string.IsNullOrEmpty(sideName))
            return Missions;

        var filtered = new List<MissionEntry>();
        int index = 0;
        while (index < Missions.Count)
        {
            MissionEntry entry = Missions[index];
            if (entry.IsHeader)
            {
                int sectionEnd = index + 1;
                while (sectionEnd < Missions.Count && !Missions[sectionEnd].IsHeader)
                    sectionEnd++;

                bool sectionHasMatch = false;
                for (int i = index + 1; i < sectionEnd; i++)
                {
                    if (MissionMatchesSide(Missions[i], sideName))
                    {
                        sectionHasMatch = true;
                        break;
                    }
                }

                if (sectionHasMatch)
                {
                    filtered.Add(entry);
                    for (int i = index + 1; i < sectionEnd; i++)
                    {
                        if (MissionMatchesSide(Missions[i], sideName))
                            filtered.Add(Missions[i]);
                    }
                }

                index = sectionEnd;
                continue;
            }

            if (MissionMatchesSide(entry, sideName))
                filtered.Add(entry);

            index++;
        }

        return filtered;
    }

    private static string SideNameForFilter(CampaignSideFilter filter)
        => filter switch
        {
            CampaignSideFilter.Allied => "Allied",
            CampaignSideFilter.Soviet => "Soviet",
            CampaignSideFilter.Ackville => "Ackville",
            _ => string.Empty,
        };

    private static bool MissionMatchesSide(MissionEntry mission, string sideName)
        => !mission.IsHeader
           && mission.SideName.Equals(sideName, StringComparison.OrdinalIgnoreCase);

    public MissionEntry? GetMission(int index)
    {
        EnsureLoaded();
        return index >= 0 && index < Missions.Count ? Missions[index] : null;
    }

    /// <summary>
    /// Issue #28 test hook: drops cached catalogs so a later EnsureLoaded()
    /// re-reads from the (test-local) game root. Serial test classes that
    /// swap ProgramConstants roots must call this in Dispose, otherwise the
    /// previous root's maps leak into every later test.
    /// </summary>
    internal void Reset()
    {
        lock (_lock)
        {
            IsLoaded = false;
            GameModes = [];
            Maps = [];
            Missions = [];
        }
    }
}
