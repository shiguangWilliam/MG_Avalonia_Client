using ClientAvalonia.Core;
using ClientAvalonia.IniUi.Layout;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// Loads [ParserConstants] from GlobalThemeSettings.ini, then DTACnCNetClient.ini when Core is available.
/// Matches INISystem.md: theme constants live in GlobalThemeSettings; client INI may override.
/// </summary>
public static class ParserConstantsLoader
{
    public static IReadOnlyDictionary<string, int> LoadForGame(string gameRoot)
    {
        var map = new Dictionary<string, int>(DefaultParserConstants.Create(), StringComparer.Ordinal);

        MergeFromIniFile(map, Path.Combine(gameRoot, "Resources", "GlobalThemeSettings.ini"), "ParserConstants");

        if (ClientCoreBootstrap.TryEnsureInitialized(gameRoot, out _))
            MergeFromCoreConfiguration(map);

        return map;
    }

    public static IReadOnlyDictionary<string, int> LoadLegacyFromGlobalThemeSettings(string gameRoot)
        => LoadForGame(gameRoot);

    private static void MergeFromCoreConfiguration(Dictionary<string, int> map)
    {
        Rampastring.Tools.IniSection? section = ClientConfiguration.Instance.GetParserConstants();
        if (section == null)
            return;

        foreach ((string key, string value) in section.Keys)
            TrySetConstant(map, key, value);
    }

    private static void MergeFromIniFile(Dictionary<string, int> map, string path, string sectionName)
    {
        if (!File.Exists(path))
            return;

        IniDocument doc = IniDocument.Load(path);
        IniSection? section = doc.GetSection(sectionName);
        if (section == null)
            return;

        foreach (KeyValuePair<string, string> kvp in section.Keys)
            TrySetConstant(map, kvp.Key, kvp.Value);
    }

    private static void TrySetConstant(Dictionary<string, int> map, string key, string rawValue)
    {
        string value = rawValue.Trim();
        int comment = value.IndexOf(';');
        if (comment >= 0)
            value = value[..comment].Trim();

        if (int.TryParse(value, out int parsed))
            map[key] = parsed;
    }
}
