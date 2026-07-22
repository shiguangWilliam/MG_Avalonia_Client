using ClientCore;
using System;
using System.Collections.Generic;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Per-listing-channel GAME wire dialect (lock-once).
/// On enter: prefer stock DX R13 emit while unlocked. The first observed peer shape
/// (R13/13-field or R10/11-field) locks that channel name; later CTCPs do not flip it.
/// Leaving the channel clears the lock so the next join re-probes.
/// </summary>
public sealed class CnCNetGameBroadcastDialect
{
    public const string LegacyRevision = "R10";

    public const int LegacyFieldCount = 11;

    private readonly object _sync = new();
    private readonly Dictionary<string, ChannelDialect> _byChannel = new(StringComparer.OrdinalIgnoreCase);

    public enum WireShape
    {
        /// <summary>Stock DX: R13 + 13 fields.</summary>
        ModernR13,

        /// <summary>Older MG DX launcher: R10 + 11 fields.</summary>
        LegacyR10,
    }

    /// <summary>Mark membership: unlocked, prefer R13 until the first peer observation.</summary>
    public void EnterChannel(string broadcastChannel)
    {
        string key = NormalizeChannel(broadcastChannel);
        lock (_sync)
        {
            if (!_byChannel.ContainsKey(key))
                _byChannel[key] = new ChannelDialect();
        }
    }

    /// <summary>Drop lock/state when PARTing the listing channel so the next JOIN re-probes.</summary>
    public void LeaveChannel(string broadcastChannel)
    {
        if (string.IsNullOrWhiteSpace(broadcastChannel))
            return;

        string key = NormalizeChannel(broadcastChannel);
        lock (_sync)
            _byChannel.Remove(key);
    }

    public void Clear()
    {
        lock (_sync)
            _byChannel.Clear();
    }

    /// <summary>True after the first successful peer GAME observation for this channel.</summary>
    public bool IsWireModeLocked(string broadcastChannel)
    {
        if (string.IsNullOrWhiteSpace(broadcastChannel))
            return false;

        string key = NormalizeChannel(broadcastChannel);
        lock (_sync)
            return _byChannel.TryGetValue(key, out ChannelDialect? state) && state.Locked;
    }

    /// <summary>
    /// Observe a peer GAME payload. Locks the channel on the first known shape; ignores later flips.
    /// Pass <paramref name="fromLocalSender"/> = true to skip (own echo must not lock R13 on an R10 channel).
    /// </summary>
    public void ObserveInbound(string broadcastChannel, string ctcpMessage, bool fromLocalSender = false)
    {
        if (fromLocalSender
            || string.IsNullOrWhiteSpace(broadcastChannel)
            || string.IsNullOrEmpty(ctcpMessage)
            || !ctcpMessage.StartsWith("GAME ", StringComparison.Ordinal)
            || ctcpMessage.Length <= 5)
        {
            return;
        }

        WireShape? shape = TryDetectShape(ctcpMessage);
        if (shape == null)
            return;

        string key = NormalizeChannel(broadcastChannel);
        lock (_sync)
        {
            if (!_byChannel.TryGetValue(key, out ChannelDialect? state))
            {
                state = new ChannelDialect();
                _byChannel[key] = state;
            }

            state.TryLock(shape.Value);
        }
    }

    /// <summary>
    /// Emit dialect for a listing channel: optional ini force → locked peer shape → default R13.
    /// </summary>
    public WireShape ResolveEmitShape(string? broadcastChannel)
    {
        string forced = ClientConfiguration.Instance.CnCNetProtocolRevision;
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return forced.Trim().Equals(LegacyRevision, StringComparison.OrdinalIgnoreCase)
                ? WireShape.LegacyR10
                : WireShape.ModernR13;
        }

        if (string.IsNullOrWhiteSpace(broadcastChannel))
            return WireShape.ModernR13;

        string key = NormalizeChannel(broadcastChannel);
        lock (_sync)
        {
            if (_byChannel.TryGetValue(key, out ChannelDialect? state))
                return state.Shape;
        }

        return WireShape.ModernR13;
    }

    public bool PrefersLegacyEmit(string? broadcastChannel)
        => ResolveEmitShape(broadcastChannel) == WireShape.LegacyR10;

    private static WireShape? TryDetectShape(string ctcpMessage)
    {
        string[] parts = ctcpMessage[5..].Split(';');
        if (parts.Length == 0)
            return null;

        string revision = parts[0];
        if (parts.Length == Protocol.CnCNetMultiplayerProtocol.GameBroadcastFieldCount
            && revision.Equals(ProgramConstants.CNCNET_PROTOCOL_REVISION, StringComparison.OrdinalIgnoreCase))
        {
            return WireShape.ModernR13;
        }

        if (parts.Length == LegacyFieldCount
            && revision.Equals(LegacyRevision, StringComparison.OrdinalIgnoreCase))
        {
            return WireShape.LegacyR10;
        }

        return null;
    }

    private static string NormalizeChannel(string channel)
    {
        string normalized = channel.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;
        return normalized.ToLowerInvariant();
    }

    private sealed class ChannelDialect
    {
        public WireShape Shape { get; private set; } = WireShape.ModernR13;

        public bool Locked { get; private set; }

        public void TryLock(WireShape shape)
        {
            if (Locked)
                return;

            Shape = shape;
            Locked = true;
        }
    }
}
