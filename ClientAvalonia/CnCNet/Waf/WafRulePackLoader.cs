using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>
/// Loads WAF rule packs from disk or the embedded default JSON.
/// Search order: <c>Client/WafRules.json</c> → <c>Client/WafRules.default.json</c> → embedded resource.
/// </summary>
public static class WafRulePackLoader
{
    public const string UserFileName = "WafRules.json";
    public const string DefaultFileName = "WafRules.default.json";
    public const string EmbeddedResourceName = "ClientAvalonia.CnCNet.Waf.rules.default.json";

    private static readonly object Sync = new();
    private static WafCompiledRulePack? _cachedDefault;
    private static WafCompiledRulePack? _cachedGamePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Embedded (or last-known) default pack — safe for unit tests.</summary>
    public static WafCompiledRulePack Default
    {
        get
        {
            lock (Sync)
                return _cachedDefault ??= LoadEmbeddedOrMinimal("embedded-default");
        }
    }

    /// <summary>
    /// Production load: prefer game-root Client overrides, else embedded.
    /// Result is cached until <see cref="InvalidateCache"/> (e.g. after Options reload).
    /// </summary>
    public static WafCompiledRulePack LoadFromGamePath()
    {
        lock (Sync)
        {
            if (_cachedGamePath != null)
                return _cachedGamePath;

            foreach ((string path, string label) in CandidatePaths())
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    string json = File.ReadAllText(path);
                    _cachedGamePath = Compile(Parse(json), label + ":" + path);
                    Logger.Log($"CnCNet WAF: loaded rule pack from {path} (v{_cachedGamePath.Version})");
                    return _cachedGamePath;
                }
                catch (Exception ex)
                {
                    Logger.Log($"CnCNet WAF: failed to load {path}: {ex.Message}");
                }
            }

            _cachedGamePath = Default;
            return _cachedGamePath;
        }
    }

    public static void InvalidateCache()
    {
        lock (Sync)
        {
            _cachedGamePath = null;
            _cachedDefault = null;
        }
    }

    /// <summary>Parse + compile arbitrary JSON (tests / hot-reload).</summary>
    public static WafCompiledRulePack CompileFromJson(string json, string source = "inline")
        => Compile(Parse(json), source);

    public static WafRulePackDocument Parse(string json)
    {
        WafRulePackDocument? doc = JsonSerializer.Deserialize<WafRulePackDocument>(json, JsonOptions);
        if (doc == null)
            throw new InvalidOperationException("WAF rule pack JSON deserialized to null.");
        return doc;
    }

    public static WafCompiledRulePack Compile(WafRulePackDocument doc, string source)
    {
        var tunnels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string t in doc.HostBotTunnels)
        {
            if (!string.IsNullOrWhiteSpace(t))
                tunnels.Add(t.Trim());
        }

        var sensitivity = new Dictionary<int, (int Warn, int Hide, int Drop)>();
        foreach (KeyValuePair<string, WafSensitivityThresholdDto> kv in doc.Sensitivity)
        {
            if (int.TryParse(kv.Key, out int level))
                sensitivity[level] = (kv.Value.Warn, kv.Value.Hide, kv.Value.Drop);
        }

        if (sensitivity.Count == 0)
        {
            sensitivity[0] = (30, 100, 9999);
            sensitivity[1] = (25, 80, 9999);
            sensitivity[2] = (15, 55, 180);
        }

        var protocol = new Dictionary<string, WafCompiledProtocolRule>(StringComparer.OrdinalIgnoreCase);
        foreach (WafProtocolRuleDto p in doc.Protocol)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
                continue;
            protocol[p.Id] = new WafCompiledProtocolRule
            {
                Id = p.Id,
                Score = p.Score,
                Reason = p.Reason ?? string.Empty,
                Threshold = p.Threshold,
                MinCount = p.MinCount,
                WindowSeconds = p.WindowSeconds,
                PerBurst = p.PerBurst,
                PerExtra = p.PerExtra,
                Cap = p.Cap,
            };
        }

        var content = new List<WafCompiledContentClass>();
        foreach (WafContentClassDto c in doc.ContentClasses)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
                continue;

            var regexes = new List<Regex>();
            foreach (string pattern in c.Regexes)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                try
                {
                    regexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
                catch (Exception ex)
                {
                    Logger.Log($"CnCNet WAF: invalid regex in {c.Id}: {ex.Message}");
                }
            }

            content.Add(new WafCompiledContentClass
            {
                Id = c.Id,
                Score = c.Score,
                Reason = string.IsNullOrEmpty(c.Reason) ? c.Id : c.Reason,
                PmReason = c.PmReason,
                Enabled = c.Enabled,
                Keywords = c.Keywords?.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray()
                           ?? Array.Empty<string>(),
                Regexes = regexes,
            });
        }

        WafPmBurstDto burstDto = doc.Pm?.Burst ?? new WafPmBurstDto();
        WafPmFirstContactDto firstDto = doc.Pm?.FirstContactPromo ?? new WafPmFirstContactDto
        {
            TriggerClasses = ["content.url", "content.contact", "content.promo"],
        };

        return new WafCompiledRulePack
        {
            Version = doc.Version <= 0 ? 2 : doc.Version,
            Description = doc.Description ?? string.Empty,
            Source = source,
            HostBotTunnels = tunnels,
            Sensitivity = sensitivity,
            Protocol = protocol,
            ContentClasses = content,
            PmBurst = new WafCompiledPmBurst
            {
                Id = string.IsNullOrEmpty(burstDto.Id) ? "content.pm.burst" : burstDto.Id,
                Reason = burstDto.Reason,
                MinCount = Math.Max(1, burstDto.MinCount),
                WindowSeconds = Math.Max(1, burstDto.WindowSeconds),
                BaseScore = burstDto.BaseScore,
                PerMessage = burstDto.PerMessage,
                Cap = burstDto.Cap <= 0 ? 70 : burstDto.Cap,
            },
            PmFirstContact = new WafCompiledPmFirstContact
            {
                Id = string.IsNullOrEmpty(firstDto.Id) ? "content.pm.first_contact_promo" : firstDto.Id,
                Reason = firstDto.Reason,
                Score = firstDto.Score,
                MinScore = firstDto.MinScore,
                TriggerClasses = new HashSet<string>(
                    firstDto.TriggerClasses ?? [],
                    StringComparer.OrdinalIgnoreCase),
            },
        };
    }

    private static IEnumerable<(string Path, string Label)> CandidatePaths()
    {
        string root = ResolveGameRoot();
        string clientDir = Path.Combine(root, "Client");
        yield return (Path.Combine(clientDir, UserFileName), "user");
        yield return (Path.Combine(clientDir, DefaultFileName), "default-file");

        // Also accept the source-tree / output copy next to the exe for debug runs.
        string baseDir = AppContext.BaseDirectory;
        yield return (Path.Combine(baseDir, "Client", UserFileName), "base-user");
        yield return (Path.Combine(baseDir, "Client", DefaultFileName), "base-default");
    }

    private static string ResolveGameRoot()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppState.Environment.GamePath))
                return AppState.Environment.GamePath;
        }
        catch
        {
            // ignore
        }

        return AppContext.BaseDirectory;
    }

    private static WafCompiledRulePack LoadEmbeddedOrMinimal(string source)
    {
        try
        {
            Assembly asm = typeof(WafRulePackLoader).Assembly;
            using Stream? stream = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return Compile(Parse(reader.ReadToEnd()), source);
            }

            // Fallback: read sibling file if not embedded (some test hosts).
            string sibling = Path.Combine(
                Path.GetDirectoryName(asm.Location) ?? AppContext.BaseDirectory,
                "CnCNet", "Waf", "rules.default.json");
            if (File.Exists(sibling))
                return Compile(Parse(File.ReadAllText(sibling)), source + ":sibling");
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet WAF: embedded rule pack load failed: {ex.Message}");
        }

        return Compile(MinimalDocument(), source + ":minimal-fallback");
    }

    /// <summary>Tiny in-memory pack so the engine never null-refs if JSON is missing.</summary>
    private static WafRulePackDocument MinimalDocument()
        => new()
        {
            Version = 2,
            Description = "minimal fallback",
            HostBotTunnels = [],
            Sensitivity =
            {
                ["0"] = new WafSensitivityThresholdDto { Warn = 30, Hide = 100, Drop = 9999 },
                ["1"] = new WafSensitivityThresholdDto { Warn = 25, Hide = 80, Drop = 9999 },
                ["2"] = new WafSensitivityThresholdDto { Warn = 15, Hide = 55, Drop = 180 },
            },
            Protocol = [],
            ContentClasses =
            [
                new()
                {
                    Id = "content.url",
                    Score = 25,
                    Reason = "文本含外链",
                    Regexes = [@"https?://|www\."],
                },
                new()
                {
                    Id = "content.promo",
                    Score = 25,
                    Reason = "疑似推广",
                    Keywords = ["代练", "加群", "boosting", "promo"],
                },
            ],
            Pm = new WafPmRulesDto(),
        };
}
