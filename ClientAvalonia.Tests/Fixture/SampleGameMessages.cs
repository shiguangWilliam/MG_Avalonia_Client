using System.Collections.Generic;
using System.Globalization;
using ClientAvalonia.Domain.Multiplayer.CnCNet;

namespace ClientAvalonia.Tests.Fixture;

/// <summary>
/// Builds well-formed GAME CTCP messages matching the DX CnCNetLobby 13-field layout
/// (and the R10 11-field legacy layout). Field order is locked by <see cref="DxAliases"/>.
///
/// Tests should call <see cref="BuildGameMessage"/> / <see cref="BuildLegacyGameMessage"/>
/// rather than hand-typing CTCP — that keeps the field order under a single choke point.
/// </summary>
internal static class SampleGameMessages
{
    /// <summary>Builds a 13-field R13 GAME broadcast. All flags default to "00000" unless overridden.</summary>
    public static string BuildGameMessage(
        string revision = DxAliases.CurrentProtocolRevision,
        string gameVersion = "1.0",
        int maxPlayers = 4,
        string channel = DxAliases.SampleChannel,
        string roomName = DxAliases.SampleRoomName,
        string flags = "00000",
        IEnumerable<string>? players = null,
        string map = "TestMap",
        string gameMode = "Standard",
        string tunnelHost = "tunnel.example.com",
        ushort tunnelPort = 50000,
        string loadedGameId = "",
        int skillLevel = 0,
        string mapHash = "ABCDEF")
    {
        string playerList = players == null ? "Host" : string.Join(",", players);
        string tunnelEndpoint = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", tunnelHost, tunnelPort);

        return string.Join(';',
            revision,             // 0  revision
            gameVersion,          // 1  gameVersion
            maxPlayers.ToString(CultureInfo.InvariantCulture), // 2 maxPlayers
            channel,              // 3  channel
            roomName,             // 4  roomName
            flags,                // 5  flags
            playerList,           // 6  players
            map,                  // 7  map
            gameMode,             // 8  gameMode
            tunnelEndpoint,       // 9  tunnel host:port
            loadedGameId,         // 10 loadedGameId
            skillLevel.ToString(CultureInfo.InvariantCulture), // 11 skillLevel
            mapHash);             // 12 mapHash
    }

    /// <summary>Returns the raw CTCP wire form ("GAME " + semicolon-joined fields).</summary>
    public static string BuildGameCtcp(string fields) => "GAME " + fields;

    /// <summary>Builds an 11-field R10 legacy GAME broadcast (no skillLevel/mapHash).</summary>
    public static string BuildLegacyGameMessage(
        string revision = DxAliases.LegacyProtocolRevision,
        string gameVersion = "1.0",
        int maxPlayers = 4,
        string channel = DxAliases.SampleChannel,
        string roomName = DxAliases.SampleRoomName,
        string flags = "00000",
        IEnumerable<string>? players = null,
        string map = "TestMap",
        string gameMode = "Standard",
        string tunnelHost = "tunnel.example.com",
        ushort tunnelPort = 50000,
        string loadedGameId = "")
    {
        string playerList = players == null ? "Host" : string.Join(",", players);
        string tunnelEndpoint = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", tunnelHost, tunnelPort);

        return string.Join(';',
            revision,             // 0  revision
            gameVersion,          // 1  gameVersion
            maxPlayers.ToString(CultureInfo.InvariantCulture), // 2 maxPlayers
            channel,              // 3  channel
            roomName,             // 4  roomName
            flags,                // 5  flags
            playerList,           // 6  players
            map,                  // 7  map
            gameMode,             // 8  gameMode
            tunnelEndpoint,       // 9  tunnel host:port
            loadedGameId);        // 10 loadedGameId
    }

    /// <summary>Single-tunnel list matching the host:port used by the sample messages.</summary>
    public static List<CnCNetTunnel> SampleTunnels(string host = "tunnel.example.com", ushort port = 50000)
        => new() { SampleTunnel(host, port) };

    /// <summary>Builds one CnCNetTunnel with the right Version (2) and a matching host:port.</summary>
    public static CnCNetTunnel SampleTunnel(string host, ushort port)
        => new()
        {
            Address = host,
            Port = port,
            Country = "Test",
            CountryCode = "TT",
            Name = "Sample Tunnel",
            RequiresPassword = false,
            Clients = 0,
            MaxClients = 100,
            Official = true,
            Recommended = true,
            Latitude = 0.0,
            Longitude = 0.0,
            Version = 2,
            Distance = 0.0,
        };

    /// <summary>Master-list text body parsable by CnCNetTunnelListLoader.Parse (v2 tunnels only).</summary>
    public static string BuildTunnelListBody(params (string host, ushort port)[] tunnels)
    {
        // Master list: first line is the count, subsequent lines are per-tunnel records.
        var lines = new List<string> { tunnels.Length.ToString(CultureInfo.InvariantCulture) };
        int index = 0;
        foreach ((string host, ushort port) in tunnels)
        {
            // address:port;country;countryCode;name;requiresPassword(0/1);clients;maxClients;status(0/1/2);lat;lng;version;distance
            lines.Add(string.Join(';',
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}", host, port),
                "TestCountry", "TT", $"Tunnel{index++}",
                "0", // RequiresPassword = false
                "0", "100", // Clients, MaxClients
                "2", // status=2 (Official)
                "0.0", "0.0", // lat, lng
                "2", // Version (SupportedTunnelVersion)
                "0.0")); // distance
        }
        return string.Join("\r\n", lines);
    }
}
