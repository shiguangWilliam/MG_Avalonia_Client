namespace ClientAvalonia.IniUi.Loading;

public sealed class IniSection
{
    public required string SectionName { get; init; }

    public List<KeyValuePair<string, string>> Keys { get; } = [];

    public bool KeyExists(string key)
        => Keys.Any(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public string GetStringValue(string key, string defaultValue)
    {
        foreach (KeyValuePair<string, string> kvp in Keys)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return defaultValue;
    }

    public void SetStringValue(string key, string value)
    {
        int idx = Keys.FindIndex(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            Keys[idx] = new KeyValuePair<string, string>(Keys[idx].Key, value);
        else
            Keys.Add(new KeyValuePair<string, string>(key, value));
    }

    public bool GetBooleanValue(string key, bool defaultValue)
        => IniConversions.BooleanFromString(GetStringValue(key, string.Empty), defaultValue);

    public int GetIntValue(string key, int defaultValue)
        => int.TryParse(GetStringValue(key, string.Empty).Trim(), out int parsed) ? parsed : defaultValue;
}

public sealed class IniDocument
{
    public string? FilePath { get; set; }

    public List<IniSection> Sections { get; } = [];

    public IniSection? GetSection(string name)
        => Sections.FirstOrDefault(s => s.SectionName.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IniSection GetOrAddSection(string name)
    {
        IniSection? existing = GetSection(name);
        if (existing != null)
            return existing;

        var section = new IniSection { SectionName = name };
        Sections.Add(section);
        return section;
    }

    public void SetStringValue(string sectionName, string key, string value)
        => GetOrAddSection(sectionName).SetStringValue(key, value);

    public void SetBooleanValue(string sectionName, string key, bool value)
        => SetStringValue(sectionName, key, value ? "True" : "False");

    public void SetIntValue(string sectionName, string key, int value)
        => SetStringValue(sectionName, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public string GetStringValue(string sectionName, string key, string defaultValue)
        => GetSection(sectionName)?.GetStringValue(key, defaultValue) ?? defaultValue;

    public bool GetBooleanValue(string sectionName, string key, bool defaultValue)
        => GetSection(sectionName)?.GetBooleanValue(key, defaultValue) ?? defaultValue;

    public int GetIntValue(string sectionName, string key, int defaultValue)
        => GetSection(sectionName)?.GetIntValue(key, defaultValue) ?? defaultValue;

    public void Save(string path)
    {
        var lines = new List<string>();
        foreach (IniSection section in Sections)
        {
            lines.Add($"[{section.SectionName}]");
            foreach (KeyValuePair<string, string> kvp in section.Keys)
                lines.Add($"{kvp.Key}={kvp.Value}");
            lines.Add(string.Empty);
        }

        File.WriteAllLines(path, lines);
        FilePath = path;
    }

    /// <summary>Loads INI with BasedOn inheritance aligned to ClientCore.CCIniFile.</summary>
    public static IniDocument Load(string path)
    {
        var doc = ParseFile(path);
        ApplyBasedOnChain(doc, path);
        ApplyBaseSections(doc);
        return doc;
    }

    /// <summary>Loads only sections physically present in the file (no BasedOn merge).</summary>
    public static IniDocument ParseOverlay(string path) => ParseFile(path);

    private static IniDocument ParseFile(string path)
    {
        var doc = new IniDocument { FilePath = path };
        IniSection? current = null;

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            int comment = line.IndexOf(';');
            if (comment > 0)
                line = line[..comment].Trim();

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new IniSection { SectionName = line[1..^1].Trim() };
                doc.Sections.Add(current);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0 || current == null)
                continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            current.Keys.Add(new KeyValuePair<string, string>(key, value));
        }

        return doc;
    }

    private static void ApplyBasedOnChain(IniDocument doc, string path)
    {
        IniSection? system = doc.GetSection("INISystem");
        if (system == null)
            return;

        string basedOnSetting = system.GetStringValue("BasedOn", string.Empty);
        if (string.IsNullOrWhiteSpace(basedOnSetting))
            return;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        foreach (string part in basedOnSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string basePath = part.Contains("$THEME_DIR$", StringComparison.Ordinal)
                ? part.Replace("$THEME_DIR$", dir, StringComparison.Ordinal)
                : Path.Combine(dir, part);

            if (!File.Exists(basePath))
                continue;

            IniDocument baseDoc = Load(basePath);
            Consolidate(baseDoc, doc);
            doc.Sections.Clear();
            doc.Sections.AddRange(baseDoc.Sections);
        }
    }

    private static void ApplyBaseSections(IniDocument doc)
    {
        foreach (IniSection section in doc.Sections)
        {
            string baseName = section.GetStringValue("$BaseSection", string.Empty);
            if (string.IsNullOrWhiteSpace(baseName))
                continue;

            IniSection? baseSection = doc.GetSection(baseName);
            if (baseSection == null)
                continue;

            int insertAt = 0;
            foreach (KeyValuePair<string, string> kvp in baseSection.Keys)
            {
                if (section.KeyExists(kvp.Key))
                    continue;

                section.Keys.Insert(insertAt++, kvp);
            }
        }
    }

    private static void Consolidate(IniDocument baseDoc, IniDocument overlay)
    {
        foreach (IniSection overlaySection in overlay.Sections)
        {
            IniSection? target = baseDoc.GetSection(overlaySection.SectionName);
            if (target == null)
            {
                baseDoc.Sections.Add(overlaySection);
                continue;
            }

            foreach (KeyValuePair<string, string> kvp in overlaySection.Keys)
            {
                int idx = target.Keys.FindIndex(k => k.Key.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    target.Keys[idx] = kvp;
                else
                    target.Keys.Add(kvp);
            }
        }
    }
}
