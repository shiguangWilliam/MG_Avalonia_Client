using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Default <see cref="ITunnelPinger"/> using ICMP. Takes 3 samples and returns
/// the minimum (matches the spirit of <c>CnCNetTunnel.UpdatePing</c> but with
/// retry). Returns -1 if all samples fail.
/// </summary>
public sealed class IcmpTunnelPinger : ITunnelPinger
{
    private const int TimeoutMs = 1000;
    private const int Samples = 3;

    public async Task<int> PingAsync(CnCNetTunnel tunnel, CancellationToken ct = default)
    {
        if (tunnel == null) return -1;

        try
        {
            using var ping = new Ping();
            int best = -1;
            for (int i = 0; i < Samples; i++)
            {
                ct.ThrowIfCancellationRequested();
                PingReply reply = await ping.SendPingAsync(tunnel.Address, TimeoutMs).WaitAsync(ct);
                if (reply.Status == IPStatus.Success)
                {
                    int ms = (int)reply.RoundtripTime;
                    if (best == -1 || ms < best) best = ms;
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is PingException or OperationCanceledException)
        {
            Logger.Log($"[IcmpTunnelPinger] {tunnel.Name} ({tunnel.Address}): {ex.Message}");
            return -1;
        }
    }
}
