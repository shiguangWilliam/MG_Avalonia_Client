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
    /// Session-aware 入口：直接吃 <see cref="SkirmishSession"/> + <see cref="ILobbyCatalogService"/>。
    /// </summary>
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

        var ui = new LobbySessionState();
        LobbyPlayerSlotUiRules.ConfigureForSkirmish(ui, session);
        Apply(root, session, ui, catalogs, resources, behaviors, gameRoomProvider, onSlotsMutated);
    }

    /// <summary>
    /// Session-aware 主入口——读 <see cref="IGameSession.PlayerSlots"/>，写 <see cref="IPlayerSlotSink"/>。
    /// </summary>
    public static void Apply(
        UiNodeViewModel root,
        IGameSession session,
        LobbySessionState uiState,
        ILobbyCatalogService catalogs,
        ResourceResolver resources,
        BehaviorRegistry behaviors,
        Func<CnCNetGameRoomSession?>? gameRoomProvider = null,
        Action? onSlotsMutated = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(uiState);
        ArgumentNullException.ThrowIfNull(catalogs);

        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        HideOrphanPlayerControls(root, panel);

        IReadOnlyList<IPlayerSlot> slots = session.PlayerSlots;
        LobbyPlayerMode mode = uiState.UIMode;
        bool allowHost = uiState.AllowHostPlayerOptions;
        IReadOnlyList<string> aiNames = catalogs.AiNames;
        IReadOnlyList<LobbySideEntry> sideEntries = catalogs.SideEntries;
        IReadOnlyList<string> teamNames = catalogs.TeamNames;
        var shield = new FlagReentrancyShield();

        if (panel.Node.Props.ContainsKey("LobbyPlayerSlotsBuilt"))
        {
            RelayoutPlayerColumns(root, panel, resources, behaviors);
            SyncUiFromState(panel, slots, mode, allowHost, aiNames, shield);
            return;
        }

        PlayerOptionLayout layout = ReadLayout(root);
        var sideItems = BuildSideItems(sideEntries, resources);
        var teamItems = BuildTeamItems(teamNames);
        var colorItems = BuildColorItems(resources);
        // Combo SelectedIndex is 0-7; labels are 1-8 (DX GameLobbyBase ddPlayerStart).
        // Slot.StartIndex stays 1-based (0 = unset/random), matching map markers.
        var startItems = Enumerable.Range(1, 8).Select(i => i.ToString()).ToArray();

        UiNodeViewModel? firstName = null;
        UiNodeViewModel? firstSide = null;
        UiNodeViewModel? firstColor = null;
        UiNodeViewModel? firstTeam = null;
        UiNodeViewModel? firstStart = null;

        for (int slot = LobbyPlayerSlot.MaxSlots - 1; slot >= 0; slot--)
        {
            double y = layout.LocationY + (DropDownHeight + layout.VerticalMargin) * slot;
            double x = layout.LocationX;

            string[] nameItems = LobbyPlayerSlotUiRules.BuildNameItems(slot, slots, mode, allowHost, aiNames);
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

            WireSlot(
                slot,
                panel,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                ddStart,
                onSlotsMutated,
                gameRoomProvider,
                session.SlotSink,
                uiState,
                session,
                catalogs,
                shield);

            panel.Children.Add(ddName);
            panel.Children.Add(ddSide);
            panel.Children.Add(ddColor);
            panel.Children.Add(ddTeam);
            if (ddStart != null)
                panel.Children.Add(ddStart);
        }

        EnsureColumnCaptions(panel, layout, firstName, firstSide, firstColor, firstTeam, firstStart, resources, behaviors);

        panel.Node.Props["LobbyPlayerSlotsBuilt"] = true;
        SyncUiFromState(panel, slots, mode, allowHost, aiNames, shield);
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

    /// <summary>
    /// Session-aware：把 UI 写回 <see cref="IGameSession.SlotSink"/>。
    /// </summary>
    public static void SyncFromUi(
        UiNodeViewModel root,
        IGameSession session,
        LobbySessionState uiState,
        IReadOnlyList<string> aiNames)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(uiState);
        ArgumentNullException.ThrowIfNull(aiNames);

        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots && slot < session.PlayerSlots.Count; slot++)
        {
            if (LobbyPlayerSlotUiRules.GetUiRowKind(
                    slot, session.PlayerSlots, uiState.UIMode, uiState.AllowHostPlayerOptions)
                == LobbyPlayerRowKind.Closed)
            {
                continue;
            }

            UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
            UiNodeViewModel? ddSide = FindVm(panel, $"ddPlayerSide{slot}");
            UiNodeViewModel? ddColor = FindVm(panel, $"ddPlayerColor{slot}");
            UiNodeViewModel? ddTeam = FindVm(panel, $"ddPlayerTeam{slot}");
            UiNodeViewModel? ddStart = FindVm(panel, $"ddPlayerStart{slot}");
            if (ddName == null)
                continue;

            SlotFieldUpdate? update = BuildSlotUpdateFromUi(
                slot,
                session.PlayerSlots[slot],
                session.PlayerSlots,
                uiState.LocalPlayerName,
                aiNames,
                uiState.UIMode,
                uiState.AllowHostPlayerOptions,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                ddStart);
            if (update == null)
                continue;

            session.SlotSink.WriteSlot(slot, update.Value);
        }
    }

    /// <summary>
    /// 纯函数版本——从 UI dropdown 读取意图，构造 <see cref="SlotFieldUpdate"/>。
    /// 返回 null 表示 "UI 选择被忽略（kick/ban）"。
    /// </summary>
    private static SlotFieldUpdate? BuildSlotUpdateFromUi(
        int slotIndex,
        IPlayerSlot current,
        IReadOnlyList<IPlayerSlot>? slots,
        string localPlayerName,
        IReadOnlyList<string> aiNames,
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions,
        UiNodeViewModel ddName,
        UiNodeViewModel? ddSide,
        UiNodeViewModel? ddColor,
        UiNodeViewModel? ddTeam,
        UiNodeViewModel? ddStart)
    {
        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName) || LobbyPlayerSlotUiRules.IsBanSelection(ddName))
            return null;

        LobbyPlayerRowKind rowKind = slots != null
            ? LobbyPlayerSlotUiRules.GetUiRowKind(slotIndex, slots, mode, allowHostPlayerOptions)
            : current.IsOccupied
                ? (current.IsAi ? LobbyPlayerRowKind.Ai : LobbyPlayerRowKind.Human)
                : LobbyPlayerRowKind.Open;

        int side = ddSide?.SelectedIndex >= 0 ? ddSide.SelectedIndex : 0;
        int color = ddColor?.SelectedIndex >= 0 ? ddColor.SelectedIndex : 0;
        int team = ddTeam?.SelectedIndex >= 0 ? ddTeam.SelectedIndex : 0;
        int start = StartIndexFromCombo(ddStart);

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

        bool isHumanLocal = name.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase);
        bool isAi = !isHumanLocal
            && aiNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        int aiLevel = isAi ? Math.Max(0, IndexOfAiName(aiNames, name)) : 0;

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
    /// 把 <see cref="SlotFieldUpdate"/> 应用到具体 <see cref="LobbyPlayerSlot"/>。
    /// Name="" 时视作 Clear。
    /// </summary>
    private static void ApplyUpdateToSlot(LobbyPlayerSlot slot, in SlotFieldUpdate u)
    {
        if (u.Name != null && u.Name.Length == 0
            && !u.IsAi.GetValueOrDefault()
            && !u.IsHumanLocal.GetValueOrDefault())
        {
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
        UiNodeViewModel panel,
        UiNodeViewModel ddName,
        UiNodeViewModel ddSide,
        UiNodeViewModel ddColor,
        UiNodeViewModel ddTeam,
        UiNodeViewModel? ddStart,
        Action? onSlotsMutated,
        Func<CnCNetGameRoomSession?>? gameRoomProvider,
        IPlayerSlotSink sink,
        LobbySessionState uiState,
        IGameSession session,
        ILobbyCatalogService catalogs,
        IReentrancyShield? shield = null)
    {
        _ = gameRoomProvider;
        shield ??= new FlagReentrancyShield();
        LobbyPlayerMode mode = uiState.UIMode;
        bool allowHost = uiState.AllowHostPlayerOptions;
        string hostName = uiState.HostPlayerName;
        IReadOnlyList<string> aiNames = catalogs.AiNames;

        IPlayerSlot ResolveSlot()
            => slotIndex < session.PlayerSlots.Count
                ? session.PlayerSlots[slotIndex]
                : session.PlayerSlots[0];

        IReadOnlyList<IPlayerSlot> ResolveSlots() => session.PlayerSlots;

        void SyncNameFromUi()
        {
            if (shield.IsEntered)
                return;

            IPlayerSlot current = ResolveSlot();
            LobbyPlayerSlot previous = current is LobbyPlayerSlot concrete
                ? concrete.Clone()
                : ToLobbyClone(current);

            SlotFieldUpdate? update = BuildSlotUpdateFromUi(
                slotIndex,
                current,
                ResolveSlots(),
                uiState.LocalPlayerName,
                aiNames,
                mode,
                allowHost,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                ddStart);
            if (update == null)
                return;

            if (current is LobbyPlayerSlot localMirror)
                ApplyUpdateToSlot(localMirror, update.Value);

            shield.Enter();
            try
            {
                sink.WriteSlot(slotIndex, update.Value);
            }
            finally
            {
                shield.Exit();
            }

            if (mode == LobbyPlayerMode.Multiplayer && session is ICnCNetGameSession cnc)
            {
                MultiplayerSlotCoordinator.HandleHostSlotEdit(
                    cnc,
                    slotIndex,
                    previous,
                    ddName,
                    allowHost,
                    hostName,
                    aiNames);
            }
            else if (mode != LobbyPlayerMode.Multiplayer)
            {
                MultiplayerSlotCoordinator.HandleSkirmishNameEdit(session, aiNames, slotIndex, ddName);
            }

            SyncUiFromState(panel, ResolveSlots(), mode, allowHost, aiNames, shield);
            onSlotsMutated?.Invoke();
        }

        void SyncOptionsFromUi()
        {
            if (shield.IsEntered)
                return;

            IPlayerSlot current = ResolveSlot();

            SlotFieldUpdate? update = BuildSlotUpdateFromUi(
                slotIndex,
                current,
                ResolveSlots(),
                uiState.LocalPlayerName,
                aiNames,
                mode,
                allowHost,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                ddStart);
            if (update == null)
                return;

            if (current is LobbyPlayerSlot localMirror)
                ApplyUpdateToSlot(localMirror, update.Value);

            shield.Enter();
            try
            {
                sink.WriteSlot(slotIndex, update.Value);
            }
            finally
            {
                shield.Exit();
            }

            if (mode == LobbyPlayerMode.Multiplayer && session is ICnCNetGameSession cnc)
            {
                if (allowHost)
                {
                    MultiplayerSlotCoordinator.HandleHostOptionsEdit(cnc, hostName, aiNames);
                    SyncUiFromState(panel, ResolveSlots(), mode, allowHost, aiNames, shield);
                }
                else if (ResolveSlot().IsHumanLocal)
                {
                    MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(cnc, slotIndex);
                }
            }

            onSlotsMutated?.Invoke();
        }

        ddName.SelectionChanged += SyncNameFromUi;
        ddSide.SelectionChanged += SyncOptionsFromUi;
        ddColor.SelectionChanged += SyncOptionsFromUi;
        ddTeam.SelectionChanged += SyncOptionsFromUi;
        if (ddStart != null)
            ddStart.SelectionChanged += SyncOptionsFromUi;
    }

    private static LobbyPlayerSlot ToLobbyClone(IPlayerSlot slot)
        => new()
        {
            Name = slot.Name,
            SideIndex = slot.SideIndex,
            ColorIndex = slot.ColorIndex,
            TeamIndex = slot.TeamIndex,
            StartIndex = slot.StartIndex,
            AiLevel = slot.AiLevel,
            IsAi = slot.IsAi,
            IsHumanLocal = slot.IsHumanLocal,
        };

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
    /// 把 UI 节点同步到给定槽位。
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
                    ApplyOptionDropdown(ddStart, showOptions, optionsEnabled, StartIndexToCombo(state.StartIndex));
                }
            }
        }
        finally
        {
            shield?.Exit();
        }
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

    /// <summary>
    /// Slot.StartIndex (0 = unset, 1-8 = spawn) → combo SelectedIndex (0-7), or -1 when unset.
    /// </summary>
    private static int StartIndexToCombo(int startIndex)
        => startIndex >= 1 && startIndex <= 8 ? startIndex - 1 : -1;

    /// <summary>
    /// Combo SelectedIndex (0-7 showing labels 1-8) → Slot.StartIndex (1-8), or 0 when unset.
    /// </summary>
    private static int StartIndexFromCombo(UiNodeViewModel? ddStart)
        => ddStart?.SelectedIndex >= 0 ? ddStart.SelectedIndex + 1 : 0;

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

    /// <summary>
    /// 纯参数版本——直接吃 <see cref="IReadOnlyList{LobbySideEntry}"/>。
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

    /// <summary>纯参数版本——直接吃 <see cref="IReadOnlyList{String}"/>。</summary>
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
/// UI 重入保护抽象——BindingApplier 在程序化写 UI 时临时启用此 shield，
/// 防止 UI SelectionChanged 事件回调把"程序化刷新"误判为"用户操作"。
/// </summary>
internal interface IReentrancyShield
{
    bool IsEntered { get; }
    void Enter();
    void Exit();
}

/// <summary>简单布尔重入盾（替代已删除的 PlayerUpdatingInProgress）。</summary>
internal sealed class FlagReentrancyShield : IReentrancyShield
{
    public bool IsEntered { get; private set; }
    public void Enter() => IsEntered = true;
    public void Exit() => IsEntered = false;
}
