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

        _ctx.SkirmishSession.SaveSkirmishSettings();

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

    public bool TryLaunchCampaign(out string message)
    {
        UiNodeViewModel? campaignRoot = null;
        if (_ctx.CurrentWindow.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase))
            campaignRoot = _ctx.ActiveRoot;
        else if (_ctx.FloatingOverlayWindow?.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase) == true)
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

        bool launched = _ctx.GameLaunch.TryLaunchCampaign(
            _ctx.Environment,
            new CampaignLaunchRequest
            {
                Mission = mission,
                DifficultyIndex = difficulty,
                OverlayRoot = campaignRoot,
            },
            out message,
            _ctx.GetOwnerWindow());

        if (launched)
            UserINISettings.Instance.SaveSettings();

        return launched;
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

        bool ok = _ctx.GameLaunch.TryLaunch(
            _ctx.Environment,
            new SinglePlayerLoadLaunchSession(selected.FileName),
            out string message,
            _ctx.GetOwnerWindow());
        _ctx.ShowStatus(ok ? message : $"Load failed: {message}");
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

        bool ok = _ctx.GameLaunch.TryLaunchLan(
            _ctx.Environment,
            request,
            startInfo,
            out message,
            _ctx.GetOwnerWindow());
        return ok;
    }
}
