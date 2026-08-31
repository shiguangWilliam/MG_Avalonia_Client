using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.Services;
using ClientCore;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Session;

/// <summary>
/// 遭遇战会话默认实现：槽位核心经 <see cref="GameSessionBase"/> 共享，
/// 本类保留遭遇战专有职责（设置持久化 / 默认装载）。
/// </summary>
public sealed class SkirmishSession : GameSessionBase, ISkirmishSession
{
    private IMapResource? _map;
    private GameSessionState _state = GameSessionState.Lobby;

    /// <inheritdoc />
    public LobbyPlayerMode Mode => LobbyPlayerMode.Skirmish;

    /// <summary>可变游戏选项。</summary>
    public GameOptionsState Options { get; } = new();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> LastLoadedGameOptions { get; private set; }
        = new Dictionary<string, string>();

    /// <summary>
    /// 最近一次 <c>TryLoadSkirmishSettings</c> 读到的选中地图 SHA1 与模式过滤名
    /// （DX [Settings] Map / GameModeMapFilter）。View 层用它恢复地图选中，
    /// 再按该地图容量钳制恢复的 AI 行。
    /// </summary>
    public string LastLoadedMapSha1 { get; private set; } = string.Empty;

    public string LastLoadedGameModeFilter { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override void ResetSlotsForMap(int maxPlayers)
    {
        IReadOnlyList<string> aiNames = ResolveCatalog().AiNames;
        try
        {
            Domain.Resources.IMultiplayerColorCatalog colors =
                GlobalState.Environment.EnvironmentServices.Resolve<Domain.Resources.IMultiplayerColorCatalog>();
            string playerName = GlobalState.Environment.EnvironmentServices
                .Resolve<GlobalState.Environment.IGameEnvironment>().PlayerName;
            IniUi.Lobby.DefaultAiSlotPolicy.AutoFillToMapCapacity(
                this, maxPlayers, playerName, colors, aiNames);
        }
        catch (InvalidOperationException)
        {
            LoadDefaultSkirmishSlots(maxPlayers);
        }
    }

    /// <inheritdoc />
    public IMapResource? Map
    {
        get => _map;
        set
        {
            _map = value;
            RaiseStateChanged();
        }
    }

    /// <summary>具体槽位数组（供需要 <see cref="LobbyPlayerSlot"/> 的调用方）。</summary>
    internal LobbyPlayerSlot[] Slots => CoreSlots;

    /// <inheritdoc />
    IGameOptionsState IGameSession.Options => Options;

    /// <inheritdoc />
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

    /// <summary>Legacy default: 1 human + 1 AI。</summary>
    public void LoadDefaultSkirmishSlots()
    {
        IReadOnlyList<string> aiNames = ResolveCatalog().AiNames;
        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();

        grid[0].Name = AppState.Environment.PlayerName;
        grid[0].IsHumanLocal = true;

        if (aiNames.Count > 0)
        {
            grid[1].Name = aiNames[0];
            grid[1].IsAi = true;
        }

        LobbySlotGrid.ApplyToSink(this, grid);
    }

    /// <summary>Default skirmish slot layout for a specific map MaxPlayers.</summary>
    public void LoadDefaultSkirmishSlots(int maxPlayers)
    {
        Domain.Resources.IMultiplayerColorCatalog colors;
        string playerName = AppState.Environment.PlayerName;
        try
        {
            colors = GlobalState.Environment.EnvironmentServices
                .Resolve<Domain.Resources.IMultiplayerColorCatalog>();
            playerName = GlobalState.Environment.EnvironmentServices
                .Resolve<GlobalState.Environment.IGameEnvironment>().PlayerName;
        }
        catch (InvalidOperationException)
        {
            colors = new Domain.Resources.MultiplayerColorCatalogAdapter();
        }

        IniUi.Lobby.DefaultAiSlotPolicy.AutoFillToMapCapacity(
            this,
            maxPlayers,
            playerName,
            colors,
            ResolveCatalog().AiNames);
    }

    public bool TryLoadSkirmishSettings()
    {
        ISkirmishSettingsService svc = ResolveSettingsService();
        SkirmishSettingsDto? dto = svc.TryLoad();
        if (dto == null)
            return false;

        List<LobbyPlayerSlot> grid = LobbySlotGrid.CreateEmpty();

        if (dto.Human is { } human)
        {
            human.Name = AppState.Environment.PlayerName;
            human.IsAi = false;
            ClampLoadedSlot(human, isAi: false);
            grid[0] = ToLobbySlot(human, isHumanLocal: true);
        }

        int aiSlot = 1;
        foreach (SkirmishPlayerDto ai in dto.Ais)
        {
            if (aiSlot >= LobbyPlayerSlot.MaxSlots)
                break;
            ai.IsAi = true;
            ClampLoadedSlot(ai, isAi: true);
            grid[aiSlot] = ToLobbySlot(ai, isHumanLocal: false);
            aiSlot++;
        }

        LobbySlotGrid.ApplyToSink(this, grid);
        LastLoadedGameOptions = dto.GameOptions;
        LastLoadedMapSha1 = dto.MapSha1;
        LastLoadedGameModeFilter = dto.GameModeMapFilter;
        return true;
    }

    /// <summary>
    /// DX CheckLoadedPlayerVariableBounds：历史 SideIndex/ColorIndex/TeamIndex/StartIndex
    /// 越界时回退默认，防止旧脏数据（如误存的 Spectator 索引）每次启动复现旁观局。
    /// </summary>
    private static void ClampLoadedSlot(SkirmishPlayerDto slot, bool isAi)
    {
        Services.LobbySideCatalogSnapshot sides = Services.LobbySideCatalog.GetSnapshot();
        int maxSide = sides.SpectatorSideIndex - (isAi ? 1 : 0);

        if (slot.SideIndex < 0 || slot.SideIndex > maxSide)
            slot.SideIndex = 0;

        if (slot.ColorIndex < 0)
            slot.ColorIndex = 0;

        // TeamIndex: 0 = none, 1..4 = A..D (DX ddPlayerTeams items count).
        if (slot.TeamIndex < 0 || slot.TeamIndex > 4)
            slot.TeamIndex = 0;

        // StartIndex: 0 = unset/random, 1..MaxPlayers per map; clamp to slot capacity.
        if (slot.StartIndex < 0 || slot.StartIndex > LobbyPlayerSlot.MaxSlots)
            slot.StartIndex = 0;
    }

    public void SaveSkirmishSettings()
        => SaveCore(gameOptions: null, mapSha1: null, gameModeFilter: null);

    /// <summary>
    /// Persists slots plus game-option control values (DX SkirmishLobby.SaveSettings
    /// under <c>SaveSkirmishGameOptions</c>). The snapshot is supplied by the View layer
    /// so the session stays free of UI dependencies.
    /// </summary>
    public void SaveSkirmishSettings(IReadOnlyDictionary<string, string> gameOptions)
        => SaveCore(gameOptions, mapSha1: null, gameModeFilter: null);

    /// <summary>
    /// Full save (DX parity): slots + [GameOptions] + selected map SHA1 / game-mode
    /// filter. The map identity is what makes restore correct — the restored map's
    /// MaxPlayers decides how many saved AI rows are valid.
    /// </summary>
    public void SaveSkirmishSettings(
        IReadOnlyDictionary<string, string> gameOptions,
        string mapSha1,
        string gameModeFilter)
        => SaveCore(gameOptions, mapSha1, gameModeFilter);

    private void SaveCore(
        IReadOnlyDictionary<string, string>? gameOptions,
        string? mapSha1,
        string? gameModeFilter)
    {
        ISkirmishSettingsService svc = ResolveSettingsService();
        var dto = new SkirmishSettingsDto();
        LobbyPlayerSlot? human = CoreSlots.FirstOrDefault(s => s.IsOccupied && !s.IsAi);
        if (human != null)
            dto.Human = ToDto(human, index: 0);

        int aiIndex = 0;
        foreach (LobbyPlayerSlot slot in CoreSlots.Where(s => s.IsOccupied && s.IsAi))
        {
            dto.Ais.Add(ToDto(slot, aiIndex + 1));
            aiIndex++;
        }

        if (gameOptions != null)
        {
            foreach (System.Collections.Generic.KeyValuePair<string, string> pair in gameOptions)
                dto.GameOptions[pair.Key] = pair.Value;
        }

        dto.MapSha1 = mapSha1 ?? string.Empty;
        dto.GameModeMapFilter = gameModeFilter ?? string.Empty;

        svc.Save(dto);
    }

    public void ClearSlots() => SlotSink.ClearAll();

    /// <summary>Rebuild AI rows from UI starting at first AI row (XNA CopyPlayerDataFromUI).</summary>
    public void RebuildAiRowsFromUi(int firstAiRow)
        => MultiplayerSlotLayout.RebuildAiRowsFromUi(CoreSlots, firstAiRow);

    private static ILobbyCatalogService ResolveCatalog()
    {
        try
        {
            return GlobalState.Environment.EnvironmentServices.Resolve<ILobbyCatalogService>();
        }
        catch (InvalidOperationException)
        {
            return LobbyCatalogService.Instance;
        }
    }

    private static ISkirmishSettingsService ResolveSettingsService()
    {
        try
        {
            return GlobalState.Environment.EnvironmentServices.Resolve<ISkirmishSettingsService>();
        }
        catch (InvalidOperationException)
        {
            return new SkirmishSettingsService();
        }
    }

    private static LobbyPlayerSlot ToLobbySlot(SkirmishPlayerDto dto, bool isHumanLocal)
        => new()
        {
            Name = dto.Name,
            SideIndex = dto.SideIndex,
            StartIndex = dto.StartIndex,
            ColorIndex = dto.ColorIndex,
            TeamIndex = dto.TeamIndex,
            AiLevel = dto.AiLevel,
            IsAi = dto.IsAi,
            IsHumanLocal = isHumanLocal,
        };

    private static SkirmishPlayerDto ToDto(LobbyPlayerSlot slot, int index)
        => new()
        {
            Name = slot.Name,
            SideIndex = slot.SideIndex,
            StartIndex = slot.StartIndex,
            ColorIndex = slot.ColorIndex,
            TeamIndex = slot.TeamIndex,
            AiLevel = slot.AiLevel,
            IsAi = slot.IsAi,
            Index = index,
        };
}
