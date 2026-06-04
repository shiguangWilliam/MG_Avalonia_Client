using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;
using ClientCore;
using ClientCore.Network;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Writes spawn.ini for CnCNet multiplayer (XNA CnCNetGameLobby.WriteSpawnIniAdditions).</summary>
public static class CnCNetMultiplayerSpawnWriter
{
    public static void Write(
        MapEntry map,
        GameModeEntry gameMode,
        CnCNetStartGameInfo startInfo,
        LobbyPlayerState? players = null,
        UiNodeViewModel? lobbyRoot = null,
        int randomSeed = 0)
    {
        SkirmishSpawnWriter.Write(map, gameMode, players, lobbyRoot, randomSeed);

        FileInfo spawnFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        if (!spawnFile.Exists)
            return;

        var spawnIni = new IniFile(spawnFile.FullName);
        spawnIni.SetStringValue("Tunnel", "Ip", startInfo.Tunnel.Address);
        spawnIni.SetIntValue("Tunnel", "Port", startInfo.Tunnel.Port);
        spawnIni.SetIntValue("Settings", "GameID", startInfo.UniqueGameId);
        spawnIni.SetBooleanValue("Settings", "Host", startInfo.IsHost);

        if (startInfo.LocalPlayerPort > 0)
            spawnIni.SetIntValue("Settings", "Port", startInfo.LocalPlayerPort);

        spawnIni.WriteIniFile();
        Logger.Log($"CnCNetMultiplayerSpawnWriter: tunnel {startInfo.Tunnel.Address}:{startInfo.Tunnel.Port}, gameId={startInfo.UniqueGameId}, port={startInfo.LocalPlayerPort}");
    }
}
