using ClientAvalonia.Core;
using ClientCore;
using ClientCore.I18N;
using ClientUpdater;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Services;

/// <summary>Update check flow for MainMenu lblUpdateStatus (aligned with DXMainClient MainMenu).</summary>
public sealed class ClientUpdateService
{
    private bool _handlersRegistered;

    public event Action? StatusChanged;

    public string UpdateStatusText { get; private set; } = "Click to check for updates.";

    public bool CanCheckForUpdates { get; private set; } = true;

    public void EnsureHandlersRegistered()
    {
        if (_handlersRegistered || !ClientStartupService.IsUpdaterInitialized)
            return;

        _handlersRegistered = true;
        Updater.OnVersionStateChanged += HandleVersionStateChanged;
        Updater.FileIdentifiersUpdated += HandleFileIdentifiersUpdated;
    }

    public void RefreshInitialStatus()
    {
        if (!ClientStartupService.IsUpdaterInitialized || AppState.Configuration.Legacy.ModMode)
        {
            UpdateStatusText = Localize("Client:Main:ClickToCheckUpdate", "Click to check for updates.");
            CanCheckForUpdates = false;
            StatusChanged?.Invoke();
            return;
        }

        if (Updater.UpdateMirrors == null || Updater.UpdateMirrors.Count < 1)
        {
            UpdateStatusText = Localize("Client:Main:NoUpdateMirrorsAvailable", "No update download mirrors available.");
            CanCheckForUpdates = false;
            StatusChanged?.Invoke();
            return;
        }

        if (UserINISettings.Instance.CheckForUpdates)
            CheckForUpdates();
        else
        {
            UpdateStatusText = Localize("Client:Main:ClickToCheckUpdate", "Click to check for updates.");
            CanCheckForUpdates = true;
            StatusChanged?.Invoke();
        }
    }

    public void CheckForUpdates()
    {
        if (!ClientStartupService.IsUpdaterInitialized || AppState.Configuration.Legacy.ModMode)
            return;

        if (Updater.UpdateMirrors == null || Updater.UpdateMirrors.Count < 1)
            return;

        Updater.CheckForUpdates();
        CanCheckForUpdates = false;
        UpdateStatusText = Localize("Client:Main:CheckingForUpdates", "Checking for updates...");
        StatusChanged?.Invoke();
    }

    private void HandleVersionStateChanged()
    {
        if (Updater.VersionState == VersionState.UPDATEINPROGRESS)
        {
            UpdateStatusText = Localize("Client:Main:Updating", "Updating...");
            CanCheckForUpdates = false;
            StatusChanged?.Invoke();
        }
    }

    private void HandleFileIdentifiersUpdated()
    {
        if (Updater.VersionState == VersionState.UPDATEINPROGRESS)
            return;

        switch (Updater.VersionState)
        {
            case VersionState.UPTODATE:
                UpdateStatusText = string.Format(
                    Localize("Client:Main:GameUpToDate", "{0} is up to date."),
                    AppState.Configuration.Legacy.LocalGame);
                CanCheckForUpdates = true;
                break;

            case VersionState.OUTDATED when Updater.ManualUpdateRequired:
                UpdateStatusText = Localize(
                    "Client:Main:UpdateAvailableManualDownloadRequired",
                    "An update is available. Manual download & installation required.");
                CanCheckForUpdates = true;
                break;

            case VersionState.OUTDATED:
                UpdateStatusText = Localize("Client:Main:UpdateAvailable", "An update is available.");
                CanCheckForUpdates = true;
                break;

            case VersionState.UNKNOWN:
                UpdateStatusText = Localize(
                    "Client:Main:CheckUpdateFailedClickToRetry",
                    "Checking for updates failed! Click to retry.");
                CanCheckForUpdates = true;
                break;

            default:
                return;
        }

        StatusChanged?.Invoke();
    }

    private static string Localize(string key, string defaultValue)
    {
        if (!ClientCoreBootstrap.IsInitialized)
            return defaultValue;

        return Translation.Instance.LookUp(key, defaultValue: defaultValue, notify: false);
    }
}
