using ClientAvalonia.Core;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.Services;

/// <summary>Initializes Rampastring file logging under Client/client.log (aligned with DXMainClient PreStartup).</summary>
public static class ClientLogService
{
    private static bool _initialized;
    private static bool _earlyOnly;

    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Pre-workspace logger under LocalAppData (no ClientDefinitions / GamePath required).
    /// </summary>
    public static void EnsureEarlyInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        _earlyOnly = true;

        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClientAvalonia",
                "Logs");
            Directory.CreateDirectory(logDir);

            string logPath = Path.Combine(logDir, "client.log");
            string previousPath = Path.Combine(logDir, "client_previous.log");
            RotateLog(logPath, previousPath);

            Logger.Initialize(logDir, "client.log");
            Logger.WriteLogFile = true;
            ProgramConstants.LogFileName = logPath;

            Logger.Log("*** ClientAvalonia logfile (early / pre-workspace) ***");
            Logger.Log($"CWD: {Environment.CurrentDirectory}");
            Logger.Log($"Exe: {AppContext.BaseDirectory}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClientLogService early init failed: {ex.Message}");
        }
    }

    /// <summary>Rebind logger to GamePath\Client after workspace selection.</summary>
    public static void EnsureGameRootInitialized()
    {
        if (_initialized && !_earlyOnly)
            return;

        try
        {
            Directory.CreateDirectory(ProgramConstants.ClientUserFilesPath);

            string logPath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "client.log");
            string previousPath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "client_previous.log");
            RotateLog(logPath, previousPath);

            Logger.Initialize(ProgramConstants.ClientUserFilesPath, "client.log");
            Logger.WriteLogFile = true;
            ProgramConstants.LogFileName = logPath;

            _initialized = true;
            _earlyOnly = false;

            Logger.Log("*** ClientAvalonia logfile ***");
            Logger.Log($"Game root: {Environment.CurrentDirectory}");

            if (ClientCoreBootstrap.IsInitialized)
                Logger.Log($"LocalGame: {ClientConfiguration.Instance.LocalGame}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ClientLogService game-root init failed: {ex.Message}");
            if (!_initialized)
                EnsureEarlyInitialized();
        }
    }

    /// <summary>Legacy entry: prefers game-root log when GamePath already points at a valid install.</summary>
    public static void EnsureInitialized()
    {
        if (InstallationRegistry.IsInstallPathValid(ProgramConstants.GamePath))
            EnsureGameRootInitialized();
        else
            EnsureEarlyInitialized();
    }

    private static void RotateLog(string logPath, string previousPath)
    {
        if (!File.Exists(logPath))
            return;

        if (File.Exists(previousPath))
            File.Delete(previousPath);
        File.Move(logPath, previousPath);
    }
}
