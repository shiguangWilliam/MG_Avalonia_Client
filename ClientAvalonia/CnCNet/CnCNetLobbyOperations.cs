using System;
using System.Linq;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Create / join game room flows (XNA CnCNetLobby + GameCreationWindow subset).</summary>
public static class CnCNetLobbyOperations
{
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

        string roomName = request.RoomName.Trim();
        if (string.IsNullOrWhiteSpace(roomName))
        {
            message = "Game room name is required.";
            return false;
        }

        if (roomName.Length > 23)
            roomName = roomName[..23];

        string channelName = GenerateUniqueGameChannel(session.Channels.ChatChannel);
        bool customPassword = !string.IsNullOrWhiteSpace(request.Password);
        string password = customPassword
            ? request.Password.Trim()
            : Utilities.CalculateSHA1ForString(channelName)[..10];

        session.JoinGameChannel(channelName, password, out string? joinError);
        if (joinError != null)
        {
            message = joinError;
            return false;
        }

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
            CustomPassword = customPassword,
        });

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

        CnCNetTunnelEntry tunnel = session.Tunnels.FirstOrDefault(t => t.Official) ?? session.Tunnels[0];
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

        if (game.CustomPassword && string.IsNullOrWhiteSpace(password))
        {
            message = "This game requires a password.";
            return false;
        }

        string joinPassword = !string.IsNullOrWhiteSpace(password)
            ? password
            : Utilities.CalculateSHA1ForString(game.ChannelName)[..10];

        session.JoinGameChannel(game.ChannelName, joinPassword, out string? joinError);
        if (joinError != null)
        {
            message = joinError;
            return false;
        }

        CnCNetTunnelEntry? tunnel = session.Tunnels.FirstOrDefault(t =>
            t.Address.Equals(game.TunnelAddress, StringComparison.OrdinalIgnoreCase)
            && t.Port == game.TunnelPort);

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
            CustomPassword = game.CustomPassword,
        });

        message = $"Joining \"{game.RoomName}\"...";
        return true;
    }

    private static string GenerateUniqueGameChannel(string chatChannel)
    {
        // XNA CnCNetLobby.RandomizeChannelName; MG mod uses localized suffix.
        string baseName = chatChannel.StartsWith('#') ? chatChannel : "#" + chatChannel;
        int suffix = Random.Shared.Next(1_000_000, 9_999_999);
        string channelSuffix = ClientConfiguration.Instance.LocalGame.Equals("MG", StringComparison.OrdinalIgnoreCase)
            ? "-游戏" + suffix
            : "-game" + suffix;
        return baseName + channelSuffix;
    }
}
