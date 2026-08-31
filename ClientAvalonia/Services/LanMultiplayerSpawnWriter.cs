using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState;
using ClientAvalonia.Rendering;
using ClientAvalonia.Session;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>LAN spawn additions (DX <c>LANGameLobby.WriteSpawnIniAdditions</c>): Port / GameID / Host, no Tunnel.</summary>
public static class LanMultiplayerSpawnWriter
{
    public static void Write(
        MapEntry map,
        GameModeEntry gameMode,
        LanStartGameInfo startInfo,
        IReadOnlyList<IPlayerSlot> slots,
        UiNodeViewModel? lobbyRoot = null,
        int sideCount = 0,
        int randomSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (randomSeed == 0)
            randomSeed = Random.Shared.Next();

        // Reuse skirmish layout (humans + OtherN), then patch LAN Settings.
        SkirmishSpawnWriter.Write(map, gameMode, slots, sideCount, lobbyRoot, randomSeed);

        FileInfo spawnFile = SafePath.GetFile(AppState.Environment.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        var ini = new IniFile(spawnFile.FullName);
        ini.SetIntValue("Settings", "Port", startInfo.InGamePort);
        ini.SetIntValue("Settings", "GameID", startInfo.UniqueGameId);
        ini.SetBooleanValue("Settings", "Host", startInfo.IsHost);

        // Ensure human OtherN sections carry LAN in-game port (DX PlayerInfo.Port = 1234).
        int otherIndex = 0;
        foreach (IPlayerSlot human in slots.Where(s => s.IsOccupied && !s.IsAi))
        {
            if (human.IsHumanLocal || human.Name.Equals(AppState.Environment.PlayerName, StringComparison.OrdinalIgnoreCase))
                continue;

            string section = "Other" + otherIndex;
            if (ini.SectionExists(section))
                ini.SetIntValue(section, "Port", startInfo.InGamePort);
            otherIndex++;
        }

        ini.WriteIniFile();
        Logger.Log($"LanMultiplayerSpawnWriter: port={startInfo.InGamePort}, gameId={startInfo.UniqueGameId}, host={startInfo.IsHost}");
    }
}

/// <summary>Minimal LAN launch metadata (no tunnel).</summary>
public sealed class LanStartGameInfo
{
    public required int UniqueGameId { get; init; }
    public required bool IsHost { get; init; }
    public int InGamePort { get; init; } = ClientCore.ProgramConstants.LAN_INGAME_PORT;
}
