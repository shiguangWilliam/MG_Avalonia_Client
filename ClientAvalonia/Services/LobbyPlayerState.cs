using ClientAvalonia.Domain;
using ClientAvalonia.Session;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

// Phase 5 P5-4: LobbyPlayerMode enum 已迁移到 ClientAvalonia.Session 命名空间
// （Session/LobbyPlayerMode.cs）。本文件通过上面的 using ClientAvalonia.Session 引用。

/// <summary>Skirmish / in-game lobby player slots (8 rows), aligned with GameLobbyBase.</summary>
public sealed class LobbyPlayerState
{
    public const string SkirmishSettingsRelativePath = "Client/SkirmishSettings.ini";

    /// <summary>
    /// 反向引用 owner <see cref="LobbySessionState"/>（Phase 2 P2-1）。
    /// 由 <see cref="LobbySessionState"/> 构造时设置。如果为 null（独立 new 出来的，
    /// 比如单测），则下面 5 个 UI 字段独立存储。
    /// 设置后：<see cref="Mode"/> / <see cref="AllowHostPlayerOptions"/> / <see cref="LocalPlayerName"/> /
    /// <see cref="HostPlayerName"/> / <see cref="PlayerUpdatingInProgress"/> 的读写会**双向转发**到 owner，
    /// 消除"双份真相"问题。
    /// </summary>
    internal LobbySessionState? Owner { get; set; }

    // 当 Owner == null 时使用的本地后备字段
    private LobbyPlayerMode _mode = LobbyPlayerMode.Skirmish;
    private bool _allowHostPlayerOptions = true;
    private string _localPlayerName = ProgramConstants.PLAYERNAME;
    private string _hostPlayerName = ProgramConstants.PLAYERNAME;
    private bool _playerUpdatingInProgress;

    public LobbyPlayerSlot[] Slots { get; } = Enumerable.Range(0, LobbyPlayerSlot.MaxSlots)
        .Select(_ => new LobbyPlayerSlot())
        .ToArray();

    public IReadOnlyList<string> SideNames { get; private set; } = [];

    public IReadOnlyList<LobbySideEntry> SideEntries { get; private set; } = [];

    public IReadOnlyList<string> AiNames { get; private set; } = [];

    public IReadOnlyList<string> TeamNames { get; private set; } = [];

    /// <summary>
    /// UI 选择的玩家模式。如果 <see cref="Owner"/> 已设置，转发到 <see cref="LobbySessionState.UIMode"/>；
    /// 否则用本地后备字段。
    /// </summary>
    public LobbyPlayerMode Mode
    {
        get => Owner?.UIMode ?? _mode;
        set
        {
            if (Owner != null) Owner.UIMode = value;
            else _mode = value;
        }
    }

    public bool AllowHostPlayerOptions
    {
        get => Owner?.AllowHostPlayerOptions ?? _allowHostPlayerOptions;
        set
        {
            if (Owner != null) Owner.AllowHostPlayerOptions = value;
            else _allowHostPlayerOptions = value;
        }
    }

    public string LocalPlayerName
    {
        get => Owner?.LocalPlayerName ?? _localPlayerName;
        set
        {
            if (Owner != null) Owner.LocalPlayerName = value;
            else _localPlayerName = value;
        }
    }

    public string HostPlayerName
    {
        get => Owner?.HostPlayerName ?? _hostPlayerName;
        set
        {
            if (Owner != null) Owner.HostPlayerName = value;
            else _hostPlayerName = value;
        }
    }

    /// <summary>Suppresses UI→state sync while applying CopyPlayerDataToUI (XNA PlayerUpdatingInProgress).</summary>
    [Obsolete("Phase 2 P2-2: 改用 IGameSession.Revision 比对来检测 UI 重入。Phase 4 完成 BindingApplier + MainWindow Revision 切换；Phase 5 删除此字段。")]
    public bool PlayerUpdatingInProgress
    {
        get => Owner?.PlayerUpdatingInProgress ?? _playerUpdatingInProgress;
        set
        {
            if (Owner != null) Owner.PlayerUpdatingInProgress = value;
            else _playerUpdatingInProgress = value;
        }
    }

    public void LoadCatalogs(bool includeSpectator = true)
    {
        // 委托给 ILobbyCatalogService（Slice 2）。优先用 EnvironmentServices 注册的实例；
        // 不可用时退回单例。这样 LobbyPlayerState 不再持有目录加载逻辑。
        ILobbyCatalogService catalog;
        try
        {
            catalog = GlobalState.Environment.EnvironmentServices.Resolve<ILobbyCatalogService>();
        }
        catch (InvalidOperationException)
        {
            catalog = LobbyCatalogService.Instance;
        }

        catalog.Reload(includeSpectator);
        SideEntries = catalog.SideEntries;
        SideNames = catalog.SideNames;
        AiNames = catalog.AiNames;
        TeamNames = catalog.TeamNames;
    }

    public void LoadDefaults(bool includeSpectator = true)
    {
        LoadCatalogs(includeSpectator);
        LoadDefaultSkirmishSlots();
    }

    /// <summary>
    /// Legacy default: 1 human + 1 AI. Retained for compatibility with callers
    /// that do not yet know the active map's MaxPlayers. New callers should
    /// prefer <see cref="LoadDefaultSkirmishSlots(int)"/>.
    /// </summary>
    public void LoadDefaultSkirmishSlots()
    {
        ClearSlots();
        Slots[0].Name = ProgramConstants.PLAYERNAME;
        Slots[0].IsHumanLocal = true;
        Slots[0].SideIndex = 0;
        Slots[0].ColorIndex = 0;
        Slots[0].TeamIndex = 0;
        Slots[0].StartIndex = 0;

        if (AiNames.Count == 0)
            return;

        Slots[1].Name = AiNames[0];
        Slots[1].IsAi = true;
        Slots[1].AiLevel = 0;
        Slots[1].SideIndex = 0;
        Slots[1].ColorIndex = 0;
        Slots[1].TeamIndex = 0;
        Slots[1].StartIndex = 0;
    }

    /// <summary>
    /// Default skirmish slot layout for a specific map: 1 local human +
    /// (maxPlayers - 1) default AIs. Delegates to <c>DefaultAiSlotPolicy</c>.
    /// </summary>
    public void LoadDefaultSkirmishSlots(int maxPlayers)
    {
        Domain.Resources.IMultiplayerColorCatalog colors;
        string playerName = ProgramConstants.PLAYERNAME;
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
            new Session.SkirmishSession(this),
            maxPlayers,
            playerName,
            colors,
            AiNames);
    }

    public int FirstEmptySlotIndex()
        => ((IReadOnlyList<IPlayerSlot>)Slots).FirstEmptySlotIndex();

    public int OccupiedSlotCount => Slots.OccupiedSlotCount();

    public void ClearSlots()
    {
        foreach (LobbyPlayerSlot slot in Slots)
        {
            slot.Name = string.Empty;
            slot.IsAi = false;
            slot.IsHumanLocal = false;
            slot.SideIndex = 0;
            slot.ColorIndex = 0;
            slot.StartIndex = 0;
            slot.TeamIndex = 0;
            slot.AiLevel = 0;
        }
    }

    /// <summary>
    /// Phase 2 P2-5：从任意 <see cref="IPlayerSlot"/> 列表（通常是
    /// <see cref="IGameSession.PlayerSlots"/>）投影到本类的 Slots 数组。
    ///
    /// 设计理由：迁移期 LobbyPlayerBindingApplier 仍读 LobbyPlayerState.Slots，
    /// 但真相源已是 Session.PlayerSlots。本方法把 Session 的最新状态投影到 UI 绑定数组，
    /// 让 BindingApplier 无需知道 Session 抽象——MainWindow 在 ApplyPlayersFromNetwork
    /// 之后调用一次即可。
    /// </summary>
    /// <param name="source">真相源槽位（长度通常等于 MaxSlots）。</param>
    public void SyncFromSlots(IReadOnlyList<IPlayerSlot> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        for (int i = 0; i < Slots.Length; i++)
        {
            if (i >= source.Count)
            {
                Slots[i].Clear();
                continue;
            }

            IPlayerSlot src = source[i];
            LobbyPlayerSlot dst = Slots[i];
            dst.Name = src.Name;
            dst.IsAi = src.IsAi;
            dst.IsHumanLocal = src.IsHumanLocal;
            dst.SideIndex = src.SideIndex;
            dst.ColorIndex = src.ColorIndex;
            dst.StartIndex = src.StartIndex;
            dst.TeamIndex = src.TeamIndex;
            dst.AiLevel = src.AiLevel;
        }
    }

    public int HumanCount => HumanRowCount;

    public int AiCount => AiRowCount;

    /// <summary>Consecutive human rows from slot 0 (XNA Players list). 委托 GameSessionExtensions.</summary>
    public int HumanRowCount => ((IReadOnlyList<IPlayerSlot>)Slots).HumanRowCount();

    /// <summary>Consecutive AI rows after humans (XNA AIPlayers list). 委托 GameSessionExtensions.</summary>
    public int AiRowCount => ((IReadOnlyList<IPlayerSlot>)Slots).AiRowCount();

    public int OccupiedRowCount => ((IReadOnlyList<IPlayerSlot>)Slots).OccupiedRowCount();

    /// <summary>Repack humans (host first) + AIs into consecutive rows (XNA Players + AIPlayers).</summary>
    public void RepopulateRows(IReadOnlyList<LobbyPlayerSlot> humans, IReadOnlyList<LobbyPlayerSlot> ais)
    {
        ClearSlots();
        int row = 0;
        foreach (LobbyPlayerSlot human in humans)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = human.Clone();
            row++;
        }

        foreach (LobbyPlayerSlot ai in ais)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = ai.Clone();
            row++;
        }
    }

    /// <summary>Host is always Players[0] in DXMain; ensure row 0 when hosting.</summary>
    [Obsolete("Phase 2 P2-5: 改用 ICnCNetGameSession.ReorderHostFirst。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public void EnsureHostAsFirstHuman(string hostName, string localNick)
    {
        hostName = NormalizeNick(hostName, localNick);

        var humans = new List<LobbyPlayerSlot>();
        var ais = new List<LobbyPlayerSlot>();
        foreach (LobbyPlayerSlot slot in Slots)
        {
            if (!slot.IsOccupied)
                continue;

            if (slot.IsAi)
                ais.Add(slot.Clone());
            else
                humans.Add(slot.Clone());
        }

        LobbyPlayerSlot host = humans.FirstOrDefault(h =>
            h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase))
            ?? new LobbyPlayerSlot
            {
                Name = hostName,
                SideIndex = 0,
                ColorIndex = 0,
                TeamIndex = 0,
                StartIndex = 0,
            };

        host.IsAi = false;
        host.IsHumanLocal = host.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);

        humans.RemoveAll(h => h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase));
        humans.Insert(0, host);
        RepopulateRows(humans, ais);
    }

    [Obsolete("Phase 2 P2-5: 改用 ICnCNetGameSession.MarkLocalHuman。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public void MarkLocalHuman(string localNick)
    {
        foreach (LobbyPlayerSlot slot in Slots)
        {
            if (slot.IsOccupied && !slot.IsAi)
                slot.IsHumanLocal = slot.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeNick(string primary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();

        return ProgramConstants.PLAYERNAME;
    }

    public LobbyPlayerRowKind GetRowKind(int slotIndex)
        => ((IReadOnlyList<IPlayerSlot>)Slots).GetRowKind(slotIndex);

    /// <summary>Rebuild AI rows from UI starting at first AI row (XNA CopyPlayerDataFromUI).</summary>
    public void RebuildAiRowsFromUi(int firstAiRow)
    {
        var preserved = new List<LobbyPlayerSlot>();
        for (int i = firstAiRow; i < Slots.Length; i++)
        {
            LobbyPlayerSlot slot = Slots[i];
            if (slot.IsOccupied && slot.IsAi)
                preserved.Add(slot.Clone());
        }

        for (int i = firstAiRow; i < Slots.Length; i++)
            Slots[i].Clear();

        int row = firstAiRow;
        foreach (LobbyPlayerSlot ai in preserved)
        {
            if (row >= Slots.Length)
                break;

            Slots[row] = ai;
            row++;
        }
    }

    public bool TryLoadSkirmishSettings()
    {
        // 委托 ISkirmishSettingsService（Slice 3）：IO 归 Service，状态归 LobbyPlayerState。
        ISkirmishSettingsService svc;
        try
        {
            svc = GlobalState.Environment.EnvironmentServices.Resolve<ISkirmishSettingsService>();
        }
        catch (InvalidOperationException)
        {
            svc = new SkirmishSettingsService();
        }

        SkirmishSettingsDto? dto = svc.TryLoad();
        if (dto == null)
            return false;

        ClearSlots();

        if (dto.Human is { } human)
        {
            human.Name = ProgramConstants.PLAYERNAME; // 强制本机玩家名
            human.IsAi = false;
            Slots[0] = ToLobbySlot(human, isHumanLocal: true);
        }

        int aiSlot = 1;
        foreach (SkirmishPlayerDto ai in dto.Ais)
        {
            if (aiSlot >= LobbyPlayerSlot.MaxSlots)
                break;
            ai.IsAi = true;
            Slots[aiSlot] = ToLobbySlot(ai, isHumanLocal: false);
            aiSlot++;
        }

        return true;
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

    public void SaveSkirmishSettings()
    {
        // 委托 ISkirmishSettingsService（Slice 3）。
        ISkirmishSettingsService svc;
        try
        {
            svc = GlobalState.Environment.EnvironmentServices.Resolve<ISkirmishSettingsService>();
        }
        catch (InvalidOperationException)
        {
            svc = new SkirmishSettingsService();
        }

        var dto = new SkirmishSettingsDto();
        LobbyPlayerSlot? human = Slots.FirstOrDefault(s => s.IsOccupied && !s.IsAi);
        if (human != null)
            dto.Human = ToDto(human, index: 0);

        int aiIndex = 0;
        foreach (LobbyPlayerSlot slot in Slots.Where(s => s.IsOccupied && s.IsAi))
        {
            dto.Ais.Add(ToDto(slot, aiIndex + 1));
            aiIndex++;
        }

        svc.Save(dto);
    }

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

    public static bool TryParsePlayerLine(string raw, out LobbyPlayerSlot? slot)
    {
        slot = null;
        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 7)
            return false;

        slot = new LobbyPlayerSlot
        {
            Name = parts[0],
            SideIndex = int.TryParse(parts[1], out int side) ? side : 0,
            StartIndex = int.TryParse(parts[2], out int start) ? start : 0,
            ColorIndex = int.TryParse(parts[3], out int color) ? color : 0,
            TeamIndex = int.TryParse(parts[4], out int team) ? team : 0,
            AiLevel = int.TryParse(parts[5], out int ai) ? ai : 0,
            IsAi = bool.TryParse(parts[6], out bool isAi) && isAi,
        };
        return !string.IsNullOrWhiteSpace(slot.Name);
    }

    public static string FormatPlayerLine(LobbyPlayerSlot slot, int index)
        => string.Join(',', slot.Name, slot.SideIndex, slot.StartIndex, slot.ColorIndex, slot.TeamIndex, slot.AiLevel, slot.IsAi, index);

    /// <summary>
    /// Phase 3 P3-2：标记为已过时——逻辑迁到 <see cref="LobbyPlayerHouseResolver.HouseHandicapFromAiLevel"/>。
    /// 保留委托以兼容现有调用方（CnCNetMultiplayerSpawnWriter 等）。
    /// </summary>
    [Obsolete("Phase 3 P3-2: 改用 LobbyPlayerHouseResolver.HouseHandicapFromAiLevel。Phase 4 完成 Session-aware 路径；Phase 5 删除。")]
    public static int HouseHandicapFromAiLevel(int aiLevel)
        => LobbyPlayerHouseResolver.HouseHandicapFromAiLevel(aiLevel);
}
