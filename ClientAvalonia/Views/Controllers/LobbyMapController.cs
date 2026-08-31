using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.Views.Controllers;

internal sealed class LobbyMapController
{
    private readonly MainWindowContext _ctx;

    public LobbyMapController(MainWindowContext ctx)
    {
        _ctx = ctx;
    }

    public void RefreshLobbyMapList()
    {
        if (_ctx.ActiveRoot == null || !_ctx.CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            return;

        ResourceResolver resources = _ctx.GetMainResources();
        UiNodeViewModel? ddGameMode = MainWindowContext.FindVm(_ctx.ActiveRoot, "ddGameMode");
        int filterIndex = ddGameMode?.SelectedIndex ?? _ctx.LobbySession.FilterIndex;
        GameDataBindingApplier.ApplyLobbyMapList(
            _ctx.ActiveRoot,
            _ctx.GameResources,
            _ctx.LobbySession,
            resources,
            filterIndex,
            _ctx.ResolveActiveLobbySlots());
        _ctx.UpdateLaunchButtonState();
    }

    public void PickRandomLobbyMap()
    {
        if (_ctx.ActiveRoot == null)
            return;

        _ctx.LobbySession.MapSearchText = string.Empty;
        UiNodeViewModel? tbMapSearch = MainWindowContext.FindVm(_ctx.ActiveRoot, "tbMapSearch");
        if (tbMapSearch != null)
            tbMapSearch.InputText = string.Empty;

        RefreshLobbyMapList();

        int index = _ctx.GameResources.PickRandomMapIndex(_ctx.LobbySession.VisibleMaps);
        if (index < 0)
        {
            _ctx.ShowStatus("No maps available for random pick.");
            return;
        }

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList");
        if (lbMapList != null)
            lbMapList.SelectedIndex = index;

        ResourceResolver resources = _ctx.GetMainResources();
        GameDataBindingApplier.ResolveStartInteractionFlags(
            _ctx.LobbySession.UIMode,
            _ctx.LobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.UpdateMapSelectionDisplay(
            _ctx.ActiveRoot,
            _ctx.LobbySession.VisibleMaps,
            index,
            resources,
            _ctx.ResolveActiveLobbySlots(),
            canAssign,
            canSelectLocal);
        _ctx.UpdateLaunchButtonState();
        _ctx.ShowStatus($"Random map: {_ctx.LobbySession.GetSelectedMap(index)?.DisplayName ?? "none"}");
    }

    public void ToggleFavoriteLobbyMap()
    {
        if (_ctx.ActiveRoot == null)
            return;

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList");
        MapEntry? map = _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _ctx.GameResources.GetGameModeForFilterIndex(_ctx.LobbySession.FilterIndex);
        if (map == null)
            return;

        bool isFavorite = _ctx.GameResources.ToggleFavoriteMap(map, gameMode);
        _ctx.ShowStatus(isFavorite ? "Map added to favorites." : "Map removed from favorites.");

        if (_ctx.LobbySession.IsFavoriteFilterSelected)
            RefreshLobbyMapList();
    }

    public void ApplyLobbyData(UiNodeViewModel root, string windowName)
    {
        _ctx.GameResources.EnsureLoaded();
        ResourceResolver resources = _ctx.GetMainResources();
        bool skirmishSettingsLoaded = false;

        if (MainWindowContext.IsGameLobbyWindow(windowName))
        {
            _ctx.LobbySession.MapSearchText = string.Empty;
            LobbyCatalogService.Instance.Reload();

            if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            {
                ICnCNetGameSession? room = _ctx.CnCNet.ActiveGameRoom;
                string localNick = _ctx.CnCNet.LocalNick;
                string hostName = room?.HostName ?? localNick;
                bool resetSlots = _ctx.LobbySession.UIMode != LobbyPlayerMode.Multiplayer;
                if (room != null)
                {
                    LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
                        _ctx.LobbySession,
                        room,
                        entries: [],
                        localNick,
                        hostName,
                        room?.IsHost == true,
                        resetSlots);
                }
                else
                {
                    _ctx.LobbySession.UIMode = LobbyPlayerMode.Multiplayer;
                    _ctx.LobbySession.AllowHostPlayerOptions = false;
                    _ctx.LobbySession.LocalPlayerName = localNick;
                    _ctx.LobbySession.HostPlayerName = hostName;
                }
            }
            else
            {
                LobbyPlayerSlotUiRules.ConfigureForSkirmish(_ctx.LobbySession, _ctx.SkirmishSession);
                if (_ctx.SkirmishSession.TryLoadSkirmishSettings())
                {
                    skirmishSettingsLoaded = true;

                    // Slot clamp deferred until after ApplyLobby + map re-selection — the
                    // capacity map comes from [Settings] Map=<SHA1> (or the list selection
                    // for legacy saves that pre-date our map persistence).

                    // DX SkirmishLobby.LoadSettings: restore [GameOptions] onto the
                    // lobby controls so saved choices survive re-entering the lobby.
                    // Controls forced by the game mode are skipped (DX parity).
                    GameModeEntry? currentMode = _ctx.GameResources.GetGameModeForFilterIndex(
                        _ctx.LobbySession.FilterIndex);
                    SkirmishGameOptionsSnapshot.Apply(
                        root,
                        _ctx.SkirmishSession.LastLoadedGameOptions,
                        currentMode);
                }
                else if (_ctx.GameResources.Maps.Count > 0)
                {
                    MapEntry defaultMap = _ctx.GameResources.Maps[0];
                    _ctx.SkirmishSession.LoadDefaultSkirmishSlots(defaultMap.MaxPlayers);
                }
                else
                {
                    _ctx.SkirmishSession.LoadDefaultSkirmishSlots();
                }
            }

            IGameSession? lobbySession = windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase)
                ? _ctx.ResolveActiveGameSession() ?? (IGameSession)_ctx.SkirmishSession
                : (IGameSession)_ctx.SkirmishSession;
            LobbyPlayerBindingApplier.Apply(
                root,
                lobbySession,
                _ctx.LobbySession,
                LobbyCatalogService.Instance,
                resources,
                _ctx.MainBehaviors,
                gameRoomProvider: () => _ctx.CnCNet.GameRoom,
                onSlotsMutated: () => _ctx.OnLobbySlotsMutated());

            if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            {
                _ctx.WireCnCNetGameOptionsBridge();
                _ctx.ApplyCnCNetGameRoomPlayers(root);
                _ctx.UpdateCnCNetGameBroadcastListing(root);
            }
        }

        if (MainWindowContext.IsChannelLobbyWindow(windowName))
        {
            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                _ctx.CnCNet.ConnectIfNeeded();
                _ctx.CnCNet.EnsureGameBroadcastChannelsJoined();
                ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service.SyncLobbyStateFromCore();
            }

            GameDataBindingApplier.ApplyChannelLobby(root, _ctx.CnCNet.LobbyState);
        }

        if (!MainWindowContext.IsGameLobbyWindow(windowName))
            return;

        int defaultFilter = _ctx.GameResources.GameModes.Count > 0 ? LobbySessionState.FavoriteFilterIndex + 1 : 0;
        GameDataBindingApplier.ApplyLobby(
            root,
            _ctx.GameResources,
            _ctx.LobbySession,
            resources,
            defaultFilter,
            _ctx.ResolveActiveLobbySlots());

        // DX LoadSettings: after the map list is populated, re-select the map that was
        // saved with the session ([Settings] Map=<SHA1>). Without this the lobby always
        // reopened on the first-listed map, discarding the player's last map choice.
        if (MainWindowContext.IsSkirmishWindow(windowName)
            && !string.IsNullOrEmpty(_ctx.SkirmishSession.LastLoadedMapSha1))
        {
            RestoreSavedMapSelection(root);
        }

        if (MainWindowContext.IsSkirmishWindow(windowName) && skirmishSettingsLoaded)
        {
            FinalizeSkirmishSettingsRestore(root, resources);
        }

        UiNodeViewModel? ddGameMode = MainWindowContext.FindVm(root, "ddGameMode");
        if (ddGameMode != null)
            ddGameMode.SelectionChanged += RefreshLobbyMapList;

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(root, "lbMapList");
        if (lbMapList != null)
        {
            lbMapList.SelectionChanged += () =>
            {
                MapEntry? newMap = _ctx.LobbySession.GetSelectedMap(lbMapList.SelectedIndex);
                if (newMap != null)
                {
                    if (MainWindowContext.IsSkirmishWindow(windowName))
                    {
                        // Requirement: switching maps must remember prior AI adjustments —
                        // keep rows when capacity matches, drop tail when it shrinks,
                        // append defaults when it grows. (DefaultAiSlotPolicy stays for
                        // first entry / fresh fills.)
                        PreserveAiSlotPolicy.ResizeToMapCapacity(
                            _ctx.SkirmishSession,
                            newMap.MaxPlayers,
                            MainWindowContext.ResolveColorCatalog(),
                            LobbyCatalogService.Instance.AiNames);
                    }
                    else
                    {
                        // CnCNet/LAN: host resets AI slots per map capacity, humans preserved.
                        _ctx.ResolveActiveGameSession()?.ResetSlotsForMap(newMap.MaxPlayers);
                    }
                }

                GameDataBindingApplier.ResolveStartInteractionFlags(
                    _ctx.LobbySession.UIMode,
                    _ctx.LobbySession.AllowHostPlayerOptions,
                    out bool canAssign,
                    out bool canSelectLocal);
                GameDataBindingApplier.UpdateMapSelectionDisplay(
                    root,
                    _ctx.LobbySession.VisibleMaps,
                    lbMapList.SelectedIndex,
                    resources,
                    _ctx.ResolveActiveLobbySlots(),
                    canAssign,
                    canSelectLocal);

                ICnCNetGameSession? activeRoom = _ctx.CnCNet.ActiveGameRoom;
                if (activeRoom is IGameSession gameSession)
                {
                    LobbyPlayerBindingApplier.Apply(
                        root,
                        gameSession,
                        _ctx.LobbySession,
                        LobbyCatalogService.Instance,
                        resources,
                        _ctx.MainBehaviors,
                        gameRoomProvider: () => _ctx.CnCNet.GameRoom,
                        onSlotsMutated: () => _ctx.OnLobbySlotsMutated());
                }
                else
                {
                    LobbyPlayerBindingApplier.Apply(
                        root,
                        _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
                        _ctx.LobbySession,
                        LobbyCatalogService.Instance,
                        resources,
                        _ctx.MainBehaviors,
                        gameRoomProvider: () => _ctx.CnCNet.GameRoom,
                        onSlotsMutated: () => _ctx.OnLobbySlotsMutated());
                }

                _ctx.UpdateLaunchButtonState();
                _ctx.RefreshCnCNetGameListing();
            };
        }

        WireMapPreviewStartMarkers(root);

        UiNodeViewModel? tbMapSearch = MainWindowContext.FindVm(root, "tbMapSearch");
        if (tbMapSearch != null)
        {
            tbMapSearch.InputText = string.Empty;
            tbMapSearch.InputTextChanged -= OnLobbyMapSearchChanged;
            tbMapSearch.InputTextChanged += OnLobbyMapSearchChanged;
        }

        int occupied = (_ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession).PlayerSlots.Count(s => s.IsOccupied);
        _ctx.ShowStatus($"Maps: {_ctx.GameResources.Maps.Count}, players: {occupied}, modes: {_ctx.GameResources.GameModes.Count}");
    }

    private void WireMapPreviewStartMarkers(UiNodeViewModel root)
    {
        UiNodeViewModel? previewBox = MainWindowContext.FindVm(root, "MapPreviewBox");
        if (previewBox == null)
            return;

        previewBox.StartMarkerLeftClicked -= OnMapStartMarkerLeftClicked;
        previewBox.StartMarkerRightClicked -= OnMapStartMarkerRightClicked;
        previewBox.StartMarkerLeftClicked += OnMapStartMarkerLeftClicked;
        previewBox.StartMarkerRightClicked += OnMapStartMarkerRightClicked;
    }

    private void OnMapStartMarkerLeftClicked(int startLocation1Based)
    {
        if (_ctx.ActiveRoot == null)
            return;

        IGameSession gameSession = _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession;
        LobbyPlayerSlot[] slots = ResolveMutableLobbySlots(gameSession);
        GameDataBindingApplier.ResolveStartInteractionFlags(
            _ctx.LobbySession.UIMode,
            _ctx.LobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        MapEntry? map = GetCurrentLobbyMap();
        bool enforce = map?.EnforceMaxPlayers ?? false;

        if (canAssign)
        {
            int target = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsOccupied && slots[i].StartIndex == 0)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].IsOccupied)
                    {
                        target = i;
                        break;
                    }
                }
            }

            if (target < 0)
                return;

            if (!MapStartLocationRules.TryApplyHostAssignment(slots, target, startLocation1Based, enforce))
                return;

            BroadcastHostSlotOptionsIfNeeded(gameSession);
            RefreshMapStartMarkersAndPlayerUi();
            return;
        }

        if (canSelectLocal)
        {
            if (!MapStartLocationRules.TryApplyJoinerSelection(
                    slots,
                    _ctx.LobbySession.LocalPlayerName,
                    startLocation1Based,
                    enforce))
            {
                _ctx.ShowStatus($"Starting location {startLocation1Based} is occupied.");
                return;
            }

            int localIndex = Array.FindIndex(slots, s => s.IsHumanLocal);
            if (localIndex < 0)
                localIndex = Array.FindIndex(
                    slots,
                    s => !s.IsAi && s.Name.Equals(_ctx.LobbySession.LocalPlayerName, StringComparison.OrdinalIgnoreCase));

            if (localIndex >= 0 && gameSession is ICnCNetGameSession cnc)
                MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(cnc, localIndex);

            RefreshMapStartMarkersAndPlayerUi();
        }
    }

    private void OnMapStartMarkerRightClicked(int startLocation1Based)
    {
        if (_ctx.ActiveRoot == null)
            return;

        IGameSession gameSession = _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession;
        LobbyPlayerSlot[] slots = ResolveMutableLobbySlots(gameSession);
        GameDataBindingApplier.ResolveStartInteractionFlags(
            _ctx.LobbySession.UIMode,
            _ctx.LobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);

        if (canAssign)
        {
            MapStartLocationRules.ClearOccupantsOf(slots, startLocation1Based);
            BroadcastHostSlotOptionsIfNeeded(gameSession);
            RefreshMapStartMarkersAndPlayerUi();
            return;
        }

        if (canSelectLocal
            && MapStartLocationRules.TryClearLocalIfOwn(slots, _ctx.LobbySession.LocalPlayerName, startLocation1Based))
        {
            int localIndex = Array.FindIndex(slots, s => s.IsHumanLocal);
            if (localIndex >= 0 && gameSession is ICnCNetGameSession cnc)
                MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(cnc, localIndex);
            RefreshMapStartMarkersAndPlayerUi();
        }
    }

    private static LobbyPlayerSlot[] ResolveMutableLobbySlots(IGameSession gameSession)
    {
        if (gameSession.PlayerSlots is LobbyPlayerSlot[] array)
            return array;

        var copy = new LobbyPlayerSlot[gameSession.PlayerSlots.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            if (gameSession.PlayerSlots[i] is LobbyPlayerSlot slot)
                copy[i] = slot;
            else
                copy[i] = new LobbyPlayerSlot();
        }

        return copy;
    }

    private void BroadcastHostSlotOptionsIfNeeded(IGameSession gameSession)
    {
        if (gameSession is not ICnCNetGameSession cnc)
            return;

        if (!_ctx.LobbySession.AllowHostPlayerOptions)
            return;

        MultiplayerSlotCoordinator.HandleHostOptionsEdit(
            cnc,
            _ctx.LobbySession.HostPlayerName,
            LobbyCatalogService.Instance.AiNames);
    }

    private void RefreshMapStartMarkersAndPlayerUi()
    {
        if (_ctx.ActiveRoot == null)
            return;

        ResourceResolver resources = _ctx.GetMainResources();
        IGameSession? gameSession = _ctx.ResolveActiveGameSession();
        if (gameSession != null)
        {
            LobbyPlayerBindingApplier.Apply(
                _ctx.ActiveRoot,
                gameSession,
                _ctx.LobbySession,
                LobbyCatalogService.Instance,
                resources,
                _ctx.MainBehaviors,
                gameRoomProvider: () => _ctx.CnCNet.GameRoom,
                onSlotsMutated: () => _ctx.OnLobbySlotsMutated());
        }
        else
        {
            LobbyPlayerBindingApplier.Apply(
                _ctx.ActiveRoot,
                _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
                _ctx.LobbySession,
                LobbyCatalogService.Instance,
                resources,
                _ctx.MainBehaviors,
                gameRoomProvider: () => _ctx.CnCNet.GameRoom,
                onSlotsMutated: () => _ctx.OnLobbySlotsMutated());
        }

        RefreshCurrentMapStartMarkers();
        _ctx.UpdateLaunchButtonState();
    }

    public void OnLobbySlotsMutated()
    {
        if (_ctx.ActiveRoot == null)
            return;

        RefreshCurrentMapStartMarkers();
        _ctx.UpdateLaunchButtonState();
    }

    public void RefreshCurrentMapStartMarkers()
    {
        if (_ctx.ActiveRoot == null)
            return;

        GameDataBindingApplier.ResolveStartInteractionFlags(
            _ctx.LobbySession.UIMode,
            _ctx.LobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.RefreshMapStartMarkers(
            _ctx.ActiveRoot,
            GetCurrentLobbyMap(),
            _ctx.ResolveActiveLobbySlots(),
            canAssign,
            canSelectLocal);
    }

    private MapEntry? GetCurrentLobbyMap()
    {
        if (_ctx.ActiveRoot == null)
            return null;
        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList");
        return _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
    }

    /// <summary>
    /// Post-<see cref="GameDataBindingApplier.ApplyLobby"/> step for a restored skirmish
    /// session: clamp AI rows to the saved map's capacity (or the currently selected map
    /// for legacy saves), then refresh player rows + start markers.
    /// </summary>
    private void FinalizeSkirmishSettingsRestore(UiNodeViewModel root, ResourceResolver resources)
    {
        MapEntry? capacityMap = ResolveSavedMap() ?? GetCurrentLobbyMap();
        if (capacityMap != null)
        {
            PreserveAiSlotPolicy.ResizeToMapCapacity(
                _ctx.SkirmishSession,
                capacityMap.MaxPlayers,
                MainWindowContext.ResolveColorCatalog(),
                LobbyCatalogService.Instance.AiNames,
                fillToCapacity: false);
        }

        LobbyPlayerBindingApplier.Apply(
            root,
            _ctx.SkirmishSession,
            _ctx.LobbySession,
            LobbyCatalogService.Instance,
            resources,
            _ctx.MainBehaviors,
            gameRoomProvider: () => _ctx.CnCNet.GameRoom,
            onSlotsMutated: () => _ctx.OnLobbySlotsMutated());

        RefreshCurrentMapStartMarkers();
    }

    /// <summary>
    /// Resolves the map saved with the skirmish session ([Settings] Map=SHA1) from the
    /// full catalog — independent of which filter is currently applied, so the capacity
    /// clamp uses the map the player actually played on.
    /// </summary>
    private MapEntry? ResolveSavedMap()
    {
        string sha1 = _ctx.SkirmishSession.LastLoadedMapSha1;
        if (string.IsNullOrEmpty(sha1))
            return null;

        return _ctx.GameResources.Maps.FirstOrDefault(m =>
            m.Sha1.Equals(sha1, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Re-selects the saved map (and its game-mode filter) after the map list has been
    /// populated. DX LoadSettings: filter → list index → scroll into view.
    /// </summary>
    private void RestoreSavedMapSelection(UiNodeViewModel root)
    {
        MapEntry? savedMap = ResolveSavedMap();
        if (savedMap == null)
            return;

        // Prefer the saved game-mode filter so the map is visible in the list.
        string savedFilter = _ctx.SkirmishSession.LastLoadedGameModeFilter;
        if (!string.IsNullOrEmpty(savedFilter))
        {
            UiNodeViewModel? ddGameMode = MainWindowContext.FindVm(root, "ddGameMode");
            if (ddGameMode != null)
            {
                for (int i = 0; i < ddGameMode.ComboItems.Count; i++)
                {
                    if (ddGameMode.ComboItems[i].Equals(savedFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        ddGameMode.SetSelectedIndexSilent(i);
                        _ctx.LobbySession.FilterIndex = i;
                        break;
                    }
                }
            }
        }

        // Refresh the list for the (possibly changed) filter, then select the map.
        _ctx.LobbySession.MapSearchText = string.Empty;
        UiNodeViewModel? tbMapSearch = MainWindowContext.FindVm(root, "tbMapSearch");
        if (tbMapSearch != null)
            tbMapSearch.InputText = string.Empty;

        GameDataBindingApplier.ApplyLobbyMapList(
            root,
            _ctx.GameResources,
            _ctx.LobbySession,
            _ctx.GetMainResources(),
            _ctx.LobbySession.FilterIndex,
            _ctx.ResolveActiveLobbySlots());

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(root, "lbMapList");
        if (lbMapList == null)
            return;

        for (int i = 0; i < _ctx.LobbySession.VisibleMaps.Count; i++)
        {
            if (_ctx.LobbySession.VisibleMaps[i].Sha1.Equals(savedMap.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                // Silent: slot clamping already happened against this exact map.
                lbMapList.SetSelectedIndexSilent(i);

                ResourceResolver resources = _ctx.GetMainResources();
                GameDataBindingApplier.ResolveStartInteractionFlags(
                    _ctx.LobbySession.UIMode,
                    _ctx.LobbySession.AllowHostPlayerOptions,
                    out bool canAssign,
                    out bool canSelectLocal);
                GameDataBindingApplier.UpdateMapSelectionDisplay(
                    root,
                    _ctx.LobbySession.VisibleMaps,
                    i,
                    resources,
                    _ctx.ResolveActiveLobbySlots(),
                    canAssign,
                    canSelectLocal);
                break;
            }
        }
    }

    private void OnLobbyMapSearchChanged()
    {
        if (_ctx.ActiveRoot == null)
            return;

        UiNodeViewModel? tbMapSearch = MainWindowContext.FindVm(_ctx.ActiveRoot, "tbMapSearch");
        if (tbMapSearch == null)
            return;

        _ctx.LobbySession.MapSearchText = tbMapSearch.InputText;
        RefreshLobbyMapList();
    }

    public void TogglePlayerExtraOptionsPanel()
    {
        if (_ctx.ActiveRoot == null)
            return;

        UiNodeViewModel? panel = MainWindowContext.FindVm(_ctx.ActiveRoot, "PlayerExtraOptionsPanel");
        if (panel == null)
        {
            _ctx.ShowStatus("Player extra options panel not found.");
            return;
        }

        panel.IsVisible = !panel.IsVisible;
        _ctx.ShowStatus(panel.IsVisible ? "Player extra options opened." : "Player extra options closed.");
    }
}
