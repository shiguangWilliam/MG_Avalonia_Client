using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Enums;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Alliance sections in spawn.ini (XNA AllianceHolder.WriteInfoToSpawnIni).</summary>
public static class SpawnAllianceWriter
{
    public static void WriteAlliances(
        IReadOnlyList<LobbyPlayerSlot> humans,
        IReadOnlyList<LobbyPlayerSlot> ais,
        IReadOnlyList<int> multiCmbIndexes,
        IReadOnlyList<int> startingWaypoints,
        IniFile spawnIni)
    {
        var team1 = new List<int>();
        var team2 = new List<int>();
        var team3 = new List<int>();
        var team4 = new List<int>();

        for (int humanIndex = 0; humanIndex < humans.Count; humanIndex++)
        {
            int teamId = humans[humanIndex].TeamIndex;
            if (teamId <= 0 && humanIndex < startingWaypoints.Count && startingWaypoints[humanIndex] >= 0)
                continue;

            if (teamId <= 0)
                continue;

            int multiId = IndexOf(multiCmbIndexes, humanIndex) + 1;
            if (multiId <= 0)
                continue;

            AddToTeam(teamId, multiId, team1, team2, team3, team4);
        }

        int aiMultiId = multiCmbIndexes.Count + 1;
        for (int aiIndex = 0; aiIndex < ais.Count; aiIndex++)
        {
            int teamId = ais[aiIndex].TeamIndex;
            if (teamId <= 0)
            {
                aiMultiId++;
                continue;
            }

            AddToTeam(teamId, aiMultiId, team1, team2, team3, team4);
            aiMultiId++;
        }

        WriteTeamAlliances(team1, spawnIni);
        WriteTeamAlliances(team2, spawnIni);
        WriteTeamAlliances(team3, spawnIni);
        WriteTeamAlliances(team4, spawnIni);
    }

    private static void AddToTeam(int teamId, int multiId, List<int> t1, List<int> t2, List<int> t3, List<int> t4)
    {
        switch (teamId)
        {
            case 1: t1.Add(multiId); break;
            case 2: t2.Add(multiId); break;
            case 3: t3.Add(multiId); break;
            case 4: t4.Add(multiId); break;
        }
    }

    private static void WriteTeamAlliances(List<int> teamMemberIds, IniFile spawnIni)
    {
        foreach (int houseId in teamMemberIds)
        {
            bool selfFound = false;
            for (int allyIndex = 0; allyIndex < teamMemberIds.Count; allyIndex++)
            {
                int allyHouseId = teamMemberIds[allyIndex];
                if (allyHouseId == houseId)
                    selfFound = true;
                else
                {
                    spawnIni.SetIntValue(
                        "Multi" + houseId + "_Alliances",
                        "HouseAlly" + GetHouseAllyIndexString(allyIndex, selfFound),
                        ClientConfiguration.Instance.ClientGameType == ClientType.RA
                            ? allyHouseId + 11
                            : allyHouseId - 1);
                }
            }
        }
    }

    private static string GetHouseAllyIndexString(int allyId, bool selfFound)
    {
        if (selfFound)
            allyId--;

        return allyId switch
        {
            0 => "One",
            1 => "Two",
            2 => "Three",
            3 => "Four",
            4 => "Five",
            5 => "Six",
            6 => "Seven",
            _ => "None" + allyId,
        };
    }

    private static int IndexOf(IReadOnlyList<int> values, int target)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == target)
                return i;
        }

        return -1;
    }
}
