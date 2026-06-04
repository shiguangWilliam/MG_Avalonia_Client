using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using Rampastring.Tools;

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

        string battleFs = ClientConfiguration.Instance.BattleFSFileName;
        if (!string.IsNullOrWhiteSpace(battleFs) && TryLoadFrom(FormattableString.Invariant($"INI/{battleFs}"), out missions))
            return missions;

        Logger.Log("MissionCatalogLoader: no Battle.ini missions loaded.");
        return [];
    }

    private static bool TryLoadFrom(string relativePath, out List<MissionEntry> missions)
    {
        missions = [];
        FileInfo battleFile = SafePath.GetFile(ProgramConstants.GamePath, relativePath);
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
                ClientConfiguration.Instance.ClientGameType is ClientCore.Enums.ClientType.YR
                    or ClientCore.Enums.ClientType.Ares);
            bool playerNormal = battleIni.GetBooleanValue(sectionName, "PlayerAlwaysOnNormalDifficulty", false);

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
            });
            index++;
        }

        Logger.Log($"MissionCatalogLoader: loaded {missions.Count} missions from {relativePath}.");
        return missions.Count > 0;
    }
}
