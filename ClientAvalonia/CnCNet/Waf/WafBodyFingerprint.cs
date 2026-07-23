using System;
using System.Security.Cryptography;
using System.Text;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Fingerprint for identical/near-identical chat bodies after WAF normalization.</summary>
public static class WafBodyFingerprint
{
    public const string KeyPrefix = "body=";

    /// <summary>Returns <c>body=&lt;sha1&gt;</c> or empty when the text is too short to be useful.</summary>
    public static string KeyFromText(string? text)
    {
        string compact = WafTextNormalizer.CompactForMatch(WafTextNormalizer.Normalize(text ?? string.Empty));
        if (compact.Length < 2)
            return string.Empty;

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(compact));
        return KeyPrefix + Convert.ToHexString(hash);
    }

    public static string KeyFromEvent(WafIngressEvent e)
    {
        string primary = !string.IsNullOrWhiteSpace(e.DisplayText) ? e.DisplayText : e.RawBody;
        string key = KeyFromText(primary);
        if (!string.IsNullOrEmpty(key))
            return key;

        if (e.Game != null)
        {
            return KeyFromText(string.Join(' ',
                e.Game.RoomName,
                e.Game.MapName,
                e.Game.GameMode));
        }

        return string.Empty;
    }
}
