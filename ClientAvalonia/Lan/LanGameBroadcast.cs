using System.Globalization;
using ClientCore;

namespace ClientAvalonia.Lan;

/// <summary>Parsed LAN UDP <c>GAME</c> advertisement (DX <c>HostedLANGame</c> 10-field payload).</summary>
public sealed class LanHostedGame
{
    public string ProtocolRevision { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public string LocalGame { get; init; } = string.Empty;
    public string MapName { get; init; } = string.Empty;
    public string GameMode { get; init; } = string.Empty;
    public string LoadedGameId { get; init; } = "0";
    public IReadOnlyList<string> Players { get; init; } = [];
    public bool Locked { get; init; }
    public bool IsLoadedGame { get; init; }
    public string MapSha1 { get; init; } = string.Empty;
    public System.Net.IPEndPoint? EndPoint { get; set; }
    public DateTime LastRefreshUtc { get; set; } = DateTime.UtcNow;

    public string HostName => Players.Count > 0 ? Players[0] : string.Empty;

    public string DisplayName
        => string.IsNullOrEmpty(HostName)
            ? "LAN Game"
            : EndPoint != null
                ? $"{HostName}'s Game [{EndPoint.Address}]"
                : $"{HostName}'s Game";
}

/// <summary>Encode / decode LAN UDP GAME payloads.</summary>
public static class LanGameBroadcastCodec
{
    /// <summary>
    /// Builds DX GAME body (without leading <c>GAME </c>):
    /// <c>RL8\x01ver\x01localGame\x01map\x01mode\x01loadedId\x01p1,p2\x01locked\x01isLoaded\x01mapSha1</c>
    /// </summary>
    public static string FormatPayload(
        string gameVersion,
        string localGame,
        string mapName,
        string gameMode,
        IReadOnlyList<string> players,
        bool locked,
        bool isLoadedGame = false,
        string loadedGameId = "0",
        string mapSha1 = "")
    {
        string playerCsv = players.Count == 0
            ? AppStateSafePlayerName()
            : string.Join(",", players.Where(p => !string.IsNullOrWhiteSpace(p)));

        return string.Join(
            LanProtocol.DataSep.ToString(),
            LanProtocol.Revision,
            gameVersion ?? string.Empty,
            localGame ?? string.Empty,
            mapName ?? string.Empty,
            gameMode ?? string.Empty,
            string.IsNullOrWhiteSpace(loadedGameId) ? "0" : loadedGameId,
            playerCsv,
            locked ? "1" : "0",
            isLoadedGame ? "1" : "0",
            mapSha1 ?? string.Empty);
    }

    public static bool TryParse(string payload, out LanHostedGame game)
    {
        game = new LanHostedGame();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        string[] parts = payload.Split(LanProtocol.DataSep);
        if (parts.Length != 10)
            return false;

        if (!parts[0].Equals(LanProtocol.Revision, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] players = parts[6]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (players.Length == 0)
            return false;

        game = new LanHostedGame
        {
            ProtocolRevision = parts[0],
            GameVersion = parts[1],
            LocalGame = parts[2],
            MapName = parts[3],
            GameMode = parts[4],
            LoadedGameId = parts[5],
            Players = players,
            Locked = ParseInt(parts[7]) > 0,
            IsLoadedGame = ParseInt(parts[8]) > 0,
            MapSha1 = parts[9],
            LastRefreshUtc = DateTime.UtcNow,
        };
        return true;
    }

    private static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    private static string AppStateSafePlayerName()
    {
        try
        {
            return GlobalState.AppState.Environment.PlayerName;
        }
        catch
        {
            return ProgramConstants.PLAYERNAME;
        }
    }
}
