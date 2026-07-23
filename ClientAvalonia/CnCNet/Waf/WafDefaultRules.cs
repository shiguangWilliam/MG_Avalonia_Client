using System;
using System.Collections.Generic;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>
/// Compatibility helpers. Keyword/regex corpora live in <c>rules.default.json</c>
/// (loaded via <see cref="WafRulePackLoader"/>); this type keeps older call sites working.
/// </summary>
public static class WafDefaultRules
{
    public static bool IsKnownHostBotTunnel(string endpoint)
        => WafRulePackLoader.Default.IsKnownHostBotTunnel(endpoint);

    public static IReadOnlyList<string> PromoKeywords
        => WafRulePackLoader.Default.GetKeywords("content.promo");

    public static IReadOnlyList<string> ContactKeywords
        => WafRulePackLoader.Default.GetKeywords("content.contact");

    public static IReadOnlyList<string> FraudKeywords
        => WafRulePackLoader.Default.GetKeywords("content.fraud");

    public static IReadOnlyList<string> SexualKeywords
        => WafRulePackLoader.Default.GetKeywords("content.sexual");

    public static IReadOnlyList<string> AbuseKeywords
        => WafRulePackLoader.Default.GetKeywords("content.abuse");

    public static bool MatchesAny(string normalizedText, IEnumerable<string> keywords)
    {
        if (string.IsNullOrEmpty(normalizedText))
            return false;

        string lower = normalizedText.ToLowerInvariant();
        string compact = WafTextNormalizer.CompactForMatch(normalizedText);
        foreach (string keyword in keywords)
        {
            if (string.IsNullOrEmpty(keyword))
                continue;

            string k = keyword.ToLowerInvariant();
            if (lower.Contains(k, StringComparison.Ordinal))
                return true;

            string compactKeyword = WafTextNormalizer.CompactForMatch(k);
            if (compactKeyword.Length > 0 && compact.Contains(compactKeyword, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
