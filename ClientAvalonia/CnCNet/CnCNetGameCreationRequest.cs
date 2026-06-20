namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>Parameters from the create-game dialog (XNA GameCreationEventArgs).</summary>
public sealed class CnCNetGameCreationRequest
{
    public required string RoomName { get; init; }

    public required int MaxPlayers { get; init; }

    public string Password { get; init; } = string.Empty;

    /// <summary>DXMain: empty password â†?channel SHA1 key; non-empty â†?custom IRC key.</summary>
    public bool Passworded => !string.IsNullOrWhiteSpace(Password);

    public required CnCNetTunnel Tunnel { get; init; }

    public int SkillLevel { get; init; }
}
