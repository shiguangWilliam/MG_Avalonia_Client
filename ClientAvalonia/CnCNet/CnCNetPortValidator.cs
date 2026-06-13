namespace ClientAvalonia.CnCNet;

/// <summary>Validates NAT tunnel player ports (XNA CnCNetTunnel.GetPlayerPortInfo).</summary>
public static class CnCNetPortValidator
{
    public const int MinPlayerPort = 1;

    public const int MaxPlayerPort = 65535;

    public static bool IsValidPlayerPort(int port)
        => port >= MinPlayerPort && port <= MaxPlayerPort;

    public static bool TryValidatePlayerPorts(
        IReadOnlyList<int> ports,
        int expectedCount,
        out string? error)
    {
        error = null;

        if (ports.Count < expectedCount)
        {
            error = "Could not contact the CnCNet tunnel server. Try another tunnel.";
            return false;
        }

        for (int i = 0; i < expectedCount; i++)
        {
            if (IsValidPlayerPort(ports[i]))
                continue;

            error = $"Tunnel returned invalid player port {ports[i]} (index {i}). Try another tunnel server.";
            return false;
        }

        return true;
    }
}
