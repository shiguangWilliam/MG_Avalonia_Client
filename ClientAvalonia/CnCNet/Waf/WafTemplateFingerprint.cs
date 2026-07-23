using System.Security.Cryptography;
using System.Text;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Stable fingerprint for hosting-bot template rooms (room|map|mode|tunnel).</summary>
public static class WafTemplateFingerprint
{
    public static string Compute(WafGameBroadcastFields game)
    {
        string raw = string.Join('|',
            Norm(game.RoomName),
            Norm(game.MapName),
            Norm(game.GameMode),
            Norm(game.TunnelEndpoint));
        if (string.IsNullOrWhiteSpace(raw.Replace("|", string.Empty)))
            return string.Empty;

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static string Norm(string? s)
        => WafTextNormalizer.CompactForMatch(WafTextNormalizer.Normalize(s ?? string.Empty));
}
