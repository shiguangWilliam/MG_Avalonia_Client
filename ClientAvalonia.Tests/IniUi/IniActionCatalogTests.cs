using System;
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
/// Locks <see cref="IniActionCatalog"/> semantics:
///   - Register / TryDispatch round-trip
///   - Case-insensitive name lookup (DX INI convention)
///   - Args parsing: <c>Name:Arg1:Arg2</c> → name + full args string
///   - DISABLE not handled here (catalog is generic; behavior layer checks DISABLE)
///   - Exceptions inside handler are swallowed, dispatch still returns true (hit registered)
///   - Re-registration overrides previous (matches BehaviorRegistry)
///   - Empty / null / whitespace names rejected
/// </summary>
public sealed class IniActionCatalogTests
{
    [Fact]
    public void TryDispatch_Unregistered_Name_Returns_False()
    {
        var catalog = new IniActionCatalog();
        catalog.TryDispatch("DoesNotExist", NewHost()).Should().BeFalse();
    }

    [Fact]
    public void TryDispatch_Registered_Name_Returns_True_And_Invokes_Handler()
    {
        var catalog = new IniActionCatalog();
        string? receivedArgs = null;
        catalog.Register("Ping", (args, _) => receivedArgs = args);

        bool hit = catalog.TryDispatch("Ping", NewHost());

        hit.Should().BeTrue();
        receivedArgs.Should().BeEmpty("no args passed");
    }

    [Fact]
    public void TryDispatch_Name_Is_Case_Insensitive()
    {
        var catalog = new IniActionCatalog();
        bool invoked = false;
        catalog.Register("LaunchSkirmish", (_, _) => invoked = true);

        catalog.TryDispatch("launchskirmish", NewHost()).Should().BeTrue();
        invoked.Should().BeTrue();

        invoked = false;
        catalog.TryDispatch("LAUNCHSKIRMISH", NewHost()).Should().BeTrue();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void TryDispatch_Passes_Args_After_First_Colon()
    {
        var catalog = new IniActionCatalog();
        string? received = null;
        catalog.Register("NavigateTo", (args, _) => received = args);

        catalog.TryDispatch("NavigateTo:SkirmishLobby", NewHost());

        received.Should().Be("SkirmishLobby");
    }

    [Fact]
    public void TryDispatch_Args_Can_Contain_Colons()
    {
        var catalog = new IniActionCatalog();
        string? received = null;
        catalog.Register("Foo", (args, _) => received = args);

        // 第一个冒号后的全部内容（含后续冒号）都作为 args 传给 handler。
        catalog.TryDispatch("Foo:a:b:c", NewHost());

        received.Should().Be("a:b:c");
    }

    [Fact]
    public void TryDispatch_Trims_Name_But_Preserves_Args_Whitespace()
    {
        var catalog = new IniActionCatalog();
        string? receivedArgs = null;
        catalog.Register("Bar", (args, _) => receivedArgs = args);

        catalog.TryDispatch("  Bar  :  hello  ", NewHost());

        // 名称被 trim，参数保留原样（让 handler 决定是否再 trim）。
        receivedArgs.Should().Be("  hello  ");
    }

    [Fact]
    public void TryDispatch_Swallows_Handler_Exception()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("Boom", (_, _) => throw new InvalidOperationException("kaboom"));

        // 不应向上传播——保证 BehaviorRegistry / 点击事件不崩 UI。
        Action act = () => catalog.TryDispatch("Boom", NewHost());

        act.Should().NotThrow();
    }

    [Fact]
    public void TryDispatch_Exception_Still_Returns_True_Because_Name_Matched()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("Boom", (_, _) => throw new InvalidOperationException("kaboom"));

        // 已注册 + 已执行（即使失败）→ true，让调用方知道动作存在。
        // 这与「名字不存在 → false」语义不同。
        catalog.TryDispatch("Boom", NewHost()).Should().BeTrue();
    }

    [Fact]
    public void Register_ReRegistration_Overrides()
    {
        var catalog = new IniActionCatalog();
        int firstCalls = 0, secondCalls = 0;
        catalog.Register("Dup", (_, _) => firstCalls++);
        catalog.Register("Dup", (_, _) => secondCalls++);

        catalog.TryDispatch("Dup", NewHost());

        firstCalls.Should().Be(0, "first handler was overridden");
        secondCalls.Should().Be(1);
    }

    [Fact]
    public void Register_ReRegistration_Does_Not_Duplicate_Name()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("Dup", (_, _) => { });
        catalog.Register("Dup", (_, _) => { });
        catalog.Register("DUP", (_, _) => { }); // case-insensitive duplicate

        catalog.RegisteredNames.Should().ContainSingle();
    }

    [Fact]
    public void Register_Null_Name_Throws()
    {
        var catalog = new IniActionCatalog();
        Action act = () => catalog.Register(null!, (_, _) => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_Empty_Name_Throws()
    {
        var catalog = new IniActionCatalog();
        Action act = () => catalog.Register("   ", (_, _) => { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_Null_Handler_Throws()
    {
        var catalog = new IniActionCatalog();
        Action act = () => catalog.Register("Foo", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsRegistered_Case_Insensitive()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("LaunchSkirmish", (_, _) => { });

        catalog.IsRegistered("LaunchSkirmish").Should().BeTrue();
        catalog.IsRegistered("launchskirmish").Should().BeTrue();
        catalog.IsRegistered("Missing").Should().BeFalse();
        catalog.IsRegistered("").Should().BeFalse();
        catalog.IsRegistered("   ").Should().BeFalse();
    }

    [Fact]
    public void RegisteredNames_Preserves_First_Registration_Order()
    {
        var catalog = new IniActionCatalog();
        catalog.Register("C", (_, _) => { });
        catalog.Register("A", (_, _) => { });
        catalog.Register("B", (_, _) => { });
        catalog.Register("A", (_, _) => { }); // re-register, no duplicate

        catalog.RegisteredNames.Should().Equal(new[] { "C", "A", "B" });
    }

    [Fact]
    public void TryDispatch_Null_Returns_False()
    {
        var catalog = new IniActionCatalog();
        catalog.TryDispatch(null!, NewHost()).Should().BeFalse();
    }

    [Fact]
    public void TryDispatch_Empty_Returns_False()
    {
        var catalog = new IniActionCatalog();
        catalog.TryDispatch("", NewHost()).Should().BeFalse();
        catalog.TryDispatch("   ", NewHost()).Should().BeFalse();
    }

    [Fact]
    public void TryDispatch_Passes_Host_To_Handler()
    {
        var catalog = new IniActionCatalog();
        IUiNavigationHost? receivedHost = null;
        catalog.Register("Inspect", (_, host) => receivedHost = host);

        var host = NewHost();
        catalog.TryDispatch("Inspect", host);

        receivedHost.Should().BeSameAs(host);
    }

    private static IUiNavigationHost NewHost()
    {
        // 最小可用 host：用 stub 实现，不依赖任何 UI 框架。
        return new StubHost();
    }

    /// <summary>测试用最小 IUiNavigationHost stub。</summary>
    private sealed class StubHost : IUiNavigationHost
    {
        public string CurrentWindow => "Test";
        public bool IsFloatingOverlayOpen => false;
        public string? FloatingOverlayWindow => null;
        public bool IsOptionsOverlayOpen => false;
        public UiNodeViewModel? ActiveRoot => null;
        public UiNodeViewModel? OverlayRoot => null;

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
        public void ShowStatus(string message) { }
        public void ExitApplication() { }
        public void CommitSettings() { }
        public void DiscardSettings() { }
        public bool TryLaunchSkirmish(out string message) { message = ""; return false; }
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
