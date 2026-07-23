using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientAvalonia.CnCNet.Waf;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

[Collection("ProgramConstantsSerial")]
public sealed class WafBlocklistTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public WafBlocklistTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void BlockEntry_DisplayLine_Shows_Kind_Target_And_Actor_Triple()
    {
        var entry = new WafBlockEntry
        {
            Key = "nick=Alice",
            Kind = "nick",
            Nick = "Alice",
            Ident = "mo.abc",
            Host = "user.gamesurge.net",
            Note = "私信推广",
            AddedUtc = new DateTime(2026, 7, 22, 6, 0, 0, DateTimeKind.Utc),
        };

        entry.ActorTriple.Should().Be("Alice!mo.abc@user.gamesurge.net");
        entry.DisplayLine.Should().Contain("[nick]");
        entry.DisplayLine.Should().Contain("Alice!mo.abc@user.gamesurge.net");
        entry.DisplayLine.Should().Contain("私信推广");
    }

    [Theory]
    [InlineData("Alice", "nick=Alice")]
    [InlineData("nick=Bob", "nick=Bob")]
    [InlineData("1.2.3.4:50000", "tunnel=1.2.3.4:50000")]
    [InlineData("#room", "room=#room")]
    public void NormalizeManualKey_Accepts_Common_Forms(string raw, string expected)
        => WafBlockEntry.NormalizeManualKey(raw).Should().Be(expected);

    [Fact]
    public void Store_RoundTrip_Preserves_Actor_Triple()
    {
        var entries = new[]
        {
            WafBlockEntry.FromKey(
                "nick=Carol",
                nick: "Carol",
                ident: "mg.xyz",
                host: "host.example",
                note: "告警加入"),
            WafBlockEntry.FromKey("tunnel=175.178.174.40:50000", note: "挂房隧道"),
        };

        WafUserListStore.SaveEntries(entries);
        IReadOnlyList<WafBlockEntry> loaded = WafUserListStore.LoadEntries();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(e =>
            e.Key == "nick=Carol"
            && e.Nick == "Carol"
            && e.Ident == "mg.xyz"
            && e.Host == "host.example");
        loaded.Should().Contain(e => e.Key.StartsWith("tunnel=", StringComparison.OrdinalIgnoreCase));

        string jsonPath = Path.Combine(_root.GameRoot, "Client", "WafBlockList.json");
        File.Exists(jsonPath).Should().BeTrue();
    }

    [Fact]
    public void Waf_Block_Unblock_And_ListEntries_Work()
    {
        var waf = new CnCNetIngressWaf(() => new WafSettings(), persistUserList: false);
        waf.Block(new WafBlockEntry
        {
            Key = "nick=Dave",
            Nick = "Dave",
            Ident = "id1",
            Host = "h1",
            Note = "test",
        });

        waf.IsBlocked("nick=Dave").Should().BeTrue();
        waf.ListBlockedEntries().Should().ContainSingle(e => e.ActorTriple == "Dave!id1@h1");

        waf.Unblock("nick=Dave");
        waf.ListBlockedEntries().Should().BeEmpty();
    }

    [Fact]
    public void Legacy_Ini_Keys_Are_Imported()
    {
        string client = Path.Combine(_root.GameRoot, "Client");
        Directory.CreateDirectory(client);
        File.WriteAllText(
            Path.Combine(client, "WafUserList.ini"),
            "[Blocked]\r\nKey=nick=Legacy\r\n");

        IReadOnlyList<WafBlockEntry> loaded = WafUserListStore.LoadEntries();
        loaded.Should().Contain(e => e.Key == "nick=Legacy");
    }
}
