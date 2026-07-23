using System;
using System.Linq;
using ClientAvalonia.CnCNet;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Online.EventArguments;
using ClientAvalonia.Tests.Fixture;
using ClientCore.Enums;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

[Collection("ProgramConstantsSerial")]
public sealed class CnCNetPrivateMessagingTests : IDisposable
{
    private readonly TempGameRoot _root = new();
    private readonly CnCNetSession _session = CnCNetSession.Instance;

    public CnCNetPrivateMessagingTests()
    {
        _root.BindToProgramConstants();
        _session.ResetPrivateMessagingForTests();
        _session.IngressWaf = null;
        _session.PrivateMessagePolicyOverrideForTests = AllowPrivateMessagesFromEnum.All;
    }

    public void Dispose()
    {
        _session.ResetPrivateMessagingForTests();
        _session.IngressWaf = null;
        _root.Dispose();
    }

    [Fact]
    public void Thread_Unread_Increments_And_MarkRead_Clears()
    {
        var thread = new CnCNetPrivateMessageThread("Alice");
        thread.Append(new CnCNetChatLine { DisplayText = "hi", Scope = CnCNetChatScope.PrivateMessage }, true);
        thread.UnreadCount.Should().Be(1);
        thread.MarkRead().Should().BeTrue();
        thread.UnreadCount.Should().Be(0);
        thread.MarkRead().Should().BeFalse();
    }

    [Theory]
    [InlineData(AllowPrivateMessagesFromEnum.All, false, true)]
    [InlineData(AllowPrivateMessagesFromEnum.None, true, false)]
    [InlineData(AllowPrivateMessagesFromEnum.CurrentChannel, false, false)]
    [InlineData(AllowPrivateMessagesFromEnum.CurrentChannel, true, true)]
    [InlineData(AllowPrivateMessagesFromEnum.Friends, false, false)]
    [InlineData(AllowPrivateMessagesFromEnum.Friends, true, true)]
    public void Policy_ShouldAccept_Matches_Dx_Table(
        AllowPrivateMessagesFromEnum policy,
        bool inChannel,
        bool expected)
    {
        CnCNetPrivateMessagePolicy.ShouldAccept(policy, inChannel).Should().Be(expected);
    }

    [Fact]
    public void Session_Receive_Increments_Unread_And_Raises_Arrived()
    {
        string? arrivedPeer = null;
        string? arrivedPreview = null;
        void Handler(string peer, string preview)
        {
            arrivedPeer = peer;
            arrivedPreview = preview;
        }

        _session.PrivateMessageArrived += Handler;
        try
        {
            _session.ProcessPrivateMessageReceivedForTests("Bob", "hello there");

            _session.UnreadPrivateMessageCount.Should().Be(1);
            _session.LastPrivateMessagePartner.Should().Be("Bob");
            arrivedPeer.Should().Be("Bob");
            arrivedPreview.Should().Be("hello there");

            var lines = _session.GetPrivateMessages("Bob");
            lines.Should().ContainSingle();
            lines[0].DisplayText.Should().Contain("Bob:");
            lines[0].DisplayText.Should().Contain("hello there");
            lines[0].Scope.Should().Be(CnCNetChatScope.PrivateMessage);
        }
        finally
        {
            _session.PrivateMessageArrived -= Handler;
        }
    }

    [Fact]
    public void Session_Receive_While_Viewing_Peer_Does_Not_Unread_Or_Notify()
    {
        int arrived = 0;
        _session.PrivateMessageArrived += (_, _) => arrived++;

        _session.SetViewingPrivateMessagePeer("Carol");
        _session.ProcessPrivateMessageReceivedForTests("Carol", "already open");

        _session.UnreadPrivateMessageCount.Should().Be(0);
        arrived.Should().Be(0);
        _session.GetPrivateMessages("Carol").Should().ContainSingle();
    }

    [Fact]
    public void Session_Private_Action_Formats_Nick_Once()
    {
        _session.ProcessPrivateMessageReceivedForTests("Dave", "\u0001ACTION waves\u0001");

        string display = _session.GetPrivateMessages("Dave").Single().DisplayText;
        display.Should().Contain("====> waves");
        display.Should().NotContain("====> Dave waves");
        // One "Dave:" label from FormatChatLine — not duplicated inside the action body.
        display.Split("Dave", StringSplitOptions.None).Length.Should().Be(2);
    }

    [Fact]
    public void Session_Policy_None_Drops_Incoming()
    {
        _session.PrivateMessagePolicyOverrideForTests = AllowPrivateMessagesFromEnum.None;
        _session.ProcessPrivateMessageReceivedForTests("Eve", "blocked");

        _session.GetPrivateMessages("Eve").Should().BeEmpty();
        _session.UnreadPrivateMessageCount.Should().Be(0);
    }

    [Fact]
    public void Session_Policy_CurrentChannel_Requires_Membership()
    {
        _session.PrivateMessagePolicyOverrideForTests = AllowPrivateMessagesFromEnum.CurrentChannel;
        _session.SeedChannelUsersForTests("Frank");

        _session.ProcessPrivateMessageReceivedForTests("Stranger", "nope");
        _session.GetPrivateMessages("Stranger").Should().BeEmpty();

        _session.ProcessPrivateMessageReceivedForTests("Frank", "ok");
        _session.GetPrivateMessages("Frank").Should().ContainSingle();
    }

    [Fact]
    public void Session_Waf_Drop_Does_Not_Store_Pm()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        waf.Block("nick=Spammer");
        _session.IngressWaf = waf;

        _session.ProcessPrivateMessageReceivedForTests("Spammer", "加群领免费代练 http://spam.vip");

        _session.GetPrivateMessages("Spammer").Should().BeEmpty();
        _session.UnreadPrivateMessageCount.Should().Be(0);
    }

    [Fact]
    public void Session_After_Ban_Subsequent_Pm_Silent_And_Not_Stored()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        _session.IngressWaf = waf;

        _session.ProcessPrivateMessageReceivedForTests("AdBot", "加群领免费代练 http://spam.vip");
        _session.GetPrivateMessages("AdBot").Should().ContainSingle();

        WafDecision d = waf.Evaluate(WafAttackFixtures.PromoPrivateMessage("AdBot", "加群领免费代练 http://spam.vip"));
        waf.BlockFromAlert(
            WafAttackFixtures.PromoPrivateMessage("AdBot", "加群领免费代练 http://spam.vip"),
            d,
            "ban");

        int alerts = 0;
        waf.AlertRaised += _ => alerts++;
        _session.ProcessPrivateMessageReceivedForTests("AdBot", "完全无关的普通私信");

        _session.GetPrivateMessages("AdBot").Should().ContainSingle(because: "dropped PM must not append");
        alerts.Should().Be(0);
    }

    [Fact]
    public void Session_Same_Banned_Body_From_Other_Peer_Not_Stored()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        _session.IngressWaf = waf;

        string body = "加群领免费代练 http://spam.vip";
        var seed = WafAttackFixtures.PromoPrivateMessage("Seed", body);
        waf.BlockFromAlert(seed, waf.Evaluate(seed), "ban");

        _session.ProcessPrivateMessageReceivedForTests("OtherPeer", body);
        _session.GetPrivateMessages("OtherPeer").Should().BeEmpty();
    }

    [Fact]
    public void Session_Waf_Warn_Stores_With_Risk_Prefix()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        _session.IngressWaf = waf;

        _session.ProcessPrivateMessageReceivedForTests("AdBot", "加群领免费代练 http://spam.vip");

        string display = _session.GetPrivateMessages("AdBot").Single().DisplayText;
        display.Should().StartWith("[风险]");
        _session.GetPrivateMessages("AdBot").Single().RiskLevel.Should().Be(WafSeverity.Warn);
    }

    [Fact]
    public void Irc_Private_Action_Passes_Raw_Soh_To_Event()
    {
        var conn = new CnCNetIrcConnection("testsys");
        conn.SetCurrentNickForTests("LocalUser");

        CnCNetPrivateMessageEventArgs? seen = null;
        conn.PrivateMessageReceived += (_, e) => seen = e;

        conn.ProcessIncomingLineForTests(
            ":Alice!a@b PRIVMSG LocalUser :\u0001ACTION dances\u0001");

        seen.Should().NotBeNull();
        seen!.Sender.Should().Be("Alice");
        seen.Message.Should().Be("\u0001ACTION dances\u0001");
    }

    [Fact]
    public void Irc_Private_Plain_Routes_To_Local_Nick()
    {
        var conn = new CnCNetIrcConnection("testsys");
        conn.SetCurrentNickForTests("LocalUser");

        CnCNetPrivateMessageEventArgs? seen = null;
        conn.PrivateMessageReceived += (_, e) => seen = e;

        conn.ProcessIncomingLineForTests(":Bob!b@c PRIVMSG LocalUser :hi");

        seen.Should().NotBeNull();
        seen!.Message.Should().Be("hi");
    }

    [Fact]
    public void Send_Wire_Format_Matches_Dx_Privmsg_Nick()
    {
        CnCNetIrcConnection.FormatPrivateMessageWire("Alice", "hello")
            .Should().Be("PRIVMSG Alice :hello");
    }
}
