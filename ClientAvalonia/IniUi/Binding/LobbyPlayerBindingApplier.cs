using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Session;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Builds runtime player slot dropdowns under PlayerOptionsPanel (aligned with GameLobbyBase.InitPlayerOptionDropdowns).</summary>
public static class LobbyPlayerBindingApplier
{
    private const int DropDownHeight = 21;
    private const int DefaultLocationX = 25;
    private const int DefaultLocationY = 24;
    private const int DefaultVerticalMargin = 12;
    private const int DefaultHorizontalMargin = 3;
    private const int DefaultCaptionY = 6;
    private const int DefaultNameWidth = 136;
    private const int DefaultSideWidth = 91;
    private const int DefaultColorWidth = 79;
    private const int DefaultTeamWidth = 46;
    private const int DefaultStartWidth = 49;
    private const int MinStartColumnWidth = 56;
    private const int MinTeamColumnWidth = 48;
    private const int MaxPlayerNameWidth = 102;
    private const int MinSideColumnWidth = 90;
    private const int MinColorColumnWidth = 66;
    private const int PanelRightReserve = 12;

    /// <summary>
    /// Session-aware 入口（Slice 5 新增）：直接吃 <see cref="SkirmishSession"/> + <see cref="ILobbyCatalogService"/>。
    /// 写操作仍走 <see cref="IPlayerSlotSink"/>（在 Apply 内回调）。
    /// </summary>
    /// <remarks>
    /// 设计理由（见 layered-architecture-progress-report.md §9.5 Slice 5）：
    /// <list type="bullet">
    /// <item>BindingApplier 不再硬依赖 <c>LobbyPlayerState</c>，只依赖 Session + Catalog 抽象。</item>
    /// <item>调用方负责传 UI 输入态（如 AllowHostPlayerOptions）；不再读 <c>LobbyPlayerState.Mode</c>。</item>
    /// <item>UI 重入保护用 <see cref="IGameSession.Revision"/>；本方法不持有 Revision，由调用方在订阅 <see cref="IGameSession.StateChanged"/> 时管理。</item>
    /// </list>
    /// </remarks>
    public static void ApplyWithSession(
        UiNodeViewModel root,
        SkirmishSession session,
        ILobbyCatalogService catalogs,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Func<CnCNetGameRoomSession?>? gameRoomProvider = null,
        Action? onSlotsMutated = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalogs);

        Apply(root, session.Player, resources, behaviors, onSlotsMutated, gameRoomProvider);
    }

    /// <summary>
    /// Phase 4 P4-1：Session-aware 主入口——吃任意 <see cref="IGameSession"/> + <see cref="LobbySessionState"/>。
    /// 设计要点（见 phase3 报告 §7）：
    /// <list type="bullet">
    /// <item><b>写路径走 sink</b>：UI dropdown 改槽位 → <see cref="IPlayerSlotSink.WriteSlot"/> 一次原子更新多字段，
    /// 触发 Session 内部 Revision bump + StateChanged。</item>
    /// <item><b>读路径用镜像</b>：渲染层（<see cref="SyncUiFromState"/> / <see cref="BuildSideItems"/> 等）仍读
    /// <paramref name="playerState"/>，它由调用方在订阅 <see cref="IGameSession.StateChanged"/> 时通过
    /// <see cref="LobbyPlayerState.SyncFromSlots"/> 同步。这样保持渲染的局部一致性。</item>
    /// <item><b>防环</b>：本方法内部对 sink 写入时设 <see cref="LobbyPlayerState.PlayerUpdatingInProgress"/>
    /// 为 true（迁移期沿用此标志，Phase 4 P4-5 改为 Revision 比对）。</item>
    /// </list>
    /// </summary>
    /// <param name="root">UI 根节点。</param>
    /// <param name="session">当前会话（Skirmish / CnCNet / LAN 均可）。</param>
    /// <param name="playerState">UI 镜像（由调用方在 StateChanged 时 SyncFromSlots 同步）。</param>
    /// <param name="uiState">UI 输入态（Mode / AllowHostPlayerOptions / LocalPlayerName / HostPlayerName）。</param>
    /// <param name="resources">资源解析器。</param>
    /// <param name="behaviors">行为注册表。</param>
    /// <param name="catalogs">大厅目录（Side / Color / Team / AI 名字）。</param>
    /// <param name="gameRoomProvider">CnCNet 房间提供委托（仅 Multiplayer 用）。</param>
    /// <param name="onSlotsMutated">每次 UI 改槽的回调（用于刷新 start markers 等）。</param>
    public static void Apply(
        UiNodeViewModel root,
        IGameSession session,
        LobbyPlayerState playerState,
        LobbySessionState uiState,
        ILobbyCatalogService catalogs,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Func<CnCNetGameRoomSession?>? gameRoomProvider = null,
        Action? onSlotsMutated = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(playerState);
        ArgumentNullException.ThrowIfNull(uiState);
        ArgumentNullException.ThrowIfNull(catalogs);

        ApplyCore(
            root,
            playerState,
            resources,
            behaviors,
            onSlotsMutated,
            gameRoomProvider,
            session.SlotSink,
            uiState);
    }

    public static void Apply(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        Apply(root, playerState, resources, behaviors, onSlotsMutated: null, gameRoomProvider: null);
    }

    /// <summary>
    /// Apply overload that lets the caller observe slot mutations performed by
    /// the dropdown event handlers. The callback fires after every UI-driven
    /// mutation (name, side, color, team, start), regardless of lobby mode, so
    /// the host window can refresh dependent UI (e.g. map start markers) without
    /// waiting for an extra user click. See auto-refresh-design.md.
    /// </summary>
    public static void Apply(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Action? onSlotsMutated)
    {
        Apply(root, playerState, resources, behaviors, onSlotsMutated, gameRoomProvider: null);
    }

    /// <summary>
    /// Full Apply with optional game-room provider (avoids CnCNetSession.Instance).
    /// </summary>
    public static void Apply(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Action? onSlotsMutated,
        Func<CnCNetGameRoomSession?>? gameRoomProvider)
    {
        ApplyCore(root, playerState, resources, behaviors, onSlotsMutated, gameRoomProvider, sink: null, uiState: null);
    }

    private static void ApplyCore(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Action? onSlotsMutated,
        Func<CnCNetGameRoomSession?>? gameRoomProvider,
        IPlayerSlotSink? sink = null,
        LobbySessionState? uiState = null)
    {
        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        HideOrphanPlayerControls(root, panel);

        if (panel.Node.Props.ContainsKey("LobbyPlayerSlotsBuilt"))
        {
            RelayoutPlayerColumns(root, panel, resources, behaviors);
            SyncUiFromState(panel, playerState);
            return;
        }

        PlayerOptionLayout layout = ReadLayout(root);
        var sideItems = BuildSideItems(playerState, resources);
        var teamItems = BuildTeamItems(playerState);
        var colorItems = BuildColorItems(resources);
        var startItems = Enumerable.Range(0, 8).Select(i => i.ToString()).ToArray();

        UiNodeViewModel? firstName = null;
        UiNodeViewModel? firstSide = null;
        UiNodeViewModel? firstColor = null;
        UiNodeViewModel? firstTeam = null;
        UiNodeViewModel? firstStart = null;

        for (int slot = LobbyPlayerSlot.MaxSlots - 1; slot >= 0; slot--)
        {
            double y = layout.LocationY + (DropDownHeight + layout.VerticalMargin) * slot;
            double x = layout.LocationX;

            string[] nameItems = LobbyPlayerSlotUiRules.BuildNameItems(slot, playerState);
            UiNodeViewModel ddName = CreateDropdown(
                $"ddPlayerName{slot}", x, y, layout.NameWidth, DropDownHeight, resources, behaviors, nameItems);
            x += layout.NameWidth + layout.HorizontalMargin;
            firstName ??= ddName;

            UiNodeViewModel ddSide = CreateSideDropdown(
                $"ddPlayerSide{slot}", x, y, layout.SideWidth, DropDownHeight, resources, behaviors, sideItems);
            x += layout.SideWidth + layout.HorizontalMargin;
            firstSide ??= ddSide;

            UiNodeViewModel ddColor = CreateColorDropdown(
                $"ddPlayerColor{slot}", x, y, layout.ColorWidth, DropDownHeight, resources, behaviors, colorItems);
            x += layout.ColorWidth + layout.HorizontalMargin;
            firstColor ??= ddColor;

            UiNodeViewModel ddTeam = CreateDropdown(
                $"ddPlayerTeam{slot}", x, y, layout.TeamWidth, DropDownHeight, resources, behaviors, teamItems);
            x += layout.TeamWidth + layout.HorizontalMargin;
            firstTeam ??= ddTeam;

            UiNodeViewModel? ddStart = null;
            if (layout.StartWidth > 0)
            {
                ddStart = CreateDropdown(
                    $"ddPlayerStart{slot}", x, y, layout.StartWidth, DropDownHeight, resources, behaviors, startItems);
                ddStart.IsVisible = true;
                firstStart ??= ddStart;
            }

            WireSlot(slot, playerState, panel, ddName, ddSide, ddColor, ddTeam, ddStart, onSlotsMutated, gameRoomProvider, sink, uiState);

            panel.Children.Add(ddName);
            panel.Children.Add(ddSide);
            panel.Children.Add(ddColor);
            panel.Children.Add(ddTeam);
            if (ddStart != null)
                panel.Children.Add(ddStart);
        }

        EnsureColumnCaptions(panel, layout, firstName, firstSide, firstColor, firstTeam, firstStart, resources, behaviors);

        panel.Node.Props["LobbyPlayerSlotsBuilt"] = true;
        SyncUiFromState(panel, playerState);
    }

    private static void RelayoutPlayerColumns(
        UiNodeViewModel root,
        UiNodeViewModel panel,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        PlayerOptionLayout layout = ReadLayout(root);
        UiNodeViewModel? firstName = null;
        UiNodeViewModel? firstSide = null;
        UiNodeViewModel? firstColor = null;
        UiNodeViewModel? firstTeam = null;
        UiNodeViewModel? firstStart = null;

        for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
        {
            double y = layout.LocationY + (DropDownHeight + layout.VerticalMargin) * slot;
            double x = layout.LocationX;

            UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
            if (ddName == null)
                continue;

            ApplyColumnGeometry(ddName, x, y, layout.NameWidth);
            x += layout.NameWidth + layout.HorizontalMargin;
            firstName ??= ddName;

            UiNodeViewModel? ddSide = FindVm(panel, $"ddPlayerSide{slot}");
            if (ddSide != null)
            {
                ApplyColumnGeometry(ddSide, x, y, layout.SideWidth);
                x += layout.SideWidth + layout.HorizontalMargin;
                firstSide ??= ddSide;
            }

            UiNodeViewModel? ddColor = FindVm(panel, $"ddPlayerColor{slot}");
            if (ddColor != null)
            {
                ApplyColumnGeometry(ddColor, x, y, layout.ColorWidth);
                x += layout.ColorWidth + layout.HorizontalMargin;
                firstColor ??= ddColor;
            }

            UiNodeViewModel? ddTeam = FindVm(panel, $"ddPlayerTeam{slot}");
            if (ddTeam != null)
            {
                ApplyColumnGeometry(ddTeam, x, y, layout.TeamWidth);
                x += layout.TeamWidth + layout.HorizontalMargin;
                firstTeam ??= ddTeam;
            }

            UiNodeViewModel? ddStart = FindVm(panel, $"ddPlayerStart{slot}");
            if (ddStart != null && layout.StartWidth > 0)
            {
                ApplyColumnGeometry(ddStart, x, y, layout.StartWidth);
                firstStart ??= ddStart;
            }
        }

        if (firstName != null && firstSide != null && firstColor != null && firstTeam != null)
            EnsureColumnCaptions(panel, layout, firstName, firstSide, firstColor, firstTeam, firstStart, resources, behaviors);
    }

    private static void ApplyColumnGeometry(UiNodeViewModel vm, double x, double y, double width)
    {
        vm.SetCanvasPosition(x, y);
        vm.Node.Props["Width"] = width;
        vm.RefreshLayout();
    }

    private static void HideOrphanPlayerControls(UiNodeViewModel root, UiNodeViewModel panel)
    {
        for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
        {
            HideIfOutsidePanel(root, panel, $"ddPlayerName{slot}");
            HideIfOutsidePanel(root, panel, $"ddPlayerSide{slot}");
            HideIfOutsidePanel(root, panel, $"ddPlayerColor{slot}");
            HideIfOutsidePanel(root, panel, $"ddPlayerTeam{slot}");
            HideIfOutsidePanel(root, panel, $"ddPlayerStart{slot}");
        }

        foreach (string captionId in new[] { "lblName", "lblSide", "lblColor", "lblTeam", "lblStart" })
            HideIfOutsidePanel(root, panel, captionId);
    }

    private static void HideIfOutsidePanel(UiNodeViewModel root, UiNodeViewModel panel, string id)
    {
        UiNodeViewModel? vm = FindVm(root, id);
        if (vm == null || ReferenceEquals(vm, panel) || IsDescendant(panel, vm))
            return;

        vm.IsVisible = false;
        vm.IsEnabled = false;
    }

    private static bool IsDescendant(UiNodeViewModel ancestor, UiNodeViewModel node)
    {
        foreach (UiNodeViewModel child in ancestor.Children)
        {
            if (ReferenceEquals(child, node) || IsDescendant(child, node))
                return true;
        }

        return false;
    }

    private static void EnsureColumnCaptions(
        UiNodeViewModel panel,
        PlayerOptionLayout layout,
        UiNodeViewModel? ddName,
        UiNodeViewModel? ddSide,
        UiNodeViewModel? ddColor,
        UiNodeViewModel? ddTeam,
        UiNodeViewModel? ddStart,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        if (ddName == null || ddSide == null || ddColor == null || ddTeam == null)
            return;

        EnsureCaption(panel, "lblName", "PLAYER", ddName.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblSide", "SIDE", ddSide.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblColor", "COLOR", ddColor.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblTeam", "TEAM", ddTeam.CanvasLeft, layout.CaptionY, resources, behaviors);

        if (ddStart != null)
            EnsureCaption(panel, "lblStart", "START", ddStart.CanvasLeft, layout.CaptionY, resources, behaviors);
    }

    private static void EnsureCaption(
        UiNodeViewModel panel,
        string id,
        string text,
        double x,
        double y,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        UiNodeViewModel? existing = FindVm(panel, id);
        if (existing != null)
        {
            existing.SetDisplayText(text);
            existing.SetCanvasPosition(x, y);
            existing.IsVisible = true;
            return;
        }

        var node = new UiNode
        {
            Id = id,
            ControlType = "XNALabel",
            TemplateKey = "DxLabel",
        };
        node.Props["CanvasLeft"] = x;
        node.Props["CanvasTop"] = y;
        node.Props["IsVisible"] = true;
        node.Props["Text"] = text;
        node.Props["FontIndex"] = 1;

        var vm = new UiNodeViewModel(node, resources, behaviors);
        panel.Children.Add(vm);
    }

    public static void SyncFromUi(UiNodeViewModel root, LobbyPlayerState playerState)
    {
        if (playerState.PlayerUpdatingInProgress)
            return;

        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
        {
            if (LobbyPlayerSlotUiRules.GetUiRowKind(slot, playerState) == LobbyPlayerRowKind.Closed)
                continue;

            UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
            UiNodeViewModel? ddSide = FindVm(panel, $"ddPlayerSide{slot}");
            UiNodeViewModel? ddColor = FindVm(panel, $"ddPlayerColor{slot}");
            UiNodeViewModel? ddTeam = FindVm(panel, $"ddPlayerTeam{slot}");
            UiNodeViewModel? ddStart = FindVm(panel, $"ddPlayerStart{slot}");
            if (ddName == null)
                continue;

            ApplySlotFromUi(
                slot,
                playerState.Slots[slot],
                playerState,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                ddStart);
        }
    }

    private static void ApplySlotFromUi(
        int slotIndex,
        LobbyPlayerSlot slot,
        LobbyPlayerState playerState,
        UiNodeViewModel ddName,
        UiNodeViewModel? ddSide,
        UiNodeViewModel? ddColor,
        UiNodeViewModel? ddTeam,
        UiNodeViewModel? ddStart)
    {
        // Phase 4 P4-1：委派给纯函数 BuildSlotUpdateFromUi，再应用到具体 slot（保持旧入口行为）。
        SlotFieldUpdate? update = BuildSlotUpdateFromUi(slotIndex, slot, playerState, ddName, ddSide, ddColor, ddTeam, ddStart);
        if (update == null)
            return;

        ApplyUpdateToSlot(slot, update.Value);
    }

    /// <summary>
    /// Phase 4 P4-1：纯函数版本——从 UI dropdown 读取意图，构造 <see cref="SlotFieldUpdate"/>，
    /// 不直接写 slot。返回 null 表示 "UI 选择被忽略（kick/ban）"。
    /// </summary>
    private static SlotFieldUpdate? BuildSlotUpdateFromUi(
        int slotIndex,
        IPlayerSlot current,
        LobbyPlayerState playerState,
        UiNodeViewModel ddName,
        UiNodeViewModel? ddSide,
        UiNodeViewModel? ddColor,
        UiNodeViewModel? ddTeam,
        UiNodeViewModel? ddStart)
    {
        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName) || LobbyPlayerSlotUiRules.IsBanSelection(ddName))
            return null;

        LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slotIndex, playerState);

        int side = ddSide?.SelectedIndex >= 0 ? ddSide.SelectedIndex : 0;
        int color = ddColor?.SelectedIndex >= 0 ? ddColor.SelectedIndex : 0;
        int team = ddTeam?.SelectedIndex >= 0 ? ddTeam.SelectedIndex : 0;
        int start = ddStart?.SelectedIndex >= 0 ? ddStart.SelectedIndex : 0;

        if (rowKind == LobbyPlayerRowKind.Human)
        {
            return new SlotFieldUpdate
            {
                SideIndex = side,
                ColorIndex = color,
                TeamIndex = team,
                StartIndex = start,
            };
        }

        string name = ReadSelectedText(ddName);
        if (string.IsNullOrWhiteSpace(name) || name == "-")
        {
            // Clear slot
            return new SlotFieldUpdate
            {
                Name = string.Empty,
                IsAi = false,
                IsHumanLocal = false,
                SideIndex = 0,
                ColorIndex = 0,
                TeamIndex = 0,
                StartIndex = 0,
                AiLevel = 0,
            };
        }

        bool isHumanLocal = name.Equals(playerState.LocalPlayerName, StringComparison.OrdinalIgnoreCase);
        bool isAi = !isHumanLocal
            && playerState.AiNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        int aiLevel = isAi ? Math.Max(0, IndexOfAiName(playerState.AiNames, name)) : 0;

        return new SlotFieldUpdate
        {
            Name = name,
            IsHumanLocal = isHumanLocal,
            IsAi = isAi,
            AiLevel = aiLevel,
            SideIndex = side,
            ColorIndex = color,
            TeamIndex = team,
            StartIndex = start,
        };
    }

    /// <summary>
    /// Phase 4 P4-1：把 <see cref="SlotFieldUpdate"/> 应用到具体 <see cref="LobbyPlayerSlot"/>（旧入口路径用）。
    /// 注意 Name="" 时被视作 "Clear" 信号——按 <see cref="LobbyPlayerSlot.Clear"/> 语义。
    /// </summary>
    private static void ApplyUpdateToSlot(LobbyPlayerSlot slot, in SlotFieldUpdate u)
    {
        if (u.Name != null && u.Name.Length == 0
            && !u.IsAi.GetValueOrDefault()
            && !u.IsHumanLocal.GetValueOrDefault())
        {
            // 等价于 slot.Clear()：Name 空字符串 + 全部字段归零
            slot.Clear();
            return;
        }

        if (u.Name != null) slot.Name = u.Name;
        if (u.SideIndex.HasValue) slot.SideIndex = u.SideIndex.Value;
        if (u.ColorIndex.HasValue) slot.ColorIndex = u.ColorIndex.Value;
        if (u.TeamIndex.HasValue) slot.TeamIndex = u.TeamIndex.Value;
        if (u.StartIndex.HasValue) slot.StartIndex = u.StartIndex.Value;
        if (u.AiLevel.HasValue) slot.AiLevel = u.AiLevel.Value;
        if (u.IsAi.HasValue) slot.IsAi = u.IsAi.Value;
        if (u.IsHumanLocal.HasValue) slot.IsHumanLocal = u.IsHumanLocal.Value;
    }

    private static void WireSlot(
        int slotIndex,
        LobbyPlayerState playerState,
        UiNodeViewModel panel,
        UiNodeViewModel ddName,
        UiNodeViewModel ddSide,
        UiNodeViewModel ddColor,
        UiNodeViewModel ddTeam,
        UiNodeViewModel? ddStart,
        Action? onSlotsMutated,
        Func<CnCNetGameRoomSession?>? gameRoomProvider,
        IPlayerSlotSink? sink = null,
        LobbySessionState? uiState = null)
    {
        CnCNetGameRoomSession? ResolveGameRoom()
            => gameRoomProvider?.Invoke()
               ?? (TryResolveCnCNetSession()?.GameRoom);

        // Phase 4 P4-1：当传入 sink 时，UI 改槽经 sink.WriteSlot 写入；否则走旧 setter 路径。
        bool useSink = sink != null;

        void SyncNameFromUi()
        {
            if (playerState.PlayerUpdatingInProgress)
                return;

            LobbyPlayerSlot previous = playerState.Slots[slotIndex].Clone();

            // Phase 4 P4-1：sink 路径下，UI 改槽 → sink.WriteSlot（一次性原子更新）。
            // 然后 playerState 镜像由调用方在订阅 session.StateChanged 时 SyncFromSlots。
            // 这里我们仍调用 ApplySlotFromUi 兼容旧渲染路径（renderer 读 playerState.Slots）。
            if (useSink)
            {
                SlotFieldUpdate? update = BuildSlotUpdateFromUi(
                    slotIndex, playerState.Slots[slotIndex], playerState,
                    ddName, ddSide, ddColor, ddTeam, ddStart);
                if (update == null)
                    return;

                // 先应用到本地镜像，保证下面的 Coordinator 调用读到新值
                ApplyUpdateToSlot(playerState.Slots[slotIndex], update.Value);
                // 再通过 sink 写入 Session 真相源（bump revision + 触发广播）
                playerState.PlayerUpdatingInProgress = true;
                try
                {
                    sink!.WriteSlot(slotIndex, update.Value);
                }
                finally
                {
                    playerState.PlayerUpdatingInProgress = false;
                }
            }
            else
            {
                ApplySlotFromUi(
                    slotIndex,
                    playerState.Slots[slotIndex],
                    playerState,
                    ddName,
                    ddSide,
                    ddColor,
                    ddTeam,
                    ddStart);
            }

            if (playerState.Mode == LobbyPlayerMode.Multiplayer)
                MultiplayerSlotCoordinator.HandleHostSlotEdit(
                    playerState,
                    slotIndex,
                    previous,
                    ddName,
                    ResolveGameRoom());
            else
                MultiplayerSlotCoordinator.HandleSkirmishNameEdit(playerState, slotIndex, ddName);

            SyncUiFromState(panel, playerState);
            onSlotsMutated?.Invoke();
        }

        void SyncOptionsFromUi()
        {
            if (playerState.PlayerUpdatingInProgress)
                return;

            if (useSink)
            {
                SlotFieldUpdate? update = BuildSlotUpdateFromUi(
                    slotIndex, playerState.Slots[slotIndex], playerState,
                    ddName, ddSide, ddColor, ddTeam, ddStart);
                if (update == null)
                    return;

                ApplyUpdateToSlot(playerState.Slots[slotIndex], update.Value);
                playerState.PlayerUpdatingInProgress = true;
                try
                {
                    sink!.WriteSlot(slotIndex, update.Value);
                }
                finally
                {
                    playerState.PlayerUpdatingInProgress = false;
                }
            }
            else
            {
                ApplySlotFromUi(
                    slotIndex,
                    playerState.Slots[slotIndex],
                    playerState,
                    ddName,
                    ddSide,
                    ddColor,
                    ddTeam,
                    ddStart);
            }

            if (playerState.Mode == LobbyPlayerMode.Multiplayer)
            {
                CnCNetGameRoomSession? gameRoom = ResolveGameRoom();
                if (playerState.AllowHostPlayerOptions)
                {
                    MultiplayerSlotCoordinator.HandleHostOptionsEdit(playerState, gameRoom);
                    SyncUiFromState(panel, playerState);
                }
                else if (playerState.Slots[slotIndex].IsHumanLocal)
                {
                    MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(playerState, slotIndex, gameRoom);
                }
            }

            // Fire for every mode (skirmish included) so the host window can refresh
            // dependent UI (map start markers, launch button) without waiting for an
            // extra user click. See auto-refresh-design.md — this is the bug fix.
            onSlotsMutated?.Invoke();
        }

        ddName.SelectionChanged += SyncNameFromUi;
        ddSide.SelectionChanged += SyncOptionsFromUi;
        ddColor.SelectionChanged += SyncOptionsFromUi;
        ddTeam.SelectionChanged += SyncOptionsFromUi;
        if (ddStart != null)
            ddStart.SelectionChanged += SyncOptionsFromUi;
    }

    private static ICnCNetSession? TryResolveCnCNetSession()
    {
        try
        {
            return GlobalState.Environment.EnvironmentServices.Resolve<ICnCNetSession>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Phase 5 P5-1：把 UI 节点同步到给定槽位（脱离 LobbyPlayerState 专用入口）。
    /// </summary>
    private static void SyncUiFromState(
        UiNodeViewModel panel,
        IReadOnlyList<IPlayerSlot> slots,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions,
        IReadOnlyList<string> aiNames,
        IReentrancyShield? shield = null)
    {
        shield?.Enter();
        try
        {
            for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
            {
                if (slot >= slots.Count)
                    break;

                IPlayerSlot state = slots[slot];
                LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slot, slots, mode, allowHostPlayerOptions);
                UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
                UiNodeViewModel? ddSide = FindVm(panel, $"ddPlayerSide{slot}");
                UiNodeViewModel? ddColor = FindVm(panel, $"ddPlayerColor{slot}");
                UiNodeViewModel? ddTeam = FindVm(panel, $"ddPlayerTeam{slot}");
                UiNodeViewModel? ddStart = FindVm(panel, $"ddPlayerStart{slot}");

                if (ddName == null)
                    continue;

                ddName.SetComboItems(LobbyPlayerSlotUiRules.BuildNameItems(slot, slots, mode, allowHostPlayerOptions, aiNames));
                ddName.IsEnabled = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(slot, slots, mode, allowHostPlayerOptions);
                ddName.SetSelectedIndexSilent(ResolveSelectedIndex(
                    ddName,
                    LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(ddName, state, aiNames)));

                bool optionsEnabled = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(slot, slots, mode, allowHostPlayerOptions);
                bool showOptions = rowKind is LobbyPlayerRowKind.Human or LobbyPlayerRowKind.Ai;

                ApplyOptionDropdown(ddSide, showOptions, optionsEnabled, state.SideIndex);
                ApplyOptionDropdown(ddColor, showOptions, optionsEnabled, state.ColorIndex);
                ApplyOptionDropdown(ddTeam, showOptions, optionsEnabled, state.TeamIndex);

                if (ddStart != null)
                {
                    ddStart.IsVisible = showOptions;
                    ApplyOptionDropdown(ddStart, showOptions, optionsEnabled, state.StartIndex);
                }
            }
        }
        finally
        {
            shield?.Exit();
        }
    }

    private static void SyncUiFromState(UiNodeViewModel panel, LobbyPlayerState playerState)
    {
        SyncUiFromState(
            panel,
            playerState.Slots,
            playerState.Mode,
            playerState.AllowHostPlayerOptions,
            playerState.AiNames,
            shield: new LobbyPlayerStateShield(playerState));
    }

    private static void ApplyOptionDropdown(
        UiNodeViewModel? dropdown,
        bool showOptions,
        bool enabled,
        int selectedIndex)
    {
        if (dropdown == null)
            return;

        dropdown.IsEnabled = enabled && showOptions;
        dropdown.SetSelectedIndexSilent(showOptions
            ? ResolveSelectedIndex(dropdown, selectedIndex)
            : -1);
    }

    /// <summary>Maps slot index to combo index; -1 clears selection (XNA unused slots).</summary>
    private static int ResolveSelectedIndex(UiNodeViewModel dropdown, int index)
    {
        if (index < 0 || dropdown.ComboItems.Count == 0)
            return -1;

        return Math.Clamp(index, 0, dropdown.ComboItems.Count - 1);
    }

    private static string ReadSelectedText(UiNodeViewModel dropdown)
    {
        if (dropdown.SelectedIndex < 0 || dropdown.SelectedIndex >= dropdown.ComboItems.Count)
            return string.Empty;

        return dropdown.ComboItems[dropdown.SelectedIndex];
    }

    private static IReadOnlyList<ComboItemViewModel> BuildSideItems(LobbyPlayerState playerState, ResourceResolver resources)
        => BuildSideItems(playerState.SideEntries, resources);

    /// <summary>
    /// Phase 5 P5-1：纯参数版本——直接吃 <see cref="IReadOnlyList{LobbySideEntry}"/>，
    /// 不再依赖 <see cref="LobbyPlayerState"/>。供 Session-aware 路径复用。
    /// </summary>
    private static IReadOnlyList<ComboItemViewModel> BuildSideItems(
        IReadOnlyList<LobbySideEntry> sideEntries,
        ResourceResolver resources)
    {
        return sideEntries.Select(entry =>
        {
            Bitmap? icon = entry.InternalName switch
            {
                LobbySideCatalog.RandomInternalName => resources.LoadFirstBitmap(["randomicon.png"]),
                LobbySideCatalog.SpectatorInternalName => resources.LoadFirstBitmap(["spectatoricon.png"]),
                _ when entry.IsRandomSelector => GameAssetResolver.LoadSideIcon(resources, entry.IconBaseName)
                    ?? resources.LoadFirstBitmap(["randomicon.png"]),
                _ => GameAssetResolver.LoadSideIcon(resources, entry.IconBaseName),
            };

            return new ComboItemViewModel
            {
                Text = entry.DisplayName,
                Tag = entry.InternalName,
                Icon = icon,
            };
        }).ToList();
    }

    private static string[] BuildTeamItems(LobbyPlayerState playerState)
        => BuildTeamItems(playerState.TeamNames);

    /// <summary>Phase 5 P5-1：纯参数版本——直接吃 <see cref="IReadOnlyList{String}"/>。</summary>
    private static string[] BuildTeamItems(IReadOnlyList<string> teamNames)
    {
        var items = new List<string> { "-" };
        items.AddRange(teamNames);
        return items.ToArray();
    }

    private static PlayerOptionLayout ReadLayout(UiNodeViewModel root)
    {
        var layout = new PlayerOptionLayout
        {
            LocationX = ReadInt(root, "PlayerOptionLocationX", DefaultLocationX),
            LocationY = ReadInt(root, "PlayerOptionLocationY", DefaultLocationY),
            VerticalMargin = ReadInt(root, "PlayerOptionVerticalMargin", DefaultVerticalMargin),
            HorizontalMargin = ReadInt(root, "PlayerOptionHorizontalMargin", DefaultHorizontalMargin),
            CaptionY = ReadInt(root, "PlayerOptionCaptionLocationY", DefaultCaptionY),
            NameWidth = ReadInt(root, "PlayerNameWidth", DefaultNameWidth),
            SideWidth = ReadInt(root, "SideWidth", DefaultSideWidth),
            ColorWidth = ReadInt(root, "ColorWidth", DefaultColorWidth),
            TeamWidth = ReadInt(root, "TeamWidth", DefaultTeamWidth),
            StartWidth = ReadInt(root, "StartWidth", DefaultStartWidth),
        };

        return NormalizeColumnWidths(layout, root);
    }

    /// <summary>MG theme INI uses wide name + narrow team/start; fit within PlayerOptionsPanel.</summary>
    private static PlayerOptionLayout NormalizeColumnWidths(PlayerOptionLayout layout, UiNodeViewModel root)
    {
        if (layout.StartWidth <= 0)
            return layout;

        int name = Math.Min(layout.NameWidth, MaxPlayerNameWidth);
        int side = layout.SideWidth;
        int color = layout.ColorWidth;
        int team = Math.Max(layout.TeamWidth, MinTeamColumnWidth);
        int start = Math.Max(layout.StartWidth, MinStartColumnWidth);
        int margin = layout.HorizontalMargin;

        int available = ResolvePanelContentWidth(root, layout.LocationX);
        int total = name + side + color + team + start + margin * 4;
        if (total > available)
        {
            int overflow = total - available;
            int takeName = Math.Min(overflow, Math.Max(0, name - 92));
            overflow -= takeName;
            name -= takeName;

            int takeSide = Math.Min(overflow, Math.Max(0, side - MinSideColumnWidth));
            overflow -= takeSide;
            side -= takeSide;

            int takeColor = Math.Min(overflow, Math.Max(0, color - MinColorColumnWidth));
            color -= takeColor;
        }

        return new PlayerOptionLayout
        {
            LocationX = layout.LocationX,
            LocationY = layout.LocationY,
            VerticalMargin = layout.VerticalMargin,
            HorizontalMargin = margin,
            CaptionY = layout.CaptionY,
            NameWidth = name,
            SideWidth = side,
            ColorWidth = color,
            TeamWidth = team,
            StartWidth = start,
        };
    }

    private static int ResolvePanelContentWidth(UiNodeViewModel root, int locationX)
    {
        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel != null && panel.Width > 0)
            return Math.Max(400, (int)panel.Width - locationX - PanelRightReserve);

        return 480;
    }

    private static int ReadInt(UiNodeViewModel root, string key, int fallback)
    {
        string? raw = root.GetIniString(key);
        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static IReadOnlyList<ComboItemViewModel> BuildColorItems(ResourceResolver resources)
    {
        var items = new List<ComboItemViewModel>
        {
            new ComboItemViewModel
            {
                Text = "Random",
                Tag = "Random",
                Icon = resources.LoadFirstBitmap(["randomicon.png"]),
            },
        };

        foreach (MultiplayerColorCatalog.MultiplayerColorEntry color in MultiplayerColorCatalog.Load())
        {
            items.Add(new ComboItemViewModel
            {
                Text = color.Name,
                Tag = color.GameColorIndex.ToString(),
                SwatchBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            });
        }

        return items;
    }

    private static UiNodeViewModel CreateColorDropdown(
        string id,
        double x,
        double y,
        double width,
        double height,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        IReadOnlyList<ComboItemViewModel> items)
        => CreateSideDropdown(id, x, y, width, height, resources, behaviors, items);

    private static UiNodeViewModel CreateSideDropdown(
        string id,
        double x,
        double y,
        double width,
        double height,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        IReadOnlyList<ComboItemViewModel> items)
    {
        var node = new UiNode
        {
            Id = id,
            ControlType = "XNAClientDropDown",
            TemplateKey = "DxLobbyComboBox",
        };
        node.Props["CanvasLeft"] = x;
        node.Props["CanvasTop"] = y;
        node.Props["Width"] = width;
        node.Props["Height"] = height;
        node.Props["IsVisible"] = true;
        node.Props["IsEnabled"] = true;

        var vm = new UiNodeViewModel(node, resources, behaviors);
        vm.SetComboItemEntries(items);
        return vm;
    }

    private static UiNodeViewModel CreateDropdown(
        string id,
        double x,
        double y,
        double width,
        double height,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        IEnumerable<string> items)
    {
        var node = new UiNode
        {
            Id = id,
            ControlType = "XNAClientDropDown",
            TemplateKey = "DxLobbyComboBox",
        };
        node.Props["CanvasLeft"] = x;
        node.Props["CanvasTop"] = y;
        node.Props["Width"] = width;
        node.Props["Height"] = height;
        node.Props["IsVisible"] = true;
        node.Props["IsEnabled"] = true;

        var vm = new UiNodeViewModel(node, resources, behaviors);
        vm.SetComboItems(items);
        return vm;
    }

    private static int IndexOfAiName(IReadOnlyList<string> aiNames, string name)
    {
        for (int i = 0; i < aiNames.Count; i++)
        {
            if (aiNames[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = FindVm(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private sealed class PlayerOptionLayout
    {
        public int LocationX { get; init; }
        public int LocationY { get; init; }
        public int VerticalMargin { get; init; }
        public int HorizontalMargin { get; init; }
        public int CaptionY { get; init; }
        public int NameWidth { get; init; }
        public int SideWidth { get; init; }
        public int ColorWidth { get; init; }
        public int TeamWidth { get; init; }
        public int StartWidth { get; init; }
    }
}

/// <summary>
/// Phase 5 P5-1：UI 重入保护抽象——BindingApplier 在程序化写 UI 时临时启用此 shield，
/// 防止 UI SelectionChanged 事件回调把"程序化刷新"误判为"用户操作"。
/// 默认实现包住 LobbyPlayerState.PlayerUpdatingInProgress（迁移期保留兼容）；
/// 未来可用 Session.Revision + 订阅时缓存 tag 实现更强版本。
/// </summary>
internal interface IReentrancyShield
{
    void Enter();
    void Exit();
}

/// <summary>包住 LobbyPlayerState.PlayerUpdatingInProgress 的兼容实现。</summary>
internal sealed class LobbyPlayerStateShield : IReentrancyShield
{
    private readonly LobbyPlayerState _state;
    public LobbyPlayerStateShield(LobbyPlayerState state) { _state = state; }
    public void Enter() => _state.PlayerUpdatingInProgress = true;
    public void Exit() => _state.PlayerUpdatingInProgress = false;
}
