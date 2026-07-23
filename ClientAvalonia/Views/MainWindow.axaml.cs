using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientCore;
using ClientCore.Settings;
using ClientAvalonia.CnCNet;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Lobby;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Overlays;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;
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
    private readonly Session.SkirmishSession _skirmishSession;
    private readonly ICnCNetSession _cncnet;
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
        _skirmishSession = new Session.SkirmishSession(_lobbySession.PlayerState);
        _cncnet = ResolveCnCNetSession();
        _bindingSession = new UiBindingSession(_environment);
        _gameLaunch.StatusChanged += msg => Dispatcher.UIThread.Post(() => ShowStatus(msg));
        _gameLaunch.GameProcessStarted += () => Dispatcher.UIThread.Post(OnGameProcessStarted);
        _gameLaunch.GameProcessExited += () => Dispatcher.UIThread.Post(OnGameProcessExited);
        _updateService.EnsureHandlersRegistered();
        _updateService.StatusChanged += OnUpdateStatusChanged;
        ClientStartupService.LocalVersionsChecked += OnLocalVersionsChecked;
        _gameResources.Loaded += OnGameResourcesLoaded;
        _cncnet.StateChanged += OnCnCNetStateChanged;
        _cncnet.PrivateMessageArrived += OnPrivateMessageArrived;
        _cncnet.GameRoomJoined += OnCnCNetGameRoomJoined;
        _cncnet.GameRoomJoinFailed += OnCnCNetGameRoomJoinFailed;
        _cncnet.GameStarting += OnCnCNetGameStarting;
        _cncnet.GameRoomHostAbandoned += OnCnCNetGameRoomHostAbandoned;
        if (_cncnet is CnCNetSessionServiceAdapter adapter)
            adapter.Service.WafAlertRaised += OnCnCNetWafAlert;
        _cncnet.EnsureStarted();
        InitializeComponent();
        KeyDown += OnKeyDown;
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
        PART_TopBarHost.Bar.BindNavigation(NavigateTo, LogoutToMainMenu, () => OpenPrivateMessagingOverlay());
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

    private void TryAutomaticCnCNetLogin()
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
            _cncnet.ConnectIfNeeded();
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
            IniBehaviorApplier.Apply(vm, _mainBehaviors, this, ResolveIniActionCatalog());

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

        if (_environment.ResolveWindowLoadTarget(windowName) is not { } target)
        {
            ShowStatus($"INI not found for window: {windowName}");
            return;
        }

        string iniPath = target.IniPath;
        string sectionName = target.SectionName;

        try
        {
            (int width, int height) = FloatingOverlayLayout.ResolveOverlaySize(iniPath, sectionName);

            _overlayBehaviors.Clear();
            FloatingOverlayBehaviors.RegisterForOverlay(_overlayBehaviors, this, windowName);

            _overlayEngine = LayoutEngine.CreateForWindow(_environment, iniPath, sectionName);
            var factory = new UiViewModelFactory(_overlayEngine.Resources, _overlayBehaviors);

            UiNodeTree tree = _overlayEngine.LoadWindow(iniPath, sectionName);
            _overlayRoot = factory.CreateTree(tree);
            IniBehaviorApplier.Apply(_overlayRoot, _overlayBehaviors, this, ResolveIniActionCatalog());
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
                WafBlocklistApplier.Apply(_overlayRoot, CnCNetSessionService.Instance.IngressWaf);
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

        if (_floatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true)
        {
            _cncnet.SetViewingPrivateMessagePeer(null);
            ResetOverlayPanelChrome();
        }

        CloseFloatingOverlayCore(restoreIniOverlayView: true);
        UpdateTopBar();
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

        var tunnels = _cncnet.Tunnels;
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
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore;
        if (room is not { IsHost: true })
        {
            ShowStatus("Only the game host can change the tunnel.");
            return;
        }

        var tunnels = _cncnet.Tunnels;
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
                if (_cncnet.TryHostChangeTunnel(tunnel))
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
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore;
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
                _cncnet.UpdateGameLobbySettings(name, max, skill, password);
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
           && _cncnet.GameRoom is { IsLocalJoined: true };

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
            _cncnet.LeaveGameRoom();

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
            || _cncnet.ActiveGameRoom != null
            || _cncnet.Connection is { IsConnected: true })
        {
            _cncnet.Disconnect();
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
            // Phase 3 P3-2：走 Session-aware 入口（Slots + SideCount）。
            Slots = _lobbySession.PlayerState.Slots,
            SideCount = _lobbySession.PlayerState.SideNames.Count,
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
        ICnCNetSession session = _cncnet;

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

        // Security tab (index 4): refresh blocklist view when entering.
        if (index == 4)
            WafBlocklistApplier.Apply(_overlayRoot, CnCNetSessionService.Instance.IngressWaf);

        ShowStatus($"Options tab {index + 1}/7");
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
            _lobbySession.UIMode,
            _lobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.UpdateMapSelectionDisplay(
            _activeRoot,
            _lobbySession.VisibleMaps,
            index,
            resources,
            _lobbySession.PlayerState.Slots,
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
                ICnCNetGameSession? room = _cncnet.ActiveGameRoom;
                string localNick = _cncnet.LocalNick;
                string hostName = room?.HostName ?? localNick;
                // Phase 2 P2-1：直接读 LobbySessionState.UIMode（真相源），不再绕道 PlayerState.Mode。
                bool resetSlots = _lobbySession.UIMode != LobbyPlayerMode.Multiplayer;
                if (room != null)
                {
                    // 走 Session-aware 重载：写 UI 态 + ApplyToSlots 到 session.PlayerSlots。
                    LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
                        _lobbySession,
                        room,
                        entries: [],
                        localNick,
                        hostName,
                        room?.IsHost == true,
                        resetSlots);
                    _lobbySession.PlayerState.SyncFromSlots(room.PlayerSlots);
                }
                else
                {
                    LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
                        _lobbySession.PlayerState,
                        localNick,
                        hostName,
                        room?.IsHost == true,
                        resetSlots);
                }
            }
            else
            {
                LobbyPlayerSlotUiRules.ConfigureForSkirmish(_lobbySession.PlayerState);
                if (_lobbySession.PlayerState.TryLoadSkirmishSettings())
                {
                    // Restored user's saved skirmish layout — keep as is.
                }
                else if (_gameResources.Maps.Count > 0)
                {
                    // New user / no saved layout: pre-fill AI to match the first map's
                    // MaxPlayers (auto-ai-slots.md v2 default behavior).
                    MapEntry defaultMap = _gameResources.Maps[0];
                    _lobbySession.PlayerState.LoadDefaultSkirmishSlots(defaultMap.MaxPlayers);
                }
                else
                {
                    _lobbySession.PlayerState.LoadDefaultSkirmishSlots();
                }
            }

            // Phase 4 P4-1：走 sink 路径（session 是 IGameSession 子类）。
            // _lobbySession.PlayerState 作为 UI 镜像，调用方在 StateChanged 时 SyncFromSlots 同步。
            LobbyPlayerBindingApplier.Apply(
                root,
                (IGameSession)_skirmishSession,
                _lobbySession.PlayerState,
                _lobbySession,
                LobbyCatalogService.Instance,
                resources,
                _mainBehaviors,
                gameRoomProvider: () => _cncnet.GameRoom,
                onSlotsMutated: OnLobbySlotsMutated);

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
                _cncnet.ConnectIfNeeded();
                _cncnet.EnsureGameBroadcastChannelsJoined();
                ((CnCNetSessionServiceAdapter)_cncnet).Service.SyncLobbyStateFromCore();
            }

            GameDataBindingApplier.ApplyChannelLobby(root, _cncnet.LobbyState);
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
                // Skirmish-only: refill AI slots to match the new map's MaxPlayers.
                // Per auto-ai-slots.md v2 — non-preserving, single-player only.
                // Multiplayer/CnCNet slots are managed by join/part events.
                if (IsSkirmishWindow(windowName))
                {
                    MapEntry? newMap = _lobbySession.GetSelectedMap(lbMapList.SelectedIndex);
                    if (newMap != null)
                    {
                        DefaultAiSlotPolicy.AutoFillToMapCapacity(
                            _skirmishSession,
                            newMap.MaxPlayers,
                            ResolvePlayerName(),
                            ResolveColorCatalog(),
                            _lobbySession.PlayerState.AiNames);
                    }
                }

                GameDataBindingApplier.ResolveStartInteractionFlags(
                    _lobbySession.UIMode,
                    _lobbySession.AllowHostPlayerOptions,
                    out bool canAssign,
                    out bool canSelectLocal);
                GameDataBindingApplier.UpdateMapSelectionDisplay(
                    root,
                    _lobbySession.VisibleMaps,
                    lbMapList.SelectedIndex,
                    resources,
                    _lobbySession.PlayerState.Slots,
                    canAssign,
                    canSelectLocal);

                // Re-apply player UI so the new AI slot layout is rendered.
                // Phase 4 P4-1：走 sink 路径（如果有 active game session），否则退回 legacy。
                ICnCNetGameSession? activeRoom = _cncnet.ActiveGameRoom;
                if (activeRoom is IGameSession gameSession)
                {
                    LobbyPlayerBindingApplier.Apply(
                        root,
                        gameSession,
                        _lobbySession.PlayerState,
                        _lobbySession,
                        LobbyCatalogService.Instance,
                        resources,
                        _mainBehaviors,
                        gameRoomProvider: () => _cncnet.GameRoom,
                        onSlotsMutated: OnLobbySlotsMutated);
                }
                else
                {
                    LobbyPlayerBindingApplier.Apply(
                        root,
                        _lobbySession.PlayerState,
                        resources,
                        _mainBehaviors,
                        onSlotsMutated: OnLobbySlotsMutated,
                        gameRoomProvider: () => _cncnet.GameRoom);
                }

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

            MultiplayerSlotCoordinator.HandleHostOptionsEdit(state, _cncnet.GameRoom);
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
                    _cncnet.GameRoom);

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
            MultiplayerSlotCoordinator.HandleHostOptionsEdit(state, _cncnet.GameRoom);
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
                    _cncnet.GameRoom);
            RefreshMapStartMarkersAndPlayerUi();
        }
    }

    private void RefreshMapStartMarkersAndPlayerUi()
    {
        if (_activeRoot == null)
            return;

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        // Phase 4 P4-1：根据当前 active session 选择 sink 路径或 legacy 路径。
        IGameSession? gameSession = ResolveActiveGameSession();
        if (gameSession != null)
        {
            LobbyPlayerBindingApplier.Apply(
                _activeRoot,
                gameSession,
                _lobbySession.PlayerState,
                _lobbySession,
                LobbyCatalogService.Instance,
                resources,
                _mainBehaviors,
                gameRoomProvider: () => _cncnet.GameRoom,
                onSlotsMutated: OnLobbySlotsMutated);
        }
        else
        {
            LobbyPlayerBindingApplier.Apply(_activeRoot, _lobbySession.PlayerState, resources, _mainBehaviors);
        }
        RefreshCurrentMapStartMarkers();
        UpdateLaunchButtonState(_activeRoot);
    }

    /// <summary>
    /// Phase 4 P4-1：解析当前 active game session（Skirmish 或 CnCNet 房间）。
    /// 返回 null 表示尚未进入具体 session（如刚连入 CnCNet 但未进房）。
    /// </summary>
    private IGameSession? ResolveActiveGameSession()
    {
        if (IsSkirmishWindow(CurrentWindow))
            return _skirmishSession;

        ICnCNetGameSession? room = _cncnet?.ActiveGameRoom;
        return room as IGameSession;
    }

    /// <summary>
    /// Fired by <see cref="LobbyPlayerBindingApplier"/> whenever a dropdown
    /// mutates slot state (name/side/color/team/start), in any lobby mode.
    /// Used to refresh dependent UI (map start markers, launch button) without
    /// waiting for an extra user click. See auto-refresh-design.md.
    /// </summary>
    private void OnLobbySlotsMutated()
    {
        if (_activeRoot == null)
            return;

        RefreshCurrentMapStartMarkers();
        UpdateLaunchButtonState(_activeRoot);
    }

    private void RefreshCurrentMapStartMarkers()
    {
        if (_activeRoot == null)
            return;

        GameDataBindingApplier.ResolveStartInteractionFlags(
            _lobbySession.UIMode,
            _lobbySession.AllowHostPlayerOptions,
            out bool canAssign,
            out bool canSelectLocal);
        GameDataBindingApplier.RefreshMapStartMarkers(
            _activeRoot,
            GetCurrentLobbyMap(),
            _lobbySession.PlayerState.Slots,
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
        ICnCNetSession session = _cncnet;
        if (session.IsGameRoomJoinPending)
            return;

        ICnCNetGameSession? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);

        _cncnet.UpdateGameRoomListing(
            map?.UntranslatedName ?? string.Empty,
            gameMode?.UntranslatedUIName ?? string.Empty,
            map?.Sha1 ?? string.Empty);
    }

    private void PushCnCNetHostLobbyState()
    {
        ICnCNetSession session = _cncnet;
        if (session.IsGameRoomJoinPending)
            return;

        ICnCNetGameSession? room = session.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        if (_activeRoot != null)
            LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        _cncnet.SyncGameRoomFromLobby(_lobbySession.PlayerState);
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
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_cncnet).Service;

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
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore;
        if (room == null)
            return;

        string localNick = _cncnet.LocalNick;
        string hostName = string.IsNullOrWhiteSpace(room.HostName) ? localNick : room.HostName;

        ICnCNetGameSession? session = _cncnet.ActiveGameRoom;
        // Phase 3 P3-5：删除 fallback——ActiveGameRoomCore 非空时 ActiveGameRoom 必非空。
        // 若确实无 session，提前返回（防御性），不再退回旧三步胶水。
        if (session == null)
        {
            Logger.Log("EnterCnCNetGameLobbyConnecting: ActiveGameRoom null — skipping slot setup.");
            return;
        }

        // Phase 2 P2-5：用 Session API 替代 LobbyPlayerState.EnsureHostAsFirstHuman。
        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _lobbySession,
            session,
            entries: [],
            localNick,
            hostName,
            room.IsHost,
            resetSlots: true);

        if (room.IsHost)
            session.InitHostSlots(localNick);

        _lobbySession.PlayerState.SyncFromSlots(session.PlayerSlots);

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
        CnCNetGameRoomSession? gameRoom = _cncnet.GameRoom;
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
            // Phase 3 P3-2：走 Session-aware 入口（Slots + SideCount）。
            Slots = _lobbySession.PlayerState.Slots,
            SideCount = _lobbySession.PlayerState.SideNames.Count,
            LobbyRoot = _activeRoot,
        };

        Logger.Log($"CnCNet GameStarting: launching {map.DisplayName} / {gameMode.DisplayName} via Syringe.");

        ShowStatus("Launching game...");

        var startSnapshot = startInfo;
        var roomPlayers = _cncnet.GameRoom?.Players;
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

        CnCNetActiveGameRoom? cncRoom = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore
            ?? _cncnet.GameRoom?.Room;
        UiNodeViewModel? btnLaunch = FindVm(root, "btnLaunchGame");
        UiNodeViewModel? chkAutoReady = FindVm(root, "chkAutoReady");

        if (CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
        {
            ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
            ICnCNetSession session = _cncnet;

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

        var lobby = _cncnet.LobbyState;
        string status = lobby.ConnectionStatus;
        if (string.IsNullOrWhiteSpace(status))
            status = _cncnet.Connection?.IsConnected == true ? "已连接" : "Offline";

        PART_TopBarHost.Bar.UpdateState(
            status,
            _cncnet.OnlinePlayerCount,
            isConnected: _cncnet.Connection?.IsConnected == true,
            unreadPrivateMessages: _cncnet.UnreadPrivateMessageCount);
    }

    public void OpenPrivateMessagingOverlay(string? focusNick = null)
    {
        if (_cncnet.Connection?.IsConnected != true)
        {
            ShowStatus("私信需要先连接 CnCNet。");
            return;
        }

        if (IsFloatingOverlayOpen
            && _floatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (PART_OverlayRawView.Content is PrivateMessagingPanel openPanel)
            {
                if (!string.IsNullOrWhiteSpace(focusNick))
                    _cncnet.EnsurePrivateConversation(focusNick);
                openPanel.Refresh(focusNick);
                if (!string.IsNullOrWhiteSpace(openPanel.SelectedNick))
                    _cncnet.SetViewingPrivateMessagePeer(openPanel.SelectedNick);
                openPanel.FocusInput();
            }

            return;
        }

        if (IsFloatingOverlayOpen)
            CloseFloatingOverlaySilently();

        if (!string.IsNullOrWhiteSpace(focusNick))
            _cncnet.EnsurePrivateConversation(focusNick);

        var panel = new PrivateMessagingPanel();
        panel.Bind(
            listPeers: () =>
            {
                var list = _cncnet.GetPrivateConversationSummaries().ToList();
                var known = new HashSet<string>(list.Select(p => p.Nick), StringComparer.OrdinalIgnoreCase);
                string local = _cncnet.LocalNick;
                foreach (string raw in _cncnet.LobbyState.ChannelPlayers)
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
            listMessages: nick => _cncnet.GetPrivateMessages(nick)
                .Select(l => l.DisplayText)
                .ToList(),
            send: (nick, text) =>
            {
                _cncnet.SendPrivateMessage(nick, text);
                _cncnet.SetViewingPrivateMessagePeer(nick);
            },
            close: () =>
            {
                _cncnet.SetViewingPrivateMessagePeer(null);
                ResetOverlayPanelChrome();
                CloseFloatingOverlayCore(restoreIniOverlayView: true);
                UpdateTopBar();
            },
            peerSelected: nick => _cncnet.SetViewingPrivateMessagePeer(nick));

        ShowRawHostOverlay(panel, 600, 520, "私信 (F4)");
        _floatingOverlayWindow = "PrivateMessagingWindow";
        panel.Refresh(focusNick ?? _cncnet.LastPrivateMessagePartner);
        if (!string.IsNullOrWhiteSpace(panel.SelectedNick))
            _cncnet.SetViewingPrivateMessagePeer(panel.SelectedNick);
        panel.FocusInput();
        UpdateTopBar();
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

    /// <summary>
    /// Single-player skirmish window. Excludes multiplayer (CnCNetGameLobby /
    /// LANGameLobby) — those slots are managed by join/part events, never auto-filled.
    /// Used by <see cref="DefaultAiSlotPolicy"/> trigger points.
    /// </summary>
    private static bool IsSkirmishWindow(string windowName)
        => windowName.Equals("SkirmishLobby", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Phase 4 P4-5：替代旧 <c>_applyingCnCNetGameRoomPlayers</c> 布尔重入标志。
    /// 记录上次成功应用 PO 到 UI 时读到的 <see cref="IGameSession.Revision"/>；
    /// 若新事件读到的 Revision 与之相同，说明本次刷新由我们自己的写入触发（冗余）—— skip。
    /// </summary>
    private long _lastAppliedGameRoomRevision = -1;

    private void OnCnCNetGameRoomJoined(ICnCNetGameSession room)
    {
        // Phase 4 P4-5：进入新房间时重置 Revision 缓存，强制下次 ApplyCnCNetGameRoomPlayers 走完整路径。
        _lastAppliedGameRoomRevision = -1;

        if (!CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            NavigateTo("CnCNetGameLobby");
        else if (_activeRoot != null)
        {
            WireCnCNetGameOptionsBridge();
            ApplyCnCNetGameRoomPlayers(_activeRoot);
            GameDataBindingApplier.ApplyGameRoomChat(_activeRoot, _cncnet.GameRoom);
        }

        CnCNetGameRoomSession? gameRoom = _cncnet.GameRoom;
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
        _cncnet.EnsureGameBroadcastChannelsJoined();
        NavigateTo("CnCNetLobby");
    }

    private void WireCnCNetGameOptionsBridge()
    {
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_cncnet).Service;
        session.GameOptionsControlCounts = () => CnCNetGameOptionsUiBridge.GetControlCounts(_activeRoot);
        session.GameOptionsProvider = CollectCnCNetGameOptions;
        session.GameOptionsReceiver = ApplyCnCNetGameOptionsFromHost;
        WireHostGameOptionChangeBroadcast();
        session.GameRoom?.TryFlushPendingGameOptions();
    }

    private void ClearCnCNetGameOptionsBridge()
    {
        UnwireHostGameOptionChangeBroadcast();
        CnCNetSessionService session = ((CnCNetSessionServiceAdapter)_cncnet).Service;
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
        CnCNetGameRoomSession? room = _cncnet.GameRoom;
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
        CnCNetGameRoomSession? room = _cncnet.GameRoom;
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
            if (_cncnet.GameRoom is { IsHost: true })
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
        int count = _cncnet.OnlinePlayerCount;
        _bindingSession.State.SetOnlinePlayerCount(count);

        if (_activeRoot != null && IsChannelLobbyWindow(CurrentWindow))
        {
            GameDataBindingApplier.ApplyChannelLobby(_activeRoot, _cncnet.LobbyState);
            if (!IsFloatingOverlayOpen)
                ShowStatus($"CnCNet: {_cncnet.LobbyState.ConnectionStatus}");
        }

        if (IsFloatingOverlayOpen
            && _floatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true
            && PART_OverlayRawView.Content is PrivateMessagingPanel pmPanel)
        {
            // Keep the user's selected peer; do not jump to LastPrivateMessagePartner.
            pmPanel.Refresh();
        }

        if (_activeRoot != null && CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            RefreshCnCNetGameRoomUiFromSession(_activeRoot);

        if (CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && _activeRoot != null)
            StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");

        UpdateTopBar();
    }

    private void OnPrivateMessageArrived(string peer, string preview)
    {
        UpdateTopBar();

        if (CnCNetPrivateMessagePolicy.PopupsDisabled())
            return;

        string clip = preview.Length > 60 ? preview[..60] + "…" : preview;
        ShowStatus($"私信 · {peer}: {clip}");
    }

    private async void OnCnCNetWafAlert(ClientAvalonia.CnCNet.Waf.WafAlert alert)
    {
        try
        {
            string surface = alert.Event.Surface switch
            {
                ClientAvalonia.CnCNet.Waf.WafSurface.PrivateMessage => "私信",
                ClientAvalonia.CnCNet.Waf.WafSurface.LobbyChat => "大厅聊天",
                ClientAvalonia.CnCNet.Waf.WafSurface.GameRoomChat => "房间聊天",
                ClientAvalonia.CnCNet.Waf.WafSurface.ListingText => "房间列表文案",
                _ => "联机协议",
            };

            string actor = string.IsNullOrWhiteSpace(alert.Event.SenderNick)
                ? (alert.Event.Game?.ChannelName ?? "未知来源")
                : alert.Event.SenderNick;

            string message =
                $"来源：{actor}\n" +
                $"场景：{surface}\n" +
                $"等级：{alert.Decision.Severity} (score={alert.Decision.Score})\n" +
                $"原因：{alert.Decision.Summary}";

            bool offerBlock = alert.Decision.SuggestedBlockKeys.Count > 0;
            bool addBlock = await ClientDialogService.ShowWafAlertAsync(
                this,
                "CnCNet 入网防护",
                message,
                offerBlock).ConfigureAwait(true);

            if (!addBlock || _cncnet is not CnCNetSessionServiceAdapter adapter)
                return;

            string note = alert.Decision.Summary;
            if (string.IsNullOrWhiteSpace(note))
                note = $"{surface}/{alert.Decision.Severity}";

            adapter.Service.IngressWaf.BlockFromAlert(alert.Event, alert.Decision, note);

            ShowStatus("已写入 WAF 屏蔽名单（含同型消息体）");
            if (IsOptionsOverlayOpen && _overlayRoot != null)
                WafBlocklistApplier.Apply(_overlayRoot, adapter.Service.IngressWaf);
        }
        catch (Exception ex)
        {
            Logger.Log($"WAF alert UI failed: {ex.Message}");
        }
    }

    /// <summary>Read session → UI only; never pushes PO/GAME/GO back to the network.</summary>
    private void RefreshCnCNetGameRoomUiFromSession(UiNodeViewModel root)
    {
        // Phase 4 P4-5：用 Revision 比对替代布尔重入标志——
        // 若读到的 Revision 与上次应用相同，说明本次刷新是冗余的（可能是我们自己写入触发的回声）。
        ICnCNetGameSession? currentSession = _cncnet?.ActiveGameRoom;
        if (currentSession != null && currentSession.Revision == _lastAppliedGameRoomRevision)
            return;

        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore;
        if (room == null)
            return;

        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: false);
            // Refresh in-room chat timeline + tbChatInput enabled state.
            GameDataBindingApplier.ApplyGameRoomChat(root, _cncnet.GameRoom);
            if (currentSession != null)
                _lastAppliedGameRoomRevision = currentSession.Revision;
        }
        catch (Exception ex)
        {
            Logger.Log($"CnCNet game room UI refresh failed: {ex.Message}");
            Logger.Log(ex.ToString());
        }
    }

    private void ApplyCnCNetGameRoomPlayers(UiNodeViewModel root)
    {
        // Phase 4 P4-5：用 Revision 比对替代布尔重入标志。
        ICnCNetGameSession? currentSession = _cncnet?.ActiveGameRoom;
        if (currentSession != null && currentSession.Revision == _lastAppliedGameRoomRevision)
            return;

        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_cncnet).ActiveGameRoomCore;
        if (room == null)
            return;

        try
        {
            ApplyCnCNetGameRoomPlayersCore(root, room, updateStatus: true);
            if (currentSession != null)
                _lastAppliedGameRoomRevision = currentSession.Revision;
        }
        finally
        {
            // 保留 finally 块以兼容未来可能的资源清理。
        }
    }

    private void ApplyCnCNetGameRoomPlayersCore(UiNodeViewModel root, CnCNetActiveGameRoom room, bool updateStatus)
    {
        ICnCNetGameSession? session = _cncnet.ActiveGameRoom;
        CnCNetGameRoomSession? gameRoom = _cncnet.GameRoom;
        string localNick = _cncnet.LocalNick;
        string hostName = room.HostName;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = gameRoom?.HostName ?? localNick;
        if (string.IsNullOrWhiteSpace(hostName))
            hostName = localNick;

        IReadOnlyList<CnCNetGameRoomPlayer> entries = gameRoom?.Players ?? [];

        // Phase 2 P2-5 + Phase 3 P3-5：Session API 单一真相源路径。
        // Phase 3 P3-5：删除 fallback（session 必非空——ActiveGameRoomCore 非空即 ActiveGameRoom 非空）。
        // 若 session 为 null，提前返回。
        if (session == null)
        {
            Logger.Log("ApplyCnCNetGameRoomPlayersCore: ActiveGameRoom null — skipping.");
            return;
        }

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
            _lobbySession,
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

        // Session.PlayerSlots 是真相源；投影到 _lobbySession.PlayerState 供 BindingApplier 读。
        _lobbySession.PlayerState.SyncFromSlots(session.PlayerSlots);

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        // Phase 4 P4-1：走 sink 路径（session 是 ICnCNetGameSession 即 IGameSession 子类）。
        LobbyPlayerBindingApplier.Apply(
            root,
            (IGameSession)session,
            _lobbySession.PlayerState,
            _lobbySession,
            LobbyCatalogService.Instance,
            resources,
            _mainBehaviors,
            gameRoomProvider: () => _cncnet.GameRoom,
            onSlotsMutated: OnLobbySlotsMutated);

        bool locked = gameRoom?.Locked ?? false;
        // Phase 3 P3-3：走 Session-aware 重载（直接吃 slots + UIMode），不再依赖 LobbyPlayerState。
        LobbyPlayerStatusApplier.Apply(
            root,
            _lobbySession.PlayerState.Slots,
            _lobbySession.UIMode,
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

        if (e.Key == Key.F4)
        {
            OpenPrivateMessagingOverlay();
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
            Key.D7 or Key.NumPad7 => 6,
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
        _cncnet.SendChatMessage(message);
        tbChat.InputText = string.Empty;
    }

    private static ICnCNetSession ResolveCnCNetSession()
    {
        try
        {
            return EnvironmentServices.Resolve<ICnCNetSession>();
        }
        catch (InvalidOperationException)
        {
            return new CnCNetSessionServiceAdapter();
        }
    }

    /// <summary>
    /// 安全解析 INI 动作目录。catalog 在 PreStartup.RegisterEnvironmentServices
    /// 后才可用；早于该点的窗口加载会拿到 null（退化为仅 DISABLE 行为）。
    /// </summary>
    private static IIniActionCatalog? ResolveIniActionCatalog()
    {
        try
        {
            return EnvironmentServices.Resolve<IIniActionCatalog>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string ResolvePlayerName()
    {
        try
        {
            return EnvironmentServices.Resolve<IGameEnvironment>().PlayerName;
        }
        catch (InvalidOperationException)
        {
            return ProgramConstants.PLAYERNAME;
        }
    }

    private static IMultiplayerColorCatalog ResolveColorCatalog()
    {
        try
        {
            return EnvironmentServices.Resolve<IMultiplayerColorCatalog>();
        }
        catch (InvalidOperationException)
        {
            return new MultiplayerColorCatalogAdapter();
        }
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
