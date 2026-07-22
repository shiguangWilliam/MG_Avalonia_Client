using System.Linq;
using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Welcome JOIN plan must keep chat/general separate from broadcast so Session can
/// route lobby channels through DX-style Persistent (delayed) JOINs without double-firing
/// the broadcast JOIN via EnsureGameBroadcastChannelsJoined.
/// </summary>
public sealed class CnCNetWelcomeJoinAntiFloodTests
{
    [Fact]
    [Trait("Category", "Regression")]
    [Trait("DXContract", "DX-PERSISTENT-CHANNEL-JOIN")]
    public void WelcomePlan_BroadcastIsDistinctRole_ForPersistentJoinPath()
    {
        var local = new CnCNetGameEntry
        {
            InternalName = "mg",
            UiName = "创世之刻",
            ChatChannel = "#yuanming-games",
            GameBroadcastChannel = "#yuanming-cg-games",
            Supported = true,
        };

        var steps = CnCNetWelcomeChannelPlan.BuildForLocalGame(local);
        steps.Count(s => s.Role == "broadcast").Should().Be(1);
        steps.Where(s => s.Role != "broadcast").Select(s => s.Channel).Should().Equal(
            "#yuanming-games",
            "#cncnet");
        steps.Single(s => s.Role == "broadcast").Channel.Should().Be("#yuanming-cg-games");
        steps.Single(s => s.Role == "broadcast").Key.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("DXContract", "DX-PERSISTENT-CHANNEL-JOIN")]
    public void WelcomePlan_DoesNotDuplicateBroadcastChannel()
    {
        var local = new CnCNetGameEntry
        {
            InternalName = "mg",
            UiName = "创世之刻",
            ChatChannel = "#yuanming-games",
            GameBroadcastChannel = "#yuanming-cg-games",
            Supported = true,
        };

        var channels = CnCNetWelcomeChannelPlan.BuildForLocalGame(local)
            .Select(s => s.Channel)
            .ToList();

        channels.Should().OnlyHaveUniqueItems(
            "duplicate welcome JOIN targets are what flooded GameSurge before Persistent delays");
    }
}
