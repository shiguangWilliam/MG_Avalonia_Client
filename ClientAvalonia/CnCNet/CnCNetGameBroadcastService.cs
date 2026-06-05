using ClientCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Hosts GAME CTCP broadcasts on the game listing channel (XNA CnCNetGameLobby.BroadcastGame).</summary>
public sealed class CnCNetGameBroadcastService : IDisposable
{
    private const double BroadcastIntervalSeconds = 30;
    private const double InitialDelaySeconds = 10;

    private readonly object _sync = new();
    private Timer? _timer;
    private CnCNetIrcConnection? _connection;
    private CnCNetGameChannels? _channels;
    private CnCNetActiveGameRoom? _room;
    private bool _locked;
    private bool _closed;
    private string _mapName = string.Empty;
    private string _gameModeName = string.Empty;
    private string _mapSha1 = string.Empty;
    private IReadOnlyList<string> _playerNames = [];

    public void ConfigureHostChannel(CnCNetIrcConnection connection, CnCNetActiveGameRoom room)
    {
        string channel = NormalizeChannel(room.ChannelName);
        string localGame = ClientConfiguration.Instance.LocalGame.ToLowerInvariant();
        connection.SendInstant($"MODE {channel} +klnNs {room.Password} {room.MaxPlayers}");
        connection.SendInstant($"TOPIC {channel} :{ProgramConstants.CNCNET_PROTOCOL_REVISION};{localGame}");
        Logger.Log($"CnCNetGameBroadcastService: host MODE/TOPIC on {channel}");
    }

    public void StartHost(
        CnCNetIrcConnection connection,
        CnCNetGameChannels channels,
        CnCNetActiveGameRoom room,
        bool configureChannel = true)
    {
        lock (_sync)
        {
            StopTimerLocked();

            _connection = connection;
            _channels = channels;
            _room = room;
            _locked = false;
            _closed = false;
            _playerNames = [ProgramConstants.PLAYERNAME];

            if (configureChannel)
                ConfigureHostChannel(connection, room);

            BroadcastLocked();
            _timer = new Timer(_ => BroadcastLocked(), null,
                TimeSpan.FromSeconds(InitialDelaySeconds),
                TimeSpan.FromSeconds(BroadcastIntervalSeconds));
        }
    }

    public void UpdateListing(
        string mapName,
        string gameModeName,
        string mapSha1,
        IReadOnlyList<string> playerNames,
        bool locked,
        bool closed)
    {
        lock (_sync)
        {
            _mapName = mapName;
            _gameModeName = gameModeName;
            _mapSha1 = mapSha1;
            _playerNames = playerNames.Count > 0 ? playerNames : [ProgramConstants.PLAYERNAME];
            _locked = locked;
            _closed = closed;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_room != null && _connection != null && _channels != null && !_closed)
            {
                _closed = true;
                SendGameMessageLocked(closed: true);
            }

            StopTimerLocked();
            _connection = null;
            _channels = null;
            _room = null;
        }
    }

    public void Dispose() => Stop();

    private void BroadcastLocked()
    {
        lock (_sync)
        {
            if (_connection == null || _channels == null || _room == null || !_room.IsHost)
                return;

            SendGameMessageLocked(_closed);
        }
    }

    private void SendGameMessageLocked(bool closed)
    {
        if (_connection == null || _channels == null || _room == null)
            return;

        string broadcastChannel = NormalizeChannel(_channels.GameBroadcastChannel);
        string payload = BuildGamePayload(closed);
        _connection.SendCtcpNotice(broadcastChannel, payload);
        Logger.Log($"CnCNetGameBroadcastService: GAME â†?{broadcastChannel} ({_room.RoomName}, closed={closed})");
    }

    private string BuildGamePayload(bool closed)
    {
        CnCNetActiveGameRoom room = _room!;
        string flags = (_locked ? "1" : "0")
                       + (room.CustomPassword ? "1" : "0")
                       + (closed ? "1" : "0")
                       + "0"
                       + "0";

        var sb = new StringBuilder("GAME ");
        sb.Append(ProgramConstants.CNCNET_PROTOCOL_REVISION);
        sb.Append(';');
        sb.Append(ProgramConstants.GAME_VERSION);
        sb.Append(';');
        sb.Append(room.MaxPlayers);
        sb.Append(';');
        sb.Append(NormalizeChannel(room.ChannelName));
        sb.Append(';');
        sb.Append(room.RoomName);
        sb.Append(';');
        sb.Append(flags);
        sb.Append(';');
        sb.Append(string.Join(',', _playerNames));
        sb.Append(';');
        sb.Append(_mapName);
        sb.Append(';');
        sb.Append(_gameModeName);
        sb.Append(';');
        sb.Append(room.Tunnel.Address);
        sb.Append(':');
        sb.Append(room.Tunnel.Port);
        sb.Append(';');
        sb.Append('0');

        if (!ProgramConstants.UsesLegacyCnCNetGameBroadcast)
        {
            sb.Append(';');
            sb.Append(room.SkillLevel);
            sb.Append(';');
            sb.Append(_mapSha1);
        }

        return sb.ToString();
    }

    private void StopTimerLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static string NormalizeChannel(string channel)
        => channel.StartsWith('#') ? channel.ToLowerInvariant() : "#" + channel.ToLowerInvariant();
}
