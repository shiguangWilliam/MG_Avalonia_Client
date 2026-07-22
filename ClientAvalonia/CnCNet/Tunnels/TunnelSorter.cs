using System;
using System.Collections.Generic;
using Rampastring.Tools;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Min-heap of CnCNet tunnels keyed by <see cref="TunnelSortKey"/>.
/// </summary>
/// <remarks>
/// Why a heap (not LINQ OrderBy):
///  <list type="bullet">
///   <item>O(1) peek of current best — needed every UI tick.</item>
///   <item>O(log n) incremental update — ping results arrive one by one.</item>
///   <item>Preserves the original <c>ServerList</c> order (heap is a separate index).</item>
///   <item>Multi-dimension sort = a struct CompareTo change, not a pipeline change.</item>
///  </list>
/// Per low-latency-tunnel.md v2.
/// </remarks>
public sealed class TunnelSorter
{
    private readonly PriorityQueue<CnCNetTunnel, TunnelSortKey> _heap = new();
    private readonly object _lock = new();
    private CnCNetTunnel? _currentBest;

    /// <summary>
    /// Raised on the calling thread whenever the best tunnel changes — either a
    /// newly-added tunnel with lower latency, or the current one re-pinged worse
    /// and a different tunnel took the lead. Subscribers typically marshal to
    /// the UI thread themselves.
    /// </summary>
    public event Action<CnCNetTunnel?>? BestTunnelChanged;

    /// <summary>
    /// Insert or refresh a tunnel's measurement. O(log n).
    /// </summary>
    /// <remarks>
    /// <see cref="PriorityQueue{TElement, TPriority}"/> has no efficient update;
    /// we accept duplicate entries and skip stale ones on Peek. With ~30 tunnels
    /// re-pinged every 5 min, total heap size stays well under 1k entries.
    /// The invariant for stale-detection is <c>tunnel.PingInMs == key.PingInMs</c>;
    /// this method keeps the invariant by syncing <paramref name="tunnel"/>'s
    /// <see cref="CnCNetTunnel.PingInMs"/> to <paramref name="pingInMs"/>.
    /// </remarks>
    public void Update(CnCNetTunnel tunnel, int pingInMs)
    {
        if (tunnel == null) throw new ArgumentNullException(nameof(tunnel));

        // Keep the invariant that PurgeStalePeek relies on — the heap key must
        // match the tunnel object's current PingInMs. Doing this here means
        // callers (CnCNetSession / TunnelPrewarmer / tests) don't have to remember.
        tunnel.PingInMs = pingInMs;

        lock (_lock)
        {
            _heap.Enqueue(tunnel, new TunnelSortKey(pingInMs, tunnel.Official, tunnel.Name));
            ReevaluateBest();
        }
    }

    /// <summary>Current best tunnel, or null if no measurements yet. O(1) amortized.</summary>
    public CnCNetTunnel? TryPeekBest()
    {
        lock (_lock)
        {
            PurgeStalePeek();
            return _heap.TryPeek(out var tunnel, out _) ? tunnel : null;
        }
    }

    /// <summary>Force a re-peek + event raise (e.g. after mass re-ping).</summary>
    public void RefreshBest()
    {
        lock (_lock) ReevaluateBest();
    }

    /// <summary>Clear all entries (e.g. on tunnel list reload from IRC).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _heap.Clear();
            if (_currentBest != null)
            {
                _currentBest = null;
                RaiseBestChanged(null);
            }
        }
    }

    private void ReevaluateBest()
    {
        PurgeStalePeek();
        if (!_heap.TryPeek(out var newBest, out _))
        {
            if (_currentBest != null)
            {
                _currentBest = null;
                RaiseBestChanged(null);
            }
            return;
        }

        if (!ReferenceEquals(newBest, _currentBest))
        {
            _currentBest = newBest;
            RaiseBestChanged(newBest);
        }
    }

    /// <summary>
    /// Pop entries whose recorded ping differs from the tunnel object's current
    /// PingInMs (meaning a newer measurement has superseded it). Stops at the
    /// first live entry. O(k log n) where k = stale entries purged.
    /// </summary>
    private void PurgeStalePeek()
    {
        while (_heap.TryPeek(out var top, out var key))
        {
            if (top.PingInMs == key.PingInMs) break;
            _heap.Dequeue();
        }
    }

    private void RaiseBestChanged(CnCNetTunnel? newBest)
    {
        try { BestTunnelChanged?.Invoke(newBest); }
        catch (Exception ex) { Logger.Log($"[TunnelSorter] BestTunnelChanged subscriber threw: {ex}"); }
    }
}
