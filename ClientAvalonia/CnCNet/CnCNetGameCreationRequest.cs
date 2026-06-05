namespace ClientAvalonia.CnCNet;

/// <summary>Parameters from the create-game dialog (XNA GameCreationEventArgs).</summary>
public sealed class CnCNetGameCreationRequest
{
    public required string RoomName { get; init; }

    public required int MaxPlayers { get; init; }

    public string Password { get; init; } = string.Empty;

    public required CnCNetTunnelEntry Tunnel { get; init; }

    public int SkillLevel { get; init; }
}
