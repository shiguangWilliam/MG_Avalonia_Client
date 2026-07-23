using ClientAvalonia.CnCNet.Waf;
using Avalonia.Media;

namespace ClientAvalonia.CnCNet;

public sealed class CnCNetChatLine
{
    public required string DisplayText { get; init; }

    public string Sender { get; init; } = string.Empty;

    public bool IsSystem { get; init; }

    /// <summary>
    /// Per-line render color (DX <c>ChatMessage.Color</c>). Defaults to white so older
    /// callers that omit it still match the previous cream/white list styling.
    /// </summary>
    public Color TextColor { get; init; } = CnCNetIrcChatText.DefaultChatColor;

    /// <summary>
    /// Which timeline this line belongs to. Defaults to <see cref="CnCNetChatScope.LobbyChannel"/>
    /// so pre-existing callers (which never set this) continue to land in the lobby timeline
    /// exactly as before — backward compatible.
    /// </summary>
    public CnCNetChatScope Scope { get; init; } = CnCNetChatScope.LobbyChannel;

    public WafSeverity RiskLevel { get; init; } = WafSeverity.Allow;

    public string RiskSummary { get; init; } = string.Empty;
}
