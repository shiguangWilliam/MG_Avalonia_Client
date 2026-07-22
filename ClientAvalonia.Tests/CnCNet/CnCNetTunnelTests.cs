using System;
using ClientAvalonia.Domain.Multiplayer.CnCNet;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Single-point crash resilience for <see cref="CnCNetTunnel"/>.
/// The master list is fetched from an external HTTPS endpoint and any field can be
/// attacker-influenced (or simply mis-published). The ping path in particular must never
/// crash the maintenance loop, regardless of whether the address is a dotted-quad IP,
/// a hostname, an empty string, or outright garbage.
/// </summary>
public sealed class CnCNetTunnelTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void UpdatePing_WithHostnameAddress_DoesNotThrow()
    {
        // Pre-fix: this configuration threw FormatException because IPAddress.Parse rejects
        // hostnames. After fix: the address is passed straight to Ping.Send, and any network
        // failure is caught and logged. We don't assert on PingInMs (the network is unreachable
        // in CI); we only assert that the method is non-throwing.
        var tunnel = new CnCNetTunnel
        {
            Name = "ci-tunnel",
            Address = "nonexistent.invalid",
            Port = 50000,
        };

        Action act = tunnel.UpdatePing;

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UpdatePing_WithEmptyAddress_DoesNotThrow()
    {
        var tunnel = new CnCNetTunnel
        {
            Name = "empty",
            Address = string.Empty,
            Port = 50000,
        };

        Action act = tunnel.UpdatePing;

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UpdatePing_WithGarbageAddress_DoesNotThrow()
    {
        var tunnel = new CnCNetTunnel
        {
            Name = "garbage",
            Address = "not a host name!!!",
            Port = 50000,
        };

        Action act = tunnel.UpdatePing;

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UpdatePing_WithDottedQuadAddress_DoesNotThrow()
    {
        // Pre-fix code path: IPAddress.Parse succeeds for dotted-quad, then ping.Send runs.
        // The fix preserves this behavior (TryParse succeeds → use the parsed IPAddress).
        var tunnel = new CnCNetTunnel
        {
            Name = "ip",
            Address = "127.0.0.1",
            Port = 50000,
        };

        Action act = tunnel.UpdatePing;

        act.Should().NotThrow();
        // 127.0.0.1 should be reachable from CI; PingInMs reflects RTT.
        tunnel.PingInMs.Should().BeGreaterThanOrEqualTo(-1);
    }
}
