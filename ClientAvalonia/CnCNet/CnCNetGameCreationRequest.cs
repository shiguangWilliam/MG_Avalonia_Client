namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>Parameters from the create-game dialog (XNA GameCreationEventArgs).</summary>
public sealed class CnCNetGameCreationRequest
{
    public required string RoomName { get; init; }

    public required int MaxPlayers { get; init; }

    /// <summary>Host-side: user chose to protect the room (create dialog checkbox).</summary>
    public bool RequiresPassword { get; init; }

    public string Password { get; init; } = string.Empty;

    public required CnCNetTunnel Tunnel { get; init; }

    public int SkillLevel { get; init; }
}
