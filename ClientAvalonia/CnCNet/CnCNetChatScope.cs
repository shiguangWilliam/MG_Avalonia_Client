namespace ClientAvalonia.CnCNet;

/// <summary>
/// Identifies which CnCNet chat timeline a <see cref="CnCNetChatLine"/> belongs to.
/// Mirrors DXMainClient's implicit split between the lobby <c>Channel</c> (game-list lobby)
/// and the in-room <c>Channel</c> handed to <c>CnCNetGameLobby</c> at SetUp.
/// </summary>
public enum CnCNetChatScope
{
    /// <summary>
    /// Lobby channel chat (the persistent game-list channel joined after CnCNet login).
    /// Corresponds to <c>_currentGame.ChatChannel</c> in <c>CnCNetSession</c>.
    /// </summary>
    LobbyChannel = 0,

    /// <summary>
    /// Active game-room chat (a private per-room IRC channel).
    /// Corresponds to <c>CnCNetActiveGameRoom.ChannelName</c>.
    /// </summary>
    GameRoom = 1,
}
