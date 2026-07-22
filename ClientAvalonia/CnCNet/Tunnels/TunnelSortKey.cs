using System;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Sort key for the tunnel priority queue. Encodes every dimension we sort on —
/// adding a new dimension is a matter of adding a field here and updating
/// <see cref="CompareTo"/>. The heap itself never needs to change.
/// </summary>
/// <remarks>
/// Per low-latency-tunnel.md v2 (min-heap design).
/// </remarks>
public readonly record struct TunnelSortKey(
    int PingInMs,    // -1 means "not measured yet"; treated as int.MaxValue in comparison
    bool Official,   // official tunnels break ties ahead of community ones
    string Name) : IComparable<TunnelSortKey>
{
    public int CompareTo(TunnelSortKey other)
    {
        // 1) Latency ascending (lower is better). Unmeasured → worst.
        int a = PingInMs < 0 ? int.MaxValue : PingInMs;
        int b = other.PingInMs < 0 ? int.MaxValue : other.PingInMs;
        int cmp = a.CompareTo(b);
        if (cmp != 0) return cmp;

        // 2) Official wins ties (more trustworthy long-term).
        //    true > false as bool.CompareTo is documented; reverse to put official first.
        cmp = other.Official.CompareTo(Official);
        if (cmp != 0) return cmp;

        // 3) Stable alphabetical tiebreak (avoids heap-order dependence on equal keys).
        return string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}
