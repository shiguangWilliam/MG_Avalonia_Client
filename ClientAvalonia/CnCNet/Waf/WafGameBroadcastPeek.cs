using System;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Best-effort GAME CTCP field peek for WAF when the stock parser rejects the payload.</summary>
public static class WafGameBroadcastPeek
{
    public static bool TryPeek(string ctcp, out WafGameBroadcastFields fields)
    {
        fields = new WafGameBroadcastFields();
        if (string.IsNullOrEmpty(ctcp) || !ctcp.StartsWith("GAME ", StringComparison.Ordinal))
            return false;

        string[] parts = ctcp.Length > 5 ? ctcp[5..].Split(';') : [];
        if (parts.Length == 0)
            return false;

        string tunnelHost = string.Empty;
        ushort tunnelPort = 0;
        if (parts.Length > 9
            && global::ClientAvalonia.CnCNet.CnCNetPortValidator.TryParseEndpoint(parts[9], out string host, out ushort port))
        {
            tunnelHost = host;
            tunnelPort = port;
        }

        string[] players = parts.Length > 6
            ? parts[6].Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [];

        fields = new WafGameBroadcastFields
        {
            Revision = parts[0],
            FieldCount = parts.Length,
            Flags = parts.Length > 5 ? parts[5] : string.Empty,
            RoomName = parts.Length > 4 ? parts[4] : string.Empty,
            MapName = parts.Length > 7 ? parts[7] : string.Empty,
            GameMode = parts.Length > 8 ? parts[8] : string.Empty,
            TunnelHost = tunnelHost,
            TunnelPort = tunnelPort,
            ChannelName = parts.Length > 3 ? parts[3] : string.Empty,
            Players = players,
        };
        return true;
    }
}
