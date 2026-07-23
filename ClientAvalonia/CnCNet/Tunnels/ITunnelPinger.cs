using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Abstraction for pinging a tunnel server. Default implementation
/// <see cref="IcmpTunnelPinger"/> uses System.Net.NetworkInformation.Ping;
/// tests inject a fake.
/// </summary>
public interface ITunnelPinger
{
    /// <summary>
    /// Returns round-trip latency in milliseconds, or -1 on failure
    /// (timeout, DNS failure, ICMP blocked, etc.).
    /// </summary>
    Task<int> PingAsync(CnCNetTunnel tunnel, CancellationToken ct = default);
}
