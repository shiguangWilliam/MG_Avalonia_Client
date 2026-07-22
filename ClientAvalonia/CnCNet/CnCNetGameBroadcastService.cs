using ClientAvalonia.CnCNet.Protocol;
using ClientCore;
using System;
using System.Collections.Generic;
using System.Threading;
using Rampastring.Tools;

namespace ClientAvalonia.CnCNet;

/// <summary>Hosts GAME CTCP broadcasts on the game listing channel (XNA CnCNetGameLobby.BroadcastGame).</summary>
public sealed class CnCNetGameBroadcastService : IDisposable
{
    private const double BroadcastIntervalSeconds = 30;
    private const double InitialDelaySeconds = 10;
    private const double BroadcastAccelerationSeconds = 10;

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
    private string _lastImmediatePayload = string.Empty;
    private CnCNetGameBroadcastDialect? _dialect;

    /// <summary>Optional wire-dialect observer; when null, emit always uses stock R13.</summary>
    public CnCNetGameBroadcastDialect? Dialect
    {
        get => _dialect;
        set => _dialect = value;
    }

    public void ConfigureHostChannel(CnCNetIrcConnection connection, CnCNetActiveGameRoom room)
    {
        // XNA CnCNetGameLobby.OnJoined: MODE/TOPIC use channel.ChannelName (original casing).
        string channel = CnCNetIrcChannelNames.Preserve(room.ChannelName);
        string localGame = ClientConfiguration.Instance.LocalGame.ToLowerInvariant();
        string topicRevision = ResolveEmitRevision(_channels?.GameBroadcastChannel);
        bool modeSent = connection.TrySendInstantOnChannel(
            channel,
            $"MODE {channel} +klnNs {room.Password} {room.MaxPlayers}");
        bool topicSent = connection.TrySendInstantOnChannel(
            channel,
            $"TOPIC {channel} :{topicRevision};{localGame}");

        if (modeSent && topicSent)
            Logger.Log($"CnCNetGameBroadcastService: host MODE/TOPIC on {channel}");
        else
            Logger.Log($"CnCNetGameBroadcastService: deferred host MODE/TOPIC on {channel} (not on channel yet).");
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
            _lastImmediatePayload = string.Empty;
            if (_playerNames.Count == 0)
                _playerNames = [ProgramConstants.PLAYERNAME];

            if (configureChannel)
                ConfigureHostChannel(connection, room);

            TrySendGameMessageLocked(_closed, force: true);
            RestartTimerLocked(TimeSpan.FromSeconds(InitialDelaySeconds));
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

            if (_connection == null || _channels == null || _room == null || !_room.IsHost || _closed)
                return;

            if (!TrySendGameMessageLocked(_closed, force: false))
                return;

            RestartTimerLocked(TimeSpan.FromSeconds(BroadcastAccelerationSeconds));
        }
    }

    public void MarkGameStarting()
    {
        lock (_sync)
        {
            if (_connection == null || _channels == null || _room == null || !_room.IsHost)
                return;

            _closed = true;
            TrySendGameMessageLocked(closed: true, force: true);
            StopTimerLocked();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_room != null && _connection != null && _channels != null && !_closed)
            {
                _closed = true;
                TrySendGameMessageLocked(closed: true, force: true);
            }

            StopTimerLocked();
            _lastImmediatePayload = string.Empty;
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

            TrySendGameMessageLocked(_closed, force: true);
        }
    }

    private bool TrySendGameMessageLocked(bool closed, bool force)
    {
        if (_connection == null || _channels == null || _room == null)
            return false;

        string broadcastChannel = CnCNetIrcChannelNames.Normalize(_channels.GameBroadcastChannel);
        if (ProgramConstants.IsInGame && _connection.GetChannelUserCount(broadcastChannel) > 500)
            return false;

        string payload = BuildGamePayload(closed);
        if (!force && payload.Equals(_lastImmediatePayload, StringComparison.Ordinal))
            return false;

        _lastImmediatePayload = payload;
        _connection.SendCtcpNotice(broadcastChannel, payload);
        Logger.Log($"CnCNetGameBroadcastService: GAME -> {broadcastChannel} ({_room.RoomName}, closed={closed})");
        return true;
    }

    private string BuildGamePayload(bool closed)
    {
        CnCNetActiveGameRoom room = _room!;
        string flags = CnCNetGameFlags.Build(_locked, room.Passworded, closed);
        string? listingChannel = _channels?.GameBroadcastChannel;
        bool legacy = _dialect?.PrefersLegacyEmit(listingChannel) == true;
        return CnCNetMultiplayerProtocol.BuildGameBroadcastPayload(
            room,
            flags,
            _playerNames,
            _mapName,
            _gameModeName,
            _mapSha1,
            useLegacyElevenField: legacy);
    }

    private string ResolveEmitRevision(string? broadcastChannel)
    {
        if (_dialect == null)
            return ProgramConstants.CNCNET_PROTOCOL_REVISION;

        return _dialect.ResolveEmitShape(broadcastChannel) == CnCNetGameBroadcastDialect.WireShape.LegacyR10
            ? CnCNetGameBroadcastDialect.LegacyRevision
            : ProgramConstants.CNCNET_PROTOCOL_REVISION;
    }

    private void RestartTimerLocked(TimeSpan firstInterval)
    {
        StopTimerLocked();
        _timer = new Timer(_ => BroadcastLocked(), null, firstInterval, TimeSpan.FromSeconds(BroadcastIntervalSeconds));
    }

    private void StopTimerLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

}
