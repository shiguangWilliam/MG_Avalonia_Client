using ClientAvalonia.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

public sealed class CnCNetWelcomeChannelPlanTests
{
    [Fact]
    public void BuildForLocalGame_OrdersChat_General_Broadcast()
    {
        var local = new CnCNetGameEntry
        {
            InternalName = "lnod",
            UiName = "LNOD",
            ChatChannel = "#cncnet-lnod",
            GameBroadcastChannel = "#cncnet-lnod-games",
        };

        var steps = CnCNetWelcomeChannelPlan.BuildForLocalGame(local);
        steps.Should().HaveCount(3);
        steps[0].Should().Be(new CnCNetWelcomeChannelPlan.JoinStep("#cncnet-lnod", "ra1-derp", "chat"));
        steps[1].Should().Be(new CnCNetWelcomeChannelPlan.JoinStep("#cncnet", "ra1-derp", "general"));
        steps[2].Should().Be(new CnCNetWelcomeChannelPlan.JoinStep("#cncnet-lnod-games", null, "broadcast"));
    }

    [Fact]
    public void IsLobbyReady_RequiresChatAndBroadcast()
    {
        CnCNetWelcomeChannelPlan.IsLobbyReady(new CnCNetGameEntry
        {
            InternalName = "x",
            UiName = "X",
            ChatChannel = "#cncnet-x",
        }).Should().BeFalse();

        CnCNetWelcomeChannelPlan.IsLobbyReady(new CnCNetGameEntry
        {
            InternalName = "x",
            UiName = "X",
            ChatChannel = "#cncnet-x",
            GameBroadcastChannel = "#cncnet-x-games",
        }).Should().BeTrue();
    }
}
