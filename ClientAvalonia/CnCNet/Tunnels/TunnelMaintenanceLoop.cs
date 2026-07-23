using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Periodically re-pings the top-K known low-latency tunnels (cheaper than
/// full re-ping) and auto-switches the user's selected tunnel if the current
/// one degrades by more than the hysteresis factor. Per low-latency-tunnel.md v2.
/// </summary>
public sealed class TunnelMaintenanceLoop : IDisposable
{
    private readonly ITunnelPinger _pinger;
    private readonly TunnelSorter _sorter;
    private readonly Func<IReadOnlyList<CnCNetTunnel>> _getAllTunnels;
    private readonly Func<CnCNetTunnel?> _getSelected;
    private readonly Action<CnCNetTunnel> _setSelected;
    private readonly Timer _timer;

    /// <summary>Re-ping the K fastest known tunnels every cycle.</summary>
    public const int DefaultTopK = 5;

    /// <summary>
    /// Switch only if current tunnel's ping exceeds best * this factor — avoids
    /// ping-pong between tunnels with near-equal latency.
    /// </summary>
    public const double DefaultSwitchHysteresis = 1.5;

    private readonly int _topK;
    private readonly double _switchHysteresis;

    public TunnelMaintenanceLoop(
        ITunnelPinger pinger,
        TunnelSorter sorter,
        Func<IReadOnlyList<CnCNetTunnel>> getAllTunnels,
        Func<CnCNetTunnel?> getSelected,
        Action<CnCNetTunnel> setSelected,
        TimeSpan? interval = null,
        int? topK = null,
        double? switchHysteresis = null)
    {
        _pinger = pinger ?? throw new ArgumentNullException(nameof(pinger));
        _sorter = sorter ?? throw new ArgumentNullException(nameof(sorter));
        _getAllTunnels = getAllTunnels ?? throw new ArgumentNullException(nameof(getAllTunnels));
        _getSelected = getSelected ?? throw new ArgumentNullException(nameof(getSelected));
        _setSelected = setSelected ?? throw new ArgumentNullException(nameof(setSelected));
        _topK = topK ?? DefaultTopK;
        _switchHysteresis = switchHysteresis ?? DefaultSwitchHysteresis;

        TimeSpan period = interval ?? TimeSpan.FromMinutes(5);
        _timer = new Timer(_ => _ = TickAsync(), null, period, period);
    }

    private async Task TickAsync()
    {
        try
        {
            IReadOnlyList<CnCNetTunnel> all = _getAllTunnels();
            if (all.Count == 0) return;

            // Re-ping top-K (by current PingInMs) plus the currently-selected
            // tunnel even if outside top-K (covers "user picked a slow official").
            List<CnCNetTunnel> toReping = all
                .Where(t => t.PingInMs > 0)
                .OrderBy(t => t.PingInMs)
                .Take(_topK)
                .ToList();

            CnCNetTunnel? selected = _getSelected();
            if (selected != null && !toReping.Contains(selected))
                toReping.Add(selected);

            if (toReping.Count == 0) return;

            await Parallel.ForEachAsync(
                toReping,
                async (t, ct) =>
                {
                    int ping = await _pinger.PingAsync(t, ct);
                    t.PingInMs = ping;
                    _sorter.Update(t, ping);
                });

            // Auto-switch if current is significantly worse than the best.
            CnCNetTunnel? best = _sorter.TryPeekBest();
            if (best == null || selected == null || ReferenceEquals(best, selected))
                return;

            if (best.PingInMs <= 0 || selected.PingInMs <= 0)
                return;

            if (selected.PingInMs > best.PingInMs * _switchHysteresis)
            {
                Logger.Log($"[TunnelMaintenance] auto-switching from {selected.Name} ({selected.PingInMs}ms) to {best.Name} ({best.PingInMs}ms)");
                _setSelected(best);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[TunnelMaintenance] tick threw: {ex}");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
