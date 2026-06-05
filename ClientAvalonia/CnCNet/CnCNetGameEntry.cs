namespace ClientAvalonia.CnCNet;

/// <summary>One CnCNet game / IRC channel group (XNA <see cref="DTAClient.Domain.Multiplayer.CnCNet.CnCNetGame"/> subset).</summary>
public sealed class CnCNetGameEntry
{
    public required string InternalName { get; init; }

    public required string UiName { get; init; }

    public required string ChatChannel { get; init; }

    public string? GameBroadcastChannel { get; init; }

    public bool Supported { get; init; } = true;

    public bool AlwaysEnabled { get; init; }

    public string IconFileName { get; init; } = "unknownicon.png";

    public bool HasGameBroadcast => !string.IsNullOrWhiteSpace(GameBroadcastChannel);

    public CnCNetGameChannels ToChannels() => new()
    {
        InternalName = InternalName,
        UiName = UiName,
        ChatChannel = ChatChannel,
        GameBroadcastChannel = GameBroadcastChannel ?? string.Empty,
    };
}
