using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;
using System.Globalization;

namespace ClientAvalonia.Services;

/// <summary>Loads official multiplayer maps from MPMaps.ini (subset of DXMainClient MapLoader).</summary>
public static class MapCatalogLoader
{
    private const string MultiMapsSection = "MultiMaps";
    private const string GameModesSection = "GameModes";
    private const string CustomMapsDirectory = "Maps/Custom";

    public static IReadOnlyList<GameModeEntry> LoadGameModes()
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return [];

        string mpMapsPath = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.MPMapsIniPath);
        if (!File.Exists(mpMapsPath))
        {
            Logger.Log($"MapCatalogLoader: MPMaps.ini not found at {mpMapsPath}");
            return [];
        }

        var ini = new IniFile(mpMapsPath);
        List<string>? keys = ini.GetSectionKeys(GameModesSection);
        if (keys == null)
            return [];

        var modes = new List<GameModeEntry>();
        foreach (string key in keys)
        {
            string name = ini.GetStringValue(GameModesSection, key, string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            string untranslated = ini.GetStringValue(name, "UIName", name);
            string display = untranslated.L10N($"INI:GameModes:{name}:UIName");
            bool multiplayerOnly = ini.GetBooleanValue(name, "MultiplayerOnly", false);
            string mapCodeIniName = ini.GetStringValue(name, "MapCodeININame", name + ".ini");
            modes.Add(new GameModeEntry
            {
                Name = name,
                DisplayName = display,
                UntranslatedUIName = untranslated,
                MapCodeIniName = mapCodeIniName,
                MultiplayerOnly = multiplayerOnly,
            });
        }

        return modes;
    }

    public static IReadOnlyList<MapEntry> LoadMaps()
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return [];

        var official = LoadOfficialMaps();
        var custom = LoadCustomMaps();
        var combined = new List<MapEntry>(official.Count + custom.Count);
        combined.AddRange(official);
        combined.AddRange(custom);
        Logger.Log($"MapCatalogLoader: loaded {official.Count} official + {custom.Count} custom maps.");
        return combined;
    }

    private static List<MapEntry> LoadOfficialMaps()
    {
        string mpMapsPath = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.MPMapsIniPath);
        if (!File.Exists(mpMapsPath))
        {
            Logger.Log($"MapCatalogLoader: MPMaps.ini not found at {mpMapsPath}");
            return [];
        }

        var ini = new IniFile(mpMapsPath);
        List<string>? keys = ini.GetSectionKeys(MultiMapsSection);
        if (keys == null)
        {
            Logger.Log("MapCatalogLoader: [MultiMaps] section missing.");
            return [];
        }

        string extension = ClientConfiguration.Instance.MapFileExtension;
        var maps = new List<MapEntry>();

        foreach (string key in keys)
        {
            string mapPath = ini.GetStringValue(MultiMapsSection, key, string.Empty).Trim();
            if (string.IsNullOrEmpty(mapPath) && key.Contains('/'))
                mapPath = key;

            if (string.IsNullOrEmpty(mapPath))
                continue;

            mapPath = mapPath.Replace('\\', '/');
            FileInfo mapFile = SafePath.GetFile(
                ProgramConstants.GamePath,
                FormattableString.Invariant($"{mapPath}.{extension}"));

            if (!mapFile.Exists)
                continue;

            if (!ini.SectionExists(mapPath))
                continue;

            string baseSectionName = ini.GetStringValue(mapPath, "BaseSection", string.Empty);
            if (!string.IsNullOrEmpty(baseSectionName))
                ini.CombineSections(baseSectionName, mapPath);

            string untranslated = ini.GetStringValue(mapPath, "Description", Path.GetFileName(mapPath));
            string display = untranslated.L10N($"INI:Maps:{mapPath}:Description");
            string gameModesRaw = ini.GetStringValue(mapPath, "GameModes", "Default");
            string[] gameModes = gameModesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string extraIniName = ini.GetStringValue(mapPath, "ExtraININame", string.Empty);
            int maxPlayers = ini.GetIntValue(mapPath, "MaxPlayers", 0);
            int minPlayers = ini.GetIntValue(mapPath, "MinPlayers", 0);
            bool enforceMaxPlayers = ini.GetBooleanValue(mapPath, "EnforceMaxPlayers", false);
            bool multiplayerOnly = ini.GetBooleanValue(mapPath, "MultiplayerOnly", false);

            string previewRelative = BuildOfficialPreviewPath(mapFile, ini, mapPath);
            string sha1 = Utilities.CalculateSHA1ForFile(mapFile.FullName);
            MapGeometryPayload geometry = ReadOfficialMapGeometry(ini, mapPath);

            maps.Add(new MapEntry
            {
                BaseFilePath = mapPath,
                DisplayName = display,
                UntranslatedName = untranslated,
                GameModes = gameModes,
                Sha1 = sha1,
                PreviewRelativePath = previewRelative,
                ExtraIniName = extraIniName,
                IsOfficial = true,
                IsCustom = false,
                MultiplayerOnly = multiplayerOnly,
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                EnforceMaxPlayers = enforceMaxPlayers,
                CompleteFilePath = mapFile.FullName,
                Waypoints = geometry.Waypoints,
                ActualSize = geometry.ActualSize,
                LocalSize = geometry.LocalSize,
                MapX = geometry.MapX,
                MapY = geometry.MapY,
                MapWidth = geometry.MapWidth,
                MapHeight = geometry.MapHeight,
            });
        }

        return maps;
    }

    private readonly record struct MapGeometryPayload(
        IReadOnlyList<string> Waypoints,
        IReadOnlyList<string> ActualSize,
        IReadOnlyList<string> LocalSize,
        int MapX,
        int MapY,
        int MapWidth,
        int MapHeight);

    private static MapGeometryPayload ReadOfficialMapGeometry(IniFile ini, string mapPath)
    {
        var waypoints = new List<string>(StartingLocationProjector.MaxPlayers);
        for (int i = 0; i < StartingLocationProjector.MaxPlayers; i++)
        {
            string waypoint = ini.GetStringValue(mapPath, "Waypoint" + i, string.Empty);
            if (string.IsNullOrEmpty(waypoint))
                break;
            waypoints.Add(waypoint);
        }

        string[] actualSize = SplitCsv4(ini.GetStringValue(mapPath, "Size", "0,0,0,0"));
        string[] localSize = SplitCsv4(ini.GetStringValue(mapPath, "LocalSize", "0,0,0,0"));
        int mapX = ini.GetIntValue(mapPath, "X", 0);
        int mapY = ini.GetIntValue(mapPath, "Y", 0);
        int mapWidth = ini.GetIntValue(mapPath, "Width", 0);
        int mapHeight = ini.GetIntValue(mapPath, "Height", 0);

        return new MapGeometryPayload(waypoints, actualSize, localSize, mapX, mapY, mapWidth, mapHeight);
    }

    private static MapGeometryPayload ReadCustomMapGeometry(IniFile customMapIni)
    {
        var waypoints = new List<string>(StartingLocationProjector.MaxPlayers);
        for (int i = 0; i < StartingLocationProjector.MaxPlayers; i++)
        {
            string waypoint = customMapIni.GetStringValue("Waypoints", i.ToString(CultureInfo.InvariantCulture), string.Empty);
            if (string.IsNullOrEmpty(waypoint))
                break;
            waypoints.Add(waypoint);
        }

        string[] actualSize = SplitCsv4(customMapIni.GetStringValue("Map", "Size", "0,0,0,0"));
        string[] localSize = SplitCsv4(customMapIni.GetStringValue("Map", "LocalSize", "0,0,0,0"));
        int mapX = customMapIni.GetIntValue("Map", "X", 0);
        int mapY = customMapIni.GetIntValue("Map", "Y", 0);
        int mapWidth = customMapIni.GetIntValue("Map", "Width", 0);
        int mapHeight = customMapIni.GetIntValue("Map", "Height", 0);

        return new MapGeometryPayload(waypoints, actualSize, localSize, mapX, mapY, mapWidth, mapHeight);
    }

    private static string[] SplitCsv4(string raw)
    {
        string[] parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 4)
            return [parts[0], parts[1], parts[2], parts[3]];
        return ["0", "0", "0", "0"];
    }

    private static string BuildOfficialPreviewPath(FileInfo mapFile, IniFile mpMapsIni, string mapPath)
    {
        string previewImage = mpMapsIni.GetStringValue(mapPath, "PreviewImage", mapFile.Name);
        return GameAssetResolver.ResolveMapPreviewRelativePath(mapPath, previewImage);
    }

    private static List<MapEntry> LoadCustomMaps()
    {
        DirectoryInfo customDir = SafePath.GetDirectory(ProgramConstants.GamePath, CustomMapsDirectory);
        if (!customDir.Exists)
            return [];

        string extension = ClientConfiguration.Instance.MapFileExtension;
        string[] allowedModes = ClientConfiguration.Instance.AllowedCustomGameModes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var maps = new List<MapEntry>();
        foreach (FileInfo mapFile in customDir.EnumerateFiles($"*.{extension}"))
        {
            MapEntry? entry = TryLoadCustomMap(mapFile, allowedModes);
            if (entry != null)
                maps.Add(entry);
        }

        return maps;
    }

    private static MapEntry? TryLoadCustomMap(FileInfo mapFile, string[] allowedModes)
    {
        try
        {
            string baseFilePath = mapFile.FullName[ProgramConstants.GamePath.Length..]
                .Replace('\\', '/')
                .TrimStart('/');
            baseFilePath = baseFilePath[..^Path.GetExtension(baseFilePath).Length];

            var customMapIni = new IniFile { FileName = mapFile.FullName };
            customMapIni.AddSection("Basic");
            customMapIni.AddSection("Map");
            customMapIni.AddSection("Waypoints");
            customMapIni.AddSection("ForcedOptions");
            customMapIni.AddSection("ForcedSpawnIniOptions");
            customMapIni.AllowNewSections = false;
            customMapIni.Parse();

            string untranslated = customMapIni.GetStringValue("Basic", "Name", "Unnamed map");
            string display = untranslated.L10N($"INI:Maps:{baseFilePath}:Description");

            string gameModesString = customMapIni.GetStringValue("Basic", "GameModes", string.Empty);
            if (string.IsNullOrEmpty(gameModesString))
                gameModesString = customMapIni.GetStringValue("Basic", "GameMode", "Default");

            string[] gameModes = gameModesString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (gameModes.Length == 0)
                return null;

            for (int i = 0; i < gameModes.Length; i++)
            {
                string mode = gameModes[i].Trim();
                if (mode.Length > 0)
                    gameModes[i] = char.ToUpperInvariant(mode[0]) + mode[1..];
            }

            if (allowedModes.Length > 0 && !gameModes.Any(gm => allowedModes.Contains(gm, StringComparer.OrdinalIgnoreCase)))
                return null;

            int maxPlayers = customMapIni.GetIntValue("Basic", "ClientMaxPlayer", 0);
            if (maxPlayers == 0)
                maxPlayers = customMapIni.GetIntValue("Basic", "MaxPlayer", 0);

            int minPlayers = customMapIni.GetIntValue("Basic", "MinPlayer", 0);
            bool enforceMaxPlayers = customMapIni.GetBooleanValue("Basic", "EnforceMaxPlayers", true);
            bool multiplayerOnly = customMapIni.GetBooleanValue("Basic", "ClientMultiplayerOnly", false);

            string previewRelative = GameAssetResolver.ResolveMapPreviewRelativePath(baseFilePath);
            if (string.IsNullOrEmpty(previewRelative))
                previewRelative = Path.ChangeExtension(baseFilePath, ".png")!.Replace('\\', '/');
            string sha1 = Utilities.CalculateSHA1ForFile(mapFile.FullName);
            MapGeometryPayload geometry = ReadCustomMapGeometry(customMapIni);

            return new MapEntry
            {
                BaseFilePath = baseFilePath,
                DisplayName = display,
                UntranslatedName = untranslated,
                GameModes = gameModes,
                Sha1 = sha1,
                PreviewRelativePath = previewRelative,
                IsOfficial = false,
                IsCustom = true,
                MultiplayerOnly = multiplayerOnly,
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                EnforceMaxPlayers = enforceMaxPlayers,
                CompleteFilePath = mapFile.FullName,
                Waypoints = geometry.Waypoints,
                ActualSize = geometry.ActualSize,
                LocalSize = geometry.LocalSize,
                MapX = geometry.MapX,
                MapY = geometry.MapY,
                MapWidth = geometry.MapWidth,
                MapHeight = geometry.MapHeight,
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"MapCatalogLoader: failed to load custom map {mapFile.FullName}: {ex.Message}");
            return null;
        }
    }
}
