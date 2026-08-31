using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>
/// Persists player-confirmed WAF block entries under <c>Client/WafBlockList.json</c>.
/// Still imports legacy <c>Client/WafUserList.ini</c> Key= lines on first load.
/// </summary>
public static class WafUserListStore
{
    private const string JsonFileName = "WafBlockList.json";
    private const string LegacyIniFileName = "WafUserList.ini";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<WafBlockEntry> LoadEntries()
    {
        try
        {
            var byKey = new Dictionary<string, WafBlockEntry>(StringComparer.OrdinalIgnoreCase);

            string jsonPath = ResolvePath(JsonFileName);
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                WafBlockListDocument? doc = JsonSerializer.Deserialize<WafBlockListDocument>(json, JsonOptions);
                if (doc?.Entries != null)
                {
                    foreach (WafBlockEntry entry in doc.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Key))
                            continue;
                        if (string.IsNullOrWhiteSpace(entry.Kind))
                            entry.Kind = WafBlockEntry.InferKind(entry.Key);
                        byKey[entry.Key.Trim()] = entry;
                    }
                }
            }

            // Legacy INI migration (Key= only).
            string iniPath = ResolvePath(LegacyIniFileName);
            if (File.Exists(iniPath))
            {
                foreach (string line in File.ReadAllLines(iniPath))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('['))
                        continue;
                    if (t.StartsWith("Key=", StringComparison.OrdinalIgnoreCase))
                        t = t[4..].Trim();
                    if (t.Length == 0 || byKey.ContainsKey(t))
                        continue;
                    byKey[t] = WafBlockEntry.FromKey(t, note: "imported from WafUserList.ini");
                }
            }

            return byKey.Values
                .OrderByDescending(e => e.AddedUtc)
                .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"WafUserListStore.LoadEntries failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>Legacy API: match keys only.</summary>
    public static IReadOnlyList<string> Load()
        => LoadEntries().Select(e => e.Key).ToList();

    public static void SaveEntries(IReadOnlyList<WafBlockEntry> entries)
    {
        try
        {
            string path = ResolvePath(JsonFileName);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var doc = new WafBlockListDocument
            {
                Entries = entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                    .GroupBy(e => e.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderByDescending(e => e.AddedUtc)
                    .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));

            // Keep a Key= mirror for older tools / hand editing.
            string iniPath = ResolvePath(LegacyIniFileName);
            var lines = new List<string>
            {
                "[Blocked]",
                "; Mirror of WafBlockList.json — prefer editing the JSON or Settings → Security.",
            };
            lines.AddRange(doc.Entries.Select(e => "Key=" + e.Key));
            File.WriteAllLines(iniPath, lines);
        }
        catch (Exception ex)
        {
            Logger.Log($"WafUserListStore.SaveEntries failed: {ex.Message}");
        }
    }

    public static void Save(IReadOnlyList<string> keys)
    {
        var entries = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => WafBlockEntry.FromKey(k))
            .ToList();
        SaveEntries(entries);
    }

    private static string ResolvePath(string fileName)
    {
        string root = AppState.Environment.GamePath;
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;
        return Path.Combine(root, "Client", fileName);
    }

    private sealed class WafBlockListDocument
    {
        public List<WafBlockEntry> Entries { get; set; } = [];
    }
}
