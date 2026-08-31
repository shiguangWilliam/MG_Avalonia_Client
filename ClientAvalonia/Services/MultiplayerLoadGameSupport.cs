using System.Security.Cryptography;
using System.Text;
using ClientAvalonia.GlobalState;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>
/// DX parity for multiplayer saved-game gate + CnCNet load-room password
/// (<c>SHA1(GameID)[..10]</c> from <c>Saved Games/spawnSG.ini</c>).
/// </summary>
public static class MultiplayerLoadGameSupport
{
    public static string SpawnSgPath
        => SafePath.CombineFilePath(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);

    public static bool SpawnSgExists()
        => SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI).Exists;

    /// <summary>DX <c>GameCreationWindow.AllowLoadingGame</c>.</summary>
    public static bool AllowHostingLoadedGame(string? playerName = null)
    {
        FileInfo file = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);
        if (!file.Exists)
            return false;

        var ini = new IniFile(file.FullName);
        string name = playerName ?? AppState.Environment.PlayerName;
        if (!ini.GetStringValue("Settings", "Name", string.Empty)
                .Equals(name, StringComparison.OrdinalIgnoreCase))
            return false;

        return ini.GetBooleanValue("Settings", "Host", false);
    }

    public static int ReadGameId()
    {
        FileInfo file = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);
        if (!file.Exists)
            return -1;
        return new IniFile(file.FullName).GetIntValue("Settings", "GameID", -1);
    }

    public static string ReadGameIdString()
    {
        FileInfo file = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);
        if (!file.Exists)
            return string.Empty;
        return new IniFile(file.FullName).GetStringValue("Settings", "GameID", string.Empty);
    }

    public static int ReadPlayerCount()
    {
        FileInfo file = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);
        if (!file.Exists)
            return 0;
        return new IniFile(file.FullName).GetIntValue("Settings", "PlayerCount", 0);
    }

    /// <summary>DX join/create password for loaded CnCNet games.</summary>
    public static string ComputeLoadedGamePassword(string? gameId = null)
    {
        string id = gameId ?? ReadGameIdString();
        return Sha1Prefix10(id);
    }

    public static string Sha1Prefix10(string input)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }

    public static IReadOnlyList<string> ReadSavedPlayerNames()
    {
        FileInfo file = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);
        if (!file.Exists)
            return [];

        var ini = new IniFile(file.FullName);
        var names = new List<string>();
        string local = ini.GetStringValue("Settings", "Name", string.Empty);
        if (!string.IsNullOrWhiteSpace(local))
            names.Add(local);

        int playerCount = ini.GetIntValue("Settings", "PlayerCount", 1);
        for (int i = 0; i < Math.Max(0, playerCount - 1); i++)
        {
            string name = ini.GetStringValue("Other" + i, "Name", string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }
}
