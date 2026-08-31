using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Loads campaign missions from Battle.ini (aligned with CampaignSelector.ParseBattleIni).</summary>
public static class MissionCatalogLoader
{
    public static IReadOnlyList<MissionEntry> LoadMissions()
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return [];

        if (TryLoadFrom("INI/Battle.ini", out List<MissionEntry> missions))
            return missions;

        string battleFs = AppState.Configuration.Legacy.BattleFSFileName;
        if (!string.IsNullOrWhiteSpace(battleFs) && TryLoadFrom(FormattableString.Invariant($"INI/{battleFs}"), out missions))
            return missions;

        Logger.Log("MissionCatalogLoader: no Battle.ini missions loaded.");
        return [];
    }

    private static bool TryLoadFrom(string relativePath, out List<MissionEntry> missions)
    {
        missions = [];
        FileInfo battleFile = SafePath.GetFile(AppState.Environment.GamePath, relativePath);
        if (!battleFile.Exists)
        {
            Logger.Log($"MissionCatalogLoader: {relativePath} not found.");
            return false;
        }

        Logger.Log($"MissionCatalogLoader: parsing {relativePath}.");
        var battleIni = new IniFile(battleFile.FullName);
        List<string>? battleKeys = battleIni.GetSectionKeys("Battles");
        if (battleKeys == null)
            return false;

        int index = 0;
        foreach (string battleEntry in battleKeys)
        {
            string sectionName = battleIni.GetStringValue("Battles", battleEntry, string.Empty).Trim();
            if (string.IsNullOrEmpty(sectionName) || !battleIni.SectionExists(sectionName))
                continue;

            string untranslated = battleIni.GetStringValue(sectionName, "Description", "Undefined mission");
            string display = untranslated.L10N($"INI:Missions:{sectionName}:Description");
            string scenario = battleIni.GetStringValue(sectionName, "Scenario", string.Empty).Trim();
            string description = battleIni.GetStringValue(sectionName, "LongDescription", string.Empty)
                .FromIniString()
                .L10N($"INI:Missions:{sectionName}:LongDescription");
            bool enabled = battleIni.GetBooleanValue(sectionName, "Enabled", true);
            int side = battleIni.GetIntValue(sectionName, "Side", 0);
            string sideName = battleIni.GetStringValue(sectionName, "SideName", string.Empty);
            bool buildOffAlly = battleIni.GetBooleanValue(sectionName, "BuildOffAlly", false);
            bool requiredAddon = battleIni.GetBooleanValue(sectionName, "RequiredAddon",
                AppState.Configuration.Legacy.ClientGameType is ClientCore.Enums.ClientType.YR
                    or ClientCore.Enums.ClientType.Ares);
            bool playerNormal = battleIni.GetBooleanValue(sectionName, "PlayerAlwaysOnNormalDifficulty", false);

            double? globeLat = ReadCoordinate(battleIni, sectionName, "GlobeLatitude", -90.0, 90.0);
            double? globeLon = ReadCoordinate(battleIni, sectionName, "GlobeLongitude", -180.0, 180.0);
            string? globeCountry = ReadCountryCode(battleIni, sectionName, "GlobeCountry");

            missions.Add(new MissionEntry
            {
                SectionName = sectionName,
                DisplayName = display,
                Description = description,
                Scenario = scenario,
                Side = side,
                SideName = sideName,
                Enabled = enabled,
                BuildOffAlly = buildOffAlly,
                RequiredAddon = requiredAddon,
                PlayerAlwaysOnNormalDifficulty = playerNormal,
                GlobeLatitude = globeLat,
                GlobeLongitude = globeLon,
                GlobeCountry = globeCountry,
            });
            index++;
        }

        Logger.Log($"MissionCatalogLoader: loaded {missions.Count} missions from {relativePath}.");
        return missions.Count > 0;
    }

    /// <summary>Reads an optional numeric coordinate key, clamped to its valid range.</summary>
    private static double? ReadCoordinate(IniFile ini, string section, string key, double min, double max)
    {
        string raw = ini.GetStringValue(section, key, string.Empty).Trim();
        if (string.IsNullOrEmpty(raw) || !double.TryParse(raw, out double value))
            return null;

        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// Reads GlobeCountry: 2- or 3-letter ISO code, case-insensitive, stored
    /// uppercase. Invalid values log once and yield null (F2 skips silently).
    /// </summary>
    internal static string? ReadCountryCode(IniFile ini, string section, string key)
    {
        string raw = ini.GetStringValue(section, key, string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return null;

        string upper = raw.ToUpperInvariant();
        if (upper.Length is 2 or 3 && upper.All(char.IsLetter))
            return upper;

        Logger.Log($"MissionCatalogLoader: invalid {key} '{raw}' in [{section}] (need ISO 2/3-letter); F2 skipped.");
        return null;
    }
}
