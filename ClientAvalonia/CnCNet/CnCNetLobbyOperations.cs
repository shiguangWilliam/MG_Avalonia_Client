using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>Create / join game room flows (XNA CnCNetLobby + GameCreationWindow subset).</summary>
public static class CnCNetLobbyOperations
{
    private const string LocalizedGameSuffixMarker = "-游戏";

    /// <summary>
    /// Default IRC +k key aligned with upstream DXMainClient:
    /// first 10 hex chars of <c>SHA1(ASCII(channelName))</c>.
    /// </summary>
    /// <remarks>
    /// Create/host always uses this DX key so mainstream DX clients can join.
    /// Join also tries MG's <c>SHA1(ASCII(channelName + GameRoomName))</c> as a fallback
    /// (MG 1.0.4.2 localizes channel suffix and hashes channel+room).
    /// ASCII encoding converts non-ASCII bytes to <c>?</c> (0x3F).
    /// </remarks>
    public static string GetDefaultChannelPassword(string channelName, string roomName)
        => GetDefaultChannelPasswordCandidates(channelName, roomName)[0];

    /// <summary>
    /// Ordered IRC +k keys for default-password rooms:
    /// DX upstream first, then MG channel+room, then codepage fallbacks when needed.
    /// </summary>
    public static IReadOnlyList<string> GetDefaultChannelPasswordCandidates(string channelName, string roomName)
    {
        string preservedChannel = CnCNetIrcChannelNames.Preserve(channelName);
        string normalizedRoom = roomName ?? string.Empty;
        var candidates = new List<string>(capacity: 5);

        // 1. DXMainClient: SHA1(ASCII(channelName)) — create/host and primary join.
        AddUniqueCandidate(candidates, preservedChannel, Encoding.ASCII);

        // 2. MG fork: SHA1(ASCII(channelName + GameRoomName)) — join fallback for MG hosts.
        AddUniqueCandidate(candidates, preservedChannel + normalizedRoom, Encoding.ASCII);

        // 3. Non-ASCII: try system ANSI codepage for both algorithms.
        if (preservedChannel.Any(static c => c > 127) || normalizedRoom.Any(static c => c > 127))
        {
            AddUniqueCandidate(candidates, preservedChannel, Encoding.Default);
            AddUniqueCandidate(candidates, preservedChannel + normalizedRoom, Encoding.Default);
        }

        return candidates;
    }

    /// <summary>DX Gcw_GameCreated: empty user password → SHA1(channelName) key; non-empty → custom key.</summary>
    public static bool ResolveCreatePassword(
        string channelName,
        string roomName,
        bool requiresPassword,
        string password,
        out string ircKey,
        out bool isCustomPassword)
    {
        if (requiresPassword && !string.IsNullOrWhiteSpace(password))
        {
            ircKey = password.Trim();
            isCustomPassword = true;
            return true;
        }

        ircKey = GetDefaultChannelPassword(channelName, roomName);
        isCustomPassword = false;
        return true;
    }

    /// <summary>DX Channel.ChangePassword — update IRC +k without dropping the channel.</summary>
    public static string BuildChannelPasswordModeCommand(string channelWire, string oldPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            return $"MODE {channelWire} -k {oldPassword}";

        if (string.IsNullOrEmpty(oldPassword))
            return $"MODE {channelWire} +k {newPassword}";

        return $"MODE {channelWire} -k+k {oldPassword} {newPassword}";
    }

    /// <summary>Maps join listing RequiresPassword to the IRC JOIN key (DX CnCNetLobby.JoinGame).</summary>
    public static bool TryResolveJoinPassword(
        CnCNetHostedGameSummary game,
        string? userPassword,
        out string joinPassword,
        out IReadOnlyList<string>? defaultPasswordCandidates,
        out string? error)
    {
        joinPassword = string.Empty;
        defaultPasswordCandidates = null;
        error = null;

        if (game.RequiresPassword)
        {
            if (string.IsNullOrWhiteSpace(userPassword))
            {
                error = "This game requires a password.";
                return false;
            }

            joinPassword = userPassword.Trim();
            return true;
        }

        if (game.IsLoadedGame)
        {
            // DX: SHA1(spawnSG GameID)[..10] — same key hosts use when creating loaded rooms.
            joinPassword = Services.MultiplayerLoadGameSupport.ComputeLoadedGamePassword();
            return true;
        }

        // DX: derive from channelName; candidates also include MG channel+room for join fallback.
        defaultPasswordCandidates = GetDefaultChannelPasswordCandidates(game.ChannelName, game.RoomName);
        joinPassword = defaultPasswordCandidates[0];
        return true;
    }

    public static bool TryCreateGame(CnCNetSession session, CnCNetGameCreationRequest request, out string message)
    {
        if (session.Connection is not { IsConnected: true })
        {
            message = "Not connected to CnCNet.";
            return false;
        }

        if (session.Channels == null)
        {
            message = "Game channels are not configured.";
            return false;
        }

        string roomName = NameValidator.GetSanitizedGameName(request.RoomName);
        NameValidationError validationError = NameValidator.IsGameNameValid(roomName, out string? errorMessage);
        if (validationError != NameValidationError.None)
        {
            message = errorMessage ?? "Game room name is invalid.";
            return false;
        }

        if (request.RequiresPassword && string.IsNullOrWhiteSpace(request.Password))
        {
            message = "Enter a password or disable password protection.";
            return false;
        }

        string channelName = GenerateUniqueGameChannel(session, session.Channels.ChatChannel);
        if (!ResolveCreatePassword(
                channelName,
                roomName,
                request.RequiresPassword,
                request.Password,
                out string password,
                out bool passworded))
        {
            message = "Enter a password or disable password protection.";
            return false;
        }

        string hostName = string.IsNullOrWhiteSpace(session.LocalNick)
            ? AppState.Environment.PlayerName
            : session.LocalNick;

        session.BeginDefaultPasswordJoinCandidates(
            passworded ? null : GetDefaultChannelPasswordCandidates(channelName, roomName));

        session.SetActiveGameRoom(new CnCNetActiveGameRoom
        {
            RoomName = roomName,
            ChannelName = channelName,
            Password = password,
            Tunnel = request.Tunnel,
            HostName = hostName,
            IsHost = true,
            MaxPlayers = request.MaxPlayers,
            SkillLevel = request.SkillLevel,
            Passworded = passworded,
            IsLoadedGame = request.IsLoadedGame,
        });

        session.JoinGameChannel(channelName, password, out string? joinError, roomName);
        if (joinError != null)
        {
            session.LeaveGameRoom();
            message = joinError;
            return false;
        }

        message = $"Creating game \"{roomName}\" on {channelName}...";
        return true;
    }

    public static bool TryCreateGame(CnCNetSession session, out string message)
    {
        if (session.Tunnels.Count == 0)
        {
            message = "No NAT tunnels available.";
            return false;
        }

        CnCNetTunnel tunnel = session.TunnelSorter.TryPeekBest()
            ?? session.Tunnels.FirstOrDefault(t => t.Official)
            ?? session.Tunnels[0];
        var request = new CnCNetGameCreationRequest
        {
            RoomName = $"{AppState.Environment.PlayerName}'s Game",
            MaxPlayers = 8,
            Tunnel = tunnel,
            SkillLevel = AppState.Configuration.Legacy.DefaultSkillLevelIndex,
        };
        return TryCreateGame(session, request, out message);
    }

    public static bool TryJoinGame(CnCNetSession session, CnCNetHostedGameSummary game, string? password, out string message)
    {
        if (session.Connection is not { IsConnected: true })
        {
            message = "Not connected to CnCNet.";
            return false;
        }

        string localGameId = AppState.Configuration.Legacy.LocalGame;
        if (!string.IsNullOrWhiteSpace(game.SourceGameId)
            && !game.SourceGameId.Equals(localGameId, StringComparison.OrdinalIgnoreCase))
        {
            CnCNetGameEntry? target = session.GameCollection?.Games
                .FirstOrDefault(g => g.InternalName.Equals(game.SourceGameId, StringComparison.OrdinalIgnoreCase));
            string gameName = target?.UiName ?? game.SourceGameId;
            message = $"The selected game is for {gameName}.";
            return false;
        }

        if (game.Locked)
        {
            message = "The selected game is locked.";
            return false;
        }

        if (game.IsLoadedGame)
        {
            IReadOnlyList<string> savedPlayers = Services.MultiplayerLoadGameSupport.ReadSavedPlayerNames();
            if (savedPlayers.Count > 0
                && !savedPlayers.Any(n => n.Equals(AppState.Environment.PlayerName, StringComparison.OrdinalIgnoreCase)))
            {
                message = "You do not exist in the saved game!";
                return false;
            }
        }

        if (game.Incompatible && AppState.Configuration.Legacy.DisallowJoiningIncompatibleGames)
        {
            message = "Cannot join game. The host is on a different game version than you.";
            return false;
        }

        if (!TryResolveJoinPassword(
                game,
                password,
                out string joinPassword,
                out IReadOnlyList<string>? defaultPasswordCandidates,
                out string? passwordError))
        {
            message = passwordError ?? "This game requires a password.";
            return false;
        }

        session.BeginDefaultPasswordJoinCandidates(defaultPasswordCandidates);

        CnCNetTunnel? tunnel = session.Tunnels.FirstOrDefault(t =>
            t.Address.Equals(game.TunnelAddress, StringComparison.OrdinalIgnoreCase)
            && t.Port == game.TunnelPort);

        if (tunnel == null)
        {
            tunnel = CnCNetTunnelListLoader.Load().FirstOrDefault(t =>
                t.Address.Equals(game.TunnelAddress, StringComparison.OrdinalIgnoreCase)
                && t.Port == game.TunnelPort);
        }

        if (tunnel == null)
        {
            message = $"Tunnel {game.TunnelAddress}:{game.TunnelPort} is unavailable.";
            return false;
        }

        session.SetActiveGameRoom(new CnCNetActiveGameRoom
        {
            RoomName = game.RoomName,
            ChannelName = game.ChannelName,
            Password = joinPassword,
            Tunnel = tunnel,
            HostName = game.HostName,
            IsHost = false,
            MaxPlayers = game.MaxPlayers,
            SkillLevel = game.SkillLevel,
            Passworded = game.RequiresPassword,
            IsLoadedGame = game.IsLoadedGame,
        });

        session.JoinGameChannel(game.ChannelName, joinPassword, out string? joinError, game.RoomName);
        if (joinError != null)
        {
            session.LeaveGameRoom();
            message = joinError;
            return false;
        }

        message = $"Joining \"{game.RoomName}\"...";
        return true;
    }

    /// <summary>DX <c>RandomizeChannelName</c>: <c>{chatChannel}-game{7digits}</c> (English suffix, no L10N).</summary>
    private static string GenerateUniqueGameChannel(CnCNetSession session, string chatChannel)
    {
        const int maxTries = 10000;

        for (int i = 0; i < maxTries; i++)
        {
            int suffix = Random.Shared.Next(1_000_000, 9_999_999);
            string channelName = BuildGameChannelName(chatChannel, suffix);
            bool exists = session.LobbyState.HostedGameDetails.Any(g =>
                g.ChannelName.Equals(channelName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
                return channelName;
        }

        throw new InvalidOperationException($"Could not find a random channel name after {maxTries} retries.");
    }

    /// <summary>
    /// DX <c>CnCNetLobby.RandomizeChannelName</c> format:
    /// <c>{chatChannel}-game{suffix}</c> with a leading <c>#</c> on the chat channel.
    /// </summary>
    internal static string BuildGameChannelName(string chatChannel, int suffix)
    {
        string baseName = chatChannel.StartsWith('#') ? chatChannel : "#" + chatChannel;
        return baseName + "-game" + suffix.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>MG localized <c>{base}-游戏{num}</c> → DX English <c>{base}-game{num}</c> (join fallback helper).</summary>
    internal static string? TryGetEnglishGameChannelName(string preservedChannelName)
    {
        int markerIndex = preservedChannelName.LastIndexOf(LocalizedGameSuffixMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        string digits = preservedChannelName[(markerIndex + LocalizedGameSuffixMarker.Length)..];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return null;

        return preservedChannelName[..markerIndex] + "-game" + digits;
    }

    private static void AddUniqueCandidate(List<string> candidates, string sha1Input, Encoding encoding)
    {
        string key = Sha1First10(sha1Input, encoding);
        if (!candidates.Contains(key))
            candidates.Add(key);
    }

    private static string Sha1First10(string input, Encoding encoding)
    {
        byte[] buffer = encoding.GetBytes(input);
#pragma warning disable CA5350
        byte[] hash = SHA1.HashData(buffer);
#pragma warning restore CA5350
        return Convert.ToHexString(hash).ToLowerInvariant()[..10];
    }
}
