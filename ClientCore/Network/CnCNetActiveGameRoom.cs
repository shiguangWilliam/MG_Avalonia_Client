namespace ClientCore.Network;

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
