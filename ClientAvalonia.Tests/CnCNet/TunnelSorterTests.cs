using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.CnCNet.Tunnels;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Unit tests for the low-latency tunnel selection (low-latency-tunnel.md v2).
/// No real ICMP traffic — tests use a fake ITunnelPinger and a synchronous
/// TunnelSorter (events are raised on the calling thread).
/// </summary>
public sealed class TunnelSorterTests
{
    [Fact]
    public void Lower_Ping_Wins()
    {
        var a = new TunnelSortKey(50, Official: false, "A");
        var b = new TunnelSortKey(200, Official: true, "B");

        a.CompareTo(b).Should().BeLessThan(0);
        b.CompareTo(a).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Official_Breaks_Tie()
    {
        var official = new TunnelSortKey(50, Official: true, "X");
        var community = new TunnelSortKey(50, Official: false, "Y");

        official.CompareTo(community).Should().BeLessThan(0);
    }

    [Fact]
    public void Unmeasured_Treated_As_Worst()
    {
        var unmeasured = new TunnelSortKey(-1, Official: true, "Official");
        var measured = new TunnelSortKey(999, Official: false, "Slow");

        measured.CompareTo(unmeasured).Should().BeLessThan(0);
    }

    [Fact]
    public void Name_Breaks_Full_Tie()
    {
        var a = new TunnelSortKey(50, true, "AAA");
        var b = new TunnelSortKey(50, true, "ZZZ");

        a.CompareTo(b).Should().BeLessThan(0);
    }

    [Fact]
    public void Update_Raises_BestTunnelChanged_On_New_Min()
    {
        var sorter = new TunnelSorter();
        var raised = new List<CnCNetTunnel?>();
        sorter.BestTunnelChanged += t => raised.Add(t);

        sorter.Update(NewTunnel("A", 200), 200);
        sorter.Update(NewTunnel("B", 50), 50);   // becomes new best
        sorter.Update(NewTunnel("C", 100), 100); // not better than B

        raised.Last().Should().NotBeNull();
        raised.Last()!.Name.Should().Be("B");
        sorter.TryPeekBest()!.Name.Should().Be("B");
    }

    [Fact]
    public void TryPeekBest_Null_When_Empty()
    {
        var sorter = new TunnelSorter();
        sorter.TryPeekBest().Should().BeNull();
    }

    [Fact]
    public void Clear_Raises_With_Null_And_Empty_Best()
    {
        var sorter = new TunnelSorter();
        sorter.Update(NewTunnel("A", 100), 100);

        CnCNetTunnel? raised = null;
        sorter.BestTunnelChanged += t => raised = t;
        sorter.Clear();

        raised.Should().BeNull();
        sorter.TryPeekBest().Should().BeNull();
    }

    [Fact]
    public void Stale_Entries_Are_Purged_On_Peek()
    {
        // First measurement: A=200, B=50. Best = B.
        var a = NewTunnel("A");
        var b = NewTunnel("B");
        var sorter = new TunnelSorter();
        sorter.Update(a, 200);
        sorter.Update(b, 50);
        sorter.TryPeekBest()!.Name.Should().Be("B");

        // A re-measured at 30. New heap entry. Old A=200 entry is now stale.
        sorter.Update(a, 30);
        sorter.TryPeekBest()!.Name.Should().Be("A");
        sorter.TryPeekBest()!.PingInMs.Should().Be(30);
    }

    [Fact]
    public async Task Prewarm_Pings_All_Tunnels_And_Updates_Sorter()
    {
        var pinger = new FakePinger { ["A"] = 200, ["B"] = 50, ["C"] = 100 };
        var sorter = new TunnelSorter();
        var prewarmer = new TunnelPrewarmer(pinger, sorter);

        await prewarmer.PrewarmAsync(new[] { NewTunnel("A"), NewTunnel("B"), NewTunnel("C") });

        sorter.TryPeekBest()!.Name.Should().Be("B");
    }

    [Fact]
    public async Task Prewarm_Mirrors_Ping_Onto_Tunnel_Object()
    {
        var pinger = new FakePinger { ["A"] = 80 };
        var sorter = new TunnelSorter();
        var tunnel = NewTunnel("A");
        tunnel.PingInMs.Should().Be(-1);

        await new TunnelPrewarmer(pinger, sorter).PrewarmAsync(new[] { tunnel });

        tunnel.PingInMs.Should().Be(80);
    }

    private static CnCNetTunnel NewTunnel(string name, int ping = -1) => new()
    {
        Name = name,
        Address = "127.0.0.1",
        Port = 1234,
        Official = false,
        PingInMs = ping,
    };

    private sealed class FakePinger : ITunnelPinger
    {
        public Dictionary<string, int> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> DelaysMs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int this[string name]
        {
            set => Responses[name] = value;
        }

        public Task<int> PingAsync(CnCNetTunnel tunnel, CancellationToken ct = default)
        {
            if (DelaysMs.TryGetValue(tunnel.Name, out int delay))
                return Task.Delay(delay, ct).ContinueWith(_ => Responses.GetValueOrDefault(tunnel.Name, -1), ct);
            return Task.FromResult(Responses.GetValueOrDefault(tunnel.Name, -1));
        }
    }
}
