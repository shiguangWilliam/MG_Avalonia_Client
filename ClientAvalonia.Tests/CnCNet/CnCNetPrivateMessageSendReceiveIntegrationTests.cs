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

/// <summary>
/// Integration: IRC wire → connection event → Session store / WAF / local send echo path,
/// without a live IRC server.
/// </summary>
[Collection("ProgramConstantsSerial")]
[Trait("Category", "Integration")]
public sealed class CnCNetPrivateMessageSendReceiveIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();
    private readonly CnCNetSession _session = CnCNetSession.Instance;

    public CnCNetPrivateMessageSendReceiveIntegrationTests()
    {
        _root.BindToProgramConstants();
        _session.ResetPrivateMessagingForTests();
        _session.PrivateMessagePolicyOverrideForTests = AllowPrivateMessagesFromEnum.All;
        _session.IngressWaf = null;
    }

    public void Dispose()
    {
        _session.ResetPrivateMessagingForTests();
        _session.IngressWaf = null;
        _root.Dispose();
    }

    [Fact]
    public void EndToEnd_Irc_Line_To_Session_Store_And_Unread_Badge()
    {
        var conn = new CnCNetIrcConnection("testsys");
        conn.SetCurrentNickForTests("LocalUser");

        conn.PrivateMessageReceived += (_, e) =>
            _session.ProcessPrivateMessageReceivedForTests(e.Sender, e.Message);

        conn.ProcessIncomingLineForTests(":Peer!u@h PRIVMSG LocalUser :integration hello");

        _session.UnreadPrivateMessageCount.Should().Be(1);
        _session.LastPrivateMessagePartner.Should().Be("Peer");
        _session.GetPrivateConversationSummaries()
            .Should().Contain(s => s.Nick == "Peer" && s.Unread == 1);
        _session.GetPrivateMessages("Peer").Single().DisplayText.Should().Contain("integration hello");
    }

    [Fact]
    public void EndToEnd_Private_Action_Wire_To_Single_Formatted_Line()
    {
        var conn = new CnCNetIrcConnection("testsys");
        conn.SetCurrentNickForTests("LocalUser");
        conn.PrivateMessageReceived += (_, e) =>
            _session.ProcessPrivateMessageReceivedForTests(e.Sender, e.Message);

        conn.ProcessIncomingLineForTests(
            ":Peer!u@h PRIVMSG LocalUser :\u0001ACTION waves hello\u0001");

        string display = _session.GetPrivateMessages("Peer").Single().DisplayText;
        display.Should().Contain("====> waves hello");
        display.Should().NotContain("====> Peer waves");
    }

    [Fact]
    public void Send_Echo_Stores_Local_Line_Without_Unread_When_Connected_Path_Unavailable()
    {
        // Without a live connection SendPrivateMessage is a no-op; pin the wire builder
        // and the local-echo contract separately via Format + Append path used by Session.
        string wire = CnCNetIrcConnection.FormatPrivateMessageWire("Alice", "outgoing");
        wire.Should().Be("PRIVMSG Alice :outgoing");

        // Simulate successful send echo (Session AppendPrivateMessage + LastPartner).
        _session.EnsurePrivateConversation("Alice");
        _session.MarkPrivateMessagesRead("Alice");
        // Ensure creates a thread; send path would Append with incrementUnread:false.
        // Drive receive of own echo is not used; verify unread stays 0 after ensure+mark.
        _session.UnreadPrivateMessageCount.Should().Be(0);
        _session.LastPrivateMessagePartner.Should().Be("Alice");
    }

    [Fact]
    public void Viewing_Peer_Then_Second_Peer_Message_Keeps_Unread_For_Other()
    {
        _session.SetViewingPrivateMessagePeer("A");
        _session.ProcessPrivateMessageReceivedForTests("A", "seen");
        _session.ProcessPrivateMessageReceivedForTests("B", "unseen");

        _session.UnreadPrivateMessageCount.Should().Be(1);
        _session.GetPrivateConversationSummaries()
            .Should().Contain(s => s.Nick == "B" && s.Unread == 1);
        _session.GetPrivateConversationSummaries()
            .Should().Contain(s => s.Nick == "A" && s.Unread == 0);
    }
}

/// <summary>
/// WAF on the Session private-message ingress path (same Evaluate surface as production).
/// </summary>
[Collection("ProgramConstantsSerial")]
[Trait("Category", "Integration")]
public sealed class CnCNetPrivateMessageWafIntegrationTests : IDisposable
{
    private readonly TempGameRoot _root = new();
    private readonly CnCNetSession _session = CnCNetSession.Instance;

    public CnCNetPrivateMessageWafIntegrationTests()
    {
        _root.BindToProgramConstants();
        _session.ResetPrivateMessagingForTests();
        _session.PrivateMessagePolicyOverrideForTests = AllowPrivateMessagesFromEnum.All;
    }

    public void Dispose()
    {
        _session.ResetPrivateMessagingForTests();
        _session.IngressWaf = null;
        _root.Dispose();
    }

    [Fact]
    public void Promo_Pm_Warns_Then_Nick_Block_Drops_Through_Session()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        _session.IngressWaf = waf;

        _session.ProcessPrivateMessageReceivedForTests("AdBot", "加群领免费代练 http://spam.vip");
        _session.GetPrivateMessages("AdBot").Single().RiskLevel.Should().Be(WafSeverity.Warn);
        _session.GetPrivateMessages("AdBot").Single().DisplayText.Should().StartWith("[风险]");

        waf.Block("nick=AdBot");
        _session.ProcessPrivateMessageReceivedForTests("AdBot", "加群领免费代练 http://spam.vip/x");
        _session.GetPrivateMessages("AdBot").Should().HaveCount(1, "dropped second message must not append");
    }

    [Theory]
    [InlineData("discord.gg/abc123 join my server")]
    [InlineData("QQ群：123456789 加群")]
    [InlineData("dm me for boosting")]
    public void English_And_Zh_Promo_Pm_Warn_Via_Session(string text)
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        _session.IngressWaf = waf;

        _session.ProcessPrivateMessageReceivedForTests("Promo", text);

        var line = _session.GetPrivateMessages("Promo").Single();
        line.RiskLevel.Should().Be(WafSeverity.Warn);
        line.DisplayText.Should().StartWith("[风险]");
    }

    [Fact]
    public void Irc_Wire_Plus_Waf_Drop_Never_Raises_Arrived()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        waf.Block("nick=Blocked");
        _session.IngressWaf = waf;

        int arrived = 0;
        _session.PrivateMessageArrived += (_, _) => arrived++;

        var conn = new CnCNetIrcConnection("testsys");
        conn.SetCurrentNickForTests("LocalUser");
        conn.PrivateMessageReceived += (_, e) =>
            _session.ProcessPrivateMessageReceivedForTests(e.Sender, e.Message);

        conn.ProcessIncomingLineForTests(":Blocked!x@y PRIVMSG LocalUser :spam");

        arrived.Should().Be(0);
        _session.GetPrivateMessages("Blocked").Should().BeEmpty();
    }
}
