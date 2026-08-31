using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientAvalonia.Services;

namespace ClientAvalonia.Services;

/// <summary>Pre-launch checks aligned with DXMainClient SkirmishLobby.CheckGameValidity.</summary>
public static class SkirmishLaunchValidator
{
    /// <summary>
    /// Session-aware：吃 <see cref="IReadOnlyList{IPlayerSlot}"/> + sideCount。
    /// </summary>
    public static string? Validate(
        MapEntry map,
        GameModeEntry gameMode,
        IReadOnlyList<IPlayerSlot> slots,
        int sideCount)
    {
        // DX: Players.Count(p => p.SideId < ddPlayerSides[0].Items.Count - 1) — spectators
        // (the last side entry) are NOT players. Mirror that explicitly instead of relying
        // on the caller passing a count whose last item happens to be Spectator.
        int spectatorSideIndex = LobbySideCatalog.SpectatorSideIndex;
        int totalPlayers = slots.Count(s => s.IsOccupied && s.SideIndex != spectatorSideIndex);

        if (gameMode.MultiplayerOnly)
        {
            return string.Format(
                "{0} can only be played on CnCNet and LAN.",
                gameMode.DisplayName);
        }

        if (map.MultiplayerOnly)
            return "The selected map can only be played on CnCNet and LAN.";

        if (map.MinPlayers > 0 && totalPlayers < map.MinPlayers)
        {
            return string.Format(
                "The selected map cannot be played with less than {0} players.",
                map.MinPlayers);
        }

        if (map.EnforceMaxPlayers && map.MaxPlayers > 0 && totalPlayers > map.MaxPlayers)
        {
            return string.Format(
                "The selected map cannot be played with more than {0} players.",
                map.MaxPlayers);
        }

        if (map.EnforceMaxPlayers)
        {
            var occupiedStarts = slots
                .Where(s => s.IsOccupied && s.StartIndex > 0)
                .Select(s => s.StartIndex)
                .ToList();

            if (occupiedStarts.GroupBy(x => x).Any(g => g.Count() > 1))
            {
                return "Multiple players cannot share the same starting location on the selected map.";
            }
        }

        return null;
    }
}
