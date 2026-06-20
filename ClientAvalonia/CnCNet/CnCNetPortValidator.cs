using System.Globalization;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Port parsing/validation aligned with DXMainClient
/// (<c>CnCNetTunnel.Parse</c>, <c>GetPlayerPortInfo</c>, <c>NonHostLaunchGame</c>)
/// but with explicit 1–65535 range checks (DX uses bare <c>int.Parse</c>/<c>Convert.ToInt32</c>).
/// </summary>
public static class CnCNetPortValidator
{
    /// <summary>Valid TCP/UDP port range; stored as <see cref="ushort"/> (not byte — ports need 16 bits).</summary>
    public const ushort MinPort = 1;

    public const ushort MaxPort = 65535;

    public const ushort UnsetPort = 0;

    /// <summary>DX <c>int.Parse</c>/<c>Convert.ToInt32</c> + Avalonia range guard.</summary>
    public static bool TryParse(string text, out ushort port)
        => TryParseTunnelPortToken(text, out port, out _);

    /// <summary>
    /// Parses a tunnel <c>/request</c> port token. Some servers return ports &gt; 32767 as signed
    /// int16 text (e.g. NAT port 39520 appears as <c>-26016</c>); DX passes that through verbatim.
    /// </summary>
    public static bool TryParseTunnelPortToken(string text, out ushort port, out string? recoveryNote)
    {
        port = UnsetPort;
        recoveryNote = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
            return false;

        if (raw >= MinPort && raw <= MaxPort)
        {
            port = (ushort)raw;
            return true;
        }

        // Signed int16 tunnel response: -26016 → ushort 39520
        if (raw is >= short.MinValue and <= short.MaxValue)
        {
            ushort recovered = unchecked((ushort)(short)raw);
            if (recovered >= MinPort)
            {
                port = recovered;
                if (raw < MinPort)
                    recoveryNote = $"recovered unsigned port {recovered} from signed token {raw}";
                return true;
            }
        }

        return false;
    }

    /// <summary>Parse <c>host:port</c> (master-list endpoint, GAME tunnel field, CHTNL, START).</summary>
    public static bool TryParseEndpoint(string endpoint, out string host, out ushort port)
    {
        host = string.Empty;
        port = UnsetPort;

        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        int colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || colon >= endpoint.Length - 1)
            return false;

        host = endpoint[..colon].Trim();
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return TryParse(endpoint[(colon + 1)..], out port);
    }

    public static bool IsValid(ushort port) => port >= MinPort && port <= MaxPort;

    public static bool TryValidatePlayerPorts(
        IReadOnlyList<ushort> ports,
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
            if (IsValid(ports[i]))
                continue;

            error = $"Tunnel returned invalid player port {ports[i]} (index {i}). Try another tunnel server.";
            return false;
        }

        return true;
    }
}
