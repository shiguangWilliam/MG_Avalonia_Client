using ClientCore;
using System;
using System.Collections.Generic;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

public static class CnCNetIrcServerList
{
    public static IReadOnlyList<CnCNetIrcServer> Load()
    {
        var servers = new List<CnCNetIrcServer>();
        foreach (string entry in AppState.Configuration.Legacy.IRCServers)
        {
            try
            {
                servers.Add(CnCNetIrcServer.Deserialize(entry));
            }
            catch (Exception ex)
            {
                Logger.Log($"CnCNetIrcServerList: skipping invalid IRC server entry '{entry}': {ex.Message}");
            }
        }

        if (servers.Count == 0)
        {
            Logger.Log("CnCNetIrcServerList: no IRC servers in config, using GameSurge fallback.");
            servers.Add(CnCNetIrcServer.Deserialize("irc.gamesurge.net|GameSurge|6667,6660,6666,6668,6669"));
        }

        return servers;
    }
}
