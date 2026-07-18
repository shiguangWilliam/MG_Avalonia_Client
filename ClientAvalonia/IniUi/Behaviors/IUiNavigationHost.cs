namespace ClientAvalonia.IniUi.Behaviors;

using ClientAvalonia.Domain;
using ClientAvalonia.Rendering;

/// <summary>Host surface for INI-driven window navigation (MainMenu → lobby/options, back).</summary>
public interface IUiNavigationHost
{
    string CurrentWindow { get; }

    bool IsFloatingOverlayOpen { get; }

    string? FloatingOverlayWindow { get; }

    bool IsOptionsOverlayOpen { get; }

    void NavigateTo(string windowName);

    void NavigateBack();

    /// <summary>Log out of CnCNet (if applicable) and return to MainMenu (XNA topBar.SwitchToPrimary).</summary>
    void LogoutToMainMenu();

    void OpenFloatingOverlay(string windowName);

    void CloseFloatingOverlay();

    void OpenOptionsOverlay();

    void CloseOptionsOverlay();

    void OpenCampaignOverlay();

    void OpenGameCreationOverlay();

    void CloseGameCreationOverlay();

    /// <summary>Host-only: open tunnel picker for the active CnCNet game room.</summary>
    void OpenGameRoomTunnelSelection();

    /// <summary>Host-only: open room name / max players / password settings.</summary>
    void OpenGameLobbySettingsOverlay();

    void ShowStatus(string message);

    void ExitApplication();

    /// <summary>Teardown current workspace and return to the multi-mod picker (§5.2).</summary>
    void ReturnToWorkspacePicker();

    void CommitSettings();

    void DiscardSettings();

    UiNodeViewModel? ActiveRoot { get; }

    UiNodeViewModel? OverlayRoot { get; }

    bool TryLaunchSkirmish(out string message);

    bool TryLaunchCampaign(out string message);

    bool TryLaunchCnCNetGame(out string message);

    void RefreshCnCNetGameListing();

    void RefreshCnCNetGameRoomPlayers();

    void TryJoinSelectedCnCNetGame();

    void EnterCnCNetGameLobbyConnecting();

    void SelectOptionsTab(int index);

    void CheckForUpdates();

    void RefreshMainMenuState();

    void RefreshLobbyMapList();

    void PickRandomLobbyMap();

    void ToggleFavoriteLobbyMap();

    void FilterCampaignBySide(CampaignSideFilter sideFilter);

    void TogglePlayerExtraOptionsPanel();
}
