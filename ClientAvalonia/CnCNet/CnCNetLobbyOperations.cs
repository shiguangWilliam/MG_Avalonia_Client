using System;
using System.Linq;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

using ClientAvalonia.Domain.Multiplayer.CnCNet;
/// <summary>Create / join game room flows (XNA CnCNetLobby + GameCreationWindow subset).</summary>
public static class CnCNetLobbyOperations
{
    /// <summary>Default IRC channel key (XNA CnCNetLobby.JoinGame when !Passworded).</summary>
    public static string GetDefaultChannelPassword(string channelName)
    {
        string channel = CnCNetIrcChannelNames.Preserve(channelName);
        return Utilities.CalculateSHA1ForString(channel)[..10];
    }

    /// <summary>Maps DXMain Passworded to the IRC JOIN key.</summary>
    public static bool TryResolveJoinPassword(
        CnCNetHostedGameSummary game,
        string? userPassword,
        out string joinPassword,
        out string? error)
    {
        joinPassword = string.Empty;
        error = null;

        if (game.Passworded)
        {
            if (string.IsNullOrWhiteSpace(userPassword))
            {
                error = "This game requires a password.";
                return false;
            }

            joinPassword = userPassword.Trim();
            return true;
        }

        // XNA: always derive from channel name; ignore any stale user input.
        joinPassword = GetDefaultChannelPassword(game.ChannelName);
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

        string channelName = GenerateUniqueGameChannel(session, session.Channels.ChatChannel);
        bool passworded = request.Passworded;
        string password = passworded
            ? request.Password.Trim()
            : GetDefaultChannelPassword(CnCNetIrcChannelNames.Preserve(channelName));

        string hostName = string.IsNullOrWhiteSpace(session.LocalNick)
            ? ProgramConstants.PLAYERNAME
            : session.LocalNick;

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
        });

        session.JoinGameChannel(channelName, password, out string? joinError);
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

        CnCNetTunnel tunnel = session.Tunnels.FirstOrDefault(t => t.Official) ?? session.Tunnels[0];
        var request = new CnCNetGameCreationRequest
        {
            RoomName = $"{ProgramConstants.PLAYERNAME}'s Game",
            MaxPlayers = 8,
            Tunnel = tunnel,
            SkillLevel = ClientConfiguration.Instance.DefaultSkillLevelIndex,
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

        string localGameId = ClientConfiguration.Instance.LocalGame;
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
            message = "Saved-game rooms are not supported yet.";
            return false;
        }

        if (game.Incompatible && ClientConfiguration.Instance.DisallowJoiningIncompatibleGames)
        {
            message = "Cannot join game. The host is on a different game version than you.";
            return false;
        }

        if (!TryResolveJoinPassword(game, password, out string joinPassword, out string? passwordError))
        {
            message = passwordError ?? "This game requires a password.";
            return false;
        }

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
            Passworded = game.Passworded,
        });

        session.JoinGameChannel(game.ChannelName, joinPassword, out string? joinError);
        if (joinError != null)
        {
            session.LeaveGameRoom();
            message = joinError;
            return false;
        }

        message = $"Joining \"{game.RoomName}\"...";
        return true;
    }

    private static string GenerateUniqueGameChannel(CnCNetSession session, string chatChannel)
    {
        string baseName = chatChannel.StartsWith('#') ? chatChannel : "#" + chatChannel;
        const int maxTries = 10000;

        for (int i = 0; i < maxTries; i++)
        {
            string channelName = baseName + "-game" + Random.Shared.Next(1_000_000, 9_999_999);
            bool exists = session.LobbyState.HostedGameDetails.Any(g =>
                g.ChannelName.Equals(channelName, StringComparison.OrdinalIgnoreCase));

            if (!exists)
                return channelName;
        }

        throw new InvalidOperationException($"Could not find a random channel name after {maxTries} retries.");
    }
}
