using System;
using System.Linq;
using Rampastring.Tools;

namespace ClientCore.Network;

/// <summary>Create / join game room flows (XNA CnCNetLobby + GameCreationWindow subset).</summary>
public static class CnCNetLobbyOperations
{
    public static bool TryCreateGame(CnCNetSession session, out string message)
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

        if (session.Tunnels.Count == 0)
        {
            message = "No NAT tunnels available.";
            return false;
        }

        CnCNetTunnelEntry tunnel = session.Tunnels.FirstOrDefault(t => t.Official) ?? session.Tunnels[0];
        string channelName = GenerateUniqueGameChannel(session.Channels.ChatChannel);
        string password = Utilities.CalculateSHA1ForString(channelName)[..10];
        string roomName = $"{ProgramConstants.PLAYERNAME}'s Game";

        session.JoinGameChannel(channelName, password, out string? joinError);
        if (joinError != null)
        {
            message = joinError;
            return false;
        }

        session.SetActiveGameRoom(new CnCNetActiveGameRoom
        {
            RoomName = roomName,
            ChannelName = channelName,
            Password = password,
            Tunnel = tunnel,
            IsHost = true,
            MaxPlayers = 8,
            SkillLevel = 0,
            CustomPassword = false,
        });

        message = $"Creating game \"{roomName}\" on {channelName}...";
        return true;
    }

    public static bool TryJoinGame(CnCNetSession session, CnCNetHostedGameSummary game, string? password, out string message)
    {
        if (session.Connection is not { IsConnected: true })
        {
            message = "Not connected to CnCNet.";
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
            message = "This game requires a password (custom password UI pending).";
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
        string baseName = chatChannel.StartsWith('#') ? chatChannel : "#" + chatChannel;
        int suffix = Random.Shared.Next(1_000_000, 9_999_999);
        return $"{baseName}-game{suffix}".ToLowerInvariant();
    }
}
