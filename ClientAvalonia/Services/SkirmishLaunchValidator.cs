using ClientAvalonia.Domain;
using ClientAvalonia.Session;

namespace ClientAvalonia.Services;

/// <summary>Pre-launch checks aligned with DXMainClient SkirmishLobby.CheckGameValidity.</summary>
public static class SkirmishLaunchValidator
{
    /// <summary>
    /// Phase 2 P2-4：Session-aware 重载——吃 <see cref="IReadOnlyList{IPlayerSlot}"/>，不再依赖
    /// <see cref="LobbyPlayerState"/>。供未来 <see cref="IGameSession.PlayerSlots"/> 直接传入。
    /// </summary>
    public static string? Validate(
        MapEntry map,
        GameModeEntry gameMode,
        IReadOnlyList<IPlayerSlot> slots,
        int sideCount)
    {
        int randomSideIndex = Math.Max(0, sideCount - 1);
        int totalPlayers = slots.Count(s => s.IsOccupied && s.SideIndex < randomSideIndex);

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

    /// <summary>Legacy overload (delegates to <see cref="Validate(MapEntry, GameModeEntry, IReadOnlyList{IPlayerSlot}, int)"/>).</summary>
    public static string? Validate(MapEntry map, GameModeEntry gameMode, LobbyPlayerState players)
    {
        return Validate(map, gameMode, players.Slots, players.SideNames.Count);
    }
}
