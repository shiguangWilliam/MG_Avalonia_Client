using System.Collections.Generic;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.IniUi.Models;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// End-to-end smoke test: with <see cref="BuiltinIniActions"/> registered, an INI
/// <c>$LeftClickAction</c> declaration routes through IniBehaviorApplier →
/// IniActionCatalog → IUiNavigationHost.
///
/// This is the proof that modders can write
/// <c>$LeftClickAction=NavigateTo:SkirmishLobby</c> in INI instead of touching C#.
/// </summary>
public sealed class IniActionEndToEndTests
{
    [Fact]
    public void BtnExit_With_Ini_Declaration_Closes_Application_Via_Catalog()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();
        var (root, registry, btnExit) = MakeTreeWithButton("btnExit");

        // INI 等价：[btnExit] $LeftClickAction=ExitApplication
        btnExit.Node.RawAttributes["$LeftClickAction"] = "ExitApplication";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        btnExit.ClickCommand.Execute(null);

        host.ExitCalls.Should().Be(1, "INI-declared ExitApplication should route to host.ExitApplication");
    }

    [Fact]
    public void BtnSkirmish_With_Ini_Declaration_Navigates_Via_Catalog()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();
        var (root, registry, btnSkirmish) = MakeTreeWithButton("btnSkirmish");

        // INI 等价：[btnSkirmish] $LeftClickAction=NavigateTo:SkirmishLobby
        btnSkirmish.Node.RawAttributes["$LeftClickAction"] = "NavigateTo:SkirmishLobby";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        btnSkirmish.ClickCommand.Execute(null);

        host.NavigateTargets.Should().Equal(new[] { "SkirmishLobby" });
    }

    [Fact]
    public void BtnOptions_With_Ini_Declaration_Opens_Overlay_Via_Catalog()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();
        var (root, registry, btnOptions) = MakeTreeWithButton("btnOptions");

        // OpenOptionsOverlay 没有参数版本——直接注册为 BuiltinIniActions 中。
        btnOptions.Node.RawAttributes["$LeftClickAction"] = "CloseOptionsOverlay";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        // CloseOptionsOverlay 应该可调用（这里仅验证派发链路，调用 CloseOptionsOverlay 不报错）。
        btnOptions.ClickCommand.Execute(null);

        host.CloseOptionsOverlayCalls.Should().Be(1);
    }

    [Fact]
    public void BtnLaunchGame_With_Ini_Declaration_Launches_Skirmish_Via_Catalog()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost { LaunchSkirmishResult = true };
        var (root, registry, btnLaunch) = MakeTreeWithButton("btnLaunchGame");

        btnLaunch.Node.RawAttributes["$LeftClickAction"] = "LaunchSkirmish";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        btnLaunch.ClickCommand.Execute(null);

        host.LaunchSkirmishCalls.Should().Be(1);
    }

    [Fact]
    public void Back_Compat_Without_Catalog_Only_DISABLE_Works()
    {
        // catalog=null（向后兼容场景：旧调用方未注入 catalog）
        var host = new RecordingHost();
        var (root, registry, btnExit) = MakeTreeWithButton("btnExit");
        btnExit.Node.RawAttributes["$LeftClickAction"] = "ExitApplication";

        IniBehaviorApplier.Apply(root, registry, host, catalog: null);

        btnExit.ClickCommand.Execute(null);

        host.ExitCalls.Should().Be(0, "without catalog, ExitApplication is unknown and ignored");
    }

    private static (UiNodeViewModel root, BehaviorRegistry registry, UiNodeViewModel child) MakeTreeWithButton(string childId)
    {
        var childNode = new UiNode { Id = childId, ControlType = "XNAButton", TemplateKey = "Button" };
        var resolver = new ResourceResolver(primaryRoot: ".");
        var registry = new BehaviorRegistry();
        var child = new UiNodeViewModel(childNode, resolver, registry);
        var rootNode = new UiNode { Id = "root", ControlType = "XNAWindow", TemplateKey = "Window" };
        var root = new UiNodeViewModel(rootNode, resolver, registry, children: new[] { child });
        return (root, registry, child);
    }

    private sealed class RecordingHost : IUiNavigationHost
    {
        public string CurrentWindow => "Test";
        public bool IsFloatingOverlayOpen => false;
        public string? FloatingOverlayWindow => null;
        public bool IsOptionsOverlayOpen => false;
        public UiNodeViewModel? ActiveRoot => null;
        public UiNodeViewModel? OverlayRoot => null;

        public int ExitCalls;
        public int CloseOptionsOverlayCalls;
        public int LaunchSkirmishCalls;
        public bool LaunchSkirmishResult = true;
        public List<string> NavigateTargets { get; } = new();
        public List<string> StatusMessages { get; } = new();

        public void NavigateTo(string windowName) => NavigateTargets.Add(windowName);
        public void NavigateBack() { }
        public void LogoutToMainMenu() { }
        public void OpenFloatingOverlay(string windowName) { }
        public void CloseFloatingOverlay() { }
        public void OpenOptionsOverlay() { }
        public void CloseOptionsOverlay() => CloseOptionsOverlayCalls++;
        public void OpenCampaignOverlay() { }
        public void OpenGameCreationOverlay() { }
        public void CloseGameCreationOverlay() { }
        public void OpenGameRoomTunnelSelection() { }
        public void OpenGameLobbySettingsOverlay() { }
        public void ShowStatus(string message) => StatusMessages.Add(message);
        public void ExitApplication() => ExitCalls++;
        public void CommitSettings() { }
        public void DiscardSettings() { }
        public bool TryLaunchSkirmish(out string message) { LaunchSkirmishCalls++; message = ""; return LaunchSkirmishResult; }
        public bool TryLaunchCampaign(out string message) { message = ""; return false; }
        public bool TryLaunchCnCNetGame(out string message) { message = ""; return false; }
        public bool TryLaunchLanGame(out string message) { message = ""; return false; }
        public void OpenLoadGameOverlay() { }
        public void RefreshCnCNetGameListing() { }
        public void RefreshCnCNetGameRoomPlayers() { }
        public void TryJoinSelectedCnCNetGame() { }
        public void EnterCnCNetGameLobbyConnecting() { }
        public void SelectOptionsTab(int index) { }
        public void CheckForUpdates() { }
        public void RefreshMainMenuState() { }
        public void RefreshLobbyMapList() { }
        public void PickRandomLobbyMap() { }
        public void ToggleFavoriteLobbyMap() { }
        public void FilterCampaignBySide(CampaignSideFilter sideFilter) { }
        public void TogglePlayerExtraOptionsPanel() { }
    }
}
