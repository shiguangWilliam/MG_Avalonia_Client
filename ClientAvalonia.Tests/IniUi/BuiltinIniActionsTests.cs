using System.Collections.Generic;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Actions;
using ClientAvalonia.IniUi.Behaviors;
using ClientAvalonia.Rendering;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.IniUi;

/// <summary>
/// Locks <see cref="BuiltinIniActions"/> registration: each registered name
/// dispatches to the correct IUiNavigationHost method with correct args.
/// </summary>
public sealed class BuiltinIniActionsTests
{
    [Fact]
    public void RegisterAll_Registers_All_Expected_Names()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);

        // 至少这 5 个核心动作必须存在（设计文档 §2.6 验收标准）。
        catalog.IsRegistered("ExitApplication").Should().BeTrue();
        catalog.IsRegistered("CheckForUpdates").Should().BeTrue();
        catalog.IsRegistered("LaunchSkirmish").Should().BeTrue();
        catalog.IsRegistered("LaunchCampaign").Should().BeTrue();
        catalog.IsRegistered("LaunchCnCNetGame").Should().BeTrue();
        catalog.IsRegistered("NavigateTo").Should().BeTrue();
    }

    [Fact]
    public void ExitApplication_Dispatches_To_Host()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("ExitApplication", host);

        host.ExitCalls.Should().Be(1);
    }

    [Fact]
    public void CheckForUpdates_Dispatches_To_Host()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("CheckForUpdates", host);

        host.CheckForUpdatesCalls.Should().Be(1);
    }

    [Fact]
    public void NavigateTo_Forwards_Arg_As_Target_Window()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("NavigateTo:SkirmishLobby", host);

        host.NavigateTargets.Should().Equal(new[] { "SkirmishLobby" });
    }

    [Fact]
    public void NavigateTo_With_Missing_Arg_Shows_Status()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("NavigateTo", host);

        host.NavigateTargets.Should().BeEmpty("missing arg should not navigate");
        host.StatusMessages.Should().NotBeEmpty();
    }

    [Fact]
    public void LaunchSkirmish_Calls_Host_And_Handles_Failure_Message()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost { LaunchSkirmishResult = false, LaunchMessage = "Missing map" };

        catalog.TryDispatch("LaunchSkirmish", host);

        host.LaunchSkirmishCalls.Should().Be(1);
        host.StatusMessages.Should().Contain("Missing map");
    }

    [Fact]
    public void LaunchSkirmish_On_Success_Does_Not_Show_Status()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost { LaunchSkirmishResult = true };

        catalog.TryDispatch("LaunchSkirmish", host);

        host.LaunchSkirmishCalls.Should().Be(1);
        host.StatusMessages.Should().BeEmpty("success should not pop status");
    }

    [Fact]
    public void SelectOptionsTab_Forwards_Integer_Arg()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("SelectOptionsTab:2", host);

        host.SelectedOptionTabs.Should().Equal(new[] { 2 });
    }

    [Fact]
    public void SelectOptionsTab_Non_Integer_Arg_Is_Silently_Ignored()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("SelectOptionsTab:not-a-number", host);

        host.SelectedOptionTabs.Should().BeEmpty();
    }

    [Fact]
    public void FilterCampaignBySide_Accepts_Enum_Name()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("FilterCampaignBySide:Allied", host);

        host.CampaignFilters.Should().Equal(new[] { CampaignSideFilter.Allied });
    }

    [Fact]
    public void FilterCampaignBySide_Is_Case_Insensitive()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        var host = new RecordingHost();

        catalog.TryDispatch("FilterCampaignBySide:soviet", host);

        host.CampaignFilters.Should().Equal(new[] { CampaignSideFilter.Soviet });
    }

    [Fact]
    public void RegisterAll_Is_Idempotent_Does_Not_Duplicate()
    {
        var catalog = new IniActionCatalog();
        BuiltinIniActions.RegisterAll(catalog);
        int firstCount = catalog.RegisteredNames.Count;

        BuiltinIniActions.RegisterAll(catalog);
        int secondCount = catalog.RegisteredNames.Count;

        secondCount.Should().Be(firstCount, "re-registration should override, not duplicate");
    }

    /// <summary>记录所有调用以便断言。</summary>
    private sealed class RecordingHost : IUiNavigationHost
    {
        public string CurrentWindow => "Test";
        public bool IsFloatingOverlayOpen => false;
        public string? FloatingOverlayWindow => null;
        public bool IsOptionsOverlayOpen => false;
        public UiNodeViewModel? ActiveRoot => null;
        public UiNodeViewModel? OverlayRoot => null;

        public int ExitCalls;
        public int CheckForUpdatesCalls;
        public int LaunchSkirmishCalls;
        public int LaunchCampaignCalls;
        public int LaunchCnCNetGameCalls;
        public bool LaunchSkirmishResult = true;
        public bool LaunchCampaignResult = true;
        public bool LaunchCnCNetGameResult = true;
        public string LaunchMessage = "";

        public List<string> StatusMessages = new();
        public List<string> NavigateTargets = new();
        public List<int> SelectedOptionTabs = new();
        public List<CampaignSideFilter> CampaignFilters = new();

        public void NavigateTo(string windowName) => NavigateTargets.Add(windowName);
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
        public void ExitApplication() => ExitCalls++;
        public void CommitSettings() { }
        public void DiscardSettings() { }
        public bool TryLaunchSkirmish(out string message) { LaunchSkirmishCalls++; message = LaunchMessage; return LaunchSkirmishResult; }
        public bool TryLaunchCampaign(out string message) { LaunchCampaignCalls++; message = LaunchMessage; return LaunchCampaignResult; }
        public bool TryLaunchCnCNetGame(out string message) { LaunchCnCNetGameCalls++; message = LaunchMessage; return LaunchCnCNetGameResult; }
        public bool TryLaunchLanGame(out string message) { message = LaunchMessage; return false; }
        public void OpenLoadGameOverlay() { }
        public void RefreshCnCNetGameListing() { }
        public void RefreshCnCNetGameRoomPlayers() { }
        public void TryJoinSelectedCnCNetGame() { }
        public void EnterCnCNetGameLobbyConnecting() { }
        public void SelectOptionsTab(int index) => SelectedOptionTabs.Add(index);
        public void CheckForUpdates() => CheckForUpdatesCalls++;
        public void RefreshMainMenuState() { }
        public void RefreshLobbyMapList() { }
        public void PickRandomLobbyMap() { }
        public void ToggleFavoriteLobbyMap() { }
        public void FilterCampaignBySide(CampaignSideFilter sideFilter) => CampaignFilters.Add(sideFilter);
        public void TogglePlayerExtraOptionsPanel() { }
    }
}
