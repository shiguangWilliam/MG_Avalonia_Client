using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
        CnCNetSessionService.Instance.GameStarting += OnCnCNetGameStarting;
        CnCNetSessionService.Instance.EnsureStarted();
        InitializeComponent();
        KeyDown += OnKeyDown;
        NavigateTo("MainMenu");
        _updateService.RefreshInitialStatus();
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

        if (!CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
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

            if (windowName.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase))
                ApplyCampaignOverlay(_overlayRoot);

            ShowStatus($"{windowName} overlay: {width}×{height} (fixed, over MainMenu)");
        }
        catch (Exception ex)
        {
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
           || (_activeRoot != null && FindVm(_activeRoot, "ddCurrentChannel") != null);

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

    public void ShowStatus(string message) => PART_Status.Text = message;

    public void ExitApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
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
        _environment = ClientEnvironment.Discover(_environment.GameRoot);
        ShowStatus($"Settings saved: {_bindingSession.Settings.SettingsPath}");

        if (CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            NavigateTo("MainMenu");
    }

    public void DiscardSettings()
    {
        _bindingSession.DiscardSettings();
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

        return _gameLaunch.TryLaunchSkirmish(
            _environment,
            new SkirmishLaunchRequest
            {
                Map = map,
                GameMode = gameMode,
                Players = _lobbySession.PlayerState,
                LobbyRoot = _activeRoot,
            },
            out message,
            this);
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
        GameDataBindingApplier.UpdateMapSelectionDisplay(_activeRoot, _lobbySession.VisibleMaps, index, resources);
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
                string localNick = CnCNetSessionService.Instance.LocalNick;
                LobbyPlayerSlotUiRules.ConfigureForMultiplayer(
                    _lobbySession.PlayerState,
                    localNick,
                    CnCNetSessionService.Instance.ActiveGameRoom?.IsHost == true);
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
                ApplyCnCNetGameRoomPlayers(root);
                UpdateCnCNetGameBroadcastListing(root);
            }
        }

        if (IsChannelLobbyWindow(windowName))
        {
            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
            {
                CnCNetSessionService.Instance.ConnectIfNeeded();
                CnCNetSessionService.Instance.SyncLobbyStateFromCore();
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
                GameDataBindingApplier.UpdateMapSelectionDisplay(
                    root,
                    _lobbySession.VisibleMaps,
                    lbMapList.SelectedIndex,
                    resources);
                UpdateLaunchButtonState(root);
                RefreshCnCNetGameListing();
            };
        }

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
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (room is not { IsHost: true })
            return;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);
        var playerNames = GetCnCNetPlayerNames();

        CnCNetSessionService.Instance.UpdateHostedGameListing(
            map?.UntranslatedName ?? string.Empty,
            gameMode?.UntranslatedUIName ?? string.Empty,
            map?.Sha1 ?? string.Empty,
            playerNames);

        CnCNetSessionService.Instance.UpdateGameRoomListing(
            map?.UntranslatedName ?? string.Empty,
            gameMode?.UntranslatedUIName ?? string.Empty,
            map?.Sha1 ?? string.Empty);
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

        CnCNetHostedGameSummary? game = session.LobbyState.GetSelectedGame();
        if (game == null)
        {
            ShowStatus("Select a game from the list first.");
            return;
        }

        string? password = null;
        if (session.SelectedGameRequiresPassword())
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

        ShowStatus(message);
        NavigateTo("CnCNetGameLobby");
    }

    private List<string> GetCnCNetPlayerNames()
    {
        CnCNetGameRoomSession? gameRoom = CnCNetSessionService.Instance.GameRoom;
        if (gameRoom != null && gameRoom.Players.Count > 0)
            return gameRoom.Players.Select(p => p.Name).ToList();

        return _lobbySession.PlayerState.Slots
            .Where(s => s.IsOccupied && !s.IsAi)
            .Select(s => s.Name)
            .ToList();
    }

    private void OnCnCNetGameStarting(CnCNetStartGameInfo startInfo)
    {
        if (_activeRoot == null || !CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            return;

        UiNodeViewModel? ddGameMode = FindVm(_activeRoot, "ddGameMode");
        if (ddGameMode != null)
            _lobbySession.FilterIndex = ddGameMode.SelectedIndex;

        LobbyPlayerBindingApplier.SyncFromUi(_activeRoot, _lobbySession.PlayerState);

        UiNodeViewModel? lbMapList = FindVm(_activeRoot, "lbMapList");
        MapEntry? map = _lobbySession.GetSelectedMap(lbMapList?.SelectedIndex ?? 0);
        GameModeEntry? gameMode = _gameResources.GetGameModeForFilterIndex(_lobbySession.FilterIndex);

        if (map == null || gameMode == null)
        {
            ShowStatus("Cannot launch: map or game mode missing.");
            return;
        }

        var request = new SkirmishLaunchRequest
        {
            Map = map,
            GameMode = gameMode,
            Players = _lobbySession.PlayerState,
            LobbyRoot = _activeRoot,
        };

        if (!_gameLaunch.TryLaunchCnCNet(_environment, startInfo, request, out string message, this))
        {
            ShowStatus($"Launch failed: {message}");
            ClientDialogService.ShowError(this, "Cannot launch game", message);
            return;
        }

        ShowStatus(message);
    }

    private void UpdateLaunchButtonState(UiNodeViewModel? root = null)
    {
        root ??= _activeRoot;
        if (root == null || !IsGameLobbyWindow(CurrentWindow))
            return;

        UiNodeViewModel? lbMapList = FindVm(root, "lbMapList");
        bool canLaunch = _lobbySession.VisibleMaps.Count > 0
            && (lbMapList?.SelectedIndex ?? -1) >= 0;
        _bindingSession.State.SetCanLaunchGame(canLaunch);
        FindVm(root, "btnLaunchGame")?.IsEnabled = canLaunch;
    }

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

    private void OnCnCNetStateChanged()
    {
        int count = CnCNetSessionService.Instance.OnlinePlayerCount;
        _bindingSession.State.SetOnlinePlayerCount(count);

        if (_activeRoot != null && IsCnCNetLobbyActive())
        {
            GameDataBindingApplier.ApplyChannelLobby(_activeRoot, CnCNetSessionService.Instance.LobbyState);
            if (!IsFloatingOverlayOpen)
                ShowStatus($"CnCNet: {CnCNetSessionService.Instance.LobbyState.ConnectionStatus}");
        }

        if (_activeRoot != null && CurrentWindow.Equals("CnCNetGameLobby", StringComparison.OrdinalIgnoreCase))
            ApplyCnCNetGameRoomPlayers(_activeRoot);

        if (CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && _activeRoot != null)
            StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");
    }

    private void ApplyCnCNetGameRoomPlayers(UiNodeViewModel root)
    {
        CnCNetGameRoomSession? gameRoom = CnCNetSessionService.Instance.GameRoom;
        CnCNetActiveGameRoom? room = CnCNetSessionService.Instance.ActiveGameRoom;
        if (gameRoom == null || room == null)
            return;

        string localNick = CnCNetSessionService.Instance.LocalNick;
        IReadOnlyList<CnCNetGameRoomPlayer> players = gameRoom.Players;

        LobbyPlayerSlotUiRules.ConfigureForMultiplayer(_lobbySession.PlayerState, localNick, room.IsHost);

        LobbyPlayerSlot[] hostAiSlots = room.IsHost
            ? _lobbySession.PlayerState.Slots.Where(s => s.IsAi).Select(s => s.Clone()).ToArray()
            : [];

        _lobbySession.PlayerState.ClearSlots();

        if (players.Count == 0 && room.IsHost)
        {
            LobbyPlayerSlot hostSlot = _lobbySession.PlayerState.Slots[0];
            hostSlot.Name = localNick;
            hostSlot.IsAi = false;
            hostSlot.IsHumanLocal = true;
        }
        else
        {
            for (int i = 0; i < players.Count && i < LobbyPlayerSlot.MaxSlots; i++)
            {
                CnCNetGameRoomPlayer player = players[i];
                LobbyPlayerSlot slot = _lobbySession.PlayerState.Slots[i];
                slot.Name = player.Name;
                slot.IsAi = false;
                slot.IsHumanLocal = player.Name.Equals(localNick, StringComparison.OrdinalIgnoreCase);
                slot.SideIndex = player.SideId;
                slot.ColorIndex = player.ColorId;
                slot.TeamIndex = player.TeamId;
                slot.StartIndex = Math.Max(0, player.StartingLocation - 1);
            }
        }

        if (room.IsHost)
        {
            int insertAt = 0;
            while (insertAt < LobbyPlayerSlot.MaxSlots && _lobbySession.PlayerState.Slots[insertAt].IsOccupied)
                insertAt++;

            foreach (LobbyPlayerSlot ai in hostAiSlots)
            {
                if (insertAt >= LobbyPlayerSlot.MaxSlots)
                    break;

                _lobbySession.PlayerState.Slots[insertAt] = ai;
                insertAt++;
                while (insertAt < LobbyPlayerSlot.MaxSlots && _lobbySession.PlayerState.Slots[insertAt].IsOccupied)
                    insertAt++;
            }
        }

        ResourceResolver resources = _mainEngine?.Resources ?? new ResourceResolver();
        LobbyPlayerBindingApplier.Apply(root, _lobbySession.PlayerState, resources, _mainBehaviors);
        UpdateLaunchButtonState(root);

        if (room.IsHost)
            RefreshCnCNetGameListing();

        ShowStatus(room.IsHost
            ? $"Hosting \"{room.RoomName}\" — waiting for players."
            : $"Joined \"{room.RoomName}\" — waiting for host.");
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
