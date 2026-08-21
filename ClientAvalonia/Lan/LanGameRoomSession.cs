using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.Lan;

/// <summary>
/// LAN game-room session: skirmish-shaped slots + host metadata (no tunnel/IRC).
/// Network I/O lives in <see cref="LanGameRoomTransport"/>; this type owns lobby state.
/// </summary>
public sealed class LanGameRoomSession : ILANGameSession
{
    private readonly LobbyPlayerSlot[] _slots = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
        .Select(_ => new LobbyPlayerSlot())
        .ToArray();

    private IMapResource? _map;
    private GameSessionState _state = GameSessionState.Lobby;
    private long _revision;

    public LanGameRoomSession(string hostName, bool isHost, string localPlayerName)
    {
        HostName = hostName;
        IsHost = isHost;
        LocalPlayerName = localPlayerName;
        UniqueGameId = GenerateGameId();
        Locked = false;

        SlotSink = new LobbyPlayerSlotSink(
            () => _slots,
            () => BumpRevision());

        if (isHost)
            SeedHostSlots(localPlayerName);
    }

    public string HostName { get; private set; }
    public bool IsHost { get; }
    public string LocalPlayerName { get; }
    public int UniqueGameId { get; set; }
    public bool Locked { get; set; }
    public bool IsLoadedGame { get; set; }
    public string LoadedGameId { get; set; } = "0";

    public LobbyPlayerMode Mode => LobbyPlayerMode.Multiplayer;
    public long Revision => _revision;
    public GameOptionsState Options { get; } = new();
    IGameOptionsState IGameSession.Options => Options;
    public IPlayerSlotSink SlotSink { get; }
    public IReadOnlyList<IPlayerSlot> PlayerSlots => _slots;
    internal LobbyPlayerSlot[] Slots => _slots;

    public IMapResource? Map
    {
        get => _map;
        set
        {
            _map = value;
            BumpRevision();
        }
    }

    public GameSessionState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;
            _state = value;
            BumpRevision();
        }
    }

    public event Action? StateChanged;

    public void NotifyStateChanged() => StateChanged?.Invoke();

    public void ResetSlotsForMap(int maxPlayers)
    {
        if (!IsHost)
            return;

        // Mirror CnCNet semantics: keep every human, drop AI so the host re-picks
        // per the new map capacity. AutoFillToMapCapacity is skirmish-only
        // (it wipes remote humans too).
        int humanCount = _slots.Count(s => s.IsOccupied && !s.IsAi);
        int maxSlots = Math.Clamp(maxPlayers, 1, LobbyPlayerSlot.MaxSlots);
        if (humanCount > maxSlots)
            humanCount = maxSlots;

        // Compact humans to the front, then clear everything else (AI rows and overflow).
        var preserved = new List<LobbyPlayerSlot>();
        for (int i = 0; i < _slots.Length && preserved.Count < humanCount; i++)
        {
            if (_slots[i].IsOccupied && !_slots[i].IsAi)
                preserved.Add(_slots[i].Clone());
        }

        for (int i = 0; i < _slots.Length; i++)
            ClearSlot(_slots[i]);

        for (int i = 0; i < preserved.Count && i < _slots.Length; i++)
        {
            LobbyPlayerSlot kept = preserved[i];
            LobbyPlayerSlot slot = _slots[i];
            slot.Name = kept.Name;
            slot.IsHumanLocal = kept.IsHumanLocal;
            slot.IsAi = false;
            slot.AiLevel = 0;
            slot.SideIndex = kept.SideIndex;
            slot.ColorIndex = kept.ColorIndex;
            slot.TeamIndex = kept.TeamIndex;
            slot.StartIndex = kept.StartIndex;
            slot.Ready = kept.Ready;
        }

        BumpRevision();
    }

    public IReadOnlyList<string> OccupiedHumanNames()
        => _slots.Where(s => s.IsOccupied && !s.IsAi)
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

    public void ApplyRemotePlayerOptions(IReadOnlyList<LanPlayerOptionRow> rows)
    {
        for (int i = 0; i < _slots.Length; i++)
            ClearSlot(_slots[i]);

        int index = 0;
        foreach (LanPlayerOptionRow row in rows)
        {
            if (index >= _slots.Length)
                break;

            LobbyPlayerSlot slot = _slots[index++];
            slot.Name = row.Name;
            slot.SideIndex = row.SideId;
            slot.ColorIndex = row.ColorId;
            slot.StartIndex = row.StartingLocation;
            slot.TeamIndex = row.TeamId;
            if (row.AiLevel < 0)
            {
                slot.IsAi = false;
                slot.IsHumanLocal = row.Name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase);
                slot.Ready = row.Ready != 0;
            }
            else
            {
                slot.IsAi = true;
                slot.AiLevel = row.AiLevel;
            }
        }

        BumpRevision();
    }

    public IReadOnlyList<LanPlayerOptionRow> SnapshotPlayerOptions()
    {
        var rows = new List<LanPlayerOptionRow>();
        foreach (LobbyPlayerSlot slot in _slots.Where(s => s.IsOccupied))
        {
            rows.Add(new LanPlayerOptionRow(
                slot.Name,
                slot.SideIndex,
                slot.ColorIndex,
                slot.StartIndex,
                slot.TeamIndex,
                slot.Ready ? 1 : 0,
                slot.IsHumanLocal ? "127.0.0.1" : string.Empty,
                slot.IsAi ? slot.AiLevel : -1));
        }

        return rows;
    }

    private void SeedHostSlots(string localPlayerName)
    {
        ClearAndSeed(localPlayerName);
        BumpRevision();
    }

    private void ClearAndSeed(string localPlayerName)
    {
        foreach (LobbyPlayerSlot slot in _slots)
            ClearSlot(slot);

        _slots[0].Name = localPlayerName;
        _slots[0].IsHumanLocal = true;
        _slots[0].SideIndex = 0;
        _slots[0].ColorIndex = 0;
        _slots[0].TeamIndex = 0;
        _slots[0].StartIndex = 0;
        _slots[0].Ready = true;
    }

    private static void ClearSlot(LobbyPlayerSlot slot)
    {
        slot.Name = string.Empty;
        slot.IsHumanLocal = false;
        slot.IsAi = false;
        slot.AiLevel = 0;
        slot.SideIndex = 0;
        slot.ColorIndex = 0;
        slot.TeamIndex = 0;
        slot.StartIndex = 0;
        slot.Ready = false;
    }

    private void BumpRevision()
    {
        _revision++;
        StateChanged?.Invoke();
    }

    private static IReadOnlyList<string> ResolveAiNames()
    {
        try
        {
            return LobbyCatalogService.Instance.AiNames;
        }
        catch
        {
            return AppState.Environment.AiPlayerNames;
        }
    }

    private static int GenerateGameId()
    {
        // DX MultiplayerGameLobby.GenerateGameID-ish: time-based int.
        DateTime now = DateTime.Now;
        return now.Day * 1000000 + now.Month * 10000 + now.Hour * 100 + now.Minute;
    }
}

/// <summary>One POPTS entry (DX LANGameLobby 8 fields).</summary>
public readonly record struct LanPlayerOptionRow(
    string Name,
    int SideId,
    int ColorId,
    int StartingLocation,
    int TeamId,
    int Ready,
    string Ip,
    int AiLevel);

public static class LanPlayerOptionsCodec
{
    public static string Format(IReadOnlyList<LanPlayerOptionRow> rows)
    {
        var parts = new List<string>();
        foreach (LanPlayerOptionRow row in rows)
        {
            parts.Add(string.Join(
                LanProtocol.DataSep.ToString(),
                row.Name,
                row.SideId,
                row.ColorId,
                row.StartingLocation,
                row.TeamId,
                row.Ready,
                row.Ip,
                row.AiLevel));
        }

        return string.Join(LanProtocol.MessageSep.ToString(), parts);
    }

    public static IReadOnlyList<LanPlayerOptionRow> Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        var rows = new List<LanPlayerOptionRow>();
        foreach (string chunk in payload.Split(LanProtocol.MessageSep, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = chunk.Split(LanProtocol.DataSep);
            if (f.Length < 8)
                continue;

            rows.Add(new LanPlayerOptionRow(
                f[0],
                ParseInt(f[1]),
                ParseInt(f[2]),
                ParseInt(f[3]),
                ParseInt(f[4]),
                ParseInt(f[5]),
                f[6],
                ParseInt(f[7])));
        }

        return rows;
    }

    private static int ParseInt(string value)
        => int.TryParse(value, out int n) ? n : 0;
}
