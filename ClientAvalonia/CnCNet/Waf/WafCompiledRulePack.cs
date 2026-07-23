using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Compiled, immutable rule pack used by <see cref="CnCNetIngressWaf"/>.</summary>
public sealed class WafCompiledRulePack
{
    public required int Version { get; init; }

    public required string Description { get; init; }

    public required string Source { get; init; }

    public required HashSet<string> HostBotTunnels { get; init; }

    public required IReadOnlyDictionary<int, (int Warn, int Hide, int Drop)> Sensitivity { get; init; }

    public required IReadOnlyDictionary<string, WafCompiledProtocolRule> Protocol { get; init; }

    public required IReadOnlyList<WafCompiledContentClass> ContentClasses { get; init; }

    public required WafCompiledPmBurst PmBurst { get; init; }

    public required WafCompiledPmFirstContact PmFirstContact { get; init; }

    public bool IsKnownHostBotTunnel(string endpoint)
        => !string.IsNullOrWhiteSpace(endpoint) && HostBotTunnels.Contains(endpoint.Trim());

    public int ProtocolScore(string id, int fallback)
        => Protocol.TryGetValue(id, out WafCompiledProtocolRule? rule) ? rule.Score : fallback;

    public string ProtocolReason(string id, string fallback)
        => Protocol.TryGetValue(id, out WafCompiledProtocolRule? rule) && !string.IsNullOrEmpty(rule.Reason)
            ? rule.Reason
            : fallback;

    public WafCompiledProtocolRule? ProtocolRule(string id)
        => Protocol.TryGetValue(id, out WafCompiledProtocolRule? rule) ? rule : null;

    public IReadOnlyList<string> GetKeywords(string classId)
    {
        WafCompiledContentClass? cls = ContentClasses.FirstOrDefault(c =>
            c.Id.Equals(classId, StringComparison.OrdinalIgnoreCase));
        return cls?.Keywords ?? Array.Empty<string>();
    }

    public (int Warn, int Hide, int Drop) Thresholds(int sensitivity)
    {
        if (Sensitivity.TryGetValue(sensitivity, out (int Warn, int Hide, int Drop) t))
            return t;
        if (Sensitivity.TryGetValue(1, out t))
            return t;
        return (25, 80, 9999);
    }
}

public sealed class WafCompiledProtocolRule
{
    public required string Id { get; init; }
    public required int Score { get; init; }
    public required string Reason { get; init; }
    public int? Threshold { get; init; }
    public int? MinCount { get; init; }
    public int? WindowSeconds { get; init; }
    public int? PerBurst { get; init; }
    public int? PerExtra { get; init; }
    public int? Cap { get; init; }
}

public sealed class WafCompiledContentClass
{
    public required string Id { get; init; }
    public required int Score { get; init; }
    public required string Reason { get; init; }
    public string? PmReason { get; init; }
    public required bool Enabled { get; init; }
    public required IReadOnlyList<string> Keywords { get; init; }
    public required IReadOnlyList<Regex> Regexes { get; init; }

    public bool Matches(string normalized, string compact)
    {
        foreach (Regex regex in Regexes)
        {
            if (regex.IsMatch(normalized) || regex.IsMatch(compact))
                return true;
        }

        return WafDefaultRules.MatchesAny(normalized, Keywords);
    }

    public string ReasonFor(string surfaceTag)
        => surfaceTag == "pm" && !string.IsNullOrEmpty(PmReason) ? PmReason! : Reason;
}

public sealed class WafCompiledPmBurst
{
    public required string Id { get; init; }
    public required string Reason { get; init; }
    public required int MinCount { get; init; }
    public required int WindowSeconds { get; init; }
    public required int BaseScore { get; init; }
    public required int PerMessage { get; init; }
    public required int Cap { get; init; }
}

public sealed class WafCompiledPmFirstContact
{
    public required string Id { get; init; }
    public required string Reason { get; init; }
    public required int Score { get; init; }
    public required int MinScore { get; init; }
    public required HashSet<string> TriggerClasses { get; init; }
}
