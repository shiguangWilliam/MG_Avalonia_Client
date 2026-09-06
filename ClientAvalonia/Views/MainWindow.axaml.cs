using ClientAvalonia.IniUi;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClientAvalonia.Core;
using ClientAvalonia.Controls;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientCore;
using ClientCore.Extensions;
using ClientCore.Settings;
using ClientAvalonia.Animation;
using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
using ClientAvalonia.Views.Controllers;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Views;

public partial class MainWindow : Window, IUiNavigationHost
{
    private const double StatusBarHeight = 24;

    private readonly MainWindowContext _ctx;
    private readonly OverlayHostController _overlay;
    private readonly CnCNetLobbyController _cncLobby;
    private readonly CnCNetGameRoomController _cncGameRoom;
    private readonly LobbyMapController _lobbyMaps;
    private readonly CampaignOverlayController _campaign;
    private readonly GameLaunchController _launch;
    private readonly ClientUpdateService _updateService = new();
    private readonly Stack<string> _navStack = new();
    private UiViewModelFactory? _mainViewModelFactory;
    private bool _restoreWindowAfterGame;
    private bool _reopenOptionsAfterStyleSwitch;

    public string CurrentWindow
    {
        get => _ctx.CurrentWindow;
        private set => _ctx.CurrentWindow = value;
    }

    public bool IsFloatingOverlayOpen => _overlay.IsOpen;

    public string? FloatingOverlayWindow => _ctx.FloatingOverlayWindow;

    public bool IsOptionsOverlayOpen
        => IsFloatingOverlayOpen
           && _ctx.FloatingOverlayWindow?.Equals(WindowKind.OptionsWindow, StringComparison.OrdinalIgnoreCase) == true;

    public bool IsGameCreationOverlayOpen
        => IsFloatingOverlayOpen
           && _ctx.FloatingOverlayWindow?.Equals("GameCreationWindow", StringComparison.OrdinalIgnoreCase) == true;

    public UiNodeViewModel? ActiveRoot => _ctx.ActiveRoot;

    public UiNodeViewModel? OverlayRoot => _ctx.OverlayRoot;

    public MainWindow()
    {
        var skirmishSession = new SkirmishSession();
        var cncnet = MainWindowContext.ResolveCnCNetSession();
        var environment = ClientEnvironment.Discover();
        var bindingSession = new UiBindingSession(environment);
        var mainBehaviors = new BehaviorRegistry();
        var overlayBehaviors = new BehaviorRegistry();
        var gameLaunch = new GameLaunchService();
        var gameResources = MainWindowContext.ResolveGameResourceCatalog();
        var lobbySession = new LobbySessionState();

        _ctx = new MainWindowContext(
            mainBehaviors,
            overlayBehaviors,
            bindingSession,
            gameLaunch,
            gameResources,
            lobbySession,
            skirmishSession,
            cncnet)
        {
            Environment = environment,
        };

        InitializeComponent();
        UpdateSolarSystemBackdrop();

        _overlay = new OverlayHostController(
            _ctx,
            PART_OverlayPanel,
            PART_OverlayView,
            PART_OverlayRawView,
            PART_FloatingOverlay,
            PART_RootView,
            PART_OverlayBackdrop);
        _cncLobby = new CnCNetLobbyController(_ctx);
        _cncGameRoom = new CnCNetGameRoomController(_ctx);
        _lobbyMaps = new LobbyMapController(_ctx);
        _campaign = new CampaignOverlayController(_ctx);
        _launch = new GameLaunchController(_ctx);

        WireContextCallbacks();

        _ctx.GameLaunch.StatusChanged += msg => Dispatcher.UIThread.Post(() => ShowStatus(msg));
        _ctx.GameLaunch.GameProcessStarted += () => Dispatcher.UIThread.Post(OnGameProcessStarted);
        _ctx.GameLaunch.GameProcessExited += () => Dispatcher.UIThread.Post(OnGameProcessExited);
        _updateService.EnsureHandlersRegistered();
        _updateService.StatusChanged += OnUpdateStatusChanged;
        ClientStartupService.LocalVersionsChecked += OnLocalVersionsChecked;
        _ctx.GameResources.Loaded += OnGameResourcesLoaded;
        _ctx.CnCNet.StateChanged += () => _cncLobby.OnCnCNetStateChanged();
        _ctx.CnCNet.PrivateMessageArrived += OnPrivateMessageArrived;
        _ctx.CnCNet.GameRoomJoined += room => _cncGameRoom.OnCnCNetGameRoomJoined(room);
        _ctx.CnCNet.GameRoomJoinFailed += msg => _cncGameRoom.OnCnCNetGameRoomJoinFailed(msg);
        _ctx.CnCNet.GameStarting += info => _cncGameRoom.OnCnCNetGameStarting(info);
        _ctx.CnCNet.GameRoomHostAbandoned += () => _cncGameRoom.OnCnCNetGameRoomHostAbandoned();
        if (_ctx.CnCNet is CnCNetSessionServiceAdapter adapter)
            adapter.Service.WafAlertRaised += OnCnCNetWafAlert;
        _ctx.CnCNet.EnsureStarted();

        KeyDown += OnKeyDown;
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
        PART_TopBarHost.Bar.BindNavigation(NavigateTo, LogoutToMainMenu, () => OpenPrivateMessagingOverlay());
        _updateService.RefreshInitialStatus();
    }

    private void WireContextCallbacks()
    {
        _ctx.ShowStatus = ShowStatus;
        _ctx.NavigateTo = NavigateTo;
        _ctx.CloseFloatingOverlaySilently = () => _overlay.CloseFloatingOverlay();
        _ctx.UpdateLaunchButtonState = () => UpdateLaunchButtonState();
        _ctx.UpdateTopBar = UpdateTopBar;
        _ctx.GetOwnerWindow = () => this;
        _ctx.IsFloatingOverlayOpen = () => IsFloatingOverlayOpen;
        _ctx.IsOptionsOverlayOpen = () => IsOptionsOverlayOpen;
        _ctx.RefreshLobbyMapList = () => _lobbyMaps.RefreshLobbyMapList();
        _ctx.RefreshCnCNetGameListing = () => _cncLobby.RefreshCnCNetGameListing();
        _ctx.RefreshCnCNetGameRoomPlayers = () => _cncGameRoom.RefreshCnCNetGameRoomPlayers();
        _ctx.PushCnCNetHostLobbyState = () => _cncLobby.PushCnCNetHostLobbyState();
        _ctx.WireCnCNetGameOptionsBridge = () => _cncGameRoom.WireCnCNetGameOptionsBridge();
        _ctx.ClearCnCNetGameOptionsBridge = () => _cncGameRoom.ClearCnCNetGameOptionsBridge();
        _ctx.OnLobbySlotsMutated = () => _lobbyMaps.OnLobbySlotsMutated();
        _ctx.RefreshCurrentMapStartMarkers = () => _lobbyMaps.RefreshCurrentMapStartMarkers();
        _ctx.ApplyCnCNetGameRoomPlayers = root => _cncGameRoom.ApplyCnCNetGameRoomPlayers(root);
        _ctx.UpdateCnCNetGameBroadcastListing = root => _cncLobby.UpdateCnCNetGameBroadcastListing(root);
        _ctx.ApplyCampaignOverlay = (root, filter) => _campaign.ApplyCampaignOverlay(root, filter);
        _ctx.CollectCnCNetGameOptions = () => _cncGameRoom.CollectCnCNetGameOptions();

        _cncLobby.OnGameRoomUiRefreshRequested = root => _cncGameRoom.RefreshCnCNetGameRoomUiFromSession(root);
        _cncLobby.OnPrivateMessagingRefreshRequested = RefreshPrivateMessagingPanelIfOpen;
        _cncGameRoom.OpenGameRoomTunnelSelection = () => _overlay.OpenGameRoomTunnelSelection();
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        var resources = new ResourceResolver();
        resources.ConfigureForGame(_ctx.Environment);

        PART_StartupLoading.IsVisible = true;
        ClientFileIntegrityResult? integrityResult = null;
        await StartupLoadingView.RunStartupSequenceAsync(
            PART_StartupLoading,
            resources,
            reportStatus =>
            {
                integrityResult = ClientFileIntegrityService.Verify(reportStatus: reportStatus);
                _ctx.GameResources.EnsureLoaded();
                return Task.CompletedTask;
            }).ConfigureAwait(true);

        PART_StartupLoading.IsVisible = false;

        if (integrityResult is { Success: false })
        {
            await ClientDialogService.ShowErrorAsync(
                this,
                "File verification failed".L10N("Client:Main:FileVerifyFailed"),
                integrityResult.Message).ConfigureAwait(true);
            return;
        }

        NavigateTo(WindowKind.MainMenu);
        TryAutomaticCnCNetLogin();
    }

    private void TryAutomaticCnCNetLogin()
    {
        try
        {
            if (!UserINISettings.Instance.AutomaticCnCNetLogin)
                return;

            if (NameValidator.IsNameValid(MainWindowContext.ResolvePlayerName(), out _) != NameValidationError.None)
            {
                Logger.Log("AutomaticCnCNetLogin: skipping — player name is not valid.");
                return;
            }

            Logger.Log("AutomaticCnCNetLogin: connecting CnCNet session at startup.");
            _ctx.CnCNet.ConnectIfNeeded();
        }
        catch (Exception ex)
        {
            Logger.Log($"AutomaticCnCNetLogin failed: {ex.Message}");
        }
    }

    public void NavigateTo(string windowName) => NavigateTo(windowName, fromBack: false);

    private void NavigateTo(string windowName, bool fromBack)
    {
        if (FloatingOverlayLayout.IsOverlayWindow(windowName))
        {
            OpenFloatingOverlay(windowName);
            return;
        }

        CloseFloatingOverlaySilently();

        if (!fromBack
            && !string.IsNullOrEmpty(CurrentWindow)
            && !CurrentWindow.Equals(windowName, StringComparison.OrdinalIgnoreCase))
        {
            _navStack.Push(CurrentWindow);
        }

        string? iniPath;
        string iniSection;
        if (_ctx.Environment.ResolveWindowLoadTarget(windowName) is { } target)
        {
            iniPath = target.IniPath;
            iniSection = target.SectionName;
        }
        else
        {
            iniPath = _ctx.Environment.ResolveWindowIni(windowName);
            if (iniPath == null)
            {
                ShowStatus($"INI not found for window: {windowName}");
                return;
            }

            iniSection = windowName;
        }

        try
        {
            UiBehaviorCatalog.RegisterForWindow(_ctx.MainBehaviors, windowName, this);

            _ctx.MainEngine = LayoutEngine.CreateForWindow(_ctx.Environment, iniPath, iniSection);
            _mainViewModelFactory = new UiViewModelFactory(_ctx.MainEngine.Resources, _ctx.MainBehaviors);

            UiNodeTree tree = _ctx.MainEngine.LoadWindow(iniPath, iniSection);
            UiNodeViewModel vm = _mainViewModelFactory.CreateTree(tree);
            IniBehaviorApplier.Apply(vm, _ctx.MainBehaviors, this, _ctx.ResolveIniActionCatalog());

            if (MainWindowContext.IsGameLobbyWindow(windowName))
                _ctx.BindingSession.State.SetCanLaunchGame(true);

            _ctx.BindingSession.ApplyToTree(vm, windowName);
            _ctx.ActiveRoot = vm;
            DxTransitions.SlideSwap(PART_RootView, () => PART_RootView.Content = vm);
            ApplyViewportSize(_ctx.MainEngine.Context.Width, _ctx.MainEngine.Context.Height);
            CurrentWindow = windowName;

            // Shared 3D backdrop: move the camera to this panel's pose.
            SolarSystemDirector.OnNavigateTo(windowName);

            if (FloatingOverlayLayout.IsCampaignWindow(windowName))
                _campaign.ApplyCampaignOverlay(vm, CampaignSideFilter.All);

            try
            {
                if (windowName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
                {
                    _lobbyMaps.ApplyLobbyData(vm, windowName);
                    UpdateLaunchButtonState(vm);
                }

                Title = $"ClientAvalonia — {windowName} ({_ctx.Environment.ThemeFolderPath.TrimEnd('/')}) {_ctx.MainEngine.Context.Width}×{_ctx.MainEngine.Context.Height}";
                ShowStatus($"{windowName}: {tree.Root.Children.Count} root controls, {tree.AllNodes().Count()} nodes");
                UpdateTopBar();
            }
            catch (Exception bindEx)
            {
                Title = $"ClientAvalonia — {windowName} (binding warning)";
                ShowStatus($"{windowName} binding: {bindEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Title = "ClientAvalonia — INI load error";
            ShowStatus($"{windowName}: {ex.Message}");
        }
    }

    public void OpenFloatingOverlay(string windowName)
        => _overlay.OpenFloatingOverlay(windowName);

    public void CloseFloatingOverlay()
        => _overlay.CloseFloatingOverlay();

    public void OpenGameCreationOverlay() => _overlay.OpenGameCreationOverlay();

    public void OpenGameRoomTunnelSelection() => _overlay.OpenGameRoomTunnelSelection();

    public void OpenGameLobbySettingsOverlay() => _overlay.OpenGameLobbySettingsOverlay();

    public void CloseGameCreationOverlay() => _overlay.CloseGameCreationOverlay();

    public void OpenOptionsOverlay() => OpenFloatingOverlay(WindowKind.OptionsWindow);

    public void CloseOptionsOverlay() => CloseFloatingOverlay();

    public void OpenCampaignOverlay() => NavigateTo(FloatingOverlayLayout.CampaignWindowName);

    private void CloseFloatingOverlaySilently() => CloseFloatingOverlay();

    public void NavigateBack()
    {
        if (IsFloatingOverlayOpen)
        {
            if (IsOptionsOverlayOpen)
                DiscardSettings();

            CloseFloatingOverlay();
            return;
        }

        if (CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
            _ctx.CnCNet.LeaveGameRoom();

        if (_navStack.Count > 0)
        {
            NavigateTo(_navStack.Pop(), fromBack: true);
            return;
        }

        NavigateTo(WindowKind.MainMenu, fromBack: true);
    }

    public void LogoutToMainMenu()
    {
        CloseFloatingOverlaySilently();

        if (CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase)
            || CurrentWindow.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
            || _ctx.CnCNet.ActiveGameRoom != null
            || _ctx.CnCNet.Connection is { IsConnected: true })
        {
            _ctx.CnCNet.Disconnect();
        }

        _navStack.Clear();
        NavigateTo(WindowKind.MainMenu, fromBack: true);
        ShowStatus("Logged out.");
    }

    public void ShowStatus(string message) => PART_Status.Text = message;

    public void ExitApplication()
        => ShutdownService.Shutdown("MainWindow.ExitApplication");

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ShutdownService.Shutdown("MainWindow.Closing"));
    }

    private void OnGameProcessStarted()
    {
        if (!UserINISettings.Instance.MinimizeWindowsOnGameStart)
            return;

        _restoreWindowAfterGame = WindowState != WindowState.Minimized;
        WindowState = WindowState.Minimized;
    }

    private void OnGameProcessExited()
    {
        UserINISettings.Instance.ReloadSettings();

        if (_restoreWindowAfterGame && UserINISettings.Instance.MinimizeWindowsOnGameStart)
        {
            WindowState = WindowState.Normal;
            Activate();
        }

        _restoreWindowAfterGame = false;

        if (_ctx.ActiveRoot != null && MainWindowContext.IsGameLobbyWindow(CurrentWindow))
            UpdateLaunchButtonState(_ctx.ActiveRoot);

        ShowStatus("Game exited — returned to lobby.");
    }

    public void CommitSettings()
    {
        _ctx.BindingSession.CommitSettings();

        string? visualStyleError = null;
        if (IsOptionsOverlayOpen && _ctx.OverlayRoot != null)
        {
            visualStyleError = TryRun("Display settings".L10N("Client:Main:StepDisplayOptions"), () => DisplayOptionsApplier.Save(_ctx.OverlayRoot));
            visualStyleError ??= TryRun("Audio settings".L10N("Client:Main:StepAudioOptions"), () => AudioOptionsApplier.Save(_ctx.OverlayRoot));
            visualStyleError ??= TryRun("Updater settings".L10N("Client:Main:StepUpdaterOptions"), () => UpdaterOptionsApplier.Save(_ctx.OverlayRoot));
        }

        _ctx.Environment = ClientEnvironment.Discover(_ctx.Environment.GameRoot);

        if (visualStyleError is not null || DisplayOptionsApplier.LastSaveError is not null)
        {
            string rendererError = DisplayOptionsApplier.LastSaveError
                ?? "Renderer configuration error".L10N("Client:Main:RendererConfigError");
            string prefix = "Some settings failed to save: ".L10N("Client:Main:PartialSaveFailed");
            ShowStatus(visualStyleError is not null
                ? $"{prefix}{visualStyleError} ({rendererError})"
                : $"{prefix}{rendererError}");
        }
        else
        {
            ShowStatus($"Settings saved: {_ctx.BindingSession.Settings.SettingsPath}");
        }

        // Lazy visual-style switch runs after the rest of the settings are committed.
        if (IsOptionsOverlayOpen && _ctx.OverlayRoot != null)
        {
            string targetStyle = DisplayOptionsApplier.ReadSelectedVisualStyle(_ctx.OverlayRoot);
            if (targetStyle != Themes.DxThemeManager.CurrentStyle)
                ApplyVisualStyleWithLazyLoad(targetStyle);
        }
    }

    /// <summary>Runs one save step in isolation; returns an error summary instead of crashing.</summary>
    private static string? TryRun(string stepName, Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"CommitSettings step '{stepName}' failed: {ex}");
            return $"{stepName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Two-phase theme switch: a loading splash covers the window while Tactical assets
    /// warm up on a background thread, then the dictionary swap animates in. Failures
    /// revert to Classic and surface in the status bar — never a crash.
    /// </summary>
    private void ApplyVisualStyleWithLazyLoad(string targetStyle)
    {
        if (targetStyle == Themes.DxThemeManager.StyleTactical)
        {
            PART_ThemeLoading.SetStatus("Loading tactical UI module...".L10N("Client:Main:LoadingTacticalUI"));
            PART_ThemeLoadingHost.IsVisible = true;

            Task.Run(() =>
            {
                Exception? preloadError = null;
                try
                {
                    Themes.DxThemeManager.PreloadTacticalAssets();
                }
                catch (Exception ex)
                {
                    preloadError = ex;
                }

                Dispatcher.UIThread.Post(() => FinishVisualStyleSwitch(targetStyle, preloadError));
            });
        }
        else
        {
            FinishVisualStyleSwitch(targetStyle, preloadError: null);
        }
    }

    private void FinishVisualStyleSwitch(string targetStyle, Exception? preloadError)
    {
        if (preloadError != null)
            Logger.Log($"VisualStyle switch to {targetStyle} preload failed: {preloadError}");

        Exception? applyError = preloadError;
        if (applyError == null)
        {
            try
            {
                // The dictionary swap MUST complete before the UI tree is rebuilt:
                // UiNodeViewModel.LoadImages picks textures based on IsTactical at
                // build time. ThemeSwap defers its callback by the fade-out duration,
                // which used to reload the tree under the OLD style — the root cause
                // of the Classic/Tactical styles appearing swapped after a switch.
                Themes.DxThemeManager.Apply(targetStyle);
                UserINISettings.Instance.VisualStyle.Value = Themes.DxThemeManager.NormalizeStyle(targetStyle);
                try
                {
                    UserINISettings.Instance.SaveSettings();
                }
                catch (Exception saveEx)
                {
                    Logger.Log($"VisualStyle SaveSettings failed: {saveEx}");
                }
            }
            catch (Exception ex)
            {
                applyError = ex;
            }
        }

        if (applyError != null)
        {
            // Failure: revert to Classic so the client stays usable.
            try
            {
                Themes.DxThemeManager.Apply(Themes.DxThemeManager.StyleDefault);
                UserINISettings.Instance.VisualStyle.Value = Themes.DxThemeManager.StyleDefault;
            }
            catch (Exception revertEx)
            {
                Logger.Log($"VisualStyle revert failed: {revertEx}");
            }

            ShowStatus("Visual style switch failed, reverted to Classic: ".L10N("Client:Main:StyleSwitchReverted") + applyError.Message);
        }
        else
        {
            // Style changed: already-built trees still hold the old style's bitmaps
            // (Tactical drops all PNG chrome). Reload the current window so the
            // switch is visible immediately without a manual re-navigation.
            // Reopen Options afterward so Display tab values follow the new style.
            _reopenOptionsAfterStyleSwitch = IsOptionsOverlayOpen;
            if (_reopenOptionsAfterStyleSwitch)
                CloseFloatingOverlaySilently();
            ReloadMainWindowUnderNewStyle();
            if (_reopenOptionsAfterStyleSwitch)
            {
                _reopenOptionsAfterStyleSwitch = false;
                OpenFloatingOverlay(WindowKind.OptionsWindow);
            }
        }

        PART_ThemeLoadingHost.IsVisible = false;
        UpdateSolarSystemBackdrop();
    }

    private void ReloadMainWindowUnderNewStyle()
    {
        try
        {
            string window = CurrentWindow;
            if (string.IsNullOrEmpty(window))
                return;

            // Reset so the reload does not pollute the back-navigation stack.
            _navStack.Clear();
            NavigateTo(window, fromBack: true);
        }
        catch (Exception ex)
        {
            Logger.Log($"VisualStyle reload of '{CurrentWindow}' failed: {ex}");
            ShowStatus("Style switched, but the current window failed to refresh: ".L10N("Client:Main:StyleReloadFailed") + ex.Message);
        }
    }

    public void DiscardSettings()
    {
        _ctx.BindingSession.DiscardSettings();

        if (IsOptionsOverlayOpen && _ctx.OverlayRoot != null)
        {
            DisplayOptionsApplier.Apply(_ctx.OverlayRoot);
            AudioOptionsApplier.Apply(_ctx.OverlayRoot);
            UpdaterOptionsApplier.Apply(_ctx.OverlayRoot);
            ComponentsOptionsApplier.Apply(_ctx.OverlayRoot);
        }

        ShowStatus("Settings changes discarded");
    }

    public bool TryLaunchSkirmish(out string message) => _launch.TryLaunchSkirmish(out message);

    public bool TryLaunchCampaign(out string message) => _launch.TryLaunchCampaign(out message);

    public bool TryLaunchCnCNetGame(out string message) => _launch.TryLaunchCnCNetGame(out message);

    public bool TryLaunchLanGame(out string message) => _launch.TryLaunchLanGame(out message);

    public void OpenLoadGameOverlay() => _launch.OpenLoadGameOverlay();

    public void SelectOptionsTab(int index)
    {
        if (!IsOptionsOverlayOpen || _ctx.OverlayRoot == null)
            return;

        OptionsWindowLayout.SetActiveTab(_ctx.OverlayRoot, index);
        _ctx.OverlayRoot.RefreshLayout();

        if (index == 4)
            WafBlocklistApplier.Apply(_ctx.OverlayRoot, ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service.IngressWaf);

        ShowStatus($"Options tab {index + 1}/7");
    }

    public void CheckForUpdates() => _updateService.CheckForUpdates();

    public void RefreshMainMenuState()
    {
        if (!CurrentWindow.Equals(WindowKind.MainMenu, StringComparison.OrdinalIgnoreCase) || _ctx.ActiveRoot == null)
            return;

        _ctx.BindingSession.State.RefreshMainMenuState();
        _ctx.BindingSession.State.SetUpdateStatusText(_updateService.UpdateStatusText);
        StateBindingApplier.Apply(_ctx.ActiveRoot, _ctx.BindingSession.State, WindowKind.MainMenu);
    }

    private void OnUpdateStatusChanged()
    {
        _ctx.BindingSession.State.SetUpdateStatusText(_updateService.UpdateStatusText);
        if (_ctx.ActiveRoot != null && CurrentWindow.Equals(WindowKind.MainMenu, StringComparison.OrdinalIgnoreCase))
            StateBindingApplier.Apply(_ctx.ActiveRoot, _ctx.BindingSession.State, WindowKind.MainMenu);
    }

    public void RefreshLobbyMapList() => _lobbyMaps.RefreshLobbyMapList();

    public void PickRandomLobbyMap() => _lobbyMaps.PickRandomLobbyMap();

    public void ToggleFavoriteLobbyMap() => _lobbyMaps.ToggleFavoriteLobbyMap();

    public void FilterCampaignBySide(CampaignSideFilter sideFilter) => _campaign.FilterCampaignBySide(sideFilter);

    public void TogglePlayerExtraOptionsPanel() => _lobbyMaps.TogglePlayerExtraOptionsPanel();

    public void RefreshCnCNetGameListing() => _cncLobby.RefreshCnCNetGameListing();

    public void RefreshCnCNetGameRoomPlayers() => _cncGameRoom.RefreshCnCNetGameRoomPlayers();

    public void TryJoinSelectedCnCNetGame() => _cncLobby.TryJoinSelectedCnCNetGame();

    public void EnterCnCNetGameLobbyConnecting() => _cncLobby.EnterCnCNetGameLobbyConnecting();

    private void UpdateLaunchButtonState(UiNodeViewModel? root = null)
    {
        root ??= _ctx.ActiveRoot;
        if (root == null || !MainWindowContext.IsGameLobbyWindow(CurrentWindow))
            return;

        UiNodeViewModel? lbMapList = MainWindowContext.FindVm(root, "lbMapList");
        bool canLaunch = _ctx.LobbySession.VisibleMaps.Count > 0
            && (lbMapList?.SelectedIndex ?? -1) >= 0;

        CnCNetActiveGameRoom? cncRoom = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore
            ?? _ctx.CnCNet.GameRoom?.Room;
        UiNodeViewModel? btnLaunch = MainWindowContext.FindVm(root, "btnLaunchGame");
        UiNodeViewModel? chkAutoReady = MainWindowContext.FindVm(root, "chkAutoReady");

        if (CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase))
        {
            ResourceResolver resources = _ctx.GetMainResources();
            ICnCNetSession session = _ctx.CnCNet;

            if (cncRoom == null)
            {
                btnLaunch?.SetDisplayText("Launch Game");
                CnCNetGameLobbyUiHelper.ApplyHostToolbar(root);
                canLaunch = false;
                _ctx.BindingSession.State.SetCanLaunchGame(false);
                btnLaunch?.IsEnabled = false;
                return;
            }

            bool isJoiner = !cncRoom.IsHost;
            bool connecting = session.IsGameRoomJoinPending;

            CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _ctx.MainBehaviors, isJoiner);

            if (connecting)
            {
                canLaunch = false;
                _ctx.BindingSession.State.SetCanLaunchGame(false);
                btnLaunch?.IsEnabled = false;
                CnCNetGameLobbyUiHelper.SetJoinerReadyEnabled(root, false);
                return;
            }

            if (isJoiner)
            {
                bool autoReady = chkAutoReady?.IsChecked == true;
                canLaunch = !autoReady;
                CnCNetGameLobbyUiHelper.UpdateManualReadyLabel(root, isJoiner: true);
                CnCNetGameLobbyUiHelper.SetJoinerReadyEnabled(root, !autoReady);
            }
            else
            {
                btnLaunch?.SetDisplayText("Launch Game");
            }
        }

        _ctx.BindingSession.State.SetCanLaunchGame(canLaunch);
        if (btnLaunch != null && btnLaunch.IsVisible)
            btnLaunch.IsEnabled = canLaunch;
    }

    private void UpdateTopBar()
    {
        UpdateSolarSystemBackdrop();
        bool show = ShouldShowTopBar();
        if (!show)
        {
            PART_TopBarHost.Deactivate();
            return;
        }

        if (!PART_TopBarHost.IsVisible)
            PART_TopBarHost.IsVisible = true;

        PART_TopBarHost.Activate(this);

        var lobby = _ctx.CnCNet.LobbyState;
        string status = lobby.ConnectionStatus;
        if (string.IsNullOrWhiteSpace(status))
            status = _ctx.CnCNet.Connection?.IsConnected == true
                ? "Connected".L10N("Client:Main:StatusConnectedShort")
                : "Offline";

        PART_TopBarHost.Bar.UpdateState(
            status,
            _ctx.CnCNet.OnlinePlayerCount,
            isConnected: _ctx.CnCNet.Connection?.IsConnected == true,
            unreadPrivateMessages: _ctx.CnCNet.UnreadPrivateMessageCount);
    }

    /// <summary>
    /// Shared 3D solar-system backdrop: visible in Tactical style behind every
    /// window (panels sample the same scene continuously). Honors the
    /// SolarSystemEnabled user setting.
    /// </summary>
    private void UpdateSolarSystemBackdrop()
    {
        bool enabled = SolarSystemEnabled;
        bool visible = Themes.DxThemeManager.IsTactical && enabled;
        if (PART_SolarSystemBackdrop.IsVisible != visible)
            PART_SolarSystemBackdrop.IsVisible = visible;

        // Let the GL starfield own the chrome fill when active; keep #111 as
        // Classic / disabled fallback so empty GL never flashes white.
        if (PART_ClientChrome != null)
            PART_ClientChrome.Background = visible
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));

        if (visible)
        {
            SolarSystemDirector.Attach(PART_SolarSystemBackdrop);
            PART_SolarSystemBackdrop.RenderOnce();
        }
        else
        {
            SolarSystemDirector.Detach();
        }
    }

    private static bool SolarSystemEnabled
    {
        get
        {
            try
            {
                return ClientCore.UserINISettings.Instance.SolarSystemEnabled.Value;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public void OpenPrivateMessagingOverlay(string? focusNick = null)
    {
        if (_ctx.CnCNet.Connection?.IsConnected != true)
        {
            ShowStatus("Private messaging requires a CnCNet connection.".L10N("Client:Main:PmNeedsConnection"));
            return;
        }

        if (IsFloatingOverlayOpen
            && _ctx.FloatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (PART_OverlayRawView.Content is PrivateMessagingPanel openPanel)
            {
                if (!string.IsNullOrWhiteSpace(focusNick))
                    _ctx.CnCNet.EnsurePrivateConversation(focusNick);
                openPanel.Refresh(focusNick);
                if (!string.IsNullOrWhiteSpace(openPanel.SelectedNick))
                    _ctx.CnCNet.SetViewingPrivateMessagePeer(openPanel.SelectedNick);
                openPanel.FocusInput();
            }

            return;
        }

        if (IsFloatingOverlayOpen)
            CloseFloatingOverlaySilently();

        if (!string.IsNullOrWhiteSpace(focusNick))
            _ctx.CnCNet.EnsurePrivateConversation(focusNick);

        var panel = new PrivateMessagingPanel();
        panel.Bind(
            listPeers: () =>
            {
                var list = _ctx.CnCNet.GetPrivateConversationSummaries().ToList();
                var known = new HashSet<string>(list.Select(p => p.Nick), StringComparer.OrdinalIgnoreCase);
                string local = _ctx.CnCNet.LocalNick;
                foreach (string raw in _ctx.CnCNet.LobbyState.ChannelPlayers)
                {
                    string nick = raw.Trim().TrimStart('@', '+', '%', '~', '&');
                    if (string.IsNullOrWhiteSpace(nick)
                        || nick.Equals(local, StringComparison.OrdinalIgnoreCase)
                        || known.Contains(nick))
                        continue;
                    list.Add((nick, 0));
                    known.Add(nick);
                }

                return list;
            },
            listMessages: nick => _ctx.CnCNet.GetPrivateMessages(nick)
                .Select(l => l.DisplayText)
                .ToList(),
            send: (nick, text) =>
            {
                _ctx.CnCNet.SendPrivateMessage(nick, text);
                _ctx.CnCNet.SetViewingPrivateMessagePeer(nick);
            },
            close: () =>
            {
                _ctx.CnCNet.SetViewingPrivateMessagePeer(null);
                _overlay.ResetOverlayPanelChrome();
                _overlay.CloseFloatingOverlayCore(restoreIniOverlayView: true);
                UpdateTopBar();
            },
            peerSelected: nick => _ctx.CnCNet.SetViewingPrivateMessagePeer(nick));

        _overlay.ShowRawHostOverlay(panel, 600, 520, "Private Messages (F4)".L10N("Client:Main:PrivateMessagesTitle"));
        _ctx.FloatingOverlayWindow = "PrivateMessagingWindow";
        panel.Refresh(focusNick ?? _ctx.CnCNet.LastPrivateMessagePartner);
        if (!string.IsNullOrWhiteSpace(panel.SelectedNick))
            _ctx.CnCNet.SetViewingPrivateMessagePeer(panel.SelectedNick);
        panel.FocusInput();
        UpdateTopBar();
    }

    private void RefreshPrivateMessagingPanelIfOpen()
    {
        if (IsFloatingOverlayOpen
            && _ctx.FloatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true
            && PART_OverlayRawView.Content is PrivateMessagingPanel pmPanel)
        {
            pmPanel.Refresh();
        }
    }

    private bool ShouldShowTopBar()
        => CurrentWindow.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals("LANLobby", StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals(WindowKind.LanGameLobby, StringComparison.OrdinalIgnoreCase);

    private void OnPrivateMessageArrived(string peer, string preview)
    {
        UpdateTopBar();

        if (CnCNetPrivateMessagePolicy.PopupsDisabled())
            return;

        string clip = preview.Length > 60 ? preview[..60] + "…" : preview;
        ShowStatus(string.Format(
            "PM - {0}: {1}".L10N("Client:Main:PmStatusPreview"),
            peer,
            clip));
    }

    private async void OnCnCNetWafAlert(ClientAvalonia.CnCNet.Waf.WafAlert alert)
    {
        try
        {
            string surface = alert.Event.Surface switch
            {
                ClientAvalonia.CnCNet.Waf.WafSurface.PrivateMessage
                    => "Private message".L10N("Client:Main:WafSurfacePrivateMessage"),
                ClientAvalonia.CnCNet.Waf.WafSurface.LobbyChat
                    => "Lobby chat".L10N("Client:Main:WafSurfaceLobbyChat"),
                ClientAvalonia.CnCNet.Waf.WafSurface.GameRoomChat
                    => "Game room chat".L10N("Client:Main:WafSurfaceGameRoomChat"),
                ClientAvalonia.CnCNet.Waf.WafSurface.ListingText
                    => "Game listing text".L10N("Client:Main:WafSurfaceListingText"),
                _ => "Network protocol".L10N("Client:Main:WafSurfaceProtocol"),
            };

            string actor = string.IsNullOrWhiteSpace(alert.Event.SenderNick)
                ? (alert.Event.Game?.ChannelName ?? "unknown source".L10N("Client:Main:WafUnknownSource"))
                : alert.Event.SenderNick;

            string message =
                "Source: ".L10N("Client:Main:WafSourceLabel") + actor + "\n" +
                "Surface: ".L10N("Client:Main:WafSurfaceLabel") + surface + "\n" +
                "Level: ".L10N("Client:Main:WafLevelLabel") +
                $"{alert.Decision.Severity} (score={alert.Decision.Score})\n" +
                "Reason: ".L10N("Client:Main:WafReasonLabel") + alert.Decision.Summary;

            bool offerBlock = alert.Decision.SuggestedBlockKeys.Count > 0;
            bool addBlock = await ClientDialogService.ShowWafAlertAsync(
                this,
                "CnCNet Ingress Protection".L10N("Client:Main:WafAlertTitle"),
                message,
                offerBlock).ConfigureAwait(true);

            if (!addBlock || _ctx.CnCNet is not CnCNetSessionServiceAdapter adapter)
                return;

            string note = alert.Decision.Summary;
            if (string.IsNullOrWhiteSpace(note))
                note = $"{surface}/{alert.Decision.Severity}";

            adapter.Service.IngressWaf.BlockFromAlert(alert.Event, alert.Decision, note);

            ShowStatus("Added to the WAF blocklist (including similar message bodies)".L10N("Client:Main:WafBlocklistSaved"));
            if (IsOptionsOverlayOpen && _ctx.OverlayRoot != null)
                WafBlocklistApplier.Apply(_ctx.OverlayRoot, adapter.Service.IngressWaf);
        }
        catch (Exception ex)
        {
            Logger.Log($"WAF alert UI failed: {ex.Message}");
        }
    }

    private void OnGameResourcesLoaded()
    {
        if (_ctx.ActiveRoot != null && CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            _lobbyMaps.ApplyLobbyData(_ctx.ActiveRoot, CurrentWindow);

        if (_ctx.ActiveRoot != null
            && FloatingOverlayLayout.IsCampaignWindow(CurrentWindow))
            _campaign.ApplyCampaignOverlay(_ctx.ActiveRoot);
        else if (_ctx.OverlayRoot != null
            && _ctx.FloatingOverlayWindow is { } overlayWindow
            && FloatingOverlayLayout.IsCampaignWindow(overlayWindow))
            _campaign.ApplyCampaignOverlay(_ctx.OverlayRoot);
    }

    private void OnLocalVersionsChecked() => RefreshMainMenuState();

    private void OnOverlayBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsFloatingOverlayOpen)
            return;

        if (IsOptionsOverlayOpen)
            DiscardSettings();

        CloseFloatingOverlay();
        ShowStatus("Overlay closed");
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && !IsFloatingOverlayOpen)
        {
            NavigateTo(WindowKind.MainMenu);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3 && !IsFloatingOverlayOpen)
        {
            NavigateTo("CnCNetLobby");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F4)
        {
            OpenPrivateMessagingOverlay();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12 && !IsFloatingOverlayOpen)
        {
            OpenFloatingOverlay(WindowKind.OptionsWindow);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _ctx.IsCnCNetLobbyActive() && !IsFloatingOverlayOpen)
        {
            TrySendChannelChat();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (IsFloatingOverlayOpen)
            {
                if (IsOptionsOverlayOpen)
                    DiscardSettings();

                CloseFloatingOverlay();
                ShowStatus("Overlay closed");
                return;
            }

            if (CurrentWindow != WindowKind.MainMenu)
                NavigateBack();

            return;
        }

        if (!IsOptionsOverlayOpen)
            return;

        int? tab = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            _ => null,
        };

        if (tab != null)
            SelectOptionsTab(tab.Value);
    }

    private void TrySendChannelChat()
    {
        if (_ctx.ActiveRoot == null)
            return;

        UiNodeViewModel? tbChat = MainWindowContext.FindVm(_ctx.ActiveRoot, "tbChatInput");
        if (tbChat == null || string.IsNullOrWhiteSpace(tbChat.InputText))
            return;

        string message = tbChat.InputText.Trim();
        _ctx.CnCNet.SendChatMessage(message);
        tbChat.InputText = string.Empty;
    }

    private void ApplyViewportSize(int width, int height)
    {
        PART_RootView.Width = width;
        PART_RootView.Height = height;
        Width = width;
        Height = height + StatusBarHeight;
        MinWidth = width;
        MinHeight = height + StatusBarHeight;
        MaxWidth = width;
        MaxHeight = height + StatusBarHeight;
    }
}
