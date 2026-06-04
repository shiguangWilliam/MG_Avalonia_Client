using System;
using System.Collections.Generic;
using Rampastring.Tools;

namespace ClientCore.Network;

/// <summary>Requests per-player NAT ports from a CnCNet tunnel server (XNA CnCNetTunnel.GetPlayerPortInfo).</summary>
public static class CnCNetTunnelPortAllocator
{
    private const int RequestTimeoutMs = 10_000;

    public static IReadOnlyList<int> RequestPlayerPorts(CnCNetTunnelEntry tunnel, int playerCount)
    {
        if (playerCount <= 0)
            return [];

        try
        {
            string url = $"http://{tunnel.Address}:{tunnel.Port}/request?clients={playerCount}";
            Logger.Log($"CnCNetTunnelPortAllocator: {url}");

            string? data = CnCNetHttp.DownloadString(url, RequestTimeoutMs);
            if (string.IsNullOrWhiteSpace(data))
                return [];

            data = data.Replace("[", string.Empty).Replace("]", string.Empty);
            var ports = new List<int>();
            foreach (string part in data.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int port))
                    ports.Add(port);
            }

            Logger.Log($"CnCNetTunnelPortAllocator: received {ports.Count} ports from {tunnel.Address}:{tunnel.Port}");
            return ports;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetTunnelPortAllocator failed: {ex.Message}");
            return [];
        }
    }
}
