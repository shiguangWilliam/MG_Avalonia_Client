using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.Views.Controllers;

internal sealed class CnCNetLobbyController
{
    private readonly MainWindowContext _ctx;

    public CnCNetLobbyController(MainWindowContext ctx)
    {
        _ctx = ctx;
    }

    public void UpdateCnCNetGameBroadcastListing(UiNodeViewModel root)
    {
        ICnCNetSession session = _ctx.CnCNet;
        if (session.IsGameRoomJoinPending)
            return;

        ICnCNetGameSession? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(root, "lbMapList");
        MapEntry? map = _ctx.LobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _ctx.GameResources.GetGameModeForFilterIndex(_ctx.LobbySession.FilterIndex);

        _ctx.CnCNet.UpdateGameRoomListing(
            map?.UntranslatedName ?? string.Empty,
            gameMode?.UntranslatedUIName ?? string.Empty,
            map?.Sha1 ?? string.Empty);
    }

    public void PushCnCNetHostLobbyState()
    {
        ICnCNetSession session = _ctx.CnCNet;
        if (session.IsGameRoomJoinPending)
            return;

        ICnCNetGameSession? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        if (_ctx.ActiveRoot != null)
            LobbyPlayerBindingApplier.SyncFromUi(
                _ctx.ActiveRoot,
                _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
                _ctx.LobbySession,
                LobbyCatalogService.Instance.AiNames);

        ICnCNetGameSession? hostRoom = _ctx.CnCNet.ActiveGameRoom;
        if (hostRoom != null)
        {
            hostRoom.BroadcastPlayerOptionsFromSlots(
                string.IsNullOrWhiteSpace(_ctx.LobbySession.HostPlayerName)
                    ? _ctx.CnCNet.LocalNick
                    : _ctx.LobbySession.HostPlayerName,
                LobbyCatalogService.Instance.AiNames);
        }

        RefreshCnCNetGameListing();
    }

    public void RefreshCnCNetGameListing()
    {
        if (_ctx.ActiveRoot != null
            && _ctx.CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
        {
            UpdateCnCNetGameBroadcastListing(_ctx.ActiveRoot);
        }
    }

    public async void TryJoinSelectedCnCNetGame()
    {
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service;

        if (session.IsGameRoomJoinPending)
        {
            _ctx.ShowStatus("Joining game room — please wait...");
            return;
        }

        if (session.ActiveGameRoom != null)
        {
            _ctx.ShowStatus("Already in a game room.");
            _ctx.NavigateTo("CnCNetGameLobby");
            return;
        }

        if (_ctx.ActiveRoot != null)
            GameDataBindingApplier.SyncChannelGameSelection(_ctx.ActiveRoot, session.LobbyState);

        CnCNetHostedGameSummary? game = session.ResolveSelectedGameForJoin();
        if (game == null)
        {
            _ctx.ShowStatus("Select a game from the list first.");
            return;
        }

        string? password = null;
        if (game.RequiresPassword)
        {
            password = await ClientDialogService.ShowPasswordPromptAsync(_ctx.GetOwnerWindow(), game.RoomName);
            if (string.IsNullOrWhiteSpace(password))
            {
                _ctx.ShowStatus("Join cancelled.");
                return;
            }
        }

        if (!session.TryJoinGame(game, password, out string message))
        {
            _ctx.ShowStatus(message);
            return;
        }

        EnterCnCNetGameLobbyConnecting();
        _ctx.ShowStatus(message);
    }

    public void EnterCnCNetGameLobbyConnecting()
    {
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore;
        if (room == null)
            return;

        string localNick = _ctx.CnCNet.LocalNick;
        string hostName = string.IsNullOrWhiteSpace(room.HostName) ? localNick : room.HostName;

        ICnCNetGameSession? session = _ctx.CnCNet.ActiveGameRoom;
        if (session == null)
        {
            Rampastring.Tools.Logger.Log("EnterCnCNetGameLobbyConnecting: ActiveGameRoom null — skipping slot setup.");
            return;
        }

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _ctx.LobbySession,
            session,
            entries: [],
            localNick,
            hostName,
            room.IsHost,
            resetSlots: true);

        if (room.IsHost)
            session.InitHostSlots(localNick);

        if (!_ctx.CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            _ctx.NavigateTo("CnCNetGameLobby");
        else if (_ctx.ActiveRoot != null)
            ApplyCnCNetGameLobbyConnectingState(_ctx.ActiveRoot, room);
    }

    private void ApplyCnCNetGameLobbyConnectingState(UiNodeViewModel root, CnCNetActiveGameRoom room)
    {
        ResourceResolver resources = _ctx.GetMainResources();
        LobbyPlayerBindingApplier.Apply(
            root,
            _ctx.ResolveActiveGameSession() ?? _ctx.SkirmishSession,
            _ctx.LobbySession,
            LobbyCatalogService.Instance,
            resources,
            _ctx.MainBehaviors,
            gameRoomProvider: () => _ctx.CnCNet.GameRoom,
            onSlotsMutated: () => _ctx.OnLobbySlotsMutated());
        _ctx.WireCnCNetGameOptionsBridge();
        CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _ctx.MainBehaviors, isJoiner: !room.IsHost);
        _ctx.UpdateLaunchButtonState();
    }

    public void OnCnCNetStateChanged()
    {
        int count = _ctx.CnCNet.OnlinePlayerCount;
        _ctx.BindingSession.State.SetOnlinePlayerCount(count);

        if (_ctx.ActiveRoot != null && MainWindowContext.IsChannelLobbyWindow(_ctx.CurrentWindow))
        {
            GameDataBindingApplier.ApplyChannelLobby(_ctx.ActiveRoot, _ctx.CnCNet.LobbyState);
            if (!_ctx.IsFloatingOverlayOpen())
                _ctx.ShowStatus($"CnCNet: {_ctx.CnCNet.LobbyState.ConnectionStatus}");
        }

        OnPrivateMessagingRefreshRequested?.Invoke();

        if (_ctx.ActiveRoot != null
            && _ctx.CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
        {
            OnGameRoomUiRefreshRequested?.Invoke(_ctx.ActiveRoot);
        }

        if (_ctx.CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && _ctx.ActiveRoot != null)
            StateBindingApplier.Apply(_ctx.ActiveRoot, _ctx.BindingSession.State, "MainMenu");

        _ctx.UpdateTopBar();
    }

    /// <summary>Wired by MainWindow to CnCNetGameRoomController.RefreshCnCNetGameRoomUiFromSession.</summary>
    public Action<UiNodeViewModel>? OnGameRoomUiRefreshRequested { get; set; }

    /// <summary>Wired by MainWindow for private messaging panel refresh during state changes.</summary>
    public Action? OnPrivateMessagingRefreshRequested { get; set; }
}
