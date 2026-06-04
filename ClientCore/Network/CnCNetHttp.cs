using System;
using System.IO;
using System.Net;
using Rampastring.Tools;

namespace ClientCore.Network;

/// <summary>HTTP helper for CnCNet status/tunnel endpoints (timeout-aware).</summary>
public static class CnCNetHttp
{
    public static string? DownloadString(string url, int timeoutMilliseconds = 5000)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            using var client = new WebClient { Proxy = null };
            // WebClient timeout via async hack - use HttpWebRequest for sync timeout
            var request = WebRequest.Create(url);
            request.Timeout = timeoutMilliseconds;
            request.Proxy = null;
            using WebResponse response = request.GetResponse();
            using Stream stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetHttp.DownloadString failed ({url}): {ex.Message}");
            return null;
        }
    }

    public static byte[]? DownloadBytes(string url, int timeoutMilliseconds = 10000)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var request = WebRequest.Create(url);
            request.Timeout = timeoutMilliseconds;
            request.Proxy = null;
            using WebResponse response = request.GetResponse();
            using Stream stream = response.GetResponseStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetHttp.DownloadBytes failed ({url}): {ex.Message}");
            return null;
        }
    }
}
