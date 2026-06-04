using System.IO;
using ClientAvalonia.IniUi.Loading;
using ClientAvalonia.Services;
using ClientCore;
using ClientCore.INIProcessing;
using ClientCore.Network;
using ClientUpdater;
using Rampastring.Tools;

namespace ClientAvalonia.Core;

/// <summary>Pre-main-window startup aligned with DXMainClient Startup.Execute (Core + Updater + INI preprocessor).</summary>
public static class ClientStartupService
{
    private static bool _ran;

    public static bool IsUpdaterInitialized { get; private set; }

    public static event Action? LocalVersionsChecked;

    public static void Run(string? gameRoot = null)
    {
        if (_ran)
            return;

        _ran = true;

        gameRoot ??= ClientEnvironment.FindGameRoot(Directory.GetCurrentDirectory());
        Environment.CurrentDirectory = gameRoot;

        ClientLogService.EnsureInitialized();

        if (!ClientCoreBootstrap.TryEnsureInitialized(gameRoot, out _))
            return;

        CnCNetIdentity.EnsurePersisted();

        ProgramConstants.RESOURCES_DIR = SafePath.CombineDirectoryPath(
            ProgramConstants.BASE_RESOURCE_PATH,
            UserINISettings.Instance.ThemeFolderPath);

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "version_u");

        TryInitializeUpdater(gameRoot);

        TryDeleteUpdaterTempFolder();
        ClientConfiguration.Instance.RefreshSettings();
        PreprocessorBackgroundTask.Instance.Run();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                GameResourceCatalog.Instance.EnsureLoaded();
            }
            catch (Exception ex)
            {
                Logger.Log($"Game resource catalog load failed: {ex.Message}");
            }
        });

        if (IsUpdaterInitialized)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Updater.OnLocalFileVersionsChecked += OnLocalFileVersionsChecked;
                    Updater.CheckLocalFileVersions();
                }
                catch
                {
                }
            });
        }
    }

    private static bool TryInitializeUpdater(string gameRoot)
    {
        try
        {
            string callingExecutable = Path.GetFileName(Environment.ProcessPath ?? "ClientAvalonia.exe");
            Updater.Initialize(
                ProgramConstants.GamePath,
                ProgramConstants.GetBaseResourcePath(),
                ClientConfiguration.Instance.SettingsIniName,
                ClientConfiguration.Instance.LocalGame,
                callingExecutable);

            IsUpdaterInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Updater initialization failed: {ex.Message}");
            IsUpdaterInitialized = false;
            return false;
        }
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

        try
        {
            updaterFolder.Delete(true);
        }
        catch
        {
        }
    }
}
