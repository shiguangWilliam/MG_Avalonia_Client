using ClientAvalonia.Network;
using ClientCore;
using ClientCore.Network;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Create / join CnCNet game rooms (XNA CnCNetLobby Gcw_GameCreated / JoinGame subset).</summary>
public sealed class CnCNetGameRoomService
{
    public sealed class ActiveRoom
    {
        public required string RoomName { get; init; }

        public required string ChannelName { get; init; }

        public required string Password { get; init; }

        public required CnCNetTunnelEntry Tunnel { get; init; }

        public int MaxPlayers { get; init; }

        public bool IsHost { get; init; }

        public bool CustomPassword { get; init; }
    }

    public ActiveRoom? CurrentRoom { get; private set; }

    public bool TryCreateGame(
        CnCNetIrcConnection connection,
        IReadOnlyList<CnCNetTunnelEntry> tunnels,
        string chatChannelPrefix,
        string roomName,
        int maxPlayers,
        string? customPassword,
        out string error)
    {
        error = string.Empty;
        if (!connection.IsConnected)
        {
            error = "Not connected to CnCNet.";
            return false;
        }

        CnCNetTunnelEntry? tunnel = tunnels.FirstOrDefault(t => t.Official) ?? tunnels.FirstOrDefault();
        if (tunnel == null)
        {
            error = "No NAT tunnels available.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(roomName))
            roomName = $"{ProgramConstants.PLAYERNAME}'s Game";

        string channelName = GenerateUniqueChannelName(chatChannelPrefix);
        bool useCustomPassword = !string.IsNullOrWhiteSpace(customPassword);
        string password = useCustomPassword
            ? customPassword!.Trim()
            : Utilities.CalculateSHA1ForString(channelName).Substring(0, 10);

        connection.JoinChannel(channelName, password);

        CurrentRoom = new ActiveRoom
        {
            RoomName = roomName,
            ChannelName = channelName,
            Password = password,
            Tunnel = tunnel,
            MaxPlayers = maxPlayers,
            IsHost = true,
            CustomPassword = useCustomPassword,
        };

        return true;
    }

    public bool TryJoinGame(CnCNetIrcConnection connection, CnCNetHostedGameSummary game, string? password, out string error)
    {
        error = string.Empty;
        if (!connection.IsConnected)
        {
            error = "Not connected to CnCNet.";
            return false;
        }

        if (game.IsClosed)
        {
            error = "Game is no longer available.";
            return false;
        }

        if (game.Locked && string.IsNullOrWhiteSpace(password))
        {
            error = "The selected game is locked (password required).";
            return false;
        }

        if (game.CustomPassword && string.IsNullOrWhiteSpace(password))
        {
            error = "This game requires a password.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(password) && !game.IsLoadedGame)
            password = Utilities.CalculateSHA1ForString(game.ChannelName).Substring(0, 10);

        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Could not resolve join password.";
            return false;
        }

        connection.JoinChannel(game.ChannelName, password);

        CurrentRoom = new ActiveRoom
        {
            RoomName = game.RoomName,
            ChannelName = game.ChannelName,
            Password = password,
            Tunnel = new CnCNetTunnelEntry
            {
                Address = game.TunnelAddress,
                Port = game.TunnelPort,
                Name = $"{game.TunnelAddress}:{game.TunnelPort}",
            },
            MaxPlayers = game.MaxPlayers,
            IsHost = false,
            CustomPassword = game.CustomPassword,
        };

        return true;
    }

    public void Clear() => CurrentRoom = null;

    private static string GenerateUniqueChannelName(string chatChannelPrefix)
    {
        string prefix = chatChannelPrefix.StartsWith('#') ? chatChannelPrefix : "#" + chatChannelPrefix;
        return $"{prefix}-game{Random.Shared.Next(1_000_000, 9_999_999)}";
    }
}
