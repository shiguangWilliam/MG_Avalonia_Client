using System;

namespace ClientAvalonia.Tests.Fixture;

/// <summary>
/// DX-aligned contract constants. Each entry references a DX source location and a fixed
/// expected value so tests can assert against a single source of truth.
///
/// Every value here MUST be traceable to a DXMainClient source line (see the plan's B.1 table).
/// Don't edit a constant unless you've re-read the cited DX source — these are regression locks.
/// </summary>
internal static class DxAliases
{
    // DX CnCNetLobby.cs:1519-1563 — GAME CTCP field layout.
    public const int GameFieldCount = 13;
    public const int LegacyGameFieldCount = 11;

    // Indexes into the 13-field GAME payload (DX CnCNetLobby.cs:1519-1563).
    public const int IdxRevision = 0;
    public const int IdxGameVersion = 1;
    public const int IdxMaxPlayers = 2;
    public const int IdxChannel = 3;
    public const int IdxRoomName = 4;
    public const int IdxFlags = 5;
    public const int IdxPlayers = 6;
    public const int IdxMap = 7;
    public const int IdxGameMode = 8;
    public const int IdxTunnel = 9;
    public const int IdxLoadedGameId = 10;
    public const int IdxSkillLevel = 11;
    public const int IdxMapHash = 12;

    // DX CnCNetLobby.cs:1547-1551 — flags field defaults when missing/empty.
    // BooleanFromString(value, default) → defaults: locked=true, passworded=false, closed=true, loaded=false, ladder=false
    public const bool DefaultLocked = true;
    public const bool DefaultPassworded = false;
    public const bool DefaultClosed = true;
    public const bool DefaultLoadedGame = false;
    public const bool DefaultLadder = false;
    public const int FlagsFieldLength = 5;

    // DX CnCNetLobby.cs:1541 — reject when revision mismatches.
    // ProgramConstants.CNCNET_PROTOCOL_REVISION default is "R13" in ClientCore.
    public const string CurrentProtocolRevision = "R13";
    public const string LegacyProtocolRevision = "R10";

    // DX CnCNetLobby.cs:1052 — host password = SHA1(channelName)[..10] (DX upstream).
    // MG differs (MG-PASSWORD-SHA1-CHANNEL-ROOM) — see CnCNetLobbyOperationsPasswordTests.
    public const int PasswordHashHexPrefixLength = 10;

    // DX CnCNetLobby.cs:1572-1589 — reject GAME when no tunnels are loaded.
    public const string RejectReasonNoTunnels = "no available tunnels";

    // DX CnCNetGameLobby.cs:29-31 — broadcast cadence.
    public const int GameBroadcastIntervalSeconds = 30;
    public const int GameBroadcastInitialDelaySeconds = 10;

    // DX CnCNetTunnel.cs port parsing — bare int.Parse, NO range check.
    // MG extension: explicit 1..65535 (CnCNetPortValidator).
    public const ushort PortMin = 1;
    public const ushort PortMax = 65535;

    // DX CnCNetGameLobby.cs GetPlayerPortInfo error string (player-port count mismatch).
    public const string TunnelPortCountError = "Could not contact the CnCNet tunnel server. Try another tunnel.";

    // DX Channel.cs — JOIN preserves case, comparison uses lower-case.
    public const string IrcChannelPrefix = "#";

    /// <summary>Stable sample inputs for SHA1 password assertions (DX vs MG divergence).</summary>
    public const string SampleChannel = "#ra3-game-1234567";
    public const string SampleRoomName = "TestRoom";
}
