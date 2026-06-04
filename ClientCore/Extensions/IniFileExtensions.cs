using System.Collections.Generic;
using System.Linq;

using Rampastring.Tools;

namespace ClientCore.Extensions;

public static class IniFileExtensions
{
    // Clone() is not on Rampastring.Tools IniFile yet. https://github.com/Rampastring/Rampastring.Tools/issues/12
    public static IniFile Clone(this IniFile iniFile)
    {
        var newIni = new IniFile();
        foreach (string sectionName in iniFile.GetSections())
        {
            IniSection oldSection = iniFile.GetSection(sectionName);
            newIni.AddSection(oldSection.Clone());
        }

        return newIni;
    }

    public static IniSection GetOrAddSection(this IniFile iniFile, string sectionName)
    {
        IniSection? section = iniFile.GetSection(sectionName);
        if (section != null)
            return section;

        section = new IniSection(sectionName);
        iniFile.AddSection(section);
        return section;
    }

    public static string[] GetStringListValue(this IniFile iniFile, string section, string key, string defaultValue, char[]? separators = null)
        => (iniFile.GetSection(section)?.GetStringValue(key, defaultValue) ?? defaultValue)
            .SplitWithCleanup(separators);
}

public static class IniSectionExtensions
{
    public static IniSection Clone(this IniSection iniSection)
    {
        IniSection newSection = new(iniSection.SectionName);

        foreach ((string key, string value) in iniSection.Keys)
            newSection.AddKey(key, value);

        return newSection;
    }

    public static void RemoveAllKeys(this IniSection iniSection)
    {
        var keys = new List<KeyValuePair<string, string>>(iniSection.Keys);
        foreach (KeyValuePair<string, string> iniSectionKey in keys)
            iniSection.RemoveKey(iniSectionKey.Key);
    }
}
