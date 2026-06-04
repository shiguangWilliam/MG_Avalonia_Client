namespace ClientAvalonia.Services;

public interface IUiStateService
{
    string GameVersion { get; }

    string UpdateStatusText { get; }

    string OnlinePlayerCountText { get; }

    bool CanLaunchGame { get; }

    void RefreshMainMenuState();

    void SetUpdateStatusText(string text);

    void SetCanLaunchGame(bool enabled);

    void SetOnlinePlayerCount(int count);
}
