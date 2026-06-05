namespace ClientAvalonia.CnCNet;

/// <summary>Active CnCNet game room the client is hosting or joining (IRC game channel).</summary>
public sealed class CnCNetActiveGameRoom
{
    public required string RoomName { get; init; }

    public required string ChannelName { get; init; }

    public required string Password { get; init; }

    public required CnCNetTunnelEntry Tunnel { get; init; }

    public string HostName { get; init; } = string.Empty;

    public bool IsHost { get; init; }

    public int MaxPlayers { get; init; }

    public int SkillLevel { get; init; }

    public bool CustomPassword { get; init; }
}

/// <summary>Player slot in an active CnCNet game room (XNA PlayerInfo subset).</summary>
public sealed class CnCNetGameRoomPlayer
{
    public string Name { get; set; } = string.Empty;

    public bool IsHost { get; set; }

    public bool IsAi { get; set; }

    public int AiLevel { get; set; }

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
