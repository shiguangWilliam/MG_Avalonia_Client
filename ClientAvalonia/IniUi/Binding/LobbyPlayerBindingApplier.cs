using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.CnCNet;

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

    public static void Apply(
        UiNodeViewModel root,
        LobbyPlayerState playerState,
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        UiNodeViewModel? panel = FindVm(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        HideOrphanPlayerControls(root, panel);

        if (panel.Node.Props.ContainsKey("LobbyPlayerSlotsBuilt"))
        {
            SyncUiFromState(panel, playerState);
            return;
        }

        PlayerOptionLayout layout = ReadLayout(root);
        var sideItems = BuildSideItems(playerState, resources);
        var teamItems = BuildTeamItems(playerState);
        var colorItems = BuildColorItems(resources);
        var startItems = Enumerable.Range(1, 8).Select(i => i.ToString()).ToArray();

        UiNodeViewModel? firstName = null;
        UiNodeViewModel? firstSide = null;
        UiNodeViewModel? firstColor = null;
        UiNodeViewModel? firstTeam = null;

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

            WireSlot(slot, playerState, panel, ddName, ddSide, ddColor, ddTeam);

            panel.Children.Add(ddName);
            panel.Children.Add(ddSide);
            panel.Children.Add(ddColor);
            panel.Children.Add(ddTeam);

            if (layout.StartWidth > 0)
            {
                UiNodeViewModel ddStart = CreateDropdown(
                    $"ddPlayerStart{slot}", x, y, layout.StartWidth, DropDownHeight, resources, behaviors, startItems);
                ddStart.IsVisible = false;
                ddStart.IsEnabled = false;
                WireStartSlot(slot, playerState, ddStart);
                panel.Children.Add(ddStart);
            }
        }

        EnsureColumnCaptions(panel, layout, firstName, firstSide, firstColor, firstTeam, resources, behaviors);

        panel.Node.Props["LobbyPlayerSlotsBuilt"] = true;
        SyncUiFromState(panel, playerState);
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
        ResourceResolver resources,
        BehaviorRegistry behaviors)
    {
        if (ddName == null || ddSide == null || ddColor == null || ddTeam == null)
            return;

        EnsureCaption(panel, "lblName", "PLAYER", ddName.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblSide", "SIDE", ddSide.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblColor", "COLOR", ddColor.CanvasLeft, layout.CaptionY, resources, behaviors);
        EnsureCaption(panel, "lblTeam", "TEAM", ddTeam.CanvasLeft, layout.CaptionY, resources, behaviors);
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
        if (LobbyPlayerSlotUiRules.IsKickSelection(ddName) || LobbyPlayerSlotUiRules.IsBanSelection(ddName))
            return;

        LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slotIndex, playerState);

        if (rowKind == LobbyPlayerRowKind.Human)
        {
            slot.SideIndex = ddSide?.SelectedIndex >= 0 ? ddSide.SelectedIndex : 0;
            slot.ColorIndex = ddColor?.SelectedIndex >= 0 ? ddColor.SelectedIndex : 0;
            slot.TeamIndex = ddTeam?.SelectedIndex >= 0 ? ddTeam.SelectedIndex : 0;
            slot.StartIndex = ddStart?.SelectedIndex >= 0 ? ddStart.SelectedIndex : 0;
            return;
        }

        string name = ReadSelectedText(ddName);
        if (string.IsNullOrWhiteSpace(name) || name == "-")
        {
            slot.Clear();
            return;
        }

        slot.Name = name;
        slot.IsHumanLocal = name.Equals(playerState.LocalPlayerName, StringComparison.OrdinalIgnoreCase);
        slot.IsAi = !slot.IsHumanLocal
            && playerState.AiNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        slot.AiLevel = slot.IsAi ? Math.Max(0, IndexOfAiName(playerState.AiNames, name)) : 0;
        slot.SideIndex = ddSide?.SelectedIndex >= 0 ? ddSide.SelectedIndex : 0;
        slot.ColorIndex = ddColor?.SelectedIndex >= 0 ? ddColor.SelectedIndex : 0;
        slot.TeamIndex = ddTeam?.SelectedIndex >= 0 ? ddTeam.SelectedIndex : 0;
        slot.StartIndex = ddStart?.SelectedIndex >= 0 ? ddStart.SelectedIndex : 0;
    }

    private static void WireSlot(
        int slotIndex,
        LobbyPlayerState playerState,
        UiNodeViewModel panel,
        UiNodeViewModel ddName,
        UiNodeViewModel ddSide,
        UiNodeViewModel ddColor,
        UiNodeViewModel ddTeam)
    {
        void SyncNameFromUi()
        {
            if (playerState.PlayerUpdatingInProgress)
                return;

            LobbyPlayerSlot previous = playerState.Slots[slotIndex].Clone();
            ApplySlotFromUi(
                slotIndex,
                playerState.Slots[slotIndex],
                playerState,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                null);

            if (playerState.Mode == LobbyPlayerMode.Multiplayer)
                MultiplayerSlotCoordinator.HandleHostSlotEdit(
                    playerState,
                    slotIndex,
                    previous,
                    ddName,
                    CnCNetSession.Instance.GameRoom);
            else
                MultiplayerSlotCoordinator.HandleSkirmishNameEdit(playerState, slotIndex, ddName);

            SyncUiFromState(panel, playerState);
        }

        void SyncOptionsFromUi()
        {
            if (playerState.PlayerUpdatingInProgress)
                return;

            ApplySlotFromUi(
                slotIndex,
                playerState.Slots[slotIndex],
                playerState,
                ddName,
                ddSide,
                ddColor,
                ddTeam,
                null);

            if (playerState.Mode == LobbyPlayerMode.Multiplayer)
                MultiplayerSlotCoordinator.HandleHostOptionsEdit(
                    playerState,
                    CnCNetSession.Instance.GameRoom);
        }

        ddName.SelectionChanged += SyncNameFromUi;
        ddSide.SelectionChanged += SyncOptionsFromUi;
        ddColor.SelectionChanged += SyncOptionsFromUi;
        ddTeam.SelectionChanged += SyncOptionsFromUi;
    }

    private static void WireStartSlot(int slotIndex, LobbyPlayerState playerState, UiNodeViewModel ddStart)
    {
        void SyncFromUi()
        {
            playerState.Slots[slotIndex].StartIndex = ddStart.SelectedIndex >= 0 ? ddStart.SelectedIndex : 0;
        }

        ddStart.SelectionChanged += SyncFromUi;
    }

    private static void SyncUiFromState(UiNodeViewModel panel, LobbyPlayerState playerState)
    {
        playerState.PlayerUpdatingInProgress = true;
        try
        {
            for (int slot = 0; slot < LobbyPlayerSlot.MaxSlots; slot++)
            {
                LobbyPlayerSlot state = playerState.Slots[slot];
                LobbyPlayerRowKind rowKind = LobbyPlayerSlotUiRules.GetUiRowKind(slot, playerState);
                UiNodeViewModel? ddName = FindVm(panel, $"ddPlayerName{slot}");
                UiNodeViewModel? ddSide = FindVm(panel, $"ddPlayerSide{slot}");
                UiNodeViewModel? ddColor = FindVm(panel, $"ddPlayerColor{slot}");
                UiNodeViewModel? ddTeam = FindVm(panel, $"ddPlayerTeam{slot}");
                UiNodeViewModel? ddStart = FindVm(panel, $"ddPlayerStart{slot}");

                if (ddName == null)
                    continue;

                ddName.SetComboItems(LobbyPlayerSlotUiRules.BuildNameItems(slot, playerState));
                ddName.IsEnabled = LobbyPlayerSlotUiRules.IsNameDropdownEnabled(slot, playerState);
                ddName.SetSelectedIndexSilent(ResolveSelectedIndex(
                    ddName,
                    LobbyPlayerSlotUiRules.ResolveNameSelectedIndex(ddName, state, playerState)));

                bool optionsEnabled = LobbyPlayerSlotUiRules.ArePlayerOptionsEnabled(slot, playerState);
                bool showOptions = rowKind is LobbyPlayerRowKind.Human or LobbyPlayerRowKind.Ai;

                ApplyOptionDropdown(ddSide, showOptions, optionsEnabled, state.SideIndex);
                ApplyOptionDropdown(ddColor, showOptions, optionsEnabled, state.ColorIndex);
                ApplyOptionDropdown(ddTeam, showOptions, optionsEnabled, state.TeamIndex);

                if (ddStart != null)
                {
                    ddStart.IsEnabled = optionsEnabled && showOptions;
                    ApplyOptionDropdown(ddStart, showOptions, optionsEnabled && showOptions, state.StartIndex);
                }
            }
        }
        finally
        {
            playerState.PlayerUpdatingInProgress = false;
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
    {
        return playerState.SideEntries.Select(entry =>
        {
            Bitmap? icon = entry.InternalName switch
            {
                LobbySideCatalog.RandomInternalName => resources.LoadFirstBitmap(["randomicon.png"]),
                LobbySideCatalog.SpectatorInternalName => resources.LoadFirstBitmap(["spectatoricon.png"]),
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
    {
        var items = new List<string> { "-" };
        items.AddRange(playerState.TeamNames);
        return items.ToArray();
    }

    private static PlayerOptionLayout ReadLayout(UiNodeViewModel root)
    {
        return new PlayerOptionLayout
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
