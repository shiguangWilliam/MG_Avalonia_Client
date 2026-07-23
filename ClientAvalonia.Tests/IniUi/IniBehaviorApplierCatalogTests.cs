using System;
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
/// Locks <see cref="IniBehaviorApplier"/> behavior when an <see cref="IIniActionCatalog"/> is provided:
///   - $LeftClickAction with registered catalog name → invokes catalog handler
///   - DISABLE → still disables root (built-in, not via catalog)
///   - Unregistered name → no handler attached (silent ignore)
///   - catalog null → backward-compatible (only DISABLE handled)
/// </summary>
public sealed class IniBehaviorApplierCatalogTests
{
    [Fact]
    public void Registered_Catalog_Action_Gets_Dispatched_On_Click()
    {
        var catalog = new IniActionCatalog();
        var host = new RecordingHost();
        catalog.Register("Boom", (_, _) => host.ShowStatus("boom!"));
        var (root, registry, vm) = MakeTreeWithButton("btnBoom");
        vm.Node.RawAttributes["$LeftClickAction"] = "Boom";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        // 触发 ClickCommand 派发到注册的 BehaviorRegistry handler。
        vm.ClickCommand.Execute(null);
        host.StatusMessages.Should().Contain("boom!");
    }

    [Fact]
    public void DISABLE_Action_Still_Disables_Root_Without_Catalog()
    {
        var host = new RecordingHost();
        var (root, registry, vm) = MakeTreeWithButton("btnX");
        vm.Node.RawAttributes["$LeftClickAction"] = "DISABLE";

        IniBehaviorApplier.Apply(root, registry, host, catalog: null);

        // DISABLE 用 RegisterAfter，下一个 click 触发。
        vm.ClickCommand.Execute(null);
        root.IsEnabled.Should().BeFalse("DISABLE should disable the root");
    }

    [Fact]
    public void DISABLE_Works_Even_When_Catalog_Provided()
    {
        var catalog = new IniActionCatalog();
        var host = new RecordingHost();
        var (root, registry, vm) = MakeTreeWithButton("btnX");
        vm.Node.RawAttributes["$LeftClickAction"] = "DISABLE";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        vm.ClickCommand.Execute(null);
        root.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Unregistered_Name_Does_Nothing()
    {
        var catalog = new IniActionCatalog();
        var host = new RecordingHost();
        var (root, registry, vm) = MakeTreeWithButton("btnX");
        vm.Node.RawAttributes["$LeftClickAction"] = "NoSuchAction";

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        // Click should not throw and should not crash.
        Action act = () => vm.ClickCommand.Execute(null);
        act.Should().NotThrow();
        host.StatusMessages.Should().BeEmpty();
    }

    [Fact]
    public void No_LeftClickAction_Attribute_Attaches_Nothing()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("Foo", (_, _) => { });
        var host = new RecordingHost();
        var (root, registry, vm) = MakeTreeWithButton("btnX");
        // No $LeftClickAction attribute set.

        IniBehaviorApplier.Apply(root, registry, host, catalog);

        // Click should be a no-op.
        Action act = () => vm.ClickCommand.Execute(null);
        act.Should().NotThrow();
    }

    private static (UiNodeViewModel root, BehaviorRegistry registry, UiNodeViewModel child) MakeTreeWithButton(string childId)
    {
        var childNode = new UiNode { Id = childId, ControlType = "XNAButton", TemplateKey = "Button" };

        var resolver = new ResourceResolver(primaryRoot: ".");
        var registry = new BehaviorRegistry();

        // 子 ViewModel 必须先构造，再用 children 参数传入根。
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

        public List<string> StatusMessages { get; } = new();

        public void NavigateTo(string windowName) { }
        public void NavigateBack() { }
        public void LogoutToMainMenu() { }
        public void OpenFloatingOverlay(string windowName) { }
        public void CloseFloatingOverlay() { }
        public void OpenOptionsOverlay() { }
        public void CloseOptionsOverlay() { }
        public void OpenCampaignOverlay() { }
        public void OpenGameCreationOverlay() { }
        public void CloseGameCreationOverlay() { }
        public void OpenGameRoomTunnelSelection() { }
        public void OpenGameLobbySettingsOverlay() { }
        public void ShowStatus(string message) => StatusMessages.Add(message);
        public void ExitApplication() { }
        public void CommitSettings() { }
        public void DiscardSettings() { }
        public bool TryLaunchSkirmish(out string message) { message = ""; return false; }
        public bool TryLaunchCampaign(out string message) { message = ""; return false; }
        public bool TryLaunchCnCNetGame(out string message) { message = ""; return false; }
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
