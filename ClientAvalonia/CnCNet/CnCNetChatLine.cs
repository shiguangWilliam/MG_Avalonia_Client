namespace ClientAvalonia.CnCNet;

public sealed class CnCNetChatLine
{
    public required string DisplayText { get; init; }

    public string Sender { get; init; } = string.Empty;

    public bool IsSystem { get; init; }
}
