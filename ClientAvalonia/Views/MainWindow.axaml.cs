using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientCore;
using ClientCore.Settings;
using ClientAvalonia.CnCNet;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Overlays;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using Rampastring.Tools;

namespace ClientAvalonia.Views;

public partial class MainWindow : Window, IUiNavigationHost
{
    private const double StatusBarHeight = 24;

    private ClientEnvironment _environment = ClientEnvironment.Discover();
    private readonly BehaviorRegistry _mainBehaviors = new();
    private readonly BehaviorRegistry _overlayBehaviors = new();
    private readonly UiBindingSession _bindingSession;
    private readonly GameLaunchService _gameLaunch = new();
    private readonly ClientUpdateService _updateService = new();
    private readonly GameResourceCatalog _gameResources = GameResourceCatalog.Instance;
    private readonly LobbySessionState _lobbySession = new();
    private LayoutEngine? _mainEngine;
    private LayoutEngine? _overlayEngine;
    private UiViewModelFactory? _mainViewModelFactory;
    private UiNodeViewModel? _activeRoot;
    private UiNodeViewModel? _overlayRoot;
    private GameCreationOverlayContext? _gameCreationOverlay;
    private string? _floatingOverlayWindow;
    private readonly Stack<string> _navStack = new();

    public string CurrentWindow { get; private set; } = "MainMenu";

    public bool IsFloatingOverlayOpen => PART_FloatingOverlay.IsVisible;

    public string? FloatingOverlayWindow => _floatingOverlayWindow;

    public bool IsOptionsOverlayOpen
        => IsFloatingOverlayOpen
           && _floatingOverlayWindow?.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsGameCreationOverlayOpen
        => IsFloatingOverlayOpen
           && _floatingOverlayWindow?.Equals("GameCreationWindow", StringComparison.OrdinalIgnoreCase) == true;

    public UiNodeViewModel? ActiveRoot => _activeRoot;

    public UiNodeViewModel? OverlayRoot => _overlayRoot;

    private bool _restoreWindowAfterGame;

    public MainWindow()
    {
        _bindingSession = new UiBindingSession(_environment);
        _gameLaunch.StatusChanged += msg => Dispatcher.UIThread.Post(() => ShowStatus(msg));
        _gameLaunch.GameProcessStarted += () => Dispatcher.UIThread.Post(OnGameProcessStarted);
        _gameLaunch.GameProcessExited += () => Dispatcher.UIThread.Post(OnGameProcessExited);
        _updateService.EnsureHandlersRegistered();
        _updateService.StatusChanged += OnUpdateStatusChanged;
        ClientStartupService.LocalVersionsChecked += OnLocalVersionsChecked;
        _gameResources.Loaded += OnGameResourcesLoaded;
        CnCNetSessionService.Instance.StateChanged += OnCnCNetStateChanged;
        CnCNetSessionService.Instance.GameRoomJoined += OnCnCNetGameRoomJoined;
        CnCNetSessionService.Instance.GameRoomJoinFailed += OnCnCNetGameRoomJoinFailed;
        CnCNetSessionService.Instance.GameStarting += OnCnCNetGameStarting;
        CnCNetSessionService.Instance.GameRoomHostAbandoned += OnCnCNetGameRoomHostAbandoned;
        CnCNetSessionService.Instance.EnsureStarted();
        InitializeComponent();
        KeyDown += OnKeyDown;
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
        PART_TopBarHost.Bar.BindNavigation(NavigateTo, LogoutToMainMenu);
        _updateService.RefreshInitialStatus();
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        var resources = new ResourceResolver();
        resources.ConfigureForGame(_environment);

        PART_StartupLoading.IsVisible = true;
        ClientFileIntegrityResult? integrityResult = null;
        await StartupLoadingView.RunStartupSequenceAsync(
            PART_StartupLoading,
            resources,
            reportStatus =>
            {
                integrityResult = ClientFileIntegrityService.Verify(reportStatus: reportStatus);
                _gameResources.EnsureLoaded();
                return Task.CompletedTask;
            }).ConfigureAwait(true);

        PART_StartupLoading.IsVisible = false;

        if (integrityResult is { Success: false })
        {
            await ClientDialogService.ShowErrorAsync(
                this,
                "文件校验失败",
                integrityResult.Message).ConfigureAwait(true);
            return;
        }

        NavigateTo("MainMenu");

        // Aligned with DX LoadingScreen.Finish / MainMenu.PostInit: when the user has
        // "AutomaticCnCNetLogin" enabled, connect IRC right after the main menu shows
        // so that broadcast channel GAME CTCPs accumulate before the user opens the
        // CnCNet lobby. Otherwise the lobby list is empty until the next 30s broadcast.
        TryAutomaticCnCNetLogin();
    }

    private static void TryAutomaticCnCNetLogin()
    {
        try
        {
            if (!UserINISettings.Instance.AutomaticCnCNetLogin)
                return;

            if (NameValidator.IsNameValid(ProgramConstants.PLAYERNAME, out _) != NameValidationError.None)
            {
                Logger.Log("AutomaticCnCNetLogin: skipping — player name is not valid.");
                return;
            }

            Logger.Log("AutomaticCnCNetLogin: connecting CnCNet session at startup.");
            CnCNetSessionService.Instance.ConnectIfNeeded();
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
        if (_environment.ResolveWindowLoadTarget(windowName) is { } target)
        {
            iniPath = target.IniPath;
            iniSection = target.SectionName;
        }
        else
        {
            iniPath = _environment.ResolveWindowIni(windowName);
            if (iniPath == null)
            {
                ShowStatus($"INI not found for window: {windowName}");
                return;
            }

            iniSection = windowName;
        }

        try
        {
            UiBehaviorCatalog.RegisterForWindow(_mainBehaviors, windowName, this);

            _mainEngine = LayoutEngine.CreateForWindow(_environment, iniPath, iniSection);
            _mainViewModelFactory = new UiViewModelFactory(_mainEngine.Resources, _mainBehaviors);

            UiNodeTree tree = _mainEngine.LoadWindow(iniPath, iniSection);
            UiNodeViewModel vm = _mainViewModelFactory.CreateTree(tree);
            IniBehaviorApplier.Apply(vm, _mainBehaviors, this);

            if (IsGameLobbyWindow(windowName))
                _bindingSession.State.SetCanLaunchGame(true);

            _bindingSession.ApplyToTree(vm, windowName);
            _activeRoot = vm;
            PART_RootView.Content = vm;
            ApplyViewportSize(_mainEngine.Context.Width, _mainEngine.Context.Height);
            CurrentWindow = windowName;

            try
            {
                if (windowName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLobbyData(vm, windowName);
                    UpdateLaunchButtonState(vm);
                }

                Title = $"ClientAvalonia — {windowName} ({_environment.ThemeFolderPath.TrimEnd('/')}) {_mainEngine.Context.Width}×{_mainEngine.Context.Height}";
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
    {
        if (IsFloatingOverlayOpen)
            return;

        if (!windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase)
            && !CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus($"{windowName} overlay is only available from MainMenu");
            return;
        }

        string? iniPath = _environment.ResolveWindowIni(windowName);
        if (iniPath == null)
        {
            ShowStatus($"INI not found for window: {windowName}");
            return;
        }

        try
        {
            (int width, int height) = FloatingOverlayLayout.ResolveOverlaySize(iniPath, windowName);

            _overlayBehaviors.Clear();
            FloatingOverlayBehaviors.RegisterForOverlay(_overlayBehaviors, this, windowName);

            _overlayEngine = LayoutEngine.CreateForWindow(_environment, iniPath, windowName);
            var factory = new UiViewModelFactory(_overlayEngine.Resources, _overlayBehaviors);

            UiNodeTree tree = _overlayEngine.LoadWindow(iniPath, windowName);
            _overlayRoot = factory.CreateTree(tree);
            IniBehaviorApplier.Apply(_overlayRoot, _overlayBehaviors, this);
            _bindingSession.ApplyToTree(_overlayRoot, windowName);

            PART_OverlayPanel.Width = width;
            PART_OverlayPanel.Height = height;
            PART_OverlayView.Width = width;
            PART_OverlayView.Height = height;
            PART_OverlayRawView.IsVisible = false;
            PART_OverlayRawView.Content = null;
            PART_OverlayView.IsVisible = true;
            PART_OverlayView.Content = _overlayRoot;
            PART_FloatingOverlay.IsVisible = true;
            PART_FloatingOverlay.IsHitTestVisible = true;
            PART_RootView.IsHitTestVisible = false;
            _floatingOverlayWindow = windowName;

            if (windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            {
                DisplayOptionsApplier.Apply(_overlayRoot);
                AudioOptionsApplier.Apply(_overlayRoot);
                UpdaterOptionsApplier.Apply(_overlayRoot);
                ComponentsOptionsApplier.Apply(_overlayRoot);
                // After INI/setting binds: force footer labels (empty Translation keys must not win).
                OptionsFooterChrome.ApplyToViewModel(_overlayRoot);
            }

            if (windowName.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase))
                ApplyCampaignOverlay(_overlayRoot);

            ShowStatus($"{windowName} overlay: {width}×{height}");
        }
        catch (Exception ex)
        {
            Logger.Log($"OpenFloatingOverlay({windowName}) failed: {ex}");
            ShowStatus($"{windowName} overlay: {ex.Message}");
        }
    }

    public void CloseFloatingOverlay()
    {
        if (_floatingOverlayWindow?.Equals(GameCreationOverlayHost.WindowName, StringComparison.OrdinalIgnoreCase) == true)
        {
            CloseGameCreationOverlay();
            return;
        }

        CloseFloatingOverlayCore(restoreIniOverlayView: true);
    }

    public void OpenGameCreationOverlay()
    {
        if (IsFloatingOverlayOpen)
            return;

        if (!IsCnCNetLobbyActive())
        {
            ShowStatus("Create game is only available from the CnCNet lobby.");
            return;
        }

        var tunnels = CnCNetSessionService.Instance.Tunnels;
        if (tunnels.Count == 0)
        {
            ShowStatus("No NAT tunnels available.");
            return;
        }

        GameCreationOverlayHost.OpenResult layout = GameCreationOverlayHost.TryResolveLayout(_environment);
        _overlayBehaviors.Clear();

        UiNodeViewModel? iniRoot = GameCreationOverlayHost.TryBuildIniOverlay(
            _environment,
            _overlayBehaviors,
            this,
            out _,
            out string? iniFailure);

        if (iniRoot != null)
        {
            ShowGameCreationOverlay(iniRoot, layout.Width, layout.Height, $"INI ({layout.Source})");
            return;
        }

        (Control root, GameCreationOverlayContext context, Size preferredSize) = GameCreationOverlayBuilder.Build(tunnels);
        _gameCreationOverlay = context;
        GameCreationOverlayBehaviors.Wire(context, this, "CnCNetGameLobby");

        string fallbackNote = string.IsNullOrWhiteSpace(iniFailure) ? "programmatic UI" : $"programmatic UI ({iniFailure})";
        ShowGameCreationOverlay(root, preferredSize.Width, preferredSize.Height, fallbackNote);
    }

    public void OpenGameRoomTunnelSelection()
    {
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room is not { IsHost: true })
        {
            ShowStatus("Only the game host can change the tunnel.");
            return;
        }

        var tunnels = CnCNetSessionService.Instance.Tunnels;
        if (tunnels.Count == 0)
        {
            ShowStatus("No NAT tunnels available.");
            return;
        }

        if (IsFloatingOverlayOpen)
            CloseFloatingOverlaySilently();

        (Control root, Size size) = RoomHostOverlayBuilder.BuildTunnelPicker(
            tunnels,
            tunnel =>
            {
                if (CnCNetSessionService.Instance.TryHostChangeTunnel(tunnel))
                {
                    ShowStatus($"Tunnel changed to {tunnel.Name}.");
                    CloseFloatingOverlaySilently();
                }
                else
                {
                    ShowStatus("Failed to change tunnel (not joined / not host).");
                }
            },
            CloseFloatingOverlaySilently);

        ShowRawHostOverlay(root, size.Width, size.Height, "Select tunnel");
    }

    public void OpenGameLobbySettingsOverlay()
    {
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room is not { IsHost: true })
        {
            ShowStatus("Only the game host can change room settings.");
            return;
        }

        if (IsFloatingOverlayOpen)
            CloseFloatingOverlaySilently();

        (Control root, Size size) = RoomHostOverlayBuilder.BuildGameLobbySettings(
            room,
            (name, max, skill, password) =>
            {
                CnCNetSessionService.Instance.UpdateGameLobbySettings(name, max, skill, password);
                ShowStatus("Room settings updated.");
                CloseFloatingOverlaySilently();
            },
            CloseFloatingOverlaySilently);

        ShowRawHostOverlay(root, size.Width, size.Height, "Game lobby settings");
    }

    private void ShowRawHostOverlay(Control content, double width, double height, string title)
    {
        PART_OverlayPanel.Width = width + 16;
        PART_OverlayPanel.Height = height + 16;
        PART_OverlayPanel.Padding = new Thickness(8);
        PART_OverlayPanel.Background = Brushes.Transparent;
        PART_OverlayPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
        PART_OverlayPanel.BorderThickness = new Thickness(2);

        _overlayRoot = null;
        PART_OverlayView.IsVisible = false;
        PART_OverlayView.Content = null;
        PART_OverlayRawView.ContentTemplate = null;
        PART_OverlayRawView.Width = width;
        PART_OverlayRawView.Height = height;
        PART_OverlayRawView.Content = content;
        PART_OverlayRawView.IsVisible = true;

        PART_FloatingOverlay.IsVisible = true;
        PART_FloatingOverlay.IsHitTestVisible = true;
        PART_RootView.IsHitTestVisible = false;
        _floatingOverlayWindow = "GameRoomHostOverlay";
        ShowStatus(title);
    }

    private void ShowGameCreationOverlay(object content, double width, double height, string sourceNote)
    {
        PART_OverlayPanel.Width = width + 16;
        PART_OverlayPanel.Height = height + 16;
        PART_OverlayPanel.Padding = new Thickness(8);
        PART_OverlayPanel.Background = Brushes.Transparent;
        PART_OverlayPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
        PART_OverlayPanel.BorderThickness = new Thickness(2);

        if (content is UiNodeViewModel iniRoot)
        {
            _overlayRoot = iniRoot;
            PART_OverlayRawView.IsVisible = false;
            PART_OverlayRawView.Content = null;
            PART_OverlayView.Width = width;
            PART_OverlayView.Height = height;
            PART_OverlayView.IsVisible = true;
            PART_OverlayView.Content = iniRoot;
        }
        else
        {
            _overlayRoot = null;
            PART_OverlayView.IsVisible = false;
            PART_OverlayView.Content = null;
            PART_OverlayRawView.ContentTemplate = null;
            PART_OverlayRawView.Width = width;
            PART_OverlayRawView.Height = height;
            PART_OverlayRawView.Content = content;
            PART_OverlayRawView.IsVisible = true;
        }

        PART_FloatingOverlay.IsVisible = true;
        PART_FloatingOverlay.IsHitTestVisible = true;
        PART_RootView.IsHitTestVisible = false;
        _floatingOverlayWindow = GameCreationOverlayHost.WindowName;
        ShowStatus($"Create game ({sourceNote}).");
    }

    public void CloseGameCreationOverlay()
    {
        if (_floatingOverlayWindow?.Equals(GameCreationOverlayHost.WindowName, StringComparison.OrdinalIgnoreCase) != true)
            return;

        ResetOverlayPanelChrome();
        CloseFloatingOverlayCore(restoreIniOverlayView: true);
        _gameCreationOverlay = null;
    }

    private void ResetOverlayPanelChrome()
    {
        PART_OverlayPanel.Padding = new Thickness(0);
        PART_OverlayPanel.Background = new SolidColorBrush(Color.FromArgb(204, 20, 16, 12));
        PART_OverlayPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 140, 50));
        PART_OverlayPanel.BorderThickness = new Thickness(1);
    }

    private void CloseFloatingOverlayCore(bool restoreIniOverlayView)
    {
        PART_FloatingOverlay.IsVisible = false;
        PART_FloatingOverlay.IsHitTestVisible = false;
        PART_OverlayView.Content = null;
        PART_OverlayRawView.Content = null;
        PART_OverlayRawView.IsVisible = false;
        if (restoreIniOverlayView)
            PART_OverlayView.IsVisible = true;
        PART_RootView.IsHitTestVisible = true;
        _overlayRoot = null;
        _overlayEngine = null;
        _overlayBehaviors.Clear();
        _floatingOverlayWindow = null;
    }

    private bool IsCnCNetLobbyActive()
        => IsChannelLobbyWindow(CurrentWindow)
           || IsCnCNetGameRoomChatEligible()
           || (_activeRoot != null && FindVm(_activeRoot, "ddCurrentChannel") != null);

    /// <summary>
    /// True when the active window is the CnCNet game-room lobby AND we are currently joined
    /// to the room channel. Used to enable Enter-to-send for in-room chat (mirrors DX
    /// CnCNetGameLobby where tbChatInput.EnterPressed fires inside the room window).
    /// </summary>
    private bool IsCnCNetGameRoomChatEligible()
        => CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase)
           && CnCNetSessionService.Instance.GameRoom is { IsLocalJoined: true };

    public void OpenOptionsOverlay() => OpenFloatingOverlay("OptionsWindow");

    public void CloseOptionsOverlay() => CloseFloatingOverlay();

    public void OpenCampaignOverlay() => OpenFloatingOverlay("CampaignSelector");

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

        if (CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            CnCNetSessionService.Instance.LeaveGameRoom();

        if (_navStack.Count > 0)
        {
            NavigateTo(_navStack.Pop(), fromBack: true);
            return;
        }

        NavigateTo("MainMenu", fromBack: true);
    }

    public void LogoutToMainMenu()
    {
        CloseFloatingOverlaySilently();

        if (CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase)
            || CurrentWindow.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
            || CnCNetSessionService.Instance.ActiveGameRoom != null
            || CnCNetSessionService.Instance.Connection is { IsConnected: true })
        {
            CnCNetSessionService.Instance.Disconnect();
        }

        _navStack.Clear();
        NavigateTo("MainMenu", fromBack: true);
        ShowStatus("Logged out.");
    }

    public void ShowStatus(string message) => PART_Status.Text = message;

    public void ExitApplication()
        => ShutdownService.Shutdown("MainWindow.ExitApplication");

    /// <summary>
    /// X-button / ALT-F4 / task manager close. ShutdownMode is OnExplicitShutdown, so closing
    /// the window alone does NOT end the lifetime — route every close intent through
    /// <see cref="ShutdownService"/> so IRC + timers are torn down regardless of how the
    /// user closed the window.
    /// </summary>
    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Dispatcher.UIThread.Post: avoid re-entering Avalonia's own closing sequence.
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

        if (_activeRoot != null && IsGameLobbyWindow(CurrentWindow))
            UpdateLaunchButtonState(_activeRoot);

        ShowStatus("Game exited — returned to lobby.");
    }

    public void CommitSettings()
    {
        _bindingSession.CommitSettings();

        if (IsOptionsOverlayOpen && _overlayRoot != null)
        {
            DisplayOptionsApplier.Save(_overlayRoot);
            AudioOptionsApplier.Save(_overlayRoot);
            UpdaterOptionsApplier.Save(_overlayRoot);
        }

        _environment = ClientEnvironment.Discover(_environment.GameRoot);
        ShowStatus($"Settings saved: {_bindingSession.Settings.SettingsPath}");
    }

    public void DiscardSettings()
    {
        _bindingSession.DiscardSettings();

        if (IsOptionsOverlayOpen && _overlayRoot != null)
        {
            DisplayOptionsApplier.Apply(_overlayRoot);
            AudioOptionsApplier.Apply(_overlayRoot);
            UpdaterOptionsApplier.Apply(_overlayRoot);
            ComponentsOptionsApplier.Apply(_overlayRoot);
        }

        ShowStatus("Settings changes discarded");
    }

    public bool TryLaunchSkirmish(out string message)
    {
        if (_activeRoot == null || !CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
        {
            message = "Not in skirmish lobby.";
            return false;
        }

        UiNodeViewModel? ddGameMode = FindVm(_activeRoot, "ddGameMode");
        if (ddGameMode != null)
            _lobbySession.FilterIndex = ddGameMode.SelectedIndex;

        LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);

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

        string? validationError = SkirmishLaunchValidator.Validate(map, gameMode, _lobbySession.PlayerState);
        if (validationError != null)
        {
            message = validationError;
            return false;
        }

        _lobbySession.PlayerState.SaveSkirmishSettings();

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Players = _lobbySession.PlayerState,
            LobbyRoot = _activeRoot,
        };

        message = "Launching game...";
        _gameLaunch.BeginLaunch(
            _environment,
            new SkirmishLaunchSession(request),
            (ok, result) => Dispatcher.UIThread.Post(() =>
            {
                if (!ok)
                {
                    ShowStatus($"Launch failed: {result}");
                    ClientDialogService.ShowError(this, "Cannot launch game", result);
                    return;
                }

                ShowStatus(result);
            }));

        return true;
    }

    public bool TryLaunchCampaign(out string message)
    {
        if (_overlayRoot == null
            || _floatingOverlayWindow?.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase) != true)
        {
            message = "Campaign overlay is not open.";
            return false;
        }

        UiNodeViewModel? lbCampaignList = FindVm(_overlayRoot, "lbCampaignList");
        MissionEntry? mission = _lobbySession.GetSelectedMission(lbCampaignList?.SelectedIndex ?? 0);
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

        int difficulty = ResolveCampaignDifficulty(_overlayRoot);
        UserINISettings.Instance.Difficulty.Value = difficulty;

        bool launched = _gameLaunch.TryLaunchCampaign(
            _environment,
            new CampaignLaunchRequest
            {
                Mission = mission,
                DifficultyIndex = difficulty,
                OverlayRoot = _overlayRoot,
            },
            out message,
            this);

        if (launched)
            UserINISettings.Instance.SaveSettings();

        return launched;
    }

    public bool TryLaunchCnCNetGame(out string message)
    {
        message = string.Empty;
        CnCNetSessionService session = CnCNetSessionService.Instance;

        if (session.IsGameRoomJoinPending)
        {
            message = "Still joining the CnCNet game room — please wait.";
            return false;
        }

        CnCNetActiveGameRoom? room = session.ActiveGameRoom ?? session.GameRoom?.Room;
        if (room == null)
        {
            message = "Not in a CnCNet game room.";
            return false;
        }

        if (_activeRoot != null)
            LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        if (room.IsHost)
        {
            session.SyncGameRoomFromLobby(_lobbySession.PlayerState);

            if (!session.TryLaunchHostedGame(out message))
                return false;

            return true;
        }

        UiNodeViewModel? chkAutoReady = _activeRoot != null ? FindVm(_activeRoot, "chkAutoReady") : null;
        bool autoReady = chkAutoReady?.IsChecked == true;

        CnCNetGameRoomPlayer? local = session.GameRoom?.Players
            .FirstOrDefault(p => p.Name.Equals(session.LocalNick, StringComparison.OrdinalIgnoreCase));

        if (autoReady)
        {
            session.SetGameRoomReady(true, autoReady: true);
            message = "Auto ready — waiting for host to launch.";
            RefreshCnCNetGameRoomPlayers();
            return true;
        }

        bool ready = !(local?.Ready ?? false);
        session.SetGameRoomReady(ready, autoReady: false);
        message = ready ? "Ready — waiting for host to launch." : "Not ready.";
        RefreshCnCNetGameRoomPlayers();
        return true;
    }

    public void SelectOptionsTab(int index)
    {
        if (!IsOptionsOverlayOpen || _overlayRoot == null)
            return;

        OptionsWindowLayout.SetActiveTab(_overlayRoot, index);
        _overlayRoot.RefreshLayout();
        ShowStatus($"Options tab {index + 1}/6");
    }

    public void CheckForUpdates() => _updateService.CheckForUpdates();

    public void RefreshMainMenuState()
    {
        if (!CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) || _activeRoot == null)
            return;

        _bindingSession.State.RefreshMainMenuState();
        _bindingSession.State.SetUpdateStatusText(_updateService.UpdateStatusText);
        StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");
    }

    private void OnUpdateStatusChanged()
    {
        _bindingSession.State.SetUpdateStatusText(_updateService.UpdateStatusText);
        if (_activeRoot != null && CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");
    }

    public void RefreshLobbyMapList()
    {
        if (_activeRoot == null || !CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            return;

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        UiNodeViewModel? ddGameMode = FindVm(_activeRoot, "ddGameMode");
        int filterIndex = ddGameMode?.SelectedIndex ?? _lobbySession.FilterIndex;
        GameDataBindingApplier.ApplyLobbyMapList(_activeRoot, _gameResources, _lobbySession, resources, filterIndex);
        UpdateLaunchButtonState();
    }

    public void PickRandomLobbyMap()
    {
        if (_activeRoot == null)
            return;

        _lobbySession.MapSearchText = string.Empty;
        UiNodeViewModel? tbMapSearch = FindVm(_activeRoot, "tbMapSearch");
        if (tbMapSearch != null)
            tbMapSearch.InputText = string.Empty;

        RefreshLobbyMapList();

        int index = _gameResources.PickRandomMapIndex(_lobbySession.VisibleMaps);
        if (index < 0)
        {
            ShowStatus("No maps available for random pick.");
            return;
        }

        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        if (lbMapList != null)
            lbMapList.SelectedIndex = index;

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        GameDataBindingApplier.ResolveStartInteractionFlags(
            _lobbySession.PlayerState,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.UpdateMapSelectionDisplay(
            _activeRoot,
            _lobbySession.VisibleMaps,
            index,
            resources,
            _lobbySession.PlayerState,
            canAssign,
            canSelectLocal);
        UpdateLaunchButtonState();
        ShowStatus($"Random map: {_lobbySession.GetSelectedMap(index)?.DisplayName ?? "none"}");
    }

    public void ToggleFavoriteLobbyMap()
    {
        if (_activeRoot == null)
            return;

        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);
        if (map == null)
            return;

        bool isFavorite = _gameResources.ToggleFavoriteMap(map, gameMode);
        ShowStatus(isFavorite ? "Map added to favorites." : "Map removed from favorites.");

        if (_lobbySession.IsFavoriteFilterSelected)
            RefreshLobbyMapList();
    }

    public void FilterCampaignBySide(CampaignSideFilter sideFilter)
    {
        if (_overlayRoot == null)
            return;

        ApplyCampaignOverlay(_overlayRoot, sideFilter);
        string label = sideFilter switch
        {
            CampaignSideFilter.Allied => "同盟国联军",
            CampaignSideFilter.Soviet => "苏维埃联盟",
            CampaignSideFilter.Ackville => "阿克维尔",
            _ => "全部",
        };
        ShowStatus($"Campaign filter: {label} ({_lobbySession.VisibleMissions.Count} missions)");
    }

    public void TogglePlayerExtraOptionsPanel()
    {
        if (_activeRoot == null)
            return;

        UiNodeViewModel? panel = FindVm(_activeRoot, "PlayerExtraOptionsPanel");
        if (panel == null)
        {
            ShowStatus("Player extra options panel not found.");
            return;
        }

        panel.IsVisible = !panel.IsVisible;
        ShowStatus(panel.IsVisible ? "Player extra options opened." : "Player extra options closed.");
    }

    private void ApplyLobbyData(UiNodeViewModel root, string windowName)
    {
        _gameResources.EnsureLoaded();
        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();

        if (IsGameLobbyWindow(windowName))
        {
            _lobbySession.MapSearchText = string.Empty;
            _lobbySession.PlayerState.LoadCatalogs();

            if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            {
                CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
                string localNick = CnCNetSessionService.Instance.LocalNick;
                string hostName = room?.HostName ?? localNick;
                bool resetSlots = _lobbySession.PlayerState.Mode != LobbyPlayerMode.Multiplayer;
                LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
                    _lobbySession.PlayerState,
                    localNick,
                    hostName,
                    room?.IsHost == true,
                    resetSlots);
            }
            else
            {
                LobbyPlayerSlotUiRules.ConfigureForSkirmish(_lobbySession.PlayerState);
                if (!_lobbySession.PlayerState.TryLoadSkirmishSettings())
                    _lobbySession.PlayerState.LoadDefaultSkirmishSlots();
            }

            LobbyPlayerBindingApplier.Apply(root, _lobbySession.PlayerState, resources, _mainBehaviors);

            if (windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            {
                WireCnCNetGameOptionsBridge();
                ApplyCnCNetGameRoomPlayers(root);
                UpdateCnCNetGameBroadcastListing(root);
            }
        }

        if (IsChannelLobbyWindow(windowName))
        {
            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                CnCNetSessionService session = CnCNetSessionService.Instance;
                session.ConnectIfNeeded();
                session.EnsureGameBroadcastChannelsJoined();
                session.SyncLobbyStateFromCore();
            }

            GameDataBindingApplier.ApplyChannelLobby(root, CnCNetSessionService.Instance.LobbyState);
        }

        if (!IsGameLobbyWindow(windowName))
            return;

        int defaultFilter = _gameResources.GameModes.Count > 0 ? LobbySessionState.FavoriteFilterIndex + 1 : 0;
        GameDataBindingApplier.ApplyLobby(root, _gameResources, _lobbySession, resources, defaultFilter);

        UiNodeViewModel? ddGameMode = FindVm(root, "ddGameMode");
        if (ddGameMode != null)
            ddGameMode.SelectionChanged += RefreshLobbyMapList;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        if (lbMapList != null)
        {
            lbMapList.SelectionChanged += () =>
            {
                GameDataBindingApplier.ResolveStartInteractionFlags(
                    _lobbySession.PlayerState,
                    out bool canAssign,
                    out bool canSelectLocal);
                GameDataBindingApplier.UpdateMapSelectionDisplay(
                    root,
                    _lobbySession.VisibleMaps,
                    lbMapList.SelectedIndex,
                    resources,
                    _lobbySession.PlayerState,
                    canAssign,
                    canSelectLocal);
                UpdateLaunchButtonState(root);
                RefreshCnCNetGameListing();
            };
        }

        WireMapPreviewStartMarkers(root);

        UiNodeViewModel? tbMapSearch = FindVm(root, "tbMapSearch");
        if (tbMapSearch != null)
        {
            tbMapSearch.InputText = string.Empty;
            tbMapSearch.InputTextChanged -= OnLobbyMapSearchChanged;
            tbMapSearch.InputTextChanged += OnLobbyMapSearchChanged;
        }

        int occupied = _lobbySession.PlayerState.Slots.Count(s => s.IsOccupied);
        ShowStatus($"Maps: {_gameResources.Maps.Count}, players: {occupied}, modes: {_gameResources.GameModes.Count}");
    }

    private void WireMapPreviewStartMarkers(UiNodeViewModel root)
    {
        UiNodeViewModel? previewBox = FindVm(root, "MapPreviewBox");
        if (previewBox == null)
            return;

        // Re-subscribe cleanly when the lobby window is reapplied.
        previewBox.StartMarkerLeftClicked -= OnMapStartMarkerLeftClicked;
        previewBox.StartMarkerRightClicked -= OnMapStartMarkerRightClicked;
        previewBox.StartMarkerLeftClicked += OnMapStartMarkerLeftClicked;
        previewBox.StartMarkerRightClicked += OnMapStartMarkerRightClicked;
    }

    private void OnMapStartMarkerLeftClicked(int startLocation1Based)
    {
        if (_activeRoot == null)
            return;

        LobbyPlayerState state = _lobbySession.PlayerState;
        GameDataBindingApplier.ResolveStartInteractionFlags(state, out bool canAssign, out bool canSelectLocal);
        MapEntry? map = GetCurrentLobbyMap();
        bool enforce = map?.EnforceMaxPlayers ?? false;

        if (canAssign)
        {
            // Host path: assign the first occupied slot that is still random (StartIndex==0),
            // else reassign slot 0. Full context menu is Phase B UI polish; protocol path is identical.
            int target = -1;
            for (int i = 0; i < state.Slots.Length; i++)
            {
                if (state.Slots[i].IsOccupied && state.Slots[i].StartIndex == 0)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                for (int i = 0; i < state.Slots.Length; i++)
                {
                    if (state.Slots[i].IsOccupied)
                    {
                        target = i;
                        break;
                    }
                }
            }

            if (target < 0)
                return;

            if (!MapStartLocationRules.TryApplyHostAssignment(state.Slots, target, startLocation1Based, enforce))
                return;

            MultiplayerSlotCoordinator.HandleHostOptionsEdit(state, CnCNetSessionService.Instance.GameRoom);
            RefreshMapStartMarkersAndPlayerUi();
            return;
        }

        if (canSelectLocal)
        {
            if (!MapStartLocationRules.TryApplyJoinerSelection(
                    state.Slots,
                    state.LocalPlayerName,
                    startLocation1Based,
                    enforce))
            {
                ShowStatus($"Starting location {startLocation1Based} is occupied.");
                return;
            }

            int localIndex = Array.FindIndex(state.Slots, s => s.IsHumanLocal);
            if (localIndex < 0)
                localIndex = Array.FindIndex(
                    state.Slots,
                    s => !s.IsAi && s.Name.Equals(state.LocalPlayerName, StringComparison.OrdinalIgnoreCase));

            if (localIndex >= 0)
                MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(
                    state,
                    localIndex,
                    CnCNetSessionService.Instance.GameRoom);

            RefreshMapStartMarkersAndPlayerUi();
        }
    }

    private void OnMapStartMarkerRightClicked(int startLocation1Based)
    {
        if (_activeRoot == null)
            return;

        LobbyPlayerState state = _lobbySession.PlayerState;
        GameDataBindingApplier.ResolveStartInteractionFlags(state, out bool canAssign, out bool canSelectLocal);

        if (canAssign)
        {
            MapStartLocationRules.ClearOccupantsOf(state.Slots, startLocation1Based);
            MultiplayerSlotCoordinator.HandleHostOptionsEdit(state, CnCNetSessionService.Instance.GameRoom);
            RefreshMapStartMarkersAndPlayerUi();
            return;
        }

        if (canSelectLocal
            && MapStartLocationRules.TryClearLocalIfOwn(state.Slots, state.LocalPlayerName, startLocation1Based))
        {
            int localIndex = Array.FindIndex(state.Slots, s => s.IsHumanLocal);
            if (localIndex >= 0)
                MultiplayerSlotCoordinator.HandleJoinerOptionsEdit(
                    state,
                    localIndex,
                    CnCNetSessionService.Instance.GameRoom);
            RefreshMapStartMarkersAndPlayerUi();
        }
    }

    private void RefreshMapStartMarkersAndPlayerUi()
    {
        if (_activeRoot == null)
            return;

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        LobbyPlayerBindingApplier.Apply(_activeRoot, _lobbySession.PlayerState, resources, _mainBehaviors);
        RefreshCurrentMapStartMarkers();
        UpdateLaunchButtonState(_activeRoot);
    }

    private void RefreshCurrentMapStartMarkers()
    {
        if (_activeRoot == null)
            return;

        GameDataBindingApplier.ResolveStartInteractionFlags(
            _lobbySession.PlayerState,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.RefreshMapStartMarkers(
            _activeRoot,
            GetCurrentLobbyMap(),
            _lobbySession.PlayerState,
            canAssign,
            canSelectLocal);
    }

    private MapEntry? GetCurrentLobbyMap()
    {
        if (_activeRoot == null)
            return null;
        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        return _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
    }

    private void OnLobbyMapSearchChanged()
    {
        if (_activeRoot == null)
            return;

        UiNodeViewModel? tbMapSearch = FindVm(_activeRoot, "tbMapSearch");
        if (tbMapSearch == null)
            return;

        _lobbySession.MapSearchText = tbMapSearch.InputText;
        RefreshLobbyMapList();
    }

    private void UpdateCnCNetGameBroadcastListing(UiNodeViewModel root)
    {
        CnCNetSessionService session = CnCNetSessionService.Instance;
        if (session.IsGameRoomJoinPending)
            return;

        CnCNetActiveGameRoom? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);

        CnCNetSessionService.Instance.UpdateGameRoomListing(
            map?.UntranslatedName ?? string.Empty,
            gameMode?.UntranslatedUIName ?? string.Empty,
            map?.Sha1 ?? string.Empty);
    }

    private void PushCnCNetHostLobbyState()
    {
        CnCNetSessionService session = CnCNetSessionService.Instance;
        if (session.IsGameRoomJoinPending)
            return;

        CnCNetActiveGameRoom? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        if (_activeRoot != null)
            LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        CnCNetSessionService.Instance.SyncGameRoomFromLobby(_lobbySession.PlayerState);
        RefreshCnCNetGameListing();
    }

    public void RefreshCnCNetGameListing()
    {
        if (_activeRoot != null && CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            UpdateCnCNetGameBroadcastListing(_activeRoot);
    }

    public void RefreshCnCNetGameRoomPlayers()
    {
        if (_activeRoot != null && CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            ApplyCnCNetGameRoomPlayers(_activeRoot);
    }

    public async void TryJoinSelectedCnCNetGame()
    {
        CnCNetSessionService session = CnCNetSessionService.Instance;

        if (session.IsGameRoomJoinPending)
        {
            ShowStatus("Joining game room — please wait...");
            return;
        }

        if (session.ActiveGameRoom != null)
        {
            ShowStatus("Already in a game room.");
            NavigateTo("CnCNetGameLobby");
            return;
        }

        if (_activeRoot != null)
            GameDataBindingApplier.SyncChannelGameSelection(_activeRoot, session.LobbyState);

        CnCNetHostedGameSummary? game = session.ResolveSelectedGameForJoin();
        if (game == null)
        {
            ShowStatus("Select a game from the list first.");
            return;
        }

        string? password = null;
        if (game.RequiresPassword)
        {
            password = await ClientDialogService.ShowPasswordPromptAsync(this, game.RoomName);
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowStatus("Join cancelled.");
                return;
            }
        }

        if (!session.TryJoinGame(game, password, out string message))
        {
            ShowStatus(message);
            return;
        }

        EnterCnCNetGameLobbyConnecting();
        ShowStatus(message);
    }

    public void EnterCnCNetGameLobbyConnecting()
    {
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room == null)
            return;

        string localNick = CnCNetSessionService.Instance.LocalNick;
        string hostName = string.IsNullOrWhiteSpace(room.HostName) ? localNick : room.HostName;

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _lobbySession.PlayerState,
            localNick,
            hostName,
            room.IsHost,
            resetSlots: true);

        if (room.IsHost)
            _lobbySession.PlayerState.EnsureHostAsFirstHuman(hostName, localNick);

        if (!CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            NavigateTo("CnCNetGameLobby");
        else if (_activeRoot != null)
            ApplyCnCNetGameLobbyConnectingState(_activeRoot, room);
    }

    private void ApplyCnCNetGameLobbyConnectingState(UiNodeViewModel root, CnCNetActiveGameRoom room)
    {
        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        LobbyPlayerBindingApplier.Apply(root, _lobbySession.PlayerState, resources, _mainBehaviors);
        WireCnCNetGameOptionsBridge();
        CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _mainBehaviors, isJoiner: !room.IsHost);
        UpdateLaunchButtonState(root);
    }

    private List<string> GetCnCNetPlayerNames()
    {
        CnCNetGameRoomSession? gameRoom = CnCNetSessionService.Instance.GameRoom;
        if (gameRoom != null)
        {
            IReadOnlyList<string> names = gameRoom.GetHumanPlayerNames();
            if (names.Count > 0)
                return names.ToList();
        }

        return _lobbySession.PlayerState.Slots
            .Where(s => s.IsOccupied && !s.IsAi)
            .Select(s => s.Name)
            .ToList();
    }

    private void OnCnCNetGameStarting(CnCNetStartGameInfo startInfo)
    {
        Logger.Log($"CnCNet GameStarting: gameId={startInfo.UniqueGameId}, tunnel={startInfo.Tunnel.Address}:{startInfo.Tunnel.Port}, localPort={startInfo.LocalPlayerPort}, window={CurrentWindow}");

        if (_activeRoot == null || !CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log("CnCNet GameStarting: aborted — not in CnCNetGameLobby.");
            return;
        }

        UiNodeViewModel? ddGameMode = FindVm(_activeRoot, "ddGameMode");
        if (ddGameMode != null)
            _lobbySession.FilterIndex = ddGameMode.SelectedIndex;

        LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);

        if (map == null || gameMode == null)
        {
            Logger.Log($"CnCNet GameStarting: map/gameMode missing (map={map?.DisplayName ?? "null"}, mode={gameMode?.DisplayName ?? "null"}, visibleMaps={_lobbySession.VisibleMaps.Count}, filter={_lobbySession.FilterIndex}).");
            ShowStatus("Cannot launch: map or game mode missing.");
            ClientDialogService.ShowError(this, "Cannot launch game", "Map or game mode is missing. Reselect a map and try again.");
            return;
        }

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Players = _lobbySession.PlayerState,
            LobbyRoot = _activeRoot,
        };

        Logger.Log($"CnCNet GameStarting: launching {map.DisplayName} / {gameMode.DisplayName} via Syringe.");

        ShowStatus("Launching game...");

        var startSnapshot = startInfo;
        var roomPlayers = CnCNetSessionService.Instance.GameRoom?.Players;
        var gameOptions = CollectCnCNetGameOptions();

        _gameLaunch.BeginLaunch(
            _environment,
            new MultiplayerLaunchSession(request, startSnapshot, roomPlayers, gameOptions),
            (ok, result) => Dispatcher.UIThread.Post(() =>
            {
                if (!ok)
                {
                    ShowStatus($"Launch failed: {result}");
                    ClientDialogService.ShowError(this, "Cannot launch game", result);
                    return;
                }

                ShowStatus(result);
            }));
    }

    private void UpdateLaunchButtonState(UiNodeViewModel? root = null)
    {
        root ??= _activeRoot;
        if (root == null || !IsGameLobbyWindow(CurrentWindow))
            return;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        bool canLaunch = _lobbySession.VisibleMaps.Count > 0
            && (lbMapList?.SelectedIndex ?? -1) >= 0;

        CnCNetActiveGameRoom? cncRoom = CnCNetSessionService.Instance.ActiveGameRoom
            ?? CnCNetSessionService.Instance.GameRoom?.Room;
        UiNodeViewModel? btnLaunch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? chkAutoReady = FindVm(root, "chkAutoReady");

        if (CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
        {
            ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
            CnCNetSessionService session = CnCNetSessionService.Instance;

            if (cncRoom == null)
            {
                btnLaunch?.SetDisplayText("Launch Game");
                CnCNetGameLobbyUiHelper.ApplyHostToolbar(root);
                canLaunch = false;
                _bindingSession.State.SetCanLaunchGame(false);
                btnLaunch?.IsEnabled = false;
                return;
            }

            bool isJoiner = !cncRoom.IsHost;
            bool connecting = session.IsGameRoomJoinPending;

            CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _mainBehaviors, isJoiner);

            if (connecting)
            {
                canLaunch = false;
                _bindingSession.State.SetCanLaunchGame(false);
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

        _bindingSession.State.SetCanLaunchGame(canLaunch);
        if (btnLaunch != null && btnLaunch.IsVisible)
            btnLaunch.IsEnabled = canLaunch;
    }

    private void UpdateTopBar()
    {
        bool show = ShouldShowTopBar();
        if (!show)
        {
            PART_TopBarHost.Deactivate();
            return;
        }

        if (!PART_TopBarHost.IsVisible)
            PART_TopBarHost.IsVisible = true;

        PART_TopBarHost.Activate(this);

        var lobby = CnCNetSessionService.Instance.LobbyState;
        string status = lobby.ConnectionStatus;
        if (string.IsNullOrWhiteSpace(status))
            status = CnCNetSessionService.Instance.Connection?.IsConnected == true ? "已连接" : "Offline";

        PART_TopBarHost.Bar.UpdateState(status, CnCNetSessionService.Instance.OnlinePlayerCount);
    }

    private bool ShouldShowTopBar()
        => CurrentWindow.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals("LANLobby", StringComparison.OrdinalIgnoreCase)
           || CurrentWindow.Equals("LANGameLobby", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameLobbyWindow(string windowName)
        => windowName.Equals("SkirmishLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("LANGameLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("MultiplayerGameLobby", StringComparison.OrdinalIgnoreCase);

    private static bool IsChannelLobbyWindow(string windowName)
        => windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase);

    private void ApplyCampaignOverlay(UiNodeViewModel root, CampaignSideFilter sideFilter = CampaignSideFilter.All)
    {
        ResourceResolver resources = _overlayEngine?.Resources ?? _mainEngine?.Resources ?? new ResourceResolver();
        GameDataBindingApplier.ApplyCampaignOverlay(root, _gameResources, _lobbySession, resources, sideFilter);
    }

    private static int ResolveCampaignDifficulty(UiNodeViewModel overlayRoot)
    {
        UiNodeViewModel? trackbar = FindVm(overlayRoot, "trbDifficultySelector");
        if (trackbar != null && trackbar.SelectedIndex >= 0)
            return Math.Clamp(trackbar.SelectedIndex, 0, 2);

        return Math.Clamp(UserINISettings.Instance.Difficulty, 0, 2);
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

    private bool _applyingCnCNetGameRoomPlayers;

    private void OnCnCNetGameRoomJoined(CnCNetActiveGameRoom room)
    {
        if (!CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            NavigateTo("CnCNetGameLobby");
        else if (_activeRoot != null)
        {
            WireCnCNetGameOptionsBridge();
            ApplyCnCNetGameRoomPlayers(_activeRoot);
            GameDataBindingApplier.ApplyGameRoomChat(_activeRoot, CnCNetSessionService.Instance.GameRoom);
        }

        CnCNetGameRoomSession? gameRoom = CnCNetSessionService.Instance.GameRoom;
        if (gameRoom != null)
        {
            gameRoom.ChangeTunnelRequested -= OpenGameRoomTunnelSelection;
            gameRoom.ChangeTunnelRequested += OpenGameRoomTunnelSelection;
        }

        if (room.IsHost)
            PushCnCNetHostLobbyState();

        ShowStatus($"Entered \"{room.RoomName}\".");
    }

    private void OnCnCNetGameRoomJoinFailed(string message)
    {
        ShowStatus(message);
        if (CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            NavigateTo("CnCNetLobby");
    }

    private void OnCnCNetGameRoomHostAbandoned()
    {
        ClearCnCNetGameOptionsBridge();
        ShowStatus("The game host has abandoned the game.");
        CnCNetSessionService.Instance.EnsureGameBroadcastChannelsJoined();
        NavigateTo("CnCNetLobby");
    }

    private void WireCnCNetGameOptionsBridge()
    {
        CnCNetSessionService session = CnCNetSessionService.Instance;
        session.GameOptionsControlCounts = () => CnCNetGameOptionsUiBridge.GetControlCounts(_activeRoot);
        session.GameOptionsProvider = CollectCnCNetGameOptions;
        session.GameOptionsReceiver = ApplyCnCNetGameOptionsFromHost;
        WireHostGameOptionChangeBroadcast();
        session.GameRoom?.TryFlushPendingGameOptions();
    }

    private void ClearCnCNetGameOptionsBridge()
    {
        UnwireHostGameOptionChangeBroadcast();
        CnCNetSessionService session = CnCNetSessionService.Instance;
        session.GameOptionsControlCounts = null;
        session.GameOptionsProvider = null;
        session.GameOptionsReceiver = null;
    }

    private void WireHostGameOptionChangeBroadcast()
    {
        UnwireHostGameOptionChangeBroadcast();
        if (_activeRoot == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(_activeRoot);

        foreach (UiNodeViewModel chk in checkBoxes)
            chk.CheckedChanged += OnHostGameOptionControlChanged;

        foreach (UiNodeViewModel dd in dropDowns)
            dd.SelectionChanged += OnHostGameOptionControlChanged;
    }

    private void UnwireHostGameOptionChangeBroadcast()
    {
        if (_activeRoot == null)
            return;

        (IReadOnlyList<UiNodeViewModel> checkBoxes, IReadOnlyList<UiNodeViewModel> dropDowns) =
            CnCNetGameOptionsCatalog.Enumerate(_activeRoot);

        foreach (UiNodeViewModel chk in checkBoxes)
            chk.CheckedChanged -= OnHostGameOptionControlChanged;

        foreach (UiNodeViewModel dd in dropDowns)
            dd.SelectionChanged -= OnHostGameOptionControlChanged;
    }

    private void OnHostGameOptionControlChanged()
    {
        CnCNetGameRoomSession? room = CnCNetSessionService.Instance.GameRoom;
        if (room is not { IsHost: true, IsLocalJoined: true })
            return;

        // UpdateHostListing → BroadcastGameOptionsLocked (GO to everyone already in the room).
        RefreshCnCNetGameListing();
    }

    private CnCNetGameOptionsState CollectCnCNetGameOptions()
    {
        UiNodeViewModel? lbMapList = _activeRoot != null ? FindVm(_activeRoot, "lbMapList") : null;
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);
        CnCNetGameRoomSession? room = CnCNetSessionService.Instance.GameRoom;
        CnCNetGameOptionsState state = CnCNetGameOptionsUiBridge.Collect(
            _activeRoot,
            map,
            gameMode,
            room?.RandomSeed ?? Random.Shared.Next(),
            room?.RemoveStartingLocations ?? false);

        if (room == null)
            return state;

        // Prefer session-persisted multiplayer timing fields when set from a prior GO.
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

    private void ApplyCnCNetGameOptionsFromHost(CnCNetGameOptionsState state)
    {
        void Apply()
        {
            if (_activeRoot == null)
                return;

            CnCNetGameOptionsUiBridge.Apply(_activeRoot, state, _gameResources, _lobbySession);
            RefreshLobbyMapList();
            // Joiner must not re-broadcast GO via RefreshCnCNetGameListing host path.
            if (CnCNetSessionService.Instance.GameRoom is { IsHost: true })
                RefreshCnCNetGameListing();
            else
                UpdateLaunchButtonState(_activeRoot);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void OnCnCNetStateChanged()
    {
        int count = CnCNetSessionService.Instance.OnlinePlayerCount;
        _bindingSession.State.SetOnlinePlayerCount(count);

        if (_activeRoot != null && IsChannelLobbyWindow(CurrentWindow))
        {
            GameDataBindingApplier.ApplyChannelLobby(_activeRoot, CnCNetSessionService.Instance.LobbyState);
            if (!IsFloatingOverlayOpen)
                ShowStatus($"CnCNet: {CnCNetSessionService.Instance.LobbyState.ConnectionStatus}");
        }

        if (_activeRoot != null && CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            RefreshCnCNetGameRoomUiFromSession(_activeRoot);

        if (CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && _activeRoot != null)
            StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");

        UpdateTopBar();
    }

    /// <summary>Read session → UI only; never pushes PO/GAME/GO back to the network.</summary>
    private void RefreshCnCNetGameRoomUiFromSession(UiNodeViewModel root)
    {
        if (_applyingCnCNetGameRoomPlayers)
            return;

        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room == null)
            return;

        _applyingCnCNetGameRoomPlayers = true;
        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: false);
            // Refresh in-room chat timeline + tbChatInput enabled state.
            GameDataBindingApplier.ApplyGameRoomChat(root, CnCNetSessionService.Instance.GameRoom);
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet game room UI refresh failed: {ex.Message}");
            Logger.Log(ex.ToString());
        }
        finally
        {
            _applyingCnCNetGameRoomPlayers = false;
        }
    }

    private void ApplyCnCNetGameRoomPlayers(UiNodeViewModel root)
    {
        if (_applyingCnCNetGameRoomPlayers)
            return;

        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room == null)
            return;

        _applyingCnCNetGameRoomPlayers = true;
        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: true);
        }
        finally
        {
            _applyingCnCNetGameRoomPlayers = false;
        }
    }

    private void ApplyCnCNetGameRoomPlayersCore(UiNodeViewModel root, CnCNetActiveGameRoom room, bool updateStatus)
    {
        CnCNetGameRoomSession? gameRoom = CnCNetSessionService.Instance.GameRoom;
        string localNick = CnCNetSessionService.Instance.LocalNick;
        string hostName = room.HostName;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = gameRoom?.HostName ?? localNick;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = localNick;

        IReadOnlyList<CnCNetGameRoomPlayer> entries = gameRoom?.Players ?? [];

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _lobbySession.PlayerState,
            localNick,
            hostName,
            room.IsHost);

        MultiplayerSlotLayout.ApplyToState(_lobbySession.PlayerState, entries, localNick);

        if (room.IsHost)
            _lobbySession.PlayerState.EnsureHostAsFirstHuman(hostName, localNick);
        else
            _lobbySession.PlayerState.MarkLocalHuman(localNick);

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        LobbyPlayerBindingApplier.Apply(root, _lobbySession.PlayerState, resources, _mainBehaviors);

        bool locked = gameRoom?.Locked ?? false;
        LobbyPlayerStatusApplier.Apply(
            root,
            _lobbySession.PlayerState,
            resources,
            _mainBehaviors,
            entries,
            locked,
            room.IsHost);

        CnCNetGameLobbyUiHelper.ApplyToolbarRole(root, resources, _mainBehaviors, isJoiner: !room.IsHost);
        CnCNetGameLobbyUiHelper.UpdateManualReadyLabel(root, isJoiner: !room.IsHost);
        ApplyLockGameButtonLabel(root, room.IsHost, locked);
        UpdateLaunchButtonState(root);
        RefreshCurrentMapStartMarkers();

        if (updateStatus)
        {
            ShowStatus(room.IsHost
                ? $"Hosting \"{room.RoomName}\" — waiting for players."
                : $"Joined \"{room.RoomName}\" — waiting for host.");
        }
    }

    private static void ApplyLockGameButtonLabel(UiNodeViewModel root, bool isHost, bool locked)
    {
        if (!isHost)
            return;

        FindVm(root, "btnLockGame")?.SetDisplayText(locked ? "Unlock Game" : "Lock Game");
    }

    private void OnGameResourcesLoaded()
    {
        if (_activeRoot != null && CurrentWindow.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            ApplyLobbyData(_activeRoot, CurrentWindow);

        if (_overlayRoot != null
            && _floatingOverlayWindow?.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase) == true)
            ApplyCampaignOverlay(_overlayRoot);
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
            NavigateTo("MainMenu");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3 && !IsFloatingOverlayOpen)
        {
            NavigateTo("CnCNetLobby");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12 && !IsFloatingOverlayOpen)
        {
            OpenFloatingOverlay("OptionsWindow");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && IsCnCNetLobbyActive() && !IsFloatingOverlayOpen)
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

            if (CurrentWindow != "MainMenu")
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
            _ => null,
        };

        if (tab != null)
            SelectOptionsTab(tab.Value);
    }

    private void TrySendChannelChat()
    {
        if (_activeRoot == null)
            return;

        UiNodeViewModel? tbChat = FindVm(_activeRoot, "tbChatInput");
        if (tbChat == null || string.IsNullOrWhiteSpace(tbChat.InputText))
            return;

        string message = tbChat.InputText.Trim();
        CnCNetSessionService.Instance.SendChatMessage(message);
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
