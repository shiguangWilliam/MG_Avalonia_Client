using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Binding;
using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.IniUi.Overlays;
using ClientAvalonia.Rendering;
using ClientAvalonia.Services;
using Rampastring.Tools;

namespace ClientAvalonia.Views.Controllers;

internal sealed class OverlayHostController
{
    private readonly MainWindowContext _ctx;
    private readonly Border _overlayPanel;
    private readonly ContentControl _overlayView;
    private readonly ContentControl _overlayRawView;
    private readonly Grid _floatingOverlay;
    private readonly Control _rootView;

    public OverlayHostController(
        MainWindowContext ctx,
        Border overlayPanel,
        ContentControl overlayView,
        ContentControl overlayRawView,
        Grid floatingOverlay,
        Control rootView)
    {
        _ctx = ctx;
        _overlayPanel = overlayPanel;
        _overlayView = overlayView;
        _overlayRawView = overlayRawView;
        _floatingOverlay = floatingOverlay;
        _rootView = rootView;
    }

    public bool IsOpen => _floatingOverlay.IsVisible;

    public void OpenFloatingOverlay(string windowName)
    {
        if (IsOpen)
            return;

        if (!windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase)
            && !_ctx.CurrentWindow.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            _ctx.ShowStatus($"{windowName} overlay is only available from MainMenu");
            return;
        }

        if (_ctx.Environment.ResolveWindowLoadTarget(windowName) is not { } target)
        {
            _ctx.ShowStatus($"INI not found for window: {windowName}");
            return;
        }

        string iniPath = target.IniPath;
        string sectionName = target.SectionName;

        try
        {
            (int width, int height) = FloatingOverlayLayout.ResolveOverlaySize(iniPath, sectionName);

            _ctx.OverlayBehaviors.Clear();
            FloatingOverlayBehaviors.RegisterForOverlay(
                _ctx.OverlayBehaviors,
                (IUiNavigationHost)_ctx.GetOwnerWindow(),
                windowName);

            _ctx.OverlayEngine = LayoutEngine.CreateForWindow(_ctx.Environment, iniPath, sectionName);
            var factory = new UiViewModelFactory(_ctx.OverlayEngine.Resources, _ctx.OverlayBehaviors);

            UiNodeTree tree = _ctx.OverlayEngine.LoadWindow(iniPath, sectionName);
            _ctx.OverlayRoot = factory.CreateTree(tree);
            IniBehaviorApplier.Apply(
                _ctx.OverlayRoot,
                _ctx.OverlayBehaviors,
                (IUiNavigationHost)_ctx.GetOwnerWindow(),
                _ctx.ResolveIniActionCatalog());
            _ctx.BindingSession.ApplyToTree(_ctx.OverlayRoot, windowName);

            _overlayPanel.Width = width;
            _overlayPanel.Height = height;
            _overlayView.Width = width;
            _overlayView.Height = height;
            _overlayRawView.IsVisible = false;
            _overlayRawView.Content = null;
            _overlayView.IsVisible = true;
            _overlayView.Content = _ctx.OverlayRoot;
            _floatingOverlay.IsVisible = true;
            _floatingOverlay.IsHitTestVisible = true;
            _rootView.IsHitTestVisible = false;
            _ctx.FloatingOverlayWindow = windowName;

            if (windowName.Equals("OptionsWindow", StringComparison.OrdinalIgnoreCase))
            {
                DisplayOptionsApplier.Apply(_ctx.OverlayRoot);
                AudioOptionsApplier.Apply(_ctx.OverlayRoot);
                UpdaterOptionsApplier.Apply(_ctx.OverlayRoot);
                ComponentsOptionsApplier.Apply(_ctx.OverlayRoot);
                WafBlocklistApplier.Apply(
                    _ctx.OverlayRoot,
                    ((CnCNetSessionServiceAdapter)_ctx.CnCNet).Service.IngressWaf);
                OptionsFooterChrome.ApplyToViewModel(_ctx.OverlayRoot);
            }

            if (windowName.Equals("CampaignSelector", StringComparison.OrdinalIgnoreCase))
                _ctx.ApplyCampaignOverlay(_ctx.OverlayRoot, CampaignSideFilter.All);

            _ctx.ShowStatus($"{windowName} overlay: {width}×{height}");
        }
        catch (Exception ex)
        {
            Logger.Log($"OpenFloatingOverlay({windowName}) failed: {ex}");
            _ctx.ShowStatus($"{windowName} overlay: {ex.Message}");
        }
    }

    public void CloseFloatingOverlay()
    {
        if (_ctx.FloatingOverlayWindow?.Equals(GameCreationOverlayHost.WindowName, StringComparison.OrdinalIgnoreCase) == true)
        {
            CloseGameCreationOverlay();
            return;
        }

        if (_ctx.FloatingOverlayWindow?.Equals("PrivateMessagingWindow", StringComparison.OrdinalIgnoreCase) == true)
        {
            _ctx.CnCNet.SetViewingPrivateMessagePeer(null);
            ResetOverlayPanelChrome();
        }

        CloseFloatingOverlayCore(restoreIniOverlayView: true);
        _ctx.UpdateTopBar();
    }

    public void OpenGameCreationOverlay()
    {
        if (IsOpen)
            return;

        if (!_ctx.IsCnCNetLobbyActive())
        {
            _ctx.ShowStatus("Create game is only available from the CnCNet lobby.");
            return;
        }

        var tunnels = _ctx.CnCNet.Tunnels;
        if (tunnels.Count == 0)
        {
            _ctx.ShowStatus("No NAT tunnels available.");
            return;
        }

        GameCreationOverlayHost.OpenResult layout = GameCreationOverlayHost.TryResolveLayout(_ctx.Environment);
        _ctx.OverlayBehaviors.Clear();

        UiNodeViewModel? iniRoot = GameCreationOverlayHost.TryBuildIniOverlay(
            _ctx.Environment,
            _ctx.OverlayBehaviors,
            (IUiNavigationHost)_ctx.GetOwnerWindow(),
            out _,
            out string? iniFailure);

        if (iniRoot != null)
        {
            ShowGameCreationOverlay(iniRoot, layout.Width, layout.Height, $"INI ({layout.Source})");
            return;
        }

        (Control root, GameCreationOverlayContext context, Size preferredSize) = GameCreationOverlayBuilder.Build(tunnels);
        _ctx.GameCreationOverlay = context;
        GameCreationOverlayBehaviors.Wire(context, (IUiNavigationHost)_ctx.GetOwnerWindow(), "CnCNetGameLobby");

        string fallbackNote = string.IsNullOrWhiteSpace(iniFailure) ? "programmatic UI" : $"programmatic UI ({iniFailure})";
        ShowGameCreationOverlay(root, preferredSize.Width, preferredSize.Height, fallbackNote);
    }

    public void OpenGameRoomTunnelSelection()
    {
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore;
        if (room is not { IsHost: true })
        {
            _ctx.ShowStatus("Only the game host can change the tunnel.");
            return;
        }

        var tunnels = _ctx.CnCNet.Tunnels;
        if (tunnels.Count == 0)
        {
            _ctx.ShowStatus("No NAT tunnels available.");
            return;
        }

        if (IsOpen)
            _ctx.CloseFloatingOverlaySilently();

        (Control root, Size size) = RoomHostOverlayBuilder.BuildTunnelPicker(
            tunnels,
            tunnel =>
            {
                if (_ctx.CnCNet.TryHostChangeTunnel(tunnel))
                {
                    _ctx.ShowStatus($"Tunnel changed to {tunnel.Name}.");
                    _ctx.CloseFloatingOverlaySilently();
                }
                else
                {
                    _ctx.ShowStatus("Failed to change tunnel (not joined / not host).");
                }
            },
            _ctx.CloseFloatingOverlaySilently);

        ShowRawHostOverlay(root, size.Width, size.Height, "Select tunnel");
    }

    public void OpenGameLobbySettingsOverlay()
    {
        CnCNetActiveGameRoom? room = ((CnCNetSessionServiceAdapter)_ctx.CnCNet).ActiveGameRoomCore;
        if (room is not { IsHost: true })
        {
            _ctx.ShowStatus("Only the game host can change room settings.");
            return;
        }

        if (IsOpen)
            _ctx.CloseFloatingOverlaySilently();

        (Control root, Size size) = RoomHostOverlayBuilder.BuildGameLobbySettings(
            room,
            (name, max, skill, password) =>
            {
                _ctx.CnCNet.UpdateGameLobbySettings(name, max, skill, password);
                _ctx.ShowStatus("Room settings updated.");
                _ctx.CloseFloatingOverlaySilently();
            },
            _ctx.CloseFloatingOverlaySilently);

        ShowRawHostOverlay(root, size.Width, size.Height, "Game lobby settings");
    }

    public void ShowRawHostOverlay(Control content, double width, double height, string title)
    {
        _overlayPanel.Width = width + 16;
        _overlayPanel.Height = height + 16;
        _overlayPanel.Padding = new Thickness(8);
        _overlayPanel.Background = Brushes.Transparent;
        _overlayPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
        _overlayPanel.BorderThickness = new Thickness(2);

        _ctx.OverlayRoot = null;
        _overlayView.IsVisible = false;
        _overlayView.Content = null;
        _overlayRawView.ContentTemplate = null;
        _overlayRawView.Width = width;
        _overlayRawView.Height = height;
        _overlayRawView.Content = content;
        _overlayRawView.IsVisible = true;

        _floatingOverlay.IsVisible = true;
        _floatingOverlay.IsHitTestVisible = true;
        _rootView.IsHitTestVisible = false;
        _ctx.FloatingOverlayWindow = "GameRoomHostOverlay";
        _ctx.ShowStatus(title);
    }

    public void ShowGameCreationOverlay(object content, double width, double height, string sourceNote)
    {
        _overlayPanel.Width = width + 16;
        _overlayPanel.Height = height + 16;
        _overlayPanel.Padding = new Thickness(8);
        _overlayPanel.Background = Brushes.Transparent;
        _overlayPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 50));
        _overlayPanel.BorderThickness = new Thickness(2);

        if (content is UiNodeViewModel iniRoot)
        {
            _ctx.OverlayRoot = iniRoot;
            _overlayRawView.IsVisible = false;
            _overlayRawView.Content = null;
            _overlayView.Width = width;
            _overlayView.Height = height;
            _overlayView.IsVisible = true;
            _overlayView.Content = iniRoot;
        }
        else
        {
            _ctx.OverlayRoot = null;
            _overlayView.IsVisible = false;
            _overlayView.Content = null;
            _overlayRawView.ContentTemplate = null;
            _overlayRawView.Width = width;
            _overlayRawView.Height = height;
            _overlayRawView.Content = content;
            _overlayRawView.IsVisible = true;
        }

        _floatingOverlay.IsVisible = true;
        _floatingOverlay.IsHitTestVisible = true;
        _rootView.IsHitTestVisible = false;
        _ctx.FloatingOverlayWindow = GameCreationOverlayHost.WindowName;
        _ctx.ShowStatus($"Create game ({sourceNote}).");
    }

    public void CloseGameCreationOverlay()
    {
        if (_ctx.FloatingOverlayWindow?.Equals(GameCreationOverlayHost.WindowName, StringComparison.OrdinalIgnoreCase) != true)
            return;

        ResetOverlayPanelChrome();
        CloseFloatingOverlayCore(restoreIniOverlayView: true);
        _ctx.GameCreationOverlay = null;
    }

    public void ResetOverlayPanelChrome()
    {
        _overlayPanel.Padding = new Thickness(0);
        _overlayPanel.Background = new SolidColorBrush(Color.FromArgb(204, 20, 16, 12));
        _overlayPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 140, 50));
        _overlayPanel.BorderThickness = new Thickness(1);
    }

    public void CloseFloatingOverlayCore(bool restoreIniOverlayView)
    {
        _floatingOverlay.IsVisible = false;
        _floatingOverlay.IsHitTestVisible = false;
        _overlayView.Content = null;
        _overlayRawView.Content = null;
        _overlayRawView.IsVisible = false;
        if (restoreIniOverlayView)
            _overlayView.IsVisible = true;
        _rootView.IsHitTestVisible = true;
        _ctx.OverlayRoot = null;
        _ctx.OverlayEngine = null;
        _ctx.OverlayBehaviors.Clear();
        _ctx.FloatingOverlayWindow = null;
    }
}
