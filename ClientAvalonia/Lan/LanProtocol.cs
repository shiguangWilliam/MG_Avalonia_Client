using System.Text;
using ClientCore;

namespace ClientAvalonia.Lan;

/// <summary>
/// DX LAN wire constants and command names (UDP discovery + TCP room).
/// Keeps protocol literals out of session/UI code.
/// </summary>
public static class LanProtocol
{
    public const string Revision = ProgramConstants.LAN_PROTOCOL_REVISION; // RL8
    public const int LobbyUdpPort = ProgramConstants.LAN_LOBBY_PORT; // 1232
    public const int GameLobbyTcpPort = ProgramConstants.LAN_GAME_LOBBY_PORT; // 1233
    public const int InGamePort = ProgramConstants.LAN_INGAME_PORT; // 1234

    public const char DataSep = ProgramConstants.LAN_DATA_SEPARATOR; // \x01
    public const char MessageSep = ProgramConstants.LAN_MESSAGE_SEPARATOR; // \x02

    public static Encoding Encoding => ProgramConstants.LAN_ENCODING;

    // UDP lobby
    public const string Alive = "ALIVE";
    public const string Chat = "CHAT";
    public const string Quit = "QUIT";
    public const string Game = "GAME";

    // TCP new-game room
    public const string Join = "JOIN";
    public const string Ready = "READY";
    public const string PlayerOptionsRequest = "POREQ";
    public const string PlayerOptions = "POPTS";
    public const string PlayerExtraOptions = "PEOPTS";
    public const string Options = "OPTS";
    public const string GameLobbyChat = "GLCHAT";
    public const string GetReady = "GETREADY";
    public const string Launch = "LAUNCH";
    public const string FileHash = "FHASH";
    public const string Return = "RETURN";
    public const string Ping = "PING";

    // TCP loading lobby (DX LANGameLoadingLobby)
    public const string Start = "START";

    public static readonly TimeSpan AliveInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PlayerInactivity = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan GameAdvertiseInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan GameListStale = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DedupTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TcpClientDropout = TimeSpan.FromSeconds(20);
}
