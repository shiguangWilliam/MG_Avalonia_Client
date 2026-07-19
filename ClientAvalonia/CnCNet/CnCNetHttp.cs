using System;
using System.IO;
using System.Net;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// HTTP helper for CnCNet status/tunnel endpoints. Provides a typed result so callers
/// can distinguish transient network failures (retry-able) from permanent business
/// errors (4xx). The legacy <c>DownloadString</c>/<c>DownloadBytes</c> returning
/// <c>null</c> on failure are kept for backwards compat; new code should prefer the
/// <c>TryDownload*</c> variants.
/// </summary>
public static class CnCNetHttp
{
    public static string? DownloadString(string url, int timeoutMilliseconds = 5000)
        => TryDownloadString(url, timeoutMilliseconds).TryGetValue(out string? body) ? body : null;

    public static byte[]? DownloadBytes(string url, int timeoutMilliseconds = 10000)
        => TryDownloadBytes(url, timeoutMilliseconds).TryGetValue(out byte[]? bytes) ? bytes : null;

    /// <summary>Downloads text content with a typed result. Never throws.</summary>
    public static HttpResult<string> TryDownloadString(string url, int timeoutMilliseconds = 5000)
    {
        HttpResult<byte[]> raw = TryDownloadBytes(url, timeoutMilliseconds);
        if (!raw.TryGetValue(out byte[]? bytes))
            return HttpResult<string>.Err(raw.Error!);

        try
        {
            string text = System.Text.Encoding.UTF8.GetString(bytes);
            return HttpResult<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return HttpResult<string>.Err(HttpError.Business("decode-failed", ex.Message));
        }
    }

    /// <summary>Downloads binary content with a typed result. Never throws.</summary>
    public static HttpResult<byte[]> TryDownloadBytes(string url, int timeoutMilliseconds = 10000)
    {
        if (string.IsNullOrWhiteSpace(url))
            return HttpResult<byte[]>.Err(HttpError.Business("empty-url", "URL must not be empty."));

        try
        {
            var request = WebRequest.Create(url);
            request.Timeout = timeoutMilliseconds;
            request.Proxy = null;
            using WebResponse response = request.GetResponse();

            // Business error: HTTP 4xx / 5xx. WebRequest.GetResponse throws WebException on
            // non-2xx, so if we reach here the response is 2xx. But we still check the protocol
            // status when available to be defensive.
            if (response is HttpWebResponse http && (int)http.StatusCode >= 400)
            {
                return HttpResult<byte[]>.Err(HttpError.Business(
                    code: $"http-{(int)http.StatusCode}",
                    message: $"Server returned {(int)http.StatusCode} {http.StatusCode}"));
            }

            using Stream stream = response.GetResponseStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return HttpResult<byte[]>.Ok(memory.ToArray());
        }
        catch (WebException ex)
        {
            // Distinguish network (DNS / timeout / connection refused) from business (HTTP error response).
            HttpError error = ClassifyWebException(ex, url);
            Logger.Log($"CnCNetHttp.TryDownloadBytes network error ({url}): {ex.Message}");
            return HttpResult<byte[]>.Err(error);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetHttp.TryDownloadBytes failed ({url}): {ex.Message}");
            return HttpResult<byte[]>.Err(HttpError.Network(ex.Message));
        }
    }

    private static HttpError ClassifyWebException(WebException ex, string url)
    {
        // If the server responded with an error status code, it's a business error (retry won't help).
        if (ex.Response is HttpWebResponse http && (int)http.StatusCode >= 400)
        {
            return HttpError.Business(
                code: $"http-{(int)http.StatusCode}",
                message: $"Server returned {(int)http.StatusCode} {http.StatusCode} for {url}");
        }

        // Otherwise: DNS failure, timeout, connection refused, etc. — network error, retry may help.
        return ex.Status switch
        {
            WebExceptionStatus.Timeout => HttpError.Network($"Timeout reaching {url}"),
            WebExceptionStatus.NameResolutionFailure => HttpError.Network($"DNS resolution failed for {url}"),
            WebExceptionStatus.ConnectFailure => HttpError.Network($"Connection refused: {url}"),
            WebExceptionStatus.ReceiveFailure or WebExceptionStatus.SendFailure
                => HttpError.Network($"Connection interrupted: {url}"),
            WebExceptionStatus.ProxyNameResolutionFailure => HttpError.Network($"Proxy DNS failed for {url}"),
            _ => HttpError.Network($"{ex.Status}: {url}"),
        };
    }
}
