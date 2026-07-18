using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClientAvalonia.Domain;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.INIProcessing;
using ClientUpdater;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>
/// Post-logger initialization aligned with DXMainClient <c>Startup.Execute</c>.
/// Avalonia continues into UI instead of <c>GameClass.Run()</c>.
/// </summary>
public sealed class Startup
{
    public static bool BootstrapSucceeded { get; internal set; }

    public static string? BootstrapError { get; internal set; }

    public static bool IsUpdaterInitialized { get; private set; }

    public static event Action? LocalVersionsChecked;

    /// <summary>Clears bootstrap flags for Avalonia workspace rebind.</summary>
    public static void ResetBootstrapState()
    {
        BootstrapSucceeded = false;
        BootstrapError = null;
        IsUpdaterInitialized = false;
    }

    public void Execute()
    {
        DirectoryInfo resourcesDirectory = SafePath.GetDirectory(ProgramConstants.GetResourcePath());
        if (!resourcesDirectory.Exists)
            throw new DirectoryNotFoundException("Theme directory not found!" + Environment.NewLine + ProgramConstants.RESOURCES_DIR);

        Logger.Log("Initializing updater.");

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "version_u");

        string callingExecutable = Path.GetFileName(Environment.ProcessPath ?? "ClientAvalonia.exe");
        Updater.Initialize(
            ProgramConstants.GamePath,
            ProgramConstants.GetBaseResourcePath(),
            ClientConfiguration.Instance.SettingsIniName,
            ClientConfiguration.Instance.LocalGame,
            callingExecutable);
        IsUpdaterInitialized = true;

        Logger.Log("OSDescription: " + RuntimeInformation.OSDescription);
        Logger.Log("OSArchitecture: " + RuntimeInformation.OSArchitecture);
        Logger.Log("ProcessArchitecture: " + RuntimeInformation.ProcessArchitecture);
        Logger.Log("FrameworkDescription: " + RuntimeInformation.FrameworkDescription);
        Logger.Log("Current culture: " + CultureInfo.CurrentCulture);

        StartupBackgroundTasks.StartHardwareProbe();
        StartupBackgroundTasks.StartOnlineIdentityGeneration();
        StartupBackgroundTasks.ScheduleDebugFolderPrune();
        StartupBackgroundTasks.ScheduleLogMigration();

        TryDeleteUpdaterTempFolder();
        EnsureSavedGamesDirectory();
        RemovePartialCustomComponentDownloads();

        FinalSunSettings.WriteFinalSunIni();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            InstallationRegistry.TryUpdateInstallPath(ProgramConstants.GamePath);

        ClientConfiguration.Instance.RefreshSettings();
        PreprocessorBackgroundTask.Instance.Run();

        GameRendererBootstrap.EnsureAppliedAtStartup();

        try
        {
            GameResourceCatalog.Instance.EnsureLoaded();
        }
        catch (Exception ex)
        {
            Logger.Log($"Game resource catalog load failed: {ex.Message}");
        }

        ScheduleLocalVersionCheck();

        BootstrapSucceeded = true;
        BootstrapError = null;
    }

    private static void ScheduleLocalVersionCheck()
    {
        if (!IsUpdaterInitialized)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Updater.OnLocalFileVersionsChecked += OnLocalFileVersionsChecked;
                Updater.CheckLocalFileVersions();
            }
            catch (Exception ex)
            {
                Logger.Log($"Local file version check failed: {ex.Message}");
            }
        });
    }

    private static void OnLocalFileVersionsChecked()
    {
        Updater.OnLocalFileVersionsChecked -= OnLocalFileVersionsChecked;
        ProgramConstants.GAME_VERSION = ClientConfiguration.Instance.ModMode ? "N/A" : Updater.GameVersion;
        LocalVersionsChecked?.Invoke();
    }

    private static void TryDeleteUpdaterTempFolder()
    {
        DirectoryInfo updaterFolder = SafePath.GetDirectory(ProgramConstants.GamePath, "Updater");
        if (!updaterFolder.Exists)
            return;

        Logger.Log("Attempting to delete temporary updater directory.");
        try
        {
            updaterFolder.Delete(true);
        }
        catch
        {
        }
    }

    private static void EnsureSavedGamesDirectory()
    {
        if (!ClientConfiguration.Instance.CreateSavedGamesDirectory)
            return;

        DirectoryInfo savedGamesFolder = SafePath.GetDirectory(ProgramConstants.GamePath, "Saved Games");
        if (savedGamesFolder.Exists)
            return;

        Logger.Log("Saved Games directory does not exist - attempting to create one.");
        try
        {
            savedGamesFolder.Create();
        }
        catch
        {
        }
    }

    private static void RemovePartialCustomComponentDownloads()
    {
        if (Updater.CustomComponents == null)
            return;

        Logger.Log("Removing partial custom component downloads.");
        foreach (var component in Updater.CustomComponents)
        {
            try
            {
                SafePath.DeleteFileIfExists(ProgramConstants.GamePath, $"{component.LocalPath}_u");
            }
            catch
            {
            }
        }
    }
}
