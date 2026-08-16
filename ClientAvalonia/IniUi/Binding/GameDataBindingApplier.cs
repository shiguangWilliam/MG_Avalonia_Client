using Avalonia.Media.Imaging;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientAvalonia.Themes;
using ClientCore;
using ClientCore.Settings;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Binds loaded map/mission catalogs to lobby and campaign INI controls.</summary>
public static class GameDataBindingApplier
{
    public static void ApplyLobby(
        UiNodeViewModel root,
        GameResourceCatalog catalog,
        LobbySessionState session,
        ResourceResolver resources,
        int filterIndex = 1,
        IReadOnlyList<IPlayerSlot>? slots = null)
    {
        catalog.EnsureLoaded();

        UiNodeViewModel? ddGameMode = FindVm(root, "ddGameMode");
        if (ddGameMode != null)
        {
            var filterItems = new List<string> { catalog.FavoriteMapsLabel };
            filterItems.AddRange(catalog.GameModes.Select(m => m.DisplayName));
            ddGameMode.SetComboItems(filterItems);

            int clamped = Math.Clamp(filterIndex, 0, Math.Max(0, filterItems.Count - 1));
            ddGameMode.SetSelectedIndexSilent(clamped);
            session.FilterIndex = clamped;
        }

        ApplyLobbyMapList(
            root,
            catalog,
            session,
            resources,
            ddGameMode?.SelectedIndex ?? filterIndex,
            slots);
    }

    public static void ApplyLobbyMapList(
        UiNodeViewModel root,
        GameResourceCatalog catalog,
        LobbySessionState session,
        ResourceResolver resources,
        int filterIndex,
        IReadOnlyList<IPlayerSlot>? slots = null)
    {
        catalog.EnsureLoaded();
        session.FilterIndex = filterIndex;
        IReadOnlyList<MapEntry> maps = catalog.GetMapsForFilterIndex(filterIndex);
        maps = catalog.FilterMapsBySearch(maps, session.MapSearchText);
        session.SetVisibleMaps(maps);

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        int selectedIndex = lbMapList?.SelectedIndex ?? 0;
        if (selectedIndex < 0 || selectedIndex >= maps.Count)
            selectedIndex = maps.Count > 0 ? 0 : -1;

        if (lbMapList != null)
        {
            lbMapList.SetListItems(maps.Select(m => m.DisplayName));
            lbMapList.SetSelectedIndexSilent(selectedIndex);
        }

        ResolveStartInteractionFlags(
            session.UIMode,
            session.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        UpdateMapSelectionDisplay(
            root,
            maps,
            selectedIndex,
            resources,
            slots,
            canAssign,
            canSelectLocal);
    }

    /// <summary>
    /// Session-aware：直接吃 <see cref="LobbyPlayerMode"/> + <c>allowHostPlayerOptions</c>。
    /// </summary>
    public static void ResolveStartInteractionFlags(
        LobbyPlayerMode mode,
        bool allowHostPlayerOptions,
        out bool canAssign,
        out bool canSelectLocal)
    {
        // Skirmish: local player is always the host. Multiplayer: host Assign / joiner Select.
        if (mode == LobbyPlayerMode.Skirmish || allowHostPlayerOptions)
        {
            canAssign = true;
            canSelectLocal = false;
            return;
        }

        canAssign = false;
        canSelectLocal = true;
    }

    /// <summary>
    /// Session-aware：直接吃 <see cref="IReadOnlyList{IPlayerSlot}"/>。
    /// </summary>
    public static void UpdateMapSelectionDisplay(
        UiNodeViewModel root,
        IReadOnlyList<MapEntry> maps,
        int selectedIndex,
        ResourceResolver resources,
        IReadOnlyList<IPlayerSlot>? slots = null,
        bool canAssignStarts = false,
        bool canSelectLocalStart = false)
    {
        MapEntry? map = selectedIndex >= 0 && selectedIndex < maps.Count ? maps[selectedIndex] : null;

        FindVm(root, "lblMapName")?.SetDisplayText(map?.DisplayName ?? string.Empty);

        UiNodeViewModel? previewBox = FindVm(root, "MapPreviewBox");
        if (previewBox != null)
        {
            Bitmap? preview = GameAssetResolver.LoadMapPreview(
                resources,
                map?.BaseFilePath,
                map?.PreviewRelativePath,
                previewBox);
            previewBox.SetPreviewImage(preview);
            MapPreviewOverlayApplier.Apply(
                previewBox,
                map,
                slots,
                canAssignStarts,
                canSelectLocalStart);
        }
    }

    /// <summary>
    /// Session-aware：刷新 start markers，吃 <see cref="IReadOnlyList{IPlayerSlot}"/>。
    /// </summary>
    public static void RefreshMapStartMarkers(
        UiNodeViewModel root,
        MapEntry? map,
        IReadOnlyList<IPlayerSlot> slots,
        bool canAssignStarts,
        bool canSelectLocalStart)
    {
        UiNodeViewModel? previewBox = FindVm(root, "MapPreviewBox");
        if (previewBox == null)
            return;

        MapPreviewOverlayApplier.Apply(
            previewBox,
            map,
            slots,
            canAssignStarts,
            canSelectLocalStart);
    }

    public static void ApplyChannelLobby(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        UiNodeViewModel? lbPlayers = FindVm(root, "lbPlayerList");
        if (lbPlayers != null)
        {
            lbPlayers.SetListItems(state.ChannelPlayers);
            lbPlayers.SelectedIndex = state.ChannelPlayers.Count > 0 ? 0 : -1;
            Logger.Log($"ApplyChannelLobby: lbPlayerList ← {state.ChannelPlayers.Count} players.");
        }
        else
        {
            Logger.Log("ApplyChannelLobby: lbPlayerList not found in UI tree.");
        }

        UiNodeViewModel? lbGames = FindVm(root, "lbGameList");
        if (lbGames != null)
        {
            var games = state.HostedGames.Count > 0
                ? state.HostedGames.ToList()
                : new List<string> { "(no hosted games)" };
            lbGames.SetListItems(games);
            if (state.HostedGameDetails.Count > 0)
            {
                int idx = state.SelectedGameIndex;
                if (idx < 0 || idx >= state.HostedGameDetails.Count)
                    idx = 0;
                state.SelectedGameIndex = idx;
                lbGames.SelectedIndex = idx;
            }
            else
            {
                lbGames.SelectedIndex = -1;
                state.SelectedGameIndex = -1;
            }

            if (!lbGames.Node.Props.ContainsKey("ChannelLobbyGamesWired"))
            {
                lbGames.Node.Props["ChannelLobbyGamesWired"] = true;
                lbGames.SelectionChanged += () =>
                {
                    int idx = lbGames.SelectedIndex;
                    if (idx >= 0 && idx < state.HostedGameDetails.Count)
                        state.SelectedGameIndex = idx;
                };
            }
            Logger.Log($"ApplyChannelLobby: lbGameList ← {state.HostedGameDetails.Count} games.");
        }
        else
        {
            Logger.Log("ApplyChannelLobby: lbGameList not found in UI tree.");
        }

        string statusText = state.ConnectionStatus;
        if (state.OnlinePlayerCount >= 0 && !statusText.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            statusText = $"在线 {state.OnlinePlayerCount} · {statusText}";

        UiNodeViewModel? lblChannel = FindVm(root, "lblCurrentChannel");
        if (lblChannel != null)
            lblChannel.SetDisplayText("当前频道：");

        UiNodeViewModel? ddChannel = FindVm(root, "ddCurrentChannel");
        if (ddChannel != null)
        {
            var channelNames = state.AvailableChannelNames.Count > 0
                ? state.AvailableChannelNames.ToList()
                : new List<string> { state.ChatChannelDisplay };

            ddChannel.SetComboItems(channelNames);
            int channelIndex = Math.Clamp(state.SelectedChannelIndex, 0, Math.Max(0, channelNames.Count - 1));
            ddChannel.SelectedIndex = channelIndex;

            if (!ddChannel.Node.Props.ContainsKey("ChannelLobbyWired"))
            {
                ddChannel.Node.Props["ChannelLobbyWired"] = true;
                ddChannel.SelectionChanged += () =>
                {
                    ICnCNetSession cncnet = EnvironmentServices.Resolve<ICnCNetSession>();
                    int idx = ddChannel.SelectedIndex;
                    if (idx >= 0 && idx != cncnet.SelectedChannelIndex)
                        cncnet.SwitchToChannel(idx);
                };
            }
        }

        FindVm(root, "lblColor")?.SetDisplayText("聊天文字颜色：");

        WireChatColorDropdown(root, state);
        WireChatMessages(root, state);
        WireChatInput(root);

        ApplyChannelLobbyButtonLabels(root);
    }

    private static void WireChatColorDropdown(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        UiNodeViewModel? ddColor = FindVm(root, "ddColor");
        if (ddColor == null)
            return;

        var colors = CnCNetChatColorCatalog.LoadSelectable();
        ddColor.SetComboItemEntries(colors.Select(c => new ComboItemViewModel
        {
            Text = c.Name,
            SwatchBrush = new SolidColorBrush(c.DisplayColor),
        }));
        int saved = state.SelectedChatColorIndex >= 0
            ? state.SelectedChatColorIndex
            : CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);
        int selectableIndex = MapToSelectableIndex(saved, colors);
        ddColor.SetSelectedIndexSilent(Math.Clamp(selectableIndex, 0, Math.Max(0, ddColor.ComboItemEntries.Count - 1)));

        if (!ddColor.Node.Props.ContainsKey("ChatColorWired"))
        {
            ddColor.Node.Props["ChatColorWired"] = true;
            ddColor.SelectionChanged += () =>
            {
                int idx = ddColor.SelectedIndex;
                if (idx < 0 || idx >= ddColor.ComboItemEntries.Count)
                    return;

                string name = ddColor.ComboItemEntries[idx].Text;
                IReadOnlyList<CnCNetChatColorEntry> all = CnCNetChatColorCatalog.LoadAll();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Name == name)
                    {
                        EnvironmentServices.Resolve<ICnCNetSession>().SetChatColorIndex(i);
                        UiNodeViewModel? tbChat = FindVm(root, "tbChatInput");
                        if (tbChat != null)
                            ApplyChatInputColor(tbChat);
                        break;
                    }
                }
            };
        }
    }

    private static int MapToSelectableIndex(int catalogIndex, IReadOnlyList<CnCNetChatColorEntry> selectable)
    {
        IReadOnlyList<CnCNetChatColorEntry> all = CnCNetChatColorCatalog.LoadAll();
        if (catalogIndex < 0 || catalogIndex >= all.Count)
            return 0;

        string name = all[catalogIndex].Name;
        for (int i = 0; i < selectable.Count; i++)
        {
            if (selectable[i].Name == name)
                return i;
        }

        return 0;
    }

    private static void WireChatMessages(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        UiNodeViewModel? lbChat = FindVm(root, "lbChatMessages");
        if (lbChat == null)
            return;

        if (state.ChatLines.Count > 0)
        {
            // PMs use Scope=PrivateMessage and must not pollute the lobby channel listbox.
            var lobbyLines = state.ChatLines
                .Where(l => l.Scope == CnCNetChatScope.LobbyChannel)
                .ToList();
            if (lobbyLines.Count > 0)
            {
                ApplyColoredChatLines(lbChat, lobbyLines);
                return;
            }
        }

        var fallback = state.ConnectionLog.Count > 0
            ? state.ConnectionLog.ToList()
            : new List<string> { "CnCNet connection log will appear here..." };
        lbChat.SetListItems(fallback);
        lbChat.SelectedIndex = fallback.Count > 0 ? fallback.Count - 1 : -1;
    }

    private static void WireChatInput(UiNodeViewModel root)
    {
        UiNodeViewModel? tbChat = FindVm(root, "tbChatInput");
        if (tbChat == null)
            return;

        tbChat.IsEnabled = EnvironmentServices.Resolve<ICnCNetSession>().Connection?.IsConnected == true;
        ApplyChatInputColor(tbChat);
    }

    private static void ApplyChatInputColor(UiNodeViewModel tbChat)
    {
        int colorIndex = EnvironmentServices.Resolve<ICnCNetSession>().LobbyState.SelectedChatColorIndex;
        if (colorIndex < 0)
            colorIndex = CnCNetChatColorCatalog.ResolveSelectedIndex(UserINISettings.Instance.ChatColor);

        tbChat.SetForeground(CnCNetChatColorCatalog.GetEntry(colorIndex).DisplayColor);
    }

    public static void SyncChannelGameSelection(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        UiNodeViewModel? lbGames = FindVm(root, "lbGameList");
        if (lbGames == null || state.HostedGameDetails.Count == 0)
            return;

        int idx = lbGames.SelectedIndex;
        state.SelectedGameIndex = idx >= 0 && idx < state.HostedGameDetails.Count ? idx : 0;
    }

    /// <summary>
    /// Push the in-room chat timeline into the game-room lobby's <c>lbChatMessages</c> +
    /// enable <c>tbChatInput</c>. Mirrors DX <c>CnCNetGameLobby.Channel_MessageAdded
    /// -> lbChatMessages.AddMessage</c>. Falls back to a friendly placeholder when the room
    /// has no chat yet so the UI never looks broken.
    /// </summary>
    public static void ApplyGameRoomChat(UiNodeViewModel root, CnCNetGameRoomSession? gameRoom)
    {
        UiNodeViewModel? lbChat = FindVm(root, "lbChatMessages");
        if (lbChat != null)
        {
            IReadOnlyList<CnCNetChatLine> lines = gameRoom?.ChatLines ?? [];
            if (lines.Count > 0)
            {
                ApplyColoredChatLines(lbChat, lines);
            }
            else
            {
                lbChat.SetListItems(new List<string> { "Type / to view a list of available chat commands." });
                lbChat.SelectedIndex = 0;
            }
        }

        UiNodeViewModel? tbChat = FindVm(root, "tbChatInput");
        if (tbChat != null)
        {
            // Chat input is enabled when we are joined to the room AND connected to IRC.
            bool enabled = gameRoom is { IsLocalJoined: true }
                           && EnvironmentServices.Resolve<ICnCNetSession>().Connection?.IsConnected == true;
            tbChat.IsEnabled = enabled;
            ApplyChatInputColor(tbChat);
        }
    }

    private static void ApplyColoredChatLines(UiNodeViewModel lbChat, IReadOnlyList<CnCNetChatLine> lines)
    {
        var items = lines.Select(line => new CatalogListItemViewModel
        {
            Text = line.DisplayText,
            ForegroundBrush = new SolidColorBrush(line.TextColor),
        }).ToList();
        lbChat.SetCatalogListItems(items);
        lbChat.SelectedIndex = items.Count > 0 ? items.Count - 1 : -1;
    }

    private static void ApplyChannelLobbyButtonLabels(UiNodeViewModel root)
    {
        FindVm(root, "btnNewGame")?.SetDisplayText("Create Game");
        FindVm(root, "btnJoinGame")?.SetDisplayText("Join Game");

        UiNodeViewModel? btnMainMenu = FindVm(root, "btnMainMenu");
        UiNodeViewModel? btnLogout = FindVm(root, "btnLogout");
        if (btnMainMenu != null && btnMainMenu.IsVisible)
        {
            btnMainMenu.SetDisplayText("Main Menu");
            if (btnLogout != null)
                btnLogout.IsVisible = false;
        }
        else
        {
            btnLogout?.SetDisplayText("Logout");
        }
    }

    public static void ApplyCampaignOverlay(
        UiNodeViewModel root,
        GameResourceCatalog catalog,
        LobbySessionState session,
        ResourceResolver resources,
        CampaignSideFilter sideFilter = CampaignSideFilter.All)
    {
        catalog.EnsureLoaded();
        session.CampaignSideFilter = sideFilter;

        IReadOnlyList<MissionEntry> missions = catalog.GetMissionsForSideFilter(sideFilter);
        session.SetVisibleMissions(missions);

        UiNodeViewModel? lbCampaignList = FindVm(root, "lbCampaignList");
        if (lbCampaignList != null)
        {
            var disabledBrush = new SolidColorBrush(Color.FromRgb(120, 112, 104));
            var enabledBrush = new SolidColorBrush(Color.FromRgb(242, 230, 216));
            var listItems = missions.Select(m => new CatalogListItemViewModel
            {
                Text = m.DisplayName,
                Icon = m.IsHeader || string.IsNullOrWhiteSpace(m.SideName)
                    ? null
                    : GameAssetResolver.LoadSideIcon(resources, m.SideName, lbCampaignList),
                IsHeader = m.IsHeader,
                IsEnabled = m.Enabled,
                ForegroundBrush = m.IsHeader
                    ? null
                    : (m.Enabled ? enabledBrush : disabledBrush),
                ToolTip = !m.IsHeader && !m.Enabled ? "未启用 — 无法开始此战役" : null,
                GlobeLatitude = m.GlobeLatitude,
                GlobeLongitude = m.GlobeLongitude,
                GlobeCountry = m.GlobeCountry,
            }).ToList();

            lbCampaignList.SetCatalogListItems(listItems);
            int firstSelectable = FindFirstSelectableMissionIndex(missions);
            session.LastSelectableCampaignIndex = firstSelectable;
            lbCampaignList.SelectedIndex = firstSelectable >= 0 ? firstSelectable : 0;
            WireCampaignSelection(
                lbCampaignList,
                FindVm(root, "tbMissionDescription"),
                FindVm(root, "btnLaunch"),
                session,
                resources);
        }

        ApplyCampaignSideTabState(root, sideFilter);
        ApplyCampaignDifficulty(root, resources);
        EnsureCampaignControlSizes(root);
        ApplyCampaignActionButtonLabels(root);
        GameAssetResolver.ApplyCampaignSideIcons(root, resources);
        GameAssetResolver.ApplyCampaignActionButtonTextures(root, resources);
    }

    private static void ApplyCampaignActionButtonLabels(UiNodeViewModel root)
    {
        // Primary chrome is orange; default IdleTexture button fg (#FFA648) vanishes on it.
        Color launchFg = Color.FromRgb(32, 22, 12);
        Color cancelFg = Color.FromRgb(242, 230, 216);

        UiNodeViewModel? launch = FindVm(root, "btnLaunch");
        if (launch != null)
        {
            launch.SetDisplayText(PickLocalizedLabel(launch.Text, "开始", "Launch"));
            launch.SetForeground(launchFg);
            launch.Node.Props["FontSize"] = 14;
            launch.RefreshLayout();
        }

        UiNodeViewModel? cancel = FindVm(root, "btnCancel");
        if (cancel != null)
        {
            cancel.SetDisplayText(PickLocalizedLabel(cancel.Text, "返回", "Cancel"));
            cancel.SetForeground(cancelFg);
            cancel.Node.Props["FontSize"] = 14;
            cancel.RefreshLayout();
        }

        ApplySideTabLabel(root, "GDI", "同盟国联军", "Allied");
        ApplySideTabLabel(root, "Nod", "苏维埃联盟", "Soviet");
        ApplySideTabLabel(root, "ThirdSide", "阿克维尔", "Ackville");
    }

    private static void ApplySideTabLabel(UiNodeViewModel root, string id, string zh, string en)
    {
        UiNodeViewModel? tab = FindVm(root, id);
        if (tab == null)
            return;

        tab.SetDisplayText(PickLocalizedLabel(tab.Text, zh, en));
        tab.RefreshLayout();
    }

    /// <summary>MG INI often stores <c>中文;English</c> bilingual labels.</summary>
    private static string PickLocalizedLabel(string? raw, string chineseFallback, string englishFallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return chineseFallback;

        int sep = raw.IndexOf(';');
        if (sep < 0)
            return raw.Trim();

        string left = raw[..sep].Trim();
        string right = raw[(sep + 1)..].Trim();
        bool preferChinese = ContainsCjk(left) || !ContainsLatinWord(left);
        if (preferChinese)
            return string.IsNullOrEmpty(left) ? (string.IsNullOrEmpty(right) ? chineseFallback : right) : left;

        return string.IsNullOrEmpty(right) ? (string.IsNullOrEmpty(left) ? englishFallback : left) : right;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
        }

        return false;
    }

    private static bool ContainsLatinWord(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// MG CampaignSelector.ini omits Size for faction tabs / difficulty chrome; DX relied on
    /// missing button textures that never shipped. Give Avalonia themed fallbacks real bounds
    /// so the option boxes stay visible without those assets.
    /// </summary>
    private static void EnsureCampaignControlSizes(UiNodeViewModel root)
    {
        // Unconditional (as on main): Classic campaign buttons use the themed
        // 14pt template whose 23px INI height clips the label; Tactical buttons
        // get their size re-derived from generated textures later anyway.
        foreach (string id in new[] { "GDI", "Nod", "ThirdSide", "FourthSide" })
        {
            UiNodeViewModel? tab = FindVm(root, id);
            if (tab == null)
                continue;

            if (tab.Width <= 1)
                tab.Node.Props["Width"] = 148d;
            if (tab.Height <= 1)
                tab.Node.Props["Height"] = 56d;
            tab.RefreshLayout();
        }

        UiNodeViewModel? trackbar = FindVm(root, "trbDifficultySelector");
        if (trackbar != null)
        {
            if (trackbar.Width <= 1)
                trackbar.Node.Props["Width"] = 280d;
            if (trackbar.Height <= 1)
                trackbar.Node.Props["Height"] = 36d;
            trackbar.RefreshLayout();
        }

        foreach (string id in new[] { "btnLaunch", "btnCancel" })
        {
            UiNodeViewModel? button = FindVm(root, id);
            if (button == null)
                continue;

            // Themed campaign buttons use 8px vertical padding + 14pt type; XNA's 23px height
            // clips the label completely when Avalonia Height is set explicitly.
            if (button.Width <= 1)
                button.Node.Props["Width"] = 147d;
            button.Node.Props["Height"] = 36d;
            button.RefreshLayout();
        }
    }

    public static void WireCampaignSelection(
        UiNodeViewModel listVm,
        UiNodeViewModel? descriptionVm,
        UiNodeViewModel? launchButton,
        LobbySessionState session,
        ResourceResolver resources)
    {
        if (descriptionVm == null)
            return;

        void UpdateDescription()
        {
            int index = listVm.SelectedIndex;
            MissionEntry? mission = session.GetSelectedMission(index);
            if (mission != null && mission.IsHeader)
            {
                int fallback = session.LastSelectableCampaignIndex;
                if (fallback >= 0 && fallback != index)
                {
                    listVm.SelectedIndex = fallback;
                    return;
                }
            }

            // Allow selecting disabled missions so players can read the briefing + locked hint.
            if (mission != null && !mission.IsHeader)
                session.LastSelectableCampaignIndex = index;

            string? lockedHint = mission != null && !mission.IsHeader && !mission.Enabled
                ? "该战役尚未开放或未启用，无法开始。"
                : null;

            MissionBriefingParsed briefing = MissionBriefingParser.Parse(mission?.Description);
            descriptionVm.SetMissionBriefing(briefing, lockedHint);
            descriptionVm.SetPreviewImage(GameAssetResolver.LoadMissionPreview(resources, mission, descriptionVm));

            if (launchButton != null)
                launchButton.IsEnabled = mission != null && mission.Enabled && !mission.IsHeader;
        }

        listVm.SelectionChanged -= UpdateDescription;
        listVm.SelectionChanged += UpdateDescription;
        UpdateDescription();
    }

    private static void ApplyCampaignSideTabState(UiNodeViewModel root, CampaignSideFilter activeFilter)
    {
        ApplySideTabSelected(root, "GDI", activeFilter == CampaignSideFilter.Allied);
        ApplySideTabSelected(root, "Nod", activeFilter == CampaignSideFilter.Soviet);
        ApplySideTabSelected(root, "ThirdSide", activeFilter == CampaignSideFilter.Ackville);
        ApplySideTabSelected(root, "FourthSide", false);
    }

    private static void ApplySideTabSelected(UiNodeViewModel root, string controlId, bool selected)
    {
        UiNodeViewModel? tab = FindVm(root, controlId);
        tab?.SetTabSelected(selected);
    }

    private static void ApplyCampaignDifficulty(UiNodeViewModel root, ResourceResolver resources)
    {
        UiNodeViewModel? trackbar = FindVm(root, "trbDifficultySelector");
        if (trackbar == null)
            return;

        int saved = Math.Clamp(UserINISettings.Instance.Difficulty, 0, 2);
        trackbar.SelectedIndex = saved;
        // Do not overlay DX trackbar thumb textures — Avalonia Slider already draws a thumb,
        // and a second static image reads as stray "dots" next to the briefing scrollbar.
        trackbar.SetThumbImage(null);
    }

    private static int FindFirstSelectableMissionIndex(IReadOnlyList<MissionEntry> missions)
    {
        for (int i = 0; i < missions.Count; i++)
        {
            if (missions[i].Enabled && !missions[i].IsHeader)
                return i;
        }

        return missions.Count > 0 ? 0 : -1;
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
}
