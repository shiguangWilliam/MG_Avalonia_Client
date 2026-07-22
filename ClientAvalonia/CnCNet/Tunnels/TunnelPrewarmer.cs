using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Pings all tunnels concurrently on CnCNet startup; each result enters the
/// heap immediately so the UI can pick a best tunnel as soon as the fastest
/// one responds — no need to wait for slow/timed-out servers. Per
/// low-latency-tunnel.md v2.
/// </summary>
public sealed class TunnelPrewarmer
{
    private readonly ITunnelPinger _pinger;
    private readonly TunnelSorter _sorter;
    private readonly int _concurrency;

    public TunnelPrewarmer(ITunnelPinger pinger, TunnelSorter sorter, int concurrency = 8)
    {
        _pinger = pinger ?? throw new System.ArgumentNullException(nameof(pinger));
        _sorter = sorter ?? throw new System.ArgumentNullException(nameof(sorter));
        _concurrency = concurrency < 1 ? 1 : concurrency;
    }

    public async Task PrewarmAsync(IReadOnlyList<CnCNetTunnel> tunnels, CancellationToken ct = default)
    {
        if (tunnels == null || tunnels.Count == 0) return;

        await Parallel.ForEachAsync(
            tunnels,
            new ParallelOptions { MaxDegreeOfParallelism = _concurrency, CancellationToken = ct },
            async (tunnel, token) =>
            {
                int ping = await _pinger.PingAsync(tunnel, token);
                // Mirror the measurement onto the tunnel object so legacy
                // code paths (which read PingInMs directly) also see fresh data.
                tunnel.PingInMs = ping;
                _sorter.Update(tunnel, ping);
            });
    }
}
