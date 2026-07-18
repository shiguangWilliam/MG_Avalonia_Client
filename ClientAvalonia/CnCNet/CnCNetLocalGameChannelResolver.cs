using System;

namespace ClientAvalonia.CnCNet;

/// <summary>
/// Resolves IRC chat/broadcast channels for a <c>LocalGame</c> that is not yet in
/// the built-in table or <c>GameCollectionConfig.ini</c> CustomGames.
/// </summary>
/// <remarks>
/// Funnel (highest priority first):
/// <list type="number">
/// <item>ClientDefinitions <c>CnCNetChatChannel</c> / <c>CnCNetGameBroadcastChannel</c></item>
/// <item>LNOD-DX convention: <c>#cncnet-{LocalGame}</c> / <c>#cncnet-{LocalGame}-games</c>
/// (verified against LNOD <c>clientdx.exe</c> JOIN logs)</item>
/// </list>
/// Built-in games and CustomGames are handled earlier by <see cref="CnCNetGameCollection"/>.
/// Settings <c>[Channels] Xxx=True</c> only controls followed broadcast rooms, not names.
/// </remarks>
public static class CnCNetLocalGameChannelResolver
{
    public enum Source
    {
        /// <summary>Explicit keys in ClientDefinitions.ini.</summary>
        ClientDefinitions,

        /// <summary><c>#cncnet-{id}</c> / <c>#cncnet-{id}-games</c> (LNOD DX / common CnCNet pattern).</summary>
        LocalGameConvention,
    }

    /// <summary>
    /// Builds the LNOD-DX / common CnCNet channel pair for an internal game id.
    /// </summary>
    public static (string Chat, string Broadcast) BuildConventionChannels(string internalName)
    {
        string id = NormalizeInternalName(internalName);
        return ($"#cncnet-{id}", $"#cncnet-{id}-games");
    }

    /// <summary>
    /// Resolves channels when LocalGame is missing from the collection.
    /// </summary>
    public static bool TryResolve(
        string? localGame,
        string? clientDefinitionsChatChannel,
        string? clientDefinitionsBroadcastChannel,
        out string chatChannel,
        out string broadcastChannel,
        out Source source)
    {
        chatChannel = string.Empty;
        broadcastChannel = string.Empty;
        source = Source.ClientDefinitions;

        string id = NormalizeInternalName(localGame);
        if (string.IsNullOrEmpty(id) || !IsValidInternalNameForChannel(id))
            return false;

        string defsChat = NormalizeChannel(clientDefinitionsChatChannel);
        string defsBroadcast = NormalizeChannel(clientDefinitionsBroadcastChannel);
        if (!string.IsNullOrEmpty(defsChat) || !string.IsNullOrEmpty(defsBroadcast))
        {
            chatChannel = string.IsNullOrEmpty(defsChat) ? defsBroadcast : defsChat;
            broadcastChannel = string.IsNullOrEmpty(defsBroadcast) ? defsChat : defsBroadcast;
            source = Source.ClientDefinitions;
            return true;
        }

        (chatChannel, broadcastChannel) = BuildConventionChannels(id);
        source = Source.LocalGameConvention;
        return true;
    }

    internal static string NormalizeInternalName(string? localGame)
        => string.IsNullOrWhiteSpace(localGame) ? string.Empty : localGame.Trim().ToLowerInvariant();

    /// <summary>IRC channel name fragment: no space/comma/BEL; must be non-empty.</summary>
    internal static bool IsValidInternalNameForChannel(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (id.Contains(' ') || id.Contains(',') || id.Contains((char)7))
            return false;

        return true;
    }

    private static string NormalizeChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return string.Empty;

        string trimmed = channel.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
