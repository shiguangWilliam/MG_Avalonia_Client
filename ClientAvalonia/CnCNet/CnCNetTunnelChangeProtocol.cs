namespace ClientAvalonia.CnCNet;

/// <summary>Formats / parses the CnCNet <c>CHTNL</c> room CTCP payload.</summary>
public static class CnCNetTunnelChangeProtocol
{
    public static string FormatChtnl(string address, ushort port)
        => $"CHTNL {address}:{port}";

    public static bool TryParse(
        string payload,
        out string address,
        out ushort port)
        => CnCNetPortValidator.TryParseEndpoint(payload, out address, out port);
}
