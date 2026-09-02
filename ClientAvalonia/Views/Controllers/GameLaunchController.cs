using Avalonia.Threading;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.GlobalState;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.Lan;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientCore;

namespace ClientAvalonia.Views.Controllers;

internal sealed class GameLaunchController
{
    private readonly MainWindowContext _ctx;

    public GameLaunchController(MainWindowContext ctx)
    {
        _ctx = ctx;
    }

    public bool TryLaunchSkirmish(out string message)
    {
        if (_ctx.ActiveRoot == null || !_ctx.CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
        {
            message = "Not in skirmish lobby.";
            return false;
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

        if (map == null)
        {
            message = "No map selected.";
            return false;
        }

        if (gameMode == null)
        {
            message = "No game mode available.";
            return false;
        }

        string? validationError = SkirmishLaunchValidator.Validate(
            map,
            gameMode,
            _ctx.SkirmishSession.PlayerSlots,
            LobbyCatalogService.Instance.SideNames.Count);
        if (validationError != null)
        {
            message = validationError;
            return false;
        }

        // DX SkirmishLobby.SaveSettings persists the map SHA1 + mode filter so the
        // next session restores the map selection AND clamps AI rows to THAT map's
        // capacity (previously only slots/options were saved — restore clamped
        // against whichever map happened to sort first, silently dropping the 9th
        // slot on restore whenever the first map had fewer players).
        UiNodeViewModel? ddGameModeForSave = MainWindowContext.FindVm(_ctx.ActiveRoot, "ddGameMode");
        string gameModeFilterName = ddGameModeForSave != null
            && ddGameModeForSave.SelectedIndex >= 0
            && ddGameModeForSave.SelectedIndex < ddGameModeForSave.ComboItems.Count
                ? ddGameModeForSave.ComboItems[ddGameModeForSave.SelectedIndex]
                : string.Empty;
        _ctx.SkirmishSession.SaveSkirmishSettings(
            SkirmishGameOptionsSnapshot.Collect(_ctx.ActiveRoot),
            map.Sha1,
            gameModeFilterName);

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Slots = _ctx.SkirmishSession.PlayerSlots,
            SideCount = LobbyCatalogService.Instance.SideNames.Count,
            LobbyRoot = _ctx.ActiveRoot,
        };

        message = "Launching game...";
        _ctx.GameLaunch.BeginLaunch(
            _ctx.Environment,
            new SkirmishLaunchSession(request),
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

        return true;
    }

    /// <summary>
    /// Issue #23: Campaign 与 skirmish 对齐——启动全程走 BeginLaunch（Task.Run 包裹 +
    /// 完成回投 UI），UI 线程不再被 INI 预处理器等待（WaitForIniPreprocessor 最多 10s）阻塞。
    /// </summary>
    public bool TryLaunchCampaign(out string message)
    {
        UiNodeViewModel? campaignRoot = null;
        if (FloatingOverlayLayout.IsCampaignWindow(_ctx.CurrentWindow))
            campaignRoot = _ctx.ActiveRoot;
        else if (_ctx.FloatingOverlayWindow is { } overlayWindow
            && FloatingOverlayLayout.IsCampaignWindow(overlayWindow))
            campaignRoot = _ctx.OverlayRoot;

        if (campaignRoot == null)
        {
            message = "Campaign panel is not open.";
            return false;
        }

        UiNodeViewModel? lbCampaignList = MainWindowContext.FindVm(campaignRoot, "lbCampaignList");
        MissionEntry? mission = _ctx.LobbySession.GetSelectedMission(lbCampaignList?.SelectedIndex ?? 0);
        if (mission == null || mission.IsHeader || string.IsNullOrWhiteSpace(mission.Scenario))
        {
            message = "No mission selected.";
            return false;
        }

        if (!mission.Enabled)
        {
            message = "Selected mission is disabled.";
            return false;
        }

        int difficulty = CampaignOverlayController.ResolveCampaignDifficulty(campaignRoot);
        UserINISettings.Instance.Difficulty.Value = difficulty;

        var request = new CampaignLaunchRequest
        {
            Mission = mission,
            DifficultyIndex = difficulty,
            OverlayRoot = campaignRoot,
        };

        message = "Launching game...";
        _ctx.GameLaunch.BeginLaunch(
            _ctx.Environment,
            new CampaignLaunchSession(request),
            (ok, result) => Dispatcher.UIThread.Post(() =>
            {
                if (!ok)
                {
                    _ctx.ShowStatus($"Launch failed: {result}");
                    ClientDialogService.ShowError(_ctx.GetOwnerWindow(), "Cannot launch game", result);
                    return;
                }

                UserINISettings.Instance.SaveSettings();
                _ctx.ShowStatus(result);
            }));

        return true;
    }

    public bool TryLaunchCnCNetGame(out string message)
    {
        message = string.Empty;
        ICnCNetSession session = _ctx.CnCNet;

        if (session.IsGameRoomJoinPending)
        {
            message = "Still joining the CnCNet game room — please wait.";
            return false;
        }

        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)session).ActiveGameRoomCore
                                     ?? session.GameRoom?.Room;
        if (room == null)
        {
            message = "Not in a CnCNet game room.";
            return false;
        }

        if (_ctx.ActiveRoot != null)
            LobbyPlayerBindingApplier.SyncFromUi(
                _ctx.ActiveRoot,
                _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
                _ctx.LobbySession,
                LobbyCatalogService.Instance.AiNames);

        if (room.IsHost)
        {
            ICnCNetGameSession? hostRoom = session.ActiveGameRoom;
            if (hostRoom != null)
            {
                hostRoom.BroadcastPlayerOptionsFromSlots(
                    string.IsNullOrWhiteSpace(_ctx.LobbySession.HostPlayerName)
                        ? session.LocalNick
                        : _ctx.LobbySession.HostPlayerName,
                    LobbyCatalogService.Instance.AiNames);
            }

            if (!session.TryLaunchHostedGame(out message))
                return false;

            return true;
        }

        UiNodeViewModel? chkAutoReady = _ctx.ActiveRoot != null
            ? MainWindowContext.FindVm(_ctx.ActiveRoot, "chkAutoReady")
            : null;
        bool autoReady = chkAutoReady?.IsChecked == true;

        CnCNetGameRoomPlayer? local = session.GameRoom?.Players
            .FirstOrDefault(p => p.Name.Equals(session.LocalNick, StringComparison.OrdinalIgnoreCase));

        if (autoReady)
        {
            session.SetGameRoomReady(true, autoReady: true);
            message = "Auto ready — waiting for host to launch.";
            _ctx.RefreshCnCNetGameRoomPlayers();
            return true;
        }

        bool ready = !(local?.Ready ?? false);
        session.SetGameRoomReady(ready, autoReady: false);
        message = ready ? "Ready — waiting for host to launch." : "Not ready.";
        _ctx.RefreshCnCNetGameRoomPlayers();
        return true;
    }

    public bool TryLaunchLanGame(out string message)
    {
        message = string.Empty;
        ILanSession lan = AppState.Lan;
        if (lan.ActiveGameRoom is not LanGameRoomSession room)
        {
            message = "Not in a LAN game room.";
            return false;
        }

        if (_ctx.ActiveRoot != null)
        {
            LobbyPlayerBindingApplier.SyncFromUi(
                _ctx.ActiveRoot,
                room,
                _ctx.LobbySession,
                LobbyCatalogService.Instance.AiNames);
        }

        if (!room.IsHost)
        {
            message = "Waiting for LAN host to launch.";
            return true;
        }

        if (!lan.TryLaunchActiveRoom(out message))
            return false;

        return BeginLanProcessLaunch(room, out message);
    }

    public void OpenLoadGameOverlay()
    {
        IReadOnlyList<SinglePlayerSavedGame> saves = SinglePlayerSavedGameCatalog.ListSaves();
        if (saves.Count == 0)
        {
            _ctx.ShowStatus("No saved games found in Saved Games/.");
            return;
        }

        _ = OpenLoadGameOverlayAsync(saves);
    }

    private async Task OpenLoadGameOverlayAsync(IReadOnlyList<SinglePlayerSavedGame> saves)
    {
        SinglePlayerSavedGame? selected =
            await ClientDialogService.ShowLoadGamePickerAsync(_ctx.GetOwnerWindow(), saves);
        if (selected == null)
            return;

        // Issue #23: 异步化——保存文件加载也可能等 INI 预处理器，不能阻塞 UI。
        _ctx.GameLaunch.BeginLaunch(
            _ctx.Environment,
            new SinglePlayerLoadLaunchSession(selected.FileName),
            (ok, message) => Dispatcher.UIThread.Post(() =>
                _ctx.ShowStatus(ok ? message : $"Load failed: {message}")));
    }

    private bool BeginLanProcessLaunch(LanGameRoomSession room, out string message)
    {
        UiNodeViewModel? lbMapList = _ctx.ActiveRoot != null
            ? MainWindowContext.FindVm(_ctx.ActiveRoot, "lbMapList")
            : null;
        MapEntry? map = _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _ctx.GameResources.GetGameModeForFilterIndex(_ctx.LobbySession.FilterIndex);

        if (map == null || gameMode == null)
        {
            message = "Select a map and game mode before launching.";
            return false;
        }

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Slots = room.PlayerSlots,
            SideCount = LobbyCatalogService.Instance.SideNames.Count,
            LobbyRoot = _ctx.ActiveRoot,
        };

        var startInfo = new LanStartGameInfo
        {
            UniqueGameId = room.UniqueGameId,
            IsHost = room.IsHost,
        };

        // Issue #23: 与 skirmish/campaign 对齐，走 BeginLaunch 避免阻塞 UI。
        message = "Launching game...";
        _ctx.GameLaunch.BeginLaunch(
            _ctx.Environment,
            new MultiplayerLaunchSession(request, cncNet: null, roomPlayers: null, gameOptions: null, lan: startInfo),
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

        return true;
    }
}
