using ClientAvalonia.Domain;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Loads map/game-mode forced spawn.ini keys from MPMaps.ini or custom map files.</summary>
public static class ForcedSpawnCatalog
{
    private const string ForcedSpawnIniOptionsSuffix = "ForcedSpawnIniOptions";

    public static void ApplyGameModeForcedSpawn(IniFile spawnIni, GameModeEntry gameMode)
    {
        string mpMapsPath = SafePath.CombineFilePath(AppState.Environment.GamePath, AppState.Configuration.Legacy.MPMapsIniPath);
        if (!File.Exists(mpMapsPath))
            return;

        var ini = new IniFile(mpMapsPath);
        if (!ini.SectionExists(gameMode.Name))
            return;

        string sectionName = ini.GetStringValue(
            gameMode.Name,
            "ForcedSpawnIniOptions",
            gameMode.Name + ForcedSpawnIniOptionsSuffix);

        ApplySpawnIniSection(spawnIni, ini, sectionName, $"GameMode:{gameMode.Name}");
    }

    public static void ApplyMapForcedSpawn(
        IniFile spawnIni,
        MapEntry map,
        int totalPlayerCount,
        int aiPlayerCount)
    {
        if (map.IsCustom)
        {
            ApplyCustomMapForcedSpawn(spawnIni, map);
            return;
        }

        string mpMapsPath = SafePath.CombineFilePath(AppState.Environment.GamePath, AppState.Configuration.Legacy.MPMapsIniPath);
        if (!File.Exists(mpMapsPath) || !map.IsOfficial)
            return;

        var ini = new IniFile(mpMapsPath);
        if (!ini.SectionExists(map.BaseFilePath))
            return;

        string sectionsRaw = ini.GetStringValue(map.BaseFilePath, "ForcedSpawnIniOptions", string.Empty);
        if (!string.IsNullOrWhiteSpace(sectionsRaw))
        {
            foreach (string section in sectionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ApplySpawnIniSection(spawnIni, ini, section, $"Map:{map.BaseFilePath}");
        }

        int credits = ini.GetIntValue(map.BaseFilePath, "Credits", -1);
        if (credits >= 0)
            spawnIni.SetIntValue("Settings", "Credits", credits);

        int unitCount = ini.GetIntValue(map.BaseFilePath, "UnitCount", -1);
        if (unitCount >= 0)
            spawnIni.SetIntValue("Settings", "UnitCount", unitCount);

        int bases = ini.GetIntValue(map.BaseFilePath, "Bases", -1);
        if (bases >= 0)
            spawnIni.SetBooleanValue("Settings", "Bases", Convert.ToBoolean(bases));

        Logger.Log($"ForcedSpawnCatalog: map metadata applied for {map.BaseFilePath} (players={totalPlayerCount}, ai={aiPlayerCount})");
    }

    private static void ApplyCustomMapForcedSpawn(IniFile spawnIni, MapEntry map)
    {
        if (!File.Exists(map.CompleteFilePath))
            return;

        try
        {
            var customMapIni = new IniFile { FileName = map.CompleteFilePath };
            customMapIni.AddSection("Basic");
            customMapIni.AddSection("Map");
            customMapIni.AddSection("Waypoints");
            customMapIni.AddSection("ForcedOptions");
            customMapIni.AddSection("ForcedSpawnIniOptions");
            customMapIni.AllowNewSections = false;
            customMapIni.Parse();

            ApplySpawnIniSection(spawnIni, customMapIni, "ForcedSpawnIniOptions", $"CustomMap:{map.BaseFilePath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"ForcedSpawnCatalog: custom map forced spawn failed ({map.CompleteFilePath}): {ex.Message}");
        }
    }

    private static void ApplySpawnIniSection(IniFile spawnIni, IniFile sourceIni, string sectionName, string logContext)
    {
        if (string.IsNullOrWhiteSpace(sectionName) || !sourceIni.SectionExists(sectionName))
            return;

        List<string>? keys = sourceIni.GetSectionKeys(sectionName);
        if (keys == null)
            return;

        foreach (string key in keys)
        {
            string value = sourceIni.GetStringValue(sectionName, key, string.Empty);
            spawnIni.SetStringValue("Settings", key, value);
            Logger.Log($"ForcedSpawnCatalog: {logContext} → Settings.{key}={value}");
        }
    }
}
