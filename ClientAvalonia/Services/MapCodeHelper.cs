using System.Text;
using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Extensions;
using ClientCore.I18N;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Port of DXMainClient MapCodeHelper (map INI patching for spawnmap.ini).</summary>
public static class MapCodeHelper
{
    private const string MapCodeBasePath = "INI/Map Code/";

    public static Encoding GetMapEncoding(string filepath)
        => Translation.Instance.MapEncoding ?? FileExtensions.GetDetectedEncoding(filepath);

    public static void ApplyMapCode(IniFile mapIni, string customIniPath, GameModeEntry? gameMode)
    {
        string associatedIniPath = SafePath.CombineFilePath(ProgramConstants.GamePath, customIniPath);
        if (!File.Exists(associatedIniPath))
            return;

        Encoding associatedIniEncoding = GetMapEncoding(associatedIniPath);
        var associatedIni = new IniFile(associatedIniPath, associatedIniEncoding);
        string? extraIniName = null;
        if (gameMode != null)
            extraIniName = associatedIni.GetStringValue("GameModeIncludes", gameMode.Name, null);

        associatedIni.EraseSectionKeys("GameModeIncludes");
        ApplyMapCode(mapIni, associatedIni);

        if (!string.IsNullOrEmpty(extraIniName))
        {
            string extraIniPath = SafePath.CombineFilePath(ProgramConstants.GamePath, extraIniName);
            if (File.Exists(extraIniPath))
            {
                Encoding extraIniEncoding = GetMapEncoding(extraIniPath);
                ApplyMapCode(mapIni, new IniFile(extraIniPath, extraIniEncoding));
            }
        }
    }

    public static void ApplyMapCode(IniFile mapIni, IniFile mapCodeIni)
    {
        ReplaceMapObjects(mapIni, mapCodeIni, "Aircraft");
        ReplaceMapObjects(mapIni, mapCodeIni, "Infantry");
        ReplaceMapObjects(mapIni, mapCodeIni, "Units");
        ReplaceMapObjects(mapIni, mapCodeIni, "Structures");
        ReplaceMapObjects(mapIni, mapCodeIni, "Terrain");
        IniFile.ConsolidateIniFiles(mapIni, mapCodeIni);
    }

    public static IniFile LoadMapIni(MapEntry map)
    {
        Encoding mapIniEncoding = GetMapEncoding(map.CompleteFilePath);
        var mapIni = new IniFile(map.CompleteFilePath, mapIniEncoding);

        if (!string.IsNullOrEmpty(map.ExtraIniName))
        {
            string extraIniPath = SafePath.CombineFilePath(ProgramConstants.GamePath, "INI", "Map Code", map.ExtraIniName);
            if (File.Exists(extraIniPath))
            {
                Encoding extraIniEncoding = GetMapEncoding(extraIniPath);
                var extraIni = new IniFile(extraIniPath, extraIniEncoding);
                IniFile.ConsolidateIniFiles(mapIni, extraIni);
            }
        }

        return mapIni;
    }

    public static string GetGameModeMapCodePath(GameModeEntry gameMode)
        => SafePath.CombineFilePath(MapCodeBasePath, gameMode.MapCodeIniName);

    private static void ReplaceMapObjects(IniFile mapIni, IniFile mapCodeIni, string sectionName)
    {
        string replaceSectionName = "ReplaceMap" + sectionName;
        List<KeyValuePair<string, string>> objectRemapPairs = GetKeyValuePairs(mapCodeIni, replaceSectionName);
        if (objectRemapPairs.Count < 1)
            return;

        List<KeyValuePair<string, string>> sectionKeyValuePairs = GetKeyValuePairs(mapIni, sectionName);

        foreach (KeyValuePair<string, string> objectRemapPair in objectRemapPairs)
        {
            List<KeyValuePair<string, string>> matchingSectionKvps =
                sectionKeyValuePairs.Where(x => GetObjectId(x.Value, sectionName) == objectRemapPair.Key).ToList();

            foreach (KeyValuePair<string, string> matchingSectionKvp in matchingSectionKvps)
            {
                string id = GetObjectId(matchingSectionKvp.Value, sectionName);

                if (!string.IsNullOrEmpty(objectRemapPair.Value))
                    mapIni.SetStringValue(sectionName, matchingSectionKvp.Key, matchingSectionKvp.Value.Replace(id, objectRemapPair.Value));
                else
                    mapIni.SetStringValue(sectionName, matchingSectionKvp.Key, string.Empty);
            }
        }

        mapCodeIni.EraseSectionKeys(replaceSectionName);
    }

    private static string GetObjectId(string value, string sectionName)
    {
        if (sectionName != "Terrain")
        {
            string[] splitValue = value.Split(',');
            return splitValue.Length < 2 ? "N/A" : splitValue[1];
        }

        return value;
    }

    private static List<KeyValuePair<string, string>> GetKeyValuePairs(IniFile iniFile, string sectionName)
    {
        IniSection? section = iniFile.GetSection(sectionName);
        return section == null ? [] : section.Keys;
    }
}
