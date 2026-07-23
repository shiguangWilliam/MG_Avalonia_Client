using ClientAvalonia.CnCNet;
using ClientCore;
using Rampastring.Tools;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace ClientAvalonia.Domain.Multiplayer.CnCNet;

/// <summary>
/// CnCNet tunnel server (DXMainClient <c>DTAClient.Domain.Multiplayer.CnCNet.CnCNetTunnel</c>).
/// Control-plane port is <see cref="Port"/>; per-player NAT ports come from <see cref="GetPlayerPortInfo"/>.
/// </summary>
public class CnCNetTunnel
{
    private const int RequestTimeoutMilliseconds = 10_000;
    private const int PingTimeoutMilliseconds = 1000;

    /// <summary>Master-list / HTTP control port (not the per-player NAT port).</summary>
    public ushort Port { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool RequiresPassword { get; set; }

    public int Clients { get; set; }

    public int MaxClients { get; set; }

    public bool Official { get; set; }

    public bool Recommended { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int Version { get; set; }

    public double Distance { get; set; }

    public int PingInMs { get; set; } = -1;

    /// <summary>
    /// DX <c>CnCNetTunnel.GetPlayerPortInfo</c> — host calls this before broadcasting START CTCP.
    /// </summary>
    public IReadOnlyList<ushort> GetPlayerPortInfo(int playerCount)
    {
        if (playerCount <= 0)
            return [];

        try
        {
            string url = $"http://{Address}:{Port}/request?clients={playerCount}";
            Logger.Log($"CnCNetTunnel: contacting {url}");

            string? data = CnCNetHttp.DownloadString(url, RequestTimeoutMilliseconds);
            if (string.IsNullOrWhiteSpace(data))
                return [];

            Logger.Log($"CnCNetTunnel: raw ports from {Address}:{Port}: {data.Trim()}");

            data = data.Replace("[", string.Empty).Replace("]", string.Empty);
            var ports = new List<ushort>();
            foreach (string part in data.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                if (CnCNetPortValidator.TryParseTunnelPortToken(token, out ushort port, out string? note))
                {
                    ports.Add(port);
                    if (note != null)
                        Logger.Log($"CnCNetTunnel: {note} (token '{token}' → {port})");
                }
                else
                    Logger.Log($"CnCNetTunnel: ignoring invalid port token '{token}' from {Address}:{Port}");
            }

            Logger.Log($"CnCNetTunnel: received {ports.Count} ports from {Address}:{Port}: {string.Join(',', ports)}");
            return ports;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetTunnel.GetPlayerPortInfo failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>DX <c>CnCNetTunnel.Parse</c> (master-list line).</summary>
    public static CnCNetTunnel? Parse(string line)
    {
        try
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            string[] parts = line.Split(';');
            if (!CnCNetPortValidator.TryParseEndpoint(parts[0], out string address, out ushort port))
                return null;

            if (address.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
                return null;

            int status = int.Parse(parts[7]);
            return new CnCNetTunnel
            {
                Address = address,
                Port = port,
                Country = parts[1],
                CountryCode = parts[2],
                Name = parts[3],
                RequiresPassword = parts[4] != "0",
                Clients = int.Parse(parts[5]),
                MaxClients = int.Parse(parts[6]),
                Official = status == 2,
                Recommended = status == 1,
                Latitude = double.Parse(parts[8], culture),
                Longitude = double.Parse(parts[9], culture),
                Version = int.Parse(parts[10], culture),
                Distance = double.Parse(parts[11], culture),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or IndexOutOfRangeException)
        {
            Logger.Log($"CnCNetTunnel.Parse failed: {ex.Message}{Environment.NewLine}Parsed string: {line}");
            return null;
        }
    }

    /// <summary>DX <c>CnCNetTunnel.UpdateFrom</c> — preserves Address, Port, PingInMs.</summary>
    public void UpdateFrom(CnCNetTunnel updatedTunnel)
    {
        Country = updatedTunnel.Country;
        CountryCode = updatedTunnel.CountryCode;
        Name = updatedTunnel.Name;
        Clients = updatedTunnel.Clients;
        MaxClients = updatedTunnel.MaxClients;
        Official = updatedTunnel.Official;
        Recommended = updatedTunnel.Recommended;
        Version = updatedTunnel.Version;
        RequiresPassword = updatedTunnel.RequiresPassword;
        Latitude = updatedTunnel.Latitude;
        Longitude = updatedTunnel.Longitude;
        Distance = updatedTunnel.Distance;
    }

    public void UpdatePing()
    {
        // Hostnames are the common case on the master list; IPAddress.Parse throws on them.
        // Ping.Send accepts either form, so we only parse to an IPAddress when the address
        // is dotted-quad, and otherwise hand the literal hostname to Ping. Any exception from
        // the network stack (DNS failure, ICMP permission, etc.) is swallowed so a malformed
        // or unreachable tunnel never crashes the maintenance loop.
        try
        {
            using var ping = new Ping();
            PingReply reply = IPAddress.TryParse(Address, out IPAddress? ip)
                ? ping.Send(ip, PingTimeoutMilliseconds)
                : ping.Send(Address, PingTimeoutMilliseconds);

            if (reply.Status == IPStatus.Success)
                PingInMs = Convert.ToInt32(reply.RoundtripTime);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNetTunnel.UpdatePing failed for {Name} ({Address}:{Port}): {ex.Message}");
        }
    }
}
