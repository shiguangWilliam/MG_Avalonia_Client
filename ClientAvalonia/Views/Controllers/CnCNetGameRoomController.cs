using ClientAvalonia.IniUi;
using Avalonia.Threading;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using Rampastring.Tools;

namespace ClientAvalonia.Views.Controllers;

internal sealed class CnCNetGameRoomController
{
    private readonly MainWindowContext _ctx;

    public CnCNetGameRoomController(MainWindowContext ctx)
    {
        _ctx = ctx;
    }

    public void RefreshCnCNetGameRoomPlayers()
    {
        if (_ctx.ActiveRoot != null
            && _ctx.CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
        {
            ApplyCnCNetGameRoomPlayers(_ctx.ActiveRoot);
        }
    }

    public void OnCnCNetGameRoomJoined(ICnCNetGameSession room)
    {
        _ctx.LastAppliedGameRoomRevision = -1;

        if (!_ctx.CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
            _ctx.NavigateTo(WindowKind.CnCNetGameLobby);
        else if (_ctx.ActiveRoot != null)
        {
            WireCnCNetGameOptionsBridge();
            ApplyCnCNetGameRoomPlayers(_ctx.ActiveRoot);
            GameDataBindingApplier.ApplyGameRoomChat(_ctx.ActiveRoot, _ctx.CnCNet.GameRoom);
        }

        CnCNetGameRoomSession? gameRoom = _ctx.CnCNet.GameRoom;
        if (gameRoom != null)
        {
            gameRoom.ChangeTunnelRequested -= OnChangeTunnelRequested;
            gameRoom.ChangeTunnelRequested += OnChangeTunnelRequested;
        }

        if (room.IsHost)
            _ctx.PushCnCNetHostLobbyState();

        _ctx.ShowStatus($"Entered \"{room.RoomName}\".");
    }

    /// <summary>Wired to OverlayHostController.OpenGameRoomTunnelSelection.</summary>
    public Action? OpenGameRoomTunnelSelection { get; set; }

    private void OnChangeTunnelRequested() => OpenGameRoomTunnelSelection?.Invoke();

    public void OnCnCNetGameRoomJoinFailed(string message)
    {
        _ctx.ShowStatus(message);
        if (_ctx.CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
            _ctx.NavigateTo("CnCNetLobby");
    }

    public void OnCnCNetGameRoomHostAbandoned()
    {
        ClearCnCNetGameOptionsBridge();
        _ctx.ShowStatus("The game host has abandoned the game.");
        _ctx.CnCNet.EnsureGameBroadcastChannelsJoined();
        _ctx.NavigateTo("CnCNetLobby");
    }

    public void OnCnCNetGameStarting(CnCNetStartGameInfo startInfo)
    {
        Logger.Log($"CnCNet GameStarting: gameId={startInfo.UniqueGameId}, tunnel={startInfo.Tunnel.Address}:{startInfo.Tunnel.Port}, localPort={startInfo.LocalPlayerPort}, window={_ctx.CurrentWindow}");

        if (_ctx.ActiveRoot == null || !_ctx.CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log("CnCNet GameStarting: aborted —?not in CnCNetGameLobby.");
            return;
        }

        UiNodeViewModel? ddGameMode = MainWindowContext.FindVm(_ctx.ActiveRoot, "ddGameMode");
        if (ddGameMode != null)
            _ctx.LobbySession.FilterIndex = ddGameMode.SelectedIndex;

        LobbyPlayerBindingApplier.SyncFromUi(
            _ctx.ActiveRoot,
            _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
            _ctx.LobbySession,
            LobbyCatalogService.Instance.AiNames);

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList");
        MapEntry? map = _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _ctx.GameResources.GetGameModeForFilterIndex(_ctx.LobbySession.FilterIndex);

        if (map == null || gameMode == null)
        {
            Logger.Log($"CnCNet GameStarting: map/gameMode missing (map={map?.DisplayName ?? "null"}, mode={gameMode?.DisplayName ?? "null"}, visibleMaps={_ctx.LobbySession.VisibleMaps.Count}, filter={_ctx.LobbySession.FilterIndex}).");
            _ctx.ShowStatus("Cannot launch: map or game mode missing.");
            ClientDialogService.ShowError(_ctx.GetOwnerWindow(), "Cannot launch game", "Map or game mode is missing. Reselect a map and try again.");
            return;
        }

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Slots = _ctx.SkirmishSession.PlayerSlots,
            SideCount = LobbyCatalogService.Instance.SideNames.Count,
            LobbyRoot = _ctx.ActiveRoot,
        };

        Logger.Log($"CnCNet GameStarting: launching {map.DisplayName} / {gameMode.DisplayName} via Syringe.");

        _ctx.ShowStatus("Launching game...");

        var startSnapshot = startInfo;
        var roomPlayers = _ctx.CnCNet.GameRoom?.Players;
        var gameOptions = CollectCnCNetGameOptions();

        _ctx.GameLaunch.BeginLaunch(
            _ctx.Environment,
            new MultiplayerLaunchSession(request, startSnapshot, roomPlayers, gameOptions),
            (ok, result) => Dispatcher.UIThread.Post(() =>
            {
                if (!ok)
                {
                    _ctx.ShowStatus($"Launch failed: {result}");
                    ClientDialogService.ShowError(_ctx.GetOwnerWindow(), "Cannot launch game", result);
                    return;
                }

                _ctx.ShowStatus(result);
            }));
    }

    public void WireCnCNetGameOptionsBridge()
    {
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service;
        // Issue #38 stage 5: Provider/ControlCounts are invoked on the IRC reader thread (GO broadcast),
        // but they enumerate the UI tree -- they MUST be marshalled back to the UI thread; provider results are cached as a volatile snapshot
        // so the IRC thread can always read the latest state synchronously without blocking for long.
        session.GameOptionsControlCounts = MarshalToUi(
            () => CnCNetGameOptionsUiBridge.GetControlCounts(_ctx.ActiveRoot));
        session.GameOptionsProvider = MarshalProviderSnapshot;
        session.GameOptionsReceiver = ApplyCnCNetGameOptionsFromHost;
        WireHostGameOptionChangeBroadcast();
        session.GameRoom?.TryFlushPendingGameOptions();
    }

    public void ClearCnCNetGameOptionsBridge()
    {
        UnwireHostGameOptionChangeBroadcast();
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service;
        session.GameOptionsControlCounts = null;
        session.GameOptionsProvider = null;
        session.GameOptionsReceiver = null;
        _lastProviderSnapshot = null;
    }

    private volatile CnCNetGameOptionsState? _lastProviderSnapshot;

    private CnCNetGameOptionsState MarshalProviderSnapshot()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _lastProviderSnapshot = CollectCnCNetGameOptions();
            return _lastProviderSnapshot;
        }

        // Already on UI thread once: reuse cached snapshot (races cost one stale broadcast,
        // next option change re-syncs —?preferable to re-posting while UI is busy).
        _lastProviderSnapshot ??= Dispatcher.UIThread.InvokeAsync(CollectCnCNetGameOptions)
            .GetAwaiter().GetResult();
        return _lastProviderSnapshot;
    }

    private static Func<T> MarshalToUi<T>(Func<T> read)
        => () => Dispatcher.UIThread.CheckAccess()
            ? read()
            : Dispatcher.UIThread.InvokeAsync(read).GetAwaiter().GetResult();

    private void WireHostGameOptionChangeBroadcast()
    {
        UnwireHostGameOptionChangeBroadcast();
        if (_ctx.ActiveRoot == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(_ctx.ActiveRoot);

        foreach (UiNodeViewModel chk in checkBoxes)
            chk.CheckedChanged += OnHostGameOptionControlChanged;

        foreach (UiNodeViewModel dd in dropDowns)
            dd.SelectionChanged += OnHostGameOptionControlChanged;
    }

    private void UnwireHostGameOptionChangeBroadcast()
    {
        if (_ctx.ActiveRoot == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(_ctx.ActiveRoot);

        foreach (UiNodeViewModel chk in checkBoxes)
            chk.CheckedChanged -= OnHostGameOptionControlChanged;

        foreach (UiNodeViewModel dd in dropDowns)
            dd.SelectionChanged -= OnHostGameOptionControlChanged;
    }

    private void OnHostGameOptionControlChanged()
    {
        CnCNetGameRoomSession? room = _ctx.CnCNet.GameRoom;
        if (room is not { IsHost: true, IsLocalJoined: true })
            return;

        _ctx.RefreshCnCNetGameListing();
    }

    public CnCNetGameOptionsState CollectCnCNetGameOptions()
    {
        UiNodeViewModel? lbMapList = _ctx.ActiveRoot != null
            ? MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList")
            : null;
        MapEntry? map = _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _ctx.GameResources.GetGameModeForFilterIndex(_ctx.LobbySession.FilterIndex);
        CnCNetGameRoomSession? room = _ctx.CnCNet.GameRoom;
        CnCNetGameOptionsState state = CnCNetGameOptionsUiBridge.Collect(
            _ctx.ActiveRoot,
            map,
            gameMode,
            room?.RandomSeed ?? Random.Shared.Next(),
            room?.RemoveStartingLocations ?? false);

        if (room == null)
            return state;

        return new CnCNetGameOptionsState
        {
            CheckBoxValues = state.CheckBoxValues,
            DropDownIndices = state.DropDownIndices,
            MapOfficial = state.MapOfficial,
            MapSha1 = state.MapSha1,
            GameModeName = state.GameModeName,
            MapUntranslatedName = state.MapUntranslatedName,
            FrameSendRate = room.FrameSendRate,
            MaxAhead = room.MaxAhead,
            ProtocolVersion = room.ProtocolVersion,
            RandomSeed = state.RandomSeed,
            RemoveStartingLocations = state.RemoveStartingLocations,
        };
    }

    public void ApplyCnCNetGameOptionsFromHost(CnCNetGameOptionsState state)
    {
        void Apply()
        {
            if (_ctx.ActiveRoot == null)
                return;

            CnCNetGameOptionsUiBridge.Apply(_ctx.ActiveRoot, state, _ctx.GameResources, _ctx.LobbySession);
            _ctx.RefreshLobbyMapList();
            if (_ctx.CnCNet.GameRoom is { IsHost: true })
                _ctx.RefreshCnCNetGameListing();
            else
                _ctx.UpdateLaunchButtonState();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public void RefreshCnCNetGameRoomUiFromSession(UiNodeViewModel root)
    {
        ICnCNetGameSession? currentSession = _ctx.CnCNet.ActiveGameRoom;
        if (currentSession != null && currentSession.Revision == _ctx.LastAppliedGameRoomRevision)
            return;

        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore;
        if (room == null)
            return;

        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: false);
            GameDataBindingApplier.ApplyGameRoomChat(root, _ctx.CnCNet.GameRoom);
            if (currentSession != null)
                _ctx.LastAppliedGameRoomRevision = currentSession.Revision;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet game room UI refresh failed: {ex.Message}");
            Logger.Log(ex.ToString());
        }
    }

    public void ApplyCnCNetGameRoomPlayers(UiNodeViewModel root)
    {
        ICnCNetGameSession? currentSession = _ctx.CnCNet.ActiveGameRoom;
        if (currentSession != null && currentSession.Revision == _ctx.LastAppliedGameRoomRevision)
            return;

        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore;
        if (room == null)
            return;

        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: true);
            if (currentSession != null)
                _ctx.LastAppliedGameRoomRevision = currentSession.Revision;
        }
        finally
        {
        }
    }

    private void ApplyCnCNetGameRoomPlayersCore(UiNodeViewModel root, CnCNetActiveGameRoom room, bool updateStatus)
    {
        ICnCNetGameSession? session = _ctx.CnCNet.ActiveGameRoom;
        CnCNetGameRoomSession? gameRoom = _ctx.CnCNet.GameRoom;
        string localNick = _ctx.CnCNet.LocalNick;
        string hostName = room.HostName;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = gameRoom?.HostName ?? localNick;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = localNick;

        IReadOnlyList<CnCNetGameRoomPlayer> entries = gameRoom?.Players ?? [];

        if (session == null)
        {
            Logger.Log("ApplyCnCNetGameRoomPlayersCore: ActiveGameRoom null —?skipping.");
            return;
        }

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _ctx.LobbySession,
            session,
            entries,
            localNick,
            hostName,
            room.IsHost,
            resetSlots: false);

        if (room.IsHost)
            session.ReorderHostFirst(hostName, localNick);
        else
            session.MarkLocalHuman(localNick);

        ResourceResolver resources = _ctx.GetMainResources();
        LobbyPlayerBindingApplier.Apply(
            root,
            (IGameSession)session,
            _ctx.LobbySession,
            LobbyCatalogService.Instance,
            resources,
            _ctx.MainBehaviors,
            gameRoomProvider: () => _ctx.CnCNet.GameRoom,
            onSlotsMutated: () => _ctx.OnLobbySlotsMutated());

        bool locked = gameRoom?.Locked ?? false;
        LobbyPlayerStatusApplier.Apply(
            root,
            session.PlayerSlots,
            _ctx.LobbySession.UIMode,
            resources,
            _ctx.MainBehaviors,
            entries,
            locked,
            room.IsHost);

        CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _ctx.MainBehaviors, isJoiner: !room.IsHost);
        CnCNetGameLobbyUiHelper.UpdateManualReadyLabel(root, isJoiner: !room.IsHost);
        ApplyLockGameButtonLabel(root, room.IsHost, locked);
        _ctx.UpdateLaunchButtonState();
        _ctx.RefreshCurrentMapStartMarkers();

        if (updateStatus)
        {
            _ctx.ShowStatus(room.IsHost
                ? $"Hosting \"{room.RoomName}\" —?waiting for players."
                : $"Joined \"{room.RoomName}\" —?waiting for host.");
        }
    }

    private static void ApplyLockGameButtonLabel(UiNodeViewModel root, bool isHost, bool locked)
    {
        if (!isHost)
            return;

        MainWindowContext.FindVm(root, "btnLockGame")?.SetDisplayText(locked ? "Unlock Game" : "Lock Game");
    }
}
