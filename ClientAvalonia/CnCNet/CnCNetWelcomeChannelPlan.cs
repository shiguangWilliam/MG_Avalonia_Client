using System;
using System.Collections.Generic;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// IRC channels joined after CnCNet welcome (mirrors <c>CnCNetSession.OnWelcomeReceived</c>).
/// Used by integration tests to assert MG/LNOD can enter the same rooms as DX without a live server.
/// </summary>
public static class CnCNetWelcomeChannelPlan
{
    public const string DefaultChatChannelKey = "ra1-derp";

    public const string GeneralChatChannel = "#cncnet";

    public sealed record JoinStep(string Channel, string? Key, string Role);

    /// <summary>
    /// Ordered JOIN steps for the local game after IRC welcome:
    /// chat (+key) → <c>#cncnet</c> (+key) → game broadcast (no key).
    /// </summary>
    public static IReadOnlyList<JoinStep> BuildForLocalGame(CnCNetGameEntry localGame)
    {
        ArgumentNullException.ThrowIfNull(localGame);

        string chat = Normalize(localGame.ChatChannel);
        if (string.IsNullOrEmpty(chat))
            throw new InvalidOperationException("Local game ChatChannel is empty; cannot join CnCNet lobby.");

        var steps = new List<JoinStep>(capacity: 3)
        {
            new(chat, DefaultChatChannelKey, "chat"),
            new(GeneralChatChannel, DefaultChatChannelKey, "general"),
        };

        if (localGame.HasGameBroadcast)
        {
            string broadcast = Normalize(localGame.GameBroadcastChannel!);
            if (!string.IsNullOrEmpty(broadcast))
                steps.Add(new(broadcast, Key: null, "broadcast"));
        }

        return steps;
    }

    /// <summary>True when welcome JOIN plan covers chat + general + broadcast (full lobby readiness).</summary>
    public static bool IsLobbyReady(CnCNetGameEntry? localGame)
    {
        if (localGame == null || !localGame.Supported)
            return false;

        if (string.IsNullOrWhiteSpace(localGame.ChatChannel))
            return false;

        if (!localGame.HasGameBroadcast)
            return false;

        IReadOnlyList<JoinStep> steps = BuildForLocalGame(localGame);
        return steps.Count >= 3
               && steps[0].Role == "chat"
               && steps[1].Channel.Equals(GeneralChatChannel, StringComparison.OrdinalIgnoreCase)
               && steps[2].Role == "broadcast";
    }

    private static string Normalize(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        string trimmed = channel.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
