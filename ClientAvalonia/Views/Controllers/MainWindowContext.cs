using ClientAvalonia.IniUi;
using Avalonia.Controls;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.Domain.Resources;
using ClientAvalonia.GlobalState;
using ClientAvalonia.GlobalState.Environment;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Overlays;
using ClientAvalonia.Lan;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using ClientAvalonia.Session;

namespace ClientAvalonia.Views.Controllers;

/// <summary>
/// Shared dependency + mutable UI-state container for MainWindow controllers.
/// Does not hold Avalonia PART_* controls —?shell callbacks operate those.
/// </summary>
internal sealed class MainWindowContext
{
    public MainWindowContext(
        BehaviorRegistry mainBehaviors,
        BehaviorRegistry overlayBehaviors,
        UiBindingSession bindingSession,
        GameLaunchService gameLaunch,
        GameResourceCatalog gameResources,
        LobbySessionState lobbySession,
        SkirmishSession skirmishSession,
        ICnCNetSession cncnet)
    {
        MainBehaviors = mainBehaviors;
        OverlayBehaviors = overlayBehaviors;
        BindingSession = bindingSession;
        GameLaunch = gameLaunch;
        GameResources = gameResources;
        LobbySession = lobbySession;
        SkirmishSession = skirmishSession;
        CnCNet = cncnet;
    }

    public ClientEnvironment Environment { get; set; } = ClientEnvironment.Discover();

    public BehaviorRegistry MainBehaviors { get; }
    public BehaviorRegistry OverlayBehaviors { get; }
    public UiBindingSession BindingSession { get; }
    public GameLaunchService GameLaunch { get; }
    public GameResourceCatalog GameResources { get; }
    public LobbySessionState LobbySession { get; }
    public SkirmishSession SkirmishSession { get; }
    public ICnCNetSession CnCNet { get; }

    public string CurrentWindow { get; set; } = WindowKind.MainMenu;
    public UiNodeViewModel? ActiveRoot { get; set; }
    public UiNodeViewModel? OverlayRoot { get; set; }
    public string? FloatingOverlayWindow { get; set; }
    public LayoutEngine? MainEngine { get; set; }
    public LayoutEngine? OverlayEngine { get; set; }
    public GameCreationOverlayContext? GameCreationOverlay { get; set; }
    public long LastAppliedGameRoomRevision { get; set; } = -1;

    public Action<string> ShowStatus { get; set; } = static _ => { };
    public Action<string> NavigateTo { get; set; } = static _ => { };
    public Action CloseFloatingOverlaySilently { get; set; } = static () => { };
    public Action UpdateLaunchButtonState { get; set; } = static () => { };
    public Action UpdateTopBar { get; set; } = static () => { };
    public Action RefreshLobbyMapList { get; set; } = static () => { };
    public Action RefreshCnCNetGameListing { get; set; } = static () => { };
    public Action RefreshCnCNetGameRoomPlayers { get; set; } = static () => { };
    public Action PushCnCNetHostLobbyState { get; set; } = static () => { };
    public Action WireCnCNetGameOptionsBridge { get; set; } = static () => { };
    public Action ClearCnCNetGameOptionsBridge { get; set; } = static () => { };
    public Action OnLobbySlotsMutated { get; set; } = static () => { };
    public Action RefreshCurrentMapStartMarkers { get; set; } = static () => { };
    public Action<UiNodeViewModel> ApplyCnCNetGameRoomPlayers { get; set; } = static _ => { };
    public Action<UiNodeViewModel> UpdateCnCNetGameBroadcastListing { get; set; } = static _ => { };
    public Action<UiNodeViewModel, CampaignSideFilter> ApplyCampaignOverlay { get; set; }
        = static (_, _) => { };
    public Func<CnCNetGameOptionsState> CollectCnCNetGameOptions { get; set; }
        = static () => new CnCNetGameOptionsState();
    public Func<Window> GetOwnerWindow { get; set; }
        = static () => throw new InvalidOperationException("Owner window not wired.");
    public Func<bool> IsFloatingOverlayOpen { get; set; } = static () => false;
    public Func<bool> IsOptionsOverlayOpen { get; set; } = static () => false;
    public Func<IIniActionCatalog?> ResolveIniActionCatalog { get; set; } = ResolveIniActionCatalogStatic;

    public ResourceResolver GetMainResources()
        => MainEngine?.Resources ?? new ResourceResolver();

    public ResourceResolver GetOverlayResources()
        => OverlayEngine?.Resources ?? MainEngine?.Resources ?? new ResourceResolver();

    public IGameSession? ResolveActiveGameSession()
    {
        if (IsSkirmishWindow(CurrentWindow))
            return SkirmishSession;

        if (IsLanGameLobbyWindow(CurrentWindow))
            return AppState.Lan.ActiveGameRoom as IGameSession;

        return CnCNet.ActiveGameRoom as IGameSession;
    }

    /// <summary>
    /// Slots of whatever lobby session owns the current window. Always yields the
    /// skirmish array as a last resort so callers never need a hardcoded fallback.
    /// </summary>
    public IReadOnlyList<IPlayerSlot> ResolveActiveLobbySlots()
        => ResolveActiveGameSession()?.PlayerSlots ?? SkirmishSession.PlayerSlots;

    public static bool IsLanGameLobbyWindow(string windowName)
        => windowName.Equals(WindowKind.LanGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("LANGameLoadingLobby", StringComparison.OrdinalIgnoreCase);

    public bool IsCnCNetGameRoomChatEligible()
        => CurrentWindow.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase)
           && CnCNet.GameRoom is { IsLocalJoined: true };

    public bool IsCnCNetLobbyActive()
        => IsChannelLobbyWindow(CurrentWindow)
           || IsCnCNetGameRoomChatEligible()
           || (ActiveRoot != null && FindVm(ActiveRoot, "ddCurrentChannel") != null);

    public static bool IsGameLobbyWindow(string windowName)
        => windowName.Equals(WindowKind.SkirmishLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals(WindowKind.CnCNetGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals(WindowKind.LanGameLobby, StringComparison.OrdinalIgnoreCase)
           || windowName.Equals(WindowKind.MultiplayerGameLobby, StringComparison.OrdinalIgnoreCase);

    public static bool IsChannelLobbyWindow(string windowName)
        => windowName.Equals("CnCNetLobby", StringComparison.OrdinalIgnoreCase)
           || windowName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase);

    public static bool IsSkirmishWindow(string windowName)
        => windowName.Equals(WindowKind.SkirmishLobby, StringComparison.OrdinalIgnoreCase);

    public static UiNodeViewModel? FindVm(UiNodeViewModel root, string id)
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

    public static string ResolvePlayerName()
    {
        try
        {
            return EnvironmentServices.Resolve<IGameEnvironment>().PlayerName;
        }
        catch (InvalidOperationException)
        {
            return AppState.Environment.PlayerName;
        }
    }

    public static IIniActionCatalog? ResolveIniActionCatalogStatic()
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

    public static IMultiplayerColorCatalog ResolveColorCatalog()
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

    public static ICnCNetSession ResolveCnCNetSession()
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

    public static GameResourceCatalog ResolveGameResourceCatalog()
    {
        try
        {
            var resolved = EnvironmentServices.Resolve<IResourceCatalog>();
            if (resolved is GameResourceCatalogAdapter adapter)
                return adapter.InternalCatalog;
        }
        catch (InvalidOperationException)
        {
        }

        return GameResourceCatalog.Instance;
    }
}
