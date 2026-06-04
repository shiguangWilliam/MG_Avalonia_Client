namespace ClientCore.Network;

/// <summary>Player slot in an active CnCNet game room (XNA PlayerInfo subset).</summary>
public sealed class CnCNetGameRoomPlayer
{
    public string Name { get; set; } = string.Empty;

    public bool IsHost { get; set; }

    public bool Ready { get; set; }

    public int Port { get; set; }

    public int SideId { get; set; }

    public int ColorId { get; set; }

    public int TeamId { get; set; }

    public int StartingLocation { get; set; }
}

/// <summary>Launch parameters parsed from START CTCP (XNA NonHostLaunchGame).</summary>
public sealed class CnCNetStartGameInfo
{
    public required int UniqueGameId { get; init; }

    public required CnCNetTunnelEntry Tunnel { get; init; }

    public required int LocalPlayerPort { get; init; }

    public required bool IsHost { get; init; }
}
