namespace ClientAvalonia.CnCNet;

/// <summary>Pure validation for host-side room settings edits (GSETTINGS path).</summary>
public static class GameLobbySettingsRules
{
    /// <summary>
    /// Returns <c>false</c> when reducing max players below the current occupied count (DX guard).
    /// </summary>
    public static bool CanSetMaxPlayers(int requestedMaxPlayers, int occupiedPlayerCount, out string? rejectNotice)
    {
        if (requestedMaxPlayers < occupiedPlayerCount)
        {
            rejectNotice =
                $"Cannot reduce maximum players to {requestedMaxPlayers} with {occupiedPlayerCount} players currently in game.";
            return false;
        }

        rejectNotice = null;
        return true;
    }
}
