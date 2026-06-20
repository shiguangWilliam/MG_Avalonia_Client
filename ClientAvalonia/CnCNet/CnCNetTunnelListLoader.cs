using ClientAvalonia.Domain.Multiplayer.CnCNet;
using ClientCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

public static class CnCNetTunnelListLoader
{
    private const int SupportedTunnelVersion = 2;

    public static IReadOnlyList<CnCNetTunnel> Load(bool cacheToDisk = true)
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

    public static IReadOnlyList<CnCNetTunnel> Parse(byte[] raw, bool cacheToDisk = true)
    {
        var tunnels = new List<CnCNetTunnel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text = Encoding.Default.GetString(raw);
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines.Skip(1))
        {
            CnCNetTunnel? tunnel = CnCNetTunnel.Parse(line);
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
