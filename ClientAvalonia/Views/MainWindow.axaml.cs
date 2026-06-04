using Avalonia.Controls;
using Avalonia.Input;
using ClientAvalonia.Core;
using ClientAvalonia.Domain;
using ClientCore;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
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
    private string? _floatingOverlayWindow;

    public string CurrentWindow { get; private set; } = "MainMenu";

    public bool IsFloatingOverlayOpen => PART_FloatingOverlay.IsVisible;

    public string? FloatingOverlayWindow => _floatingOverlayWindow;

    public bool IsOptionsOverlayOpen
        => IsFloatingOverlayOpen
           && _floatingOverlayWindow?.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase) == true;

    public UiNodeViewModel? ActiveRoot => _activeRoot;

    public UiNodeViewModel? OverlayRoot => _overlayRoot;

    public MainWindow()
    {
        _bindingSession = new UiBindingSession(_environment);
        _gameLaunch.StatusChanged += msg => ShowStatus(msg);
        _updateService.EnsureHandlersRegistered();
        _updateService.StatusChanged += OnUpdateStatusChanged;
        ClientStartupService.LocalVersionsChecked += OnLocalVersionsChecked;
        _gameResources.Loaded += OnGameResourcesLoaded;
        CnCNetSessionService.Instance.StateChanged += OnCnCNetStateChanged;
        CnCNetSessionService.Instance.EnsureStarted();
        InitializeComponent();
        KeyDown += OnKeyDown;
        NavigateTo("MainMenu");
        _updateService.RefreshInitialStatus();
    }

    public void NavigateTo(string windowName)
    {
        if (FloatingOverlayLayout.IsOverlayWindow(windowName))
        {
            OpenFloatingOverlay(windowName);
            return;
        }

        CloseFloatingOverlaySilently();

        string? iniPath = _environment.ResolveWindowIni(windowName);
        if (iniPath == null)
        {
            ShowStatus($"INI not found for window: {windowName}");
            return;
        }

        try
        {
            UiBehaviorCatalog.RegisterForWindow(_mainBehaviors, windowName, this);

            _mainEngine = LayoutEngine.CreateForWindow(_environment, iniPath, windowName);
            _mainViewModelFactory = new UiViewModelFactory(_mainEngine.Resources, _mainBehaviors);

            UiNodeTree tree = _mainEngine.LoadWindow(iniPath, windowName);
            UiNodeViewModel vm = _mainViewModelFactory.CreateTree(tree);
            IniBehaviorApplier.Apply(vm, _mainBehaviors, this);

            if (IsGameLobbyWindow(windowName))
                _bindingSession.State.SetCanLaunchGame(true);

            _bindingSession.ApplyToTree(vm, windowName);
            _activeRoot = vm;
            PART_RootView.Content = vm;
            ApplyViewportSize(_mainEngine.Context.Width, _mainEngine.Context.Height);

            if (windowName.Contains("Lobby", StringComparison.OrdinalIgnoreCase))
            {
                ApplyLobbyData(vm, windowName);
                UpdateLaunchButtonState(vm);
            }

            CurrentWindow = windowName;
            Title = $"ClientAvalonia — {windowName} ({_environment.ThemeFolderPath.TrimEnd('/')}) {_mainEngine.Context.Width}×{_mainEngine.Context.Height}";
            ShowStatus($"{windowName}: {tree.Root.Children.Count} root controls, {tree.AllNodes().Count()} nodes");
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
        PART_FloatingOverlay.IsVisible = false;
        PART_FloatingOverlay.IsHitTestVisible = false;
        PART_OverlayView.Content = null;
        PART_RootView.IsHitTestVisible = true;
        _overlayRoot = null;
        _overlayEngine = null;
        _overlayBehaviors.Clear();
        _floatingOverlayWindow = null;
    }

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

        NavigateTo("MainMenu");
    }

    public void ShowStatus(string message) => PART_Status.Text = message;

    public void ExitApplication() => Close();

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
            _lobbySession.PlayerState.LoadDefaults();
            if (!_lobbySession.PlayerState.TryLoadSkirmishSettings())
                _lobbySession.PlayerState.LoadDefaults();

            LobbyPlayerBindingApplier.Apply(root, _lobbySession.PlayerState, resources, _mainBehaviors);
        }

        if (IsChannelLobbyWindow(windowName))
        {
            if (windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase))
                CnCNetSessionService.Instance.ConnectIfNeeded();

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

        if (_activeRoot != null && IsChannelLobbyWindow(CurrentWindow))
        {
            GameDataBindingApplier.ApplyChannelLobby(_activeRoot, CnCNetSessionService.Instance.LobbyState);
            ShowStatus($"CnCNet: {CnCNetSessionService.Instance.LobbyState.ConnectionStatus}");
        }

        if (CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) && _activeRoot != null)
            StateBindingApplier.Apply(_activeRoot, _bindingSession.State, "MainMenu");
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
