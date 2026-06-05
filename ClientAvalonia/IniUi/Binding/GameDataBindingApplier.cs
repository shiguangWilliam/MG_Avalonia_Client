using Avalonia.Media.Imaging;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientCore;
using ClientAvalonia.CnCNet;
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
        int filterIndex = 1)
    {
        catalog.EnsureLoaded();

        UiNodeViewModel? ddGameMode = FindVm(root, "ddGameMode");
        if (ddGameMode != null)
        {
            var filterItems = new List<string> { catalog.FavoriteMapsLabel };
            filterItems.AddRange(catalog.GameModes.Select(m => m.DisplayName));
            ddGameMode.SetComboItems(filterItems);

            int clamped = Math.Clamp(filterIndex, 0, Math.Max(0, filterItems.Count - 1));
            ddGameMode.SelectedIndex = clamped;
            session.FilterIndex = clamped;
        }

        ApplyLobbyMapList(root, catalog, session, resources, ddGameMode?.SelectedIndex ?? filterIndex);
    }

    public static void ApplyLobbyMapList(
        UiNodeViewModel root,
        GameResourceCatalog catalog,
        LobbySessionState session,
        ResourceResolver resources,
        int filterIndex)
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
            lbMapList.SelectedIndex = selectedIndex;
        }

        UpdateMapSelectionDisplay(root, maps, selectedIndex, resources);
    }

    public static void UpdateMapSelectionDisplay(
        UiNodeViewModel root,
        IReadOnlyList<MapEntry> maps,
        int selectedIndex,
        ResourceResolver resources)
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
        }
    }

    public static void ApplyChannelLobby(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        state.RefreshFromCore();

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
                    int idx = ddChannel.SelectedIndex;
                    if (idx >= 0 && idx != CnCNetSessionService.Instance.SelectedChannelIndex)
                        CnCNetSessionService.Instance.SwitchToChannel(idx);
                };
            }
        }

        FindVm(root, "lblColor")?.SetDisplayText("聊天文字颜色：");

        UiNodeViewModel? lbChat = FindVm(root, "lbChatMessages");
        if (lbChat != null)
        {
            var logLines = state.ConnectionLog.Count > 0
                ? state.ConnectionLog.ToList()
                : new List<string> { "CnCNet connection log will appear here..." };
            lbChat.SetListItems(logLines);
            lbChat.SelectedIndex = logLines.Count > 0 ? logLines.Count - 1 : -1;
        }

        ApplyChannelLobbyButtonLabels(root);
    }

    public static void SyncChannelGameSelection(UiNodeViewModel root, MultiplayerLobbyState state)
    {
        UiNodeViewModel? lbGames = FindVm(root, "lbGameList");
        if (lbGames == null || state.HostedGameDetails.Count == 0)
            return;

        int idx = lbGames.SelectedIndex;
        state.SelectedGameIndex = idx >= 0 && idx < state.HostedGameDetails.Count ? idx : 0;
    }

    private static void ApplyChannelLobbyButtonLabels(UiNodeViewModel root)
    {
        FindVm(root, "btnNewGame")?.SetDisplayText("Create Game");
        FindVm(root, "btnJoinGame")?.SetDisplayText("Join Game");
        FindVm(root, "btnLogout")?.SetDisplayText("Logout");
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
            var listItems = missions.Select(m => new CatalogListItemViewModel
            {
                Text = m.DisplayName,
                Icon = m.IsHeader || string.IsNullOrWhiteSpace(m.SideName)
                    ? null
                    : GameAssetResolver.LoadSideIcon(resources, m.SideName, lbCampaignList),
                IsHeader = m.IsHeader,
                IsEnabled = m.Enabled,
            }).ToList();

            lbCampaignList.SetCatalogListItems(listItems);
            int firstSelectable = FindFirstSelectableMissionIndex(missions);
            session.LastSelectableCampaignIndex = firstSelectable;
            lbCampaignList.SelectedIndex = firstSelectable >= 0 ? firstSelectable : 0;
            WireCampaignSelection(lbCampaignList, FindVm(root, "tbMissionDescription"), session, resources);
        }

        ApplyCampaignSideTabState(root, sideFilter);
        ApplyCampaignDifficulty(root, resources);
        GameAssetResolver.ApplyCampaignSideIcons(root, resources);
        GameAssetResolver.ApplyCampaignActionButtonTextures(root, resources);
    }

    public static void WireCampaignSelection(
        UiNodeViewModel listVm,
        UiNodeViewModel? descriptionVm,
        LobbySessionState session,
        ResourceResolver resources)
    {
        if (descriptionVm == null)
            return;

        void UpdateDescription()
        {
            int index = listVm.SelectedIndex;
            MissionEntry? mission = session.GetSelectedMission(index);
            if (mission != null && (mission.IsHeader || !mission.Enabled))
            {
                int fallback = session.LastSelectableCampaignIndex;
                if (fallback >= 0 && fallback != index)
                {
                    listVm.SelectedIndex = fallback;
                    return;
                }
            }

            if (mission != null && mission.Enabled && !mission.IsHeader)
                session.LastSelectableCampaignIndex = index;

            descriptionVm.SetDisplayText(mission?.Description ?? string.Empty);
            descriptionVm.SetPreviewImage(GameAssetResolver.LoadMissionPreview(resources, mission, descriptionVm));
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
        GameAssetResolver.ApplyDifficultyTrackbarTextures(trackbar, resources);
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
