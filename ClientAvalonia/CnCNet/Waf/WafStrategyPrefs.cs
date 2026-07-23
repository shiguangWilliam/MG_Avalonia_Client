using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Per-strategy player override: Off / Warning / Drop.</summary>
public enum WafStrategyMode
{
    Off = 0,
    Warn = 1,
    Drop = 2,
}

/// <summary>One previewable strategy row for Settings / strategy window.</summary>
public sealed class WafStrategyRow
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    /// <summary>Human-readable content: reason + sample keywords / params.</summary>
    public required string Content { get; init; }

    public WafStrategyMode Mode { get; set; } = WafStrategyMode.Warn;
}

/// <summary>Persists player strategy modes under <c>Client/WafStrategyPrefs.json</c>.</summary>
public sealed class WafStrategyPrefs
{
    private const string FileName = "WafStrategyPrefs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ConcurrentDictionary<string, WafStrategyMode> _modes =
        new(StringComparer.OrdinalIgnoreCase);

    public WafStrategyMode GetMode(string strategyId)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
            return WafStrategyMode.Warn;
        return _modes.TryGetValue(strategyId.Trim(), out WafStrategyMode mode)
            ? mode
            : WafStrategyMode.Warn;
    }

    public void SetMode(string strategyId, WafStrategyMode mode)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
            return;
        _modes[strategyId.Trim()] = mode;
    }

    public IReadOnlyDictionary<string, WafStrategyMode> Snapshot()
        => _modes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        try
        {
            string path = ResolvePath();
            if (!File.Exists(path))
                return;

            string json = File.ReadAllText(path);
            WafStrategyPrefsDocument? doc = JsonSerializer.Deserialize<WafStrategyPrefsDocument>(json, JsonOptions);
            if (doc?.Modes == null)
                return;

            _modes.Clear();
            foreach (KeyValuePair<string, WafStrategyMode> kv in doc.Modes)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key))
                    _modes[kv.Key.Trim()] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"WafStrategyPrefs.Load failed: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            string path = ResolvePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var doc = new WafStrategyPrefsDocument
            {
                Modes = _modes
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Log($"WafStrategyPrefs.Save failed: {ex.Message}");
        }
    }

    private static string ResolvePath()
    {
        string root = ProgramConstants.GamePath;
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;
        return Path.Combine(root, "Client", FileName);
    }

    private sealed class WafStrategyPrefsDocument
    {
        [JsonPropertyName("modes")]
        public Dictionary<string, WafStrategyMode> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
