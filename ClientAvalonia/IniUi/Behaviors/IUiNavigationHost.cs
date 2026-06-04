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

    void OpenFloatingOverlay(string windowName);

    void CloseFloatingOverlay();

    void OpenOptionsOverlay();

    void CloseOptionsOverlay();

    void OpenCampaignOverlay();

    void ShowStatus(string message);

    void ExitApplication();

    void CommitSettings();

    void DiscardSettings();

    UiNodeViewModel? ActiveRoot { get; }

    UiNodeViewModel? OverlayRoot { get; }

    bool TryLaunchSkirmish(out string message);

    bool TryLaunchCampaign(out string message);

    void SelectOptionsTab(int index);

    void CheckForUpdates();

    void RefreshMainMenuState();

    void RefreshLobbyMapList();

    void PickRandomLobbyMap();

    void ToggleFavoriteLobbyMap();

    void FilterCampaignBySide(CampaignSideFilter sideFilter);

    void TogglePlayerExtraOptionsPanel();
}
