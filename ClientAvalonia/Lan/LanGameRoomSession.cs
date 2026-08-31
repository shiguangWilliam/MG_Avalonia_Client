using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.Lan;

/// <summary>
/// LAN game-room session: skirmish-shaped slots + host metadata (no tunnel/IRC).
/// Network I/O lives in <see cref="LanGameRoomTransport"/>; this type owns lobby state.
/// </summary>
public sealed class LanGameRoomSession : GameSessionBase, ILANGameSession
{
    private IMapResource? _map;
    private GameSessionState _state = GameSessionState.Lobby;

    public LanGameRoomSession(string hostName, bool isHost, string localPlayerName)
    {
        HostName = hostName;
        IsHost = isHost;
        LocalPlayerName = localPlayerName;
        UniqueGameId = GenerateGameId();
        Locked = false;

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
    public GameOptionsState Options { get; } = new();
    IGameOptionsState IGameSession.Options => Options;

    internal LobbyPlayerSlot[] Slots => CoreSlots;

    public IMapResource? Map
    {
        get => _map;
        set
        {
            _map = value;
            RaiseStateChanged();
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
            RaiseStateChanged();
        }
    }

    public override void ResetSlotsForMap(int maxPlayers)
    {
        if (!IsHost)
            return;

        int humanCount = CoreSlots.Count(s => s.IsOccupied && !s.IsAi);
        int maxSlots = Math.Clamp(maxPlayers, 1, LobbyPlayerSlot.MaxSlots);
        if (humanCount > maxSlots)
            humanCount = maxSlots;

        var preserved = new List<LobbyPlayerSlot>();
        for (int i = 0; i < CoreSlots.Length && preserved.Count < humanCount; i++)
        {
            if (CoreSlots[i].IsOccupied && !CoreSlots[i].IsAi)
                preserved.Add(CoreSlots[i].Clone());
        }

        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();
        for (int i = 0; i < preserved.Count && i < grid.Count; i++)
        {
            LobbyPlayerSlot kept = preserved[i];
            LobbyPlayerSlot slot = grid[i];
            slot.Name = kept.Name;
            slot.IsHumanLocal = kept.IsHumanLocal;
            slot.IsAi = false;
            slot.SideIndex = kept.SideIndex;
            slot.ColorIndex = kept.ColorIndex;
            slot.TeamIndex = kept.TeamIndex;
            slot.StartIndex = kept.StartIndex;
            slot.Ready = kept.Ready;
        }

        LobbySlotGrid.ApplyToSink(this, grid);
    }

    public IReadOnlyList<string> OccupiedHumanNames()
        => CoreSlots.Where(s => s.IsOccupied && !s.IsAi)
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

    /// <summary>入站 POPTS：经 sink 写入，不触发 <see cref="OnLocalSlotMutated"/>（反回声）。</summary>
    public void ApplyRemotePlayerOptions(IReadOnlyList<LanPlayerOptionRow> rows)
    {
        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();
        int index = 0;
        foreach (LanPlayerOptionRow row in rows)
        {
            if (index >= grid.Count)
                break;

            LobbyPlayerSlot slot = grid[index++];
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

        SlotSink.CopyFrom(grid);
    }

    public IReadOnlyList<LanPlayerOptionRow> SnapshotPlayerOptions()
    {
        var rows = new List<LanPlayerOptionRow>();
        foreach (LobbyPlayerSlot slot in CoreSlots.Where(s => s.IsOccupied))
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
        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();
        grid[0].Name = localPlayerName;
        grid[0].IsHumanLocal = true;
        grid[0].Ready = true;
        LobbySlotGrid.ApplyToSink(this, grid);
    }

    private static int GenerateGameId()
    {
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
