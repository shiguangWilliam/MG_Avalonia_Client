using System;
using System.Linq;

namespace ClientCore.Network;

/// <summary>IRC server entry from NetworkDefinitions.ini [IRCServers] (host|name|ports).</summary>
public readonly struct CnCNetIrcServer
{
    public CnCNetIrcServer(string host, string name, int[] ports)
    {
        Host = host;
        Name = name;
        Ports = ports;
    }

    public string Host { get; }

    public string Name { get; }

    public int[] Ports { get; }

    public static CnCNetIrcServer Deserialize(string serialized)
    {
        string[] parts = serialized.Split('|');
        string host = parts[0];
        string name = parts.Length > 1 ? parts[1] : host;
        string[] portStrings = (parts.Length > 2 ? parts[2] : "6667").Split(',');
        int[] ports = portStrings.Select(int.Parse).ToArray();
        return new CnCNetIrcServer(host, name, ports);
    }
}
