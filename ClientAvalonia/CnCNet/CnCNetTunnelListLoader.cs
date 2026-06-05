using ClientCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

public sealed class CnCNetTunnelEntry
{
    public string Address { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool RequiresPassword { get; init; }

    public bool Official { get; init; }

    public int Version { get; init; }

    /// <summary>Requests per-player NAT ports (XNA CnCNetTunnel.GetPlayerPortInfo).</summary>
    public IReadOnlyList<int> RequestPlayerPorts(int playerCount)
    {
        if (playerCount <= 0)
            return [];

        try
        {
            string url = $"http://{Address}:{Port}/request?clients={playerCount}";
            Logger.Log($"CnCNetTunnelEntry: {url}");

            string? data = CnCNetHttp.DownloadString(url, 10_000);
            if (string.IsNullOrWhiteSpace(data))
                return [];

            data = data.Replace("[", string.Empty).Replace("]", string.Empty);
            var ports = new List<int>();
            foreach (string part in data.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int port))
                    ports.Add(port);
            }

            Logger.Log($"CnCNetTunnelEntry: received {ports.Count} ports from {Address}:{Port}");
            return ports;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetTunnelEntry.RequestPlayerPorts failed: {ex.Message}");
            return [];
        }
    }

    public static CnCNetTunnelEntry? Parse(string line)
    {
        try
        {
            string[] parts = line.Split(';');
            string[] addressParts = parts[0].Split(':');
            int status = int.Parse(parts[7]);
            return new CnCNetTunnelEntry
            {
                Address = addressParts[0],
                Port = int.Parse(addressParts[1]),
                Name = parts[3],
                RequiresPassword = parts[4] != "0",
                Official = status == 2,
                Version = int.Parse(parts[10], CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetTunnelEntry.Parse failed: {ex.Message}");
            return null;
        }
    }
}

public static class CnCNetTunnelListLoader
{
    private const int SupportedTunnelVersion = 2;

    public static IReadOnlyList<CnCNetTunnelEntry> Load(bool cacheToDisk = true)
    {
        byte[]? raw = LoadRawBytes();
        if (raw == null || raw.Length == 0)
            return [];

        return Parse(raw, cacheToDisk);
    }

    public static byte[]? LoadRawBytes()
    {
        string url = ClientConfiguration.Instance.CnCNetTunnelListURL;
        if (!string.IsNullOrWhiteSpace(url))
        {
            byte[]? online = CnCNetHttp.DownloadBytes(url);
            if (online != null && online.Length > 0)
                return online;
        }

        string cachePath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
        return File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
    }

    public static IReadOnlyList<CnCNetTunnelEntry> Parse(byte[] raw, bool cacheToDisk = true)
    {
        var tunnels = new List<CnCNetTunnelEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text = Encoding.Default.GetString(raw);
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines.Skip(1))
        {
            CnCNetTunnelEntry? tunnel = CnCNetTunnelEntry.Parse(line);
            if (tunnel == null || tunnel.RequiresPassword || tunnel.Version != SupportedTunnelVersion)
                continue;

            string key = $"{tunnel.Address}:{tunnel.Port}";
            if (!seen.Add(key))
                continue;

            tunnels.Add(tunnel);
        }

        if (cacheToDisk && tunnels.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(ProgramConstants.ClientUserFilesPath);
                File.WriteAllBytes(SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "tunnel_cache"), raw);
            }
            catch (Exception ex)
            {
                Logger.Log($"CnCNetTunnelListLoader: cache write failed: {ex.Message}");
            }
        }

        Logger.Log($"CnCNetTunnelListLoader: loaded {tunnels.Count} tunnels.");
        return tunnels;
    }
}
