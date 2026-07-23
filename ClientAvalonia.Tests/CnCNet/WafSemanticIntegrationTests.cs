using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class WafSemanticIntegrationTests
{
    static WafSemanticIntegrationTests()
    {
        WafRulePackLoader.InvalidateCache();
    }

    private static CnCNetIngressWaf CreateWaf()
        => new(() => new WafSettings(), persistUserList: false);

    [Fact]
    public void Corpus_Has_At_Least_220_Cases()
    {
        var cases = WafSemanticCorpus.Build();
        cases.Count.Should().BeGreaterThanOrEqualTo(220);
        cases.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(WafSemanticCorpus.TheoryRows), MemberType = typeof(WafSemanticCorpus))]
    public void Corpus_Case_Matches_Expected_Severity(
        string id,
        string surface,
        string text,
        bool expectWarn,
        string ruleHint)
    {
        id.Should().NotBeNullOrWhiteSpace();
        surface.Should().BeOneOf("lobby", "pm");

        WafDecision d = CreateWaf().Evaluate(ToEvent(surface, text));

        if (expectWarn)
        {
            ((int)d.Severity).Should().BeGreaterThanOrEqualTo((int)WafSeverity.Warn, because: $"{id}: {text}");
            if (!string.IsNullOrEmpty(ruleHint))
                d.MatchedRuleIds.Should().Contain(ruleHint, because: $"{id}: {text}");
        }
        else
        {
            d.Severity.Should().Be(WafSeverity.Allow, because: $"{id}: {text}");
        }
    }

    private static WafIngressEvent ToEvent(string surface, string text)
        => surface == "pm"
            ? WafAttackFixtures.PromoPrivateMessage("CorpusNick", text)
            : WafAttackFixtures.LobbyPromoChat("CorpusNick", text);
}
